using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>Scores for one image as returned by the WD14TaggerScoredPath node, or the error for that file.</summary>
public class ComfyTagResult
{
    public Dictionary<string, float> rating = new Dictionary<string, float>();
    public Dictionary<string, float> general = new Dictionary<string, float>();
    public Dictionary<string, float> character = new Dictionary<string, float>();
    public string error;
}

/// <summary>Face-crop emotion data for one image as returned by the MisteraFaceEmotion node.</summary>
public class ComfyEmotionResult
{
    public bool face;
    public Dictionary<string, float> faceTags;
    public string vlm;
    public string vlmRaw;
    public string error;
}

/// <summary>
/// Talks to ComfyUI's HTTP API for the MistEra path-based nodes (workingdir/Tools/comfy/mistera_wd14_scored):
/// GET /object_info/&lt;node&gt; to check it is installed, POST /prompt with an API workflow template, then polls GET /history/&lt;id&gt;.
/// Each node returns one JSON string at outputs.&lt;node id&gt;.&lt;outputKey&gt;[0]: { "&lt;path&gt;": { ... } | { error } }.
/// </summary>
public class ComfyTaggerClient : IDisposable
{
    static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    readonly HttpClient http;
    readonly string baseUrl;
    readonly string clientId = Guid.NewGuid().ToString();
    readonly string nodeClass;
    readonly JObject workflowTemplate;
    readonly string nodeId;

    /// <summary>Per batch; the first batch also loads the models (eva02-large / a VLM: ~10-30 s).</summary>
    public TimeSpan BatchTimeout = TimeSpan.FromMinutes(5);

    public ComfyTaggerClient(string baseUrl, string workflowPath, string nodeClass = ImageClassifierSettings.TaggerNodeClass)
    {
        this.baseUrl = baseUrl.TrimEnd('/');
        this.nodeClass = nodeClass;
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        workflowTemplate = JObject.Parse(File.ReadAllText(workflowPath, Encoding.UTF8));
        nodeId = workflowTemplate.Properties().FirstOrDefault(p => (string)p.Value["class_type"] == nodeClass)?.Name;
        if (nodeId == null) throw new InvalidDataException($"Workflow [{workflowPath}] has no {nodeClass} node.");
    }

    public void Dispose() => http.Dispose();

    /// <summary>The template's inputs for the node (e.g. to hash them), without image_paths.</summary>
    public JObject TemplateInputs => (JObject)workflowTemplate[nodeId]["inputs"];

    /// <summary>Returns the options of the node's "model"/"tagger_model" input (installed tagger models);
    /// throws with a readable message when ComfyUI is down or the node is missing.</summary>
    public async Task<List<string>> CheckNodeAsync(CancellationToken ct)
    {
        string text;
        try
        {
            text = await http.GetStringAsync($"{baseUrl}/object_info/{nodeClass}");
        }
        catch (HttpRequestException e)
        {
            throw new InvalidOperationException($"ComfyUI not reachable at {baseUrl}: {e.Message}");
        }
        ct.ThrowIfCancellationRequested();
        var info = JObject.Parse(text)[nodeClass];
        if (info == null)
            throw new InvalidOperationException($"ComfyUI is running but has no {nodeClass} node. " +
                "Copy workingdir/Tools/comfy/mistera_wd14_scored into ComfyUI/custom_nodes and restart ComfyUI.");
        var required = info["input"]?["required"];
        var models = (required?["model"] ?? required?["tagger_model"])?[0] as JArray;
        return models?.Select(m => (string)m).ToList() ?? new List<string>();
    }

    /// <summary>Tags one batch of absolute paths. Every path gets a result (an error result when the whole prompt failed).</summary>
    public async Task<Dictionary<string, ComfyTagResult>> TagBatchAsync(IReadOnlyList<string> absolutePaths, string model,
        float threshold, float characterThreshold, IEnumerable<string> excludeTags, CancellationToken ct)
    {
        var inputs = new JObject
        {
            ["model"] = model,
            ["threshold"] = threshold,
            ["character_threshold"] = characterThreshold,
            ["exclude_tags"] = string.Join(",", excludeTags),
        };
        return await RunBatchAsync(absolutePaths, inputs, "tags_json",
            v => v.ToObject<ComfyTagResult>(), e => new ComfyTagResult { error = e }, ct);
    }

    /// <summary>Emotion pass for one batch: face crop + WD14 on the crop + one VLM label (MisteraFaceEmotion).</summary>
    public async Task<Dictionary<string, ComfyEmotionResult>> EmotionBatchAsync(IReadOnlyList<string> absolutePaths, IEnumerable<string> labels,
        string prompt, CancellationToken ct)
    {
        var inputs = new JObject { ["labels"] = string.Join(", ", labels), ["prompt"] = prompt };
        return await RunBatchAsync(absolutePaths, inputs, "emotion_json",
            v => v.ToObject<ComfyEmotionResult>(), e => new ComfyEmotionResult { error = e }, ct);
    }

    /// <summary>Patches image_paths + the given inputs into the template, runs it and parses the node's per-path JSON.</summary>
    async Task<Dictionary<string, T>> RunBatchAsync<T>(IReadOnlyList<string> absolutePaths, JObject inputPatch, string outputKey,
        Func<JObject, T> parse, Func<string, T> error, CancellationToken ct)
    {
        var graph = (JObject)workflowTemplate.DeepClone();
        var inputs = (JObject)graph[nodeId]["inputs"];
        inputs["image_paths"] = string.Join("\n", absolutePaths);
        foreach (var p in inputPatch.Properties()) inputs[p.Name] = p.Value;

        string promptId = await QueuePromptAsync(graph, ct);
        JObject entry;
        try
        {
            entry = await WaitForHistoryAsync(promptId, ct);
        }
        catch (OperationCanceledException)
        {
            await TryDeleteFromQueueAsync(promptId);
            throw;
        }

        var results = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        var json = entry["outputs"]?[nodeId]?[outputKey]?[0]?.ToString();
        if (json == null)
        {
            var status = entry["status"]?.ToString(Formatting.None) ?? "no status";
            foreach (var p in absolutePaths) results[p] = error($"prompt failed: {status}");
            return results;
        }
        foreach (var prop in JObject.Parse(json).Properties())
        {
            var value = (JObject)prop.Value;
            results[prop.Name] = value.ContainsKey("error") ? error((string)value["error"]) : parse(value);
        }
        foreach (var p in absolutePaths)
            if (!results.ContainsKey(p)) results[p] = error("missing from node output");
        return results;
    }

    async Task<string> QueuePromptAsync(JObject graph, CancellationToken ct)
    {
        var body = new JObject { ["prompt"] = graph, ["client_id"] = clientId };
        using var content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync($"{baseUrl}/prompt", content, ct);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"ComfyUI rejected the prompt ({(int)response.StatusCode}): {text}");
        return (string)JObject.Parse(text)["prompt_id"];
    }

    async Task<JObject> WaitForHistoryAsync(string promptId, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + BatchTimeout;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var text = await http.GetStringAsync($"{baseUrl}/history/{promptId}");
            if (JObject.Parse(text)[promptId] is JObject entry) return entry;
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"ComfyUI did not finish prompt {promptId} within {BatchTimeout.TotalMinutes} min.");
            await Task.Delay(PollInterval, ct);
        }
    }

    async Task TryDeleteFromQueueAsync(string promptId)
    {
        try
        {
            var body = new JObject { ["delete"] = new JArray(promptId) };
            using var content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
            using var _ = await http.PostAsync($"{baseUrl}/queue", content);
        }
        catch (Exception)
        {
            // best effort: a prompt left in the queue just runs once more
        }
    }
}

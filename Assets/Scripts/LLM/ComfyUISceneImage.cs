using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// One scene image request (Docs/ImagePortraits/Plan_ComfyUISceneImage.md): what was sent and what came back, so later processing
/// knows which scene (actors, actions, scene info) an image batch belongs to. Also written as temp.json next to the images.
/// </summary>
public class ComfyUISceneRequest
{
    public enum State { Building, Queued, Done, Failed }

    public string requestId = Guid.NewGuid().ToString();
    public DateTime createdUtc = DateTime.UtcNow;
    public State state = State.Building;
    public string error;

    public Character_Trainable mainActor;
    public ActionSceneTags tags;
    public ActionScenePrompt prompt;
    /// <summary> placeholder ($name$) -> the text put into the workflow </summary>
    public Dictionary<string, string> placeholders = new Dictionary<string, string>();
    public long seed;

    /// <summary> ComfyUI prompt id (POST /prompt) </summary>
    public string promptId;
    /// <summary> the images of the batch as saved, in batch order </summary>
    public List<string> imagePaths = new List<string>();

    public JObject ToJson()
    {
        JObject scene = null;
        if (tags != null && prompt != null)
        {
            try { scene = JObject.Parse(ActionScenePromptUtility.ToJson(tags, prompt)); }
            catch (Exception) { scene = null; }
        }
        return new JObject
        {
            ["requestId"] = requestId,
            ["createdUtc"] = createdUtc.ToString("o"),
            ["state"] = state.ToString(),
            ["error"] = error,
            ["mainActor"] = mainActor == null ? null : new JObject { ["refID"] = mainActor.RefID, ["name"] = mainActor.FullName },
            ["promptId"] = promptId,
            ["seed"] = seed,
            ["images"] = new JArray(imagePaths.Select(p => (object)p).ToArray()),
            ["placeholders"] = JObject.FromObject(placeholders),
            ["scene"] = scene,
        };
    }
}

/// <summary>
/// Sends the scene prompt of an actor (ActionScenePromptUtility) to ComfyUI and stores the result
/// (Docs/ImagePortraits/Plan_ComfyUISceneImage.md). API hardcoded for now; workflow = Mods/Assetpacks/ComfyUI.json (API format, read every
/// request) with $name$ placeholders in its string inputs. The images of the node titled MisteraOutput (else the first image output) are
/// downloaded (GET /view, works for PreviewImage temp files and SaveImage outputs) to Mods/Assetpacks/_generated/temp_N.ext + temp.json.
/// <br/>Async: Collect / Build run on the calling (main) thread, the awaits resume on it (Unity synchronization context), so Completed
/// is raised on the main thread.
/// </summary>
public static class ComfyUISceneImage
{
    public const string BaseUrl = "http://127.0.0.1:8188";
    public const string WorkflowFileName = "ComfyUI.json";
    public const string OutputFolderName = "_generated";
    public const string OutputFileName = "temp";
    public const string OutputNodeTitle = "MisteraOutput";
    public const string ListSeparator = "||";

    public static TimeSpan RequestTimeout = TimeSpan.FromMinutes(10);
    public static float Wd14Threshold = 0.2f;
    public static float Wd14CharacterThreshold = 0.85f;
    static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    static readonly string clientId = Guid.NewGuid().ToString();
    static readonly Regex leftoverPlaceholder = new Regex(@"\$[A-Za-z_][A-Za-z0-9_]*\$");

    public static string WorkflowPath { get { return $"{scr_System_Serializer.AssetPacksPath}/{WorkflowFileName}"; } }
    public static string OutputFolder { get { return $"{scr_System_Serializer.AssetPacksPath}/{OutputFolderName}"; } }

    /// <summary> the request in flight (one at a time: the output file names are fixed) </summary>
    public static ComfyUISceneRequest Current { get; private set; }
    /// <summary> raised once per request, Done or Failed, on the main thread </summary>
    public static event Action<ComfyUISceneRequest> Completed;

    /// <summary> $actors_complex$: actors whose own prompt (short tag not counted) has at least this many tags; fewer = $actors_simple$ </summary>
    public const int ComplexActorMinTags = 10;

    /// <summary>
    /// $composition$ $scene$ $location$ = those sections; $actions$ = one entry per interaction section, $actors$ = one entry per actor
    /// section starting with its short tag, all joined with ||; $mainActors$ = the $actors$ entry of the main actor (the actor the scene was
    /// collected for, ActionSceneTags.mainActor), $secondaryActors$ = the other entries; $actors_complex$ = the entries with a large prompt
    /// (ComplexActorMinTags+, || list), $actors_simple$ = the smaller ones merged into one ", " prompt without duplicates (not a list);
    /// $prompt$ = the flat prompt; $seed$ = the request seed; $wd14_threshold$ / $wd14_character_threshold$ = tagger thresholds;
    /// $exclude_tags$ = the config's excluded_tags joined with commas.
    /// </summary>
    public static Dictionary<string, string> BuildPlaceholders(ActionSceneTags t, ActionScenePrompt p, long seed)
    {
        string Join(ScenePromptSectionKind kind) { return string.Join(", ", p.sections.Where(s => s.kind == kind).SelectMany(s => s.tags)); }
        var actions = p.sections.Where(s => s.kind == ScenePromptSectionKind.interaction).Select(s => string.Join(", ", s.tags));
        var actors = new List<string>();
        var mainActors = new List<string>();
        var secondaryActors = new List<string>();
        var complexActors = new List<string>();
        var simpleActors = new List<string>();
        foreach (var s in p.sections.Where(s => s.kind == ScenePromptSectionKind.doer || s.kind == ScenePromptSectionKind.receiver))
        {
            var shortTag = t?.actors.Find(a => a.character == s.actor)?.shortTag;
            var own = s.tags.Where(x => !string.Equals(x, shortTag, StringComparison.OrdinalIgnoreCase)).ToList();
            var tags = new List<string>();
            if (!string.IsNullOrEmpty(shortTag)) tags.Add(shortTag);
            tags.AddRange(own);
            var entry = string.Join(", ", tags);
            actors.Add(entry);
            (t != null && s.actor == t.mainActor ? mainActors : secondaryActors).Add(entry);
            if (own.Count >= ComplexActorMinTags) complexActors.Add(entry);
            else foreach (var tag in tags) if (!simpleActors.Contains(tag)) simpleActors.Add(tag);
        }
        return new Dictionary<string, string>
        {
            ["$composition$"] = Join(ScenePromptSectionKind.composition),
            ["$scene$"] = Join(ScenePromptSectionKind.scene),
            ["$location$"] = Join(ScenePromptSectionKind.room),
            ["$actions$"] = string.Join(ListSeparator, actions),
            ["$actors$"] = string.Join(ListSeparator, actors),
            ["$mainActors$"] = string.Join(ListSeparator, mainActors),
            ["$secondaryActors$"] = string.Join(ListSeparator, secondaryActors),
            ["$actors_complex$"] = string.Join(ListSeparator, complexActors),
            ["$actors_simple$"] = string.Join(", ", simpleActors),
            ["$prompt$"] = p.Flatten(),
            ["$seed$"] = seed.ToString(),
            ["$wd14_threshold$"] = Wd14Threshold.ToString(CultureInfo.InvariantCulture),
            ["$wd14_character_threshold$"] = Wd14CharacterThreshold.ToString(CultureInfo.InvariantCulture),
            ["$exclude_tags$"] = string.Join(",", ImageRulesJsonLoader.ReadExcludedTags(GlobalPortraitTemplate.ConfigPath)),
        };
    }

    /// <summary> console: fire and log </summary>
    public static async void GenerateFromConsole(Character_Trainable mainActor)
    {
        var request = await GenerateAsync(mainActor);
        if (request.state == ComfyUISceneRequest.State.Done)
            Debug.Log($"ComfyUISceneImage: done [{request.requestId}] {request.imagePaths.Count} image(s):\n{string.Join("\n", request.imagePaths)}\n{request.ToJson().ToString(Formatting.Indented)}");
        else
            Debug.LogError($"ComfyUISceneImage: failed [{request.requestId}]: {request.error}");
    }

    /// <summary> builds the scene prompt of mainActor, runs the workflow and saves the image batch. Never throws: see request.state / error </summary>
    public static async Task<ComfyUISceneRequest> GenerateAsync(Character_Trainable mainActor)
    {
        var request = new ComfyUISceneRequest { mainActor = mainActor };
        if (Current != null)
        {
            request.state = ComfyUISceneRequest.State.Failed;
            request.error = $"request [{Current.requestId}] is still running";
            return request;
        }
        Current = request;
        try
        {
            if (mainActor == null) throw new InvalidOperationException("main actor null");
            // game data: read here, on the main thread, before the first await
            request.tags = ActionScenePromptUtility.Collect(mainActor);
            request.prompt = ActionScenePromptUtility.Build(request.tags);
            request.seed = UnityEngine.Random.Range(0, int.MaxValue);
            request.placeholders = BuildPlaceholders(request.tags, request.prompt, request.seed);

            var path = WorkflowPath;
            if (!File.Exists(path)) throw new FileNotFoundException($"workflow not found at [{path}] (export it from ComfyUI with File > Export (API))");
            var graph = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
            var leftovers = new HashSet<string>();
            ReplacePlaceholders(graph, request.placeholders, leftovers);
            if (leftovers.Count > 0) Debug.LogWarning($"ComfyUISceneImage: unknown placeholders in the workflow: {string.Join(", ", leftovers)}");

            request.promptId = await QueuePromptAsync(graph);
            request.state = ComfyUISceneRequest.State.Queued;
            var entry = await WaitForHistoryAsync(request.promptId);

            var images = PickImages(graph, entry);
            if (images.Count == 0) throw new InvalidOperationException($"the workflow produced no image (no image output, or none titled {OutputNodeTitle})");

            Directory.CreateDirectory(OutputFolder);
            ClearPreviousOutput();
            for (int i = 0; i < images.Count; i++)
            {
                var img = images[i];
                var filename = (string)img["filename"];
                var url = $"{BaseUrl}/view?filename={Uri.EscapeDataString(filename ?? "")}&subfolder={Uri.EscapeDataString((string)img["subfolder"] ?? "")}&type={Uri.EscapeDataString((string)img["type"] ?? "output")}";
                var bytes = await http.GetByteArrayAsync(url);
                var ext = Path.GetExtension(filename);
                if (string.IsNullOrEmpty(ext)) ext = ".png";
                var outPath = Path.Combine(OutputFolder, $"{OutputFileName}_{i}{ext}");
                File.WriteAllBytes(outPath, bytes);
                request.imagePaths.Add(outPath);
            }
            request.state = ComfyUISceneRequest.State.Done;
        }
        catch (Exception e)
        {
            request.state = ComfyUISceneRequest.State.Failed;
            request.error = e is HttpRequestException ? $"ComfyUI not reachable at {BaseUrl}: {e.Message}" : e.Message;
        }
        finally
        {
            Current = null;
        }

        try
        {
            if (request.state == ComfyUISceneRequest.State.Done)
                File.WriteAllText(Path.Combine(OutputFolder, OutputFileName + ".json"), request.ToJson().ToString(Formatting.Indented), Encoding.UTF8);
        }
        catch (Exception e) { Debug.LogWarning($"ComfyUISceneImage: cannot write {OutputFileName}.json: {e.Message}"); }

        try { Completed?.Invoke(request); }
        catch (Exception e) { Debug.LogError($"ComfyUISceneImage: Completed handler failed: {e}"); }
        return request;
    }

    /// <summary> the previous batch may have had more images: drop every temp_N.* and temp.json </summary>
    static void ClearPreviousOutput()
    {
        foreach (var file in Directory.GetFiles(OutputFolder, OutputFileName + "*"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name == OutputFileName || name.StartsWith(OutputFileName + "_"))
            {
                try { File.Delete(file); } catch (Exception) { }
            }
        }
    }

    /// <summary> every string value of the graph: each $name$ replaced by its text (spliced into longer strings); unknown $name$ collected </summary>
    static void ReplacePlaceholders(JToken token, Dictionary<string, string> values, HashSet<string> leftovers)
    {
        if (token is JObject o)
        {
            foreach (var prop in o.Properties().ToList()) prop.Value = ReplaceValue(prop.Value, values, leftovers);
        }
        else if (token is JArray a)
        {
            for (int i = 0; i < a.Count; i++) a[i] = ReplaceValue(a[i], values, leftovers);
        }
    }

    static JToken ReplaceValue(JToken value, Dictionary<string, string> values, HashSet<string> leftovers)
    {
        if (value.Type != JTokenType.String)
        {
            ReplacePlaceholders(value, values, leftovers);
            return value;
        }
        var text = (string)value;
        if (text.IndexOf('$') < 0) return value;
        foreach (var kv in values) if (text.Contains(kv.Key)) text = text.Replace(kv.Key, kv.Value ?? "");
        foreach (Match m in leftoverPlaceholder.Matches(text)) leftovers.Add(m.Value);
        return new JValue(text);
    }

    static async Task<string> QueuePromptAsync(JObject graph)
    {
        var body = new JObject { ["prompt"] = graph, ["client_id"] = clientId };
        using var content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync($"{BaseUrl}/prompt", content);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"ComfyUI rejected the workflow ({(int)response.StatusCode}): {text}");
        var id = (string)JObject.Parse(text)["prompt_id"];
        if (string.IsNullOrEmpty(id)) throw new InvalidOperationException($"ComfyUI returned no prompt_id: {text}");
        return id;
    }

    static async Task<JObject> WaitForHistoryAsync(string promptId)
    {
        var deadline = DateTime.UtcNow + RequestTimeout;
        while (true)
        {
            var text = await http.GetStringAsync($"{BaseUrl}/history/{promptId}");
            if (JObject.Parse(text)[promptId] is JObject entry)
            {
                var status = entry["status"] as JObject;
                if (string.Equals((string)status?["status_str"], "error", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"ComfyUI failed the workflow: {status["messages"]?.ToString(Formatting.None)}");
                return entry;
            }
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"ComfyUI did not finish prompt {promptId} within {RequestTimeout.TotalMinutes} min");
            await Task.Delay(PollInterval);
        }
    }

    /// <summary> images ({ filename, subfolder, type }) of the node titled MisteraOutput, else of the first output node with images </summary>
    static List<JObject> PickImages(JObject graph, JObject historyEntry)
    {
        var outputs = historyEntry["outputs"] as JObject;
        if (outputs == null) return new List<JObject>();
        JArray chosen = null;
        foreach (var prop in outputs.Properties())
        {
            if (!(prop.Value?["images"] is JArray images) || images.Count == 0) continue;
            var title = (string)graph[prop.Name]?["_meta"]?["title"];
            if (string.Equals(title, OutputNodeTitle, StringComparison.Ordinal)) { chosen = images; break; }
            if (chosen == null) chosen = images;
        }
        return chosen?.OfType<JObject>().ToList() ?? new List<JObject>();
    }
}

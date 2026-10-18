using UnityEditor;

/// <summary>
/// Image classifier tool settings, stored per machine in EditorPrefs.
/// Only tool / connection settings live here. Classification logic (categories, tags, names, thresholds)
/// lives in the rules JSON (see Docs/ImagePortraits/Plan_ImageClassifier.md, decision 13).
/// </summary>
public static class ImageClassifierSettings
{
    const string Prefix = "MistEra.ImageClassifier.";

    public const string DefaultWorkflowPath = "Assets/Editor/ImageClassifier/TaggerWorkflow_API.json";
    public const string DefaultRulesPath = "Assets/Image_Generation_Config.json";
    /// <summary>Where the rules lived before they moved into Image_Generation_Config.json (P3.5); a stored setting with this value is upgraded.</summary>
    const string OldDraftRulesPath = "workingdir/Plans/Draft_ImageRules.json";
    public const string TaggerNodeClass = "WD14TaggerScoredPath";
    public const string EmotionNodeClass = "MisteraFaceEmotion";
    public const string DefaultEmotionWorkflowPath = "Assets/Editor/ImageClassifier/EmotionWorkflow_API.json";

    /// <summary>API-format template for the emotion pass (MisteraFaceEmotion node): face model, tagger, VLM files.
    /// Labels and prompt come from the rules JSON (emotion_pass).</summary>
    public static string EmotionWorkflowPath
    {
        get => EditorPrefs.GetString(Prefix + "EmotionWorkflowPath", DefaultEmotionWorkflowPath);
        set => EditorPrefs.SetString(Prefix + "EmotionWorkflowPath", value);
    }

    /// <summary>Images per emotion pass request (each needs a face crop, a WD14 pass and a VLM answer).</summary>
    public static int EmotionBatchSize
    {
        get => EditorPrefs.GetInt(Prefix + "EmotionBatchSize", 8);
        set => EditorPrefs.SetInt(Prefix + "EmotionBatchSize", value);
    }

    public static string LastFolder
    {
        get => EditorPrefs.GetString(Prefix + "LastFolder", "");
        set => EditorPrefs.SetString(Prefix + "LastFolder", value);
    }

    public static string ComfyUrl
    {
        get => EditorPrefs.GetString(Prefix + "ComfyUrl", "http://127.0.0.1:8188");
        set => EditorPrefs.SetString(Prefix + "ComfyUrl", value);
    }

    /// <summary>API-format workflow template; the tool patches the WD14TaggerScoredPath node's inputs.</summary>
    public static string WorkflowPath
    {
        get => EditorPrefs.GetString(Prefix + "WorkflowPath", DefaultWorkflowPath);
        set => EditorPrefs.SetString(Prefix + "WorkflowPath", value);
    }

    /// <summary>Rules JSON (relative to the project root or absolute): Assets/Image_Generation_Config.json.</summary>
    public static string RulesPath
    {
        get
        {
            var path = EditorPrefs.GetString(Prefix + "RulesPath", DefaultRulesPath);
            return path.Replace('\\', '/') == OldDraftRulesPath ? DefaultRulesPath : path;
        }
        set => EditorPrefs.SetString(Prefix + "RulesPath", value);
    }

    public static string Model
    {
        get => EditorPrefs.GetString(Prefix + "Model", "wd-eva02-large-tagger-v3");
        set => EditorPrefs.SetString(Prefix + "Model", value);
    }

    /// <summary>Tagging threshold. Kept low so Analyze can re-threshold from the stored scores without re-tagging.</summary>
    public static float TagThreshold
    {
        get => EditorPrefs.GetFloat(Prefix + "TagThreshold", 0.2f);
        set => EditorPrefs.SetFloat(Prefix + "TagThreshold", value);
    }

    public static float CharacterThreshold
    {
        get => EditorPrefs.GetFloat(Prefix + "CharacterThreshold", 0.85f);
        set => EditorPrefs.SetFloat(Prefix + "CharacterThreshold", value);
    }

    /// <summary>Folder with the WD14 models (&lt;model&gt;.onnx + &lt;model&gt;.csv), e.g. ComfyUI/custom_nodes/ComfyUI-WD14-Tagger/models.
    /// Only used to read the tag vocabulary for the rule check; empty = skip that check.</summary>
    public static string ModelsFolder
    {
        get => EditorPrefs.GetString(Prefix + "ModelsFolder", "");
        set => EditorPrefs.SetString(Prefix + "ModelsFolder", value);
    }

    /// <summary>Image paths sent per /prompt request.</summary>
    public static int BatchSize
    {
        get => EditorPrefs.GetInt(Prefix + "BatchSize", 16);
        set => EditorPrefs.SetInt(Prefix + "BatchSize", value);
    }
}

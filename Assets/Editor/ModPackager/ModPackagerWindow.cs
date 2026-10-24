using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Lists data mods and asset packs with their last build time and source state; builds the selected ones into
/// the output folder (Docs/Mods/Plan_ModPackaging.md). Publishing is done by the runner (ci/publish_mods.ps1).
/// </summary>
public class ModPackagerWindow : EditorWindow
{
    List<ModPackageSource> sources = new List<ModPackageSource>();
    ModPackageManifest manifest;
    readonly HashSet<string> selected = new HashSet<string>();
    readonly HashSet<string> selectedOrphans = new HashSet<string>();
    bool showSettings;
    bool editNames;
    Vector2 scroll;
    string lastResult;

    static readonly GUIContent[] Header =
    {
        new GUIContent(""), new GUIContent("Package"), new GUIContent("Archive name"), new GUIContent("Files"),
        new GUIContent("Source size"), new GUIContent("Last built"), new GUIContent("Source modified"), new GUIContent("Status"),
    };
    static readonly float[] ColumnWidths = { 20, 220, 200, 50, 80, 130, 130, 110 };

    [MenuItem("Tools/Mod Packager")]
    static void Open()
    {
        var window = GetWindow<ModPackagerWindow>("Mod Packager");
        window.minSize = new Vector2(760, 360);
    }

    void OnEnable() => Refresh();

    void Refresh()
    {
        sources = ModPackager.Discover();
        manifest = ModPackager.LoadManifest();
        var ids = new HashSet<string>(sources.Select(s => s.id));
        selected.RemoveWhere(id => !ids.Contains(id));
        selectedOrphans.RemoveWhere(id => manifest == null || manifest.packages.All(p => p.id != id));
    }

    ModPackageEntry EntryOf(string id) => manifest?.packages.FirstOrDefault(p => p.id == id);

    List<ModPackageEntry> Orphans() =>
        manifest == null ? new List<ModPackageEntry>() : manifest.packages.Where(p => sources.All(s => s.id != p.id)).ToList();

    void OnGUI()
    {
        DrawSettings();
        if (manifest == null)
        {
            EditorGUILayout.HelpBox($"{ModPackager.ManifestPath} could not be read (see console). Fix or remove it, then Refresh.", MessageType.Error);
            if (GUILayout.Button("Refresh")) Refresh();
            return;
        }
        DrawToolbar();
        if (!string.IsNullOrEmpty(lastResult)) EditorGUILayout.HelpBox(lastResult, MessageType.Info);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawHeader();
        DrawSection("Data mods", sources.Where(s => s.kind == ModPackageKind.Mod).ToList());
        DrawSection("Asset packs", sources.Where(s => s.kind == ModPackageKind.AssetPack).ToList());
        DrawOrphans();
        EditorGUILayout.EndScrollView();
    }

    void DrawSettings()
    {
        showSettings = EditorGUILayout.Foldout(showSettings, "Settings", true);
        if (!showSettings) return;
        using (new EditorGUI.IndentLevelScope())
        {
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                string output = EditorGUILayout.TextField("Output folder", ModPackagerSettings.OutputFolder);
                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    var picked = EditorUtility.OpenFolderPanel("Mod Packager output folder", ModPackagerSettings.OutputFolder, "");
                    if (!string.IsNullOrEmpty(picked)) output = picked;
                }
                ModPackagerSettings.OutputFolder = output;
            }
            ModPackagerSettings.SevenZipPath = EditorGUILayout.TextField("7z.exe", ModPackagerSettings.SevenZipPath);
            ModPackagerSettings.Password = EditorGUILayout.TextField("Archive password", ModPackagerSettings.Password);
            editNames = EditorGUILayout.Toggle(new GUIContent("Edit archive names", "Set an alias per package; empty = automatic ASCII name"), editNames);
            if (EditorGUI.EndChangeCheck()) manifest = ModPackager.LoadManifest();
        }
        EditorGUILayout.Space();
    }

    void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Refresh")) Refresh();
            if (GUILayout.Button("Select changed/new"))
                foreach (var s in sources)
                    if (s.errors.Count == 0 && ModPackager.GetStatus(s, EntryOf(s.id)) != ModPackageStatus.UpToDate) selected.Add(s.id);
            if (GUILayout.Button("Select none")) selected.Clear();
            using (new EditorGUI.DisabledScope(selected.Count == 0))
                if (GUILayout.Button($"Build selected ({selected.Count})")) BuildSelected();
            if (GUILayout.Button("Open output folder"))
            {
                Directory.CreateDirectory(ModPackagerSettings.OutputFolder);
                EditorUtility.RevealInFinder(ModPackagerSettings.OutputFolder + Path.DirectorySeparatorChar);
            }
        }
    }

    void DrawHeader()
    {
        using (new EditorGUILayout.HorizontalScope())
            for (int i = 0; i < Header.Length; i++)
                GUILayout.Label(Header[i], EditorStyles.miniBoldLabel, GUILayout.Width(ColumnWidths[i]));
    }

    void DrawSection(string title, List<ModPackageSource> list)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"{title} ({list.Count})", EditorStyles.boldLabel);
        string lastGroup = null;
        foreach (var src in list)
        {
            if (src.isSplit && src.group != lastGroup)
                EditorGUILayout.LabelField($"{src.group}  (split into {list.Count(s => s.group == src.group)})", EditorStyles.miniBoldLabel);
            lastGroup = src.group;
            DrawRow(src);
        }
    }

    void DrawRow(ModPackageSource src)
    {
        var entry = EntryOf(src.id);
        var status = ModPackager.GetStatus(src, entry);
        bool blocked = src.errors.Count > 0;

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(blocked))
            {
                bool on = EditorGUILayout.Toggle(selected.Contains(src.id) && !blocked, GUILayout.Width(ColumnWidths[0]));
                if (on) selected.Add(src.id); else selected.Remove(src.id);
            }
            string label = src.isSplit ? "    " + src.id : src.id;
            GUILayout.Label(new GUIContent(label, $"Mods/{string.Join("\nMods/", src.roots)}"), GUILayout.Width(ColumnWidths[1]));
            if (editNames)
            {
                string alias = EditorGUILayout.DelayedTextField(ModPackagerSettings.GetAlias(src.id) ?? "", GUILayout.Width(ColumnWidths[2]));
                if (alias != (ModPackagerSettings.GetAlias(src.id) ?? "")) ModPackagerSettings.SetAlias(src.id, alias);
            }
            else GUILayout.Label(ModPackager.AssetName(src.id), EditorStyles.miniLabel, GUILayout.Width(ColumnWidths[2]));
            GUILayout.Label(src.files.Count.ToString(), GUILayout.Width(ColumnWidths[3]));
            GUILayout.Label(ModPackager.FormatSize(src.size), GUILayout.Width(ColumnWidths[4]));
            GUILayout.Label(entry == null ? "-" : LocalTime(entry.builtAt), GUILayout.Width(ColumnWidths[5]));
            GUILayout.Label(src.files.Count == 0 ? "-" : src.latestWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), GUILayout.Width(ColumnWidths[6]));
            DrawStatus(blocked ? "Error" : StatusText(status), blocked ? Color.red : StatusColor(status), ColumnWidths[7]);
        }
        foreach (var e in src.errors) EditorGUILayout.HelpBox(e, MessageType.Error);
        foreach (var w in src.warnings) EditorGUILayout.HelpBox(w, MessageType.Warning);
    }

    void DrawOrphans()
    {
        var orphans = Orphans();
        if (orphans.Count == 0) return;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"Orphans ({orphans.Count}) — built packages whose source no longer exists", EditorStyles.boldLabel);
        foreach (var o in orphans)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool on = EditorGUILayout.Toggle(selectedOrphans.Contains(o.id), GUILayout.Width(ColumnWidths[0]));
                if (on) selectedOrphans.Add(o.id); else selectedOrphans.Remove(o.id);
                GUILayout.Label(new GUIContent(o.id, $"Mods/{o.source}"), GUILayout.Width(ColumnWidths[1]));
                GUILayout.Label(o.asset, EditorStyles.miniLabel, GUILayout.Width(ColumnWidths[2]));
                GUILayout.Label(o.fileCount.ToString(), GUILayout.Width(ColumnWidths[3]));
                GUILayout.Label(ModPackager.FormatSize(o.size), GUILayout.Width(ColumnWidths[4]));
                GUILayout.Label(LocalTime(o.builtAt), GUILayout.Width(ColumnWidths[5]));
                GUILayout.Label("-", GUILayout.Width(ColumnWidths[6]));
                DrawStatus("Orphan", new Color(1f, 0.6f, 0.2f), ColumnWidths[7]);
            }
        }
        using (new EditorGUI.DisabledScope(selectedOrphans.Count == 0))
        {
            if (GUILayout.Button($"Retire selected orphans ({selectedOrphans.Count})", GUILayout.Width(260))
                && EditorUtility.DisplayDialog("Retire packages",
                    $"Delete these archives and their manifest entries? The next publish removes them from the release.\n\n{string.Join("\n", selectedOrphans)}",
                    "Retire", "Cancel"))
            {
                foreach (var id in selectedOrphans.ToList()) ModPackager.Retire(manifest, id);
                lastResult = $"Retired {selectedOrphans.Count} package(s).";
                selectedOrphans.Clear();
                Refresh();
            }
        }
    }

    void BuildSelected()
    {
        var todo = sources.Where(s => selected.Contains(s.id)).ToList();
        var failed = new List<string>();
        int built = 0;
        try
        {
            for (int i = 0; i < todo.Count; i++)
            {
                var src = todo[i];
                if (EditorUtility.DisplayCancelableProgressBar("Mod Packager",
                        $"Packing {src.id} ({i + 1}/{todo.Count}, {ModPackager.FormatSize(src.size)})", i / (float)todo.Count))
                    break;
                string error = ModPackager.Build(src, manifest);
                if (error == null) { built++; selected.Remove(src.id); }
                else failed.Add($"{src.id}: {error}");
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Refresh();
        }
        lastResult = $"Built {built}/{todo.Count} package(s)." + (failed.Count > 0 ? "\nFailed:\n" + string.Join("\n", failed) : "");
        if (failed.Count > 0) Debug.LogError("Mod Packager: " + lastResult);
    }

    static void DrawStatus(string text, Color color, float width)
    {
        var old = GUI.contentColor;
        GUI.contentColor = color;
        GUILayout.Label(text, EditorStyles.boldLabel, GUILayout.Width(width));
        GUI.contentColor = old;
    }

    static string StatusText(ModPackageStatus status) => status switch
    {
        ModPackageStatus.New => "New",
        ModPackageStatus.UpToDate => "Up to date",
        ModPackageStatus.SourceChanged => "Source changed",
        ModPackageStatus.Renamed => "Name changed",
        ModPackageStatus.ArchiveMissing => "Archive missing",
        _ => status.ToString(),
    };

    static Color StatusColor(ModPackageStatus status) => status switch
    {
        ModPackageStatus.UpToDate => new Color(0.4f, 0.85f, 0.4f),
        ModPackageStatus.New => new Color(0.4f, 0.7f, 1f),
        _ => new Color(1f, 0.8f, 0.3f),
    };

    static string LocalTime(string utc)
    {
        var t = ModPackager.ParseUtc(utc);
        return t == DateTime.MinValue ? "-" : t.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }
}

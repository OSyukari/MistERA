using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;

[CustomEditor(typeof(scr_System_Serializer))]
public class Editor_scr_System_Serializer : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(10);
        if (GUILayout.Button("Save zh-cn Snapshot"))
        {
            if (LocalizeDictionary.Instance == null)
            {
                Debug.LogWarning("[SourceTracking] LocalizeDictionary not loaded. Enter Play mode and load data first.");
                return;
            }

            var zhCn = LocalizeDictionary.Instance.Index.Entries["zh-cn"];
            string snapshotPath = Application.dataPath + "/dict_zh_cn_snapshot.json";
            File.WriteAllText(snapshotPath, JsonConvert.SerializeObject(zhCn, Formatting.Indented));
            Debug.Log($"[SourceTracking] Snapshot saved with {zhCn.Count} entries to {snapshotPath}");
        }
    }
}
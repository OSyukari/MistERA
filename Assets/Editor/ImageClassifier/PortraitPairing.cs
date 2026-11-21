using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Character pairing view of the image report: the data the page needs to resolve, per command, which portrait handler variant
/// (and so which images) a character shows as doer or receiver with a partner - the same steps as PortraitManager.GetValidPortrait over
/// the global handlers (scr_System_CentralControl.FillGlobalPortraitHandlers), evaluated in the page against the "portrait" view data.
/// Characters come from characterDBs.json (CharacterDatabaseExporter: actor_tags as exported, global_handlers = useGlobalHandler, which
/// MasterList.json never holds); commands from MasterList.json. Static approximation: no runtime state beyond the exported actor tags.
/// </summary>
public static class PortraitPairing
{
    /// <summary>
    /// Writes the "pairing" value of the report JSON:
    /// { masterList, characterDb, errors: [string], states: [[state, [keywords]]] (image_states, priority order),
    ///   characters: [{ id (portrait template id), name, tags (actor_tags), handlers: [{ path, self, target, fallback }] | null (not exported) }],
    ///   coms: [{ id, tags (comTags), pt, ptd, ptr (portraitTags, _Doer, _Receiver), solo (selfOnly) }] }
    /// </summary>
    public static void Write(JsonTextWriter w, string rulesPath, string masterListPath, string characterDbPath)
    {
        var errors = new List<string>();
        w.WriteStartObject();
        w.WritePropertyName("masterList"); w.WriteValue(masterListPath);
        w.WritePropertyName("characterDb"); w.WriteValue(characterDbPath);

        w.WritePropertyName("states");
        w.WriteStartArray();
        try
        {
            if (JObject.Parse(File.ReadAllText(rulesPath, Encoding.UTF8))["image_states"] is JObject states)
                foreach (var p in states.Properties())
                {
                    if (p.Name.StartsWith("_") || !(p.Value is JObject state)) continue;
                    w.WriteStartArray();
                    w.WriteValue(p.Name);
                    WriteStrings(w, state["keywords"]);
                    w.WriteEndArray();
                }
        }
        catch (Exception e) { errors.Add("image_states not read from " + rulesPath + ": " + e.Message); }
        w.WriteEndArray();

        w.WritePropertyName("characters");
        w.WriteStartArray();
        try
        {
            if (!File.Exists(characterDbPath)) throw new FileNotFoundException("file not found");
            var db = JObject.Parse(File.ReadAllText(characterDbPath, Encoding.UTF8));
            foreach (var p in db.Properties())
            {
                if (!(p.Value is JObject c)) continue;
                w.WriteStartObject();
                w.WritePropertyName("id"); w.WriteValue(p.Name);
                w.WritePropertyName("name"); w.WriteValue((string)c["name"] ?? p.Name);
                w.WritePropertyName("tags"); WriteStrings(w, c["actor_tags"]);
                w.WritePropertyName("handlers");
                if (c["global_handlers"] is JArray handlers)
                {
                    w.WriteStartArray();
                    foreach (var h in handlers)
                    {
                        if (!(h is JObject e) || string.IsNullOrEmpty((string)e["path"])) continue;
                        w.WriteStartObject();
                        w.WritePropertyName("path"); w.WriteValue((string)e["path"]);
                        w.WritePropertyName("self"); WriteStrings(w, e["selfKeys"]);
                        w.WritePropertyName("target"); WriteStrings(w, e["targetKeys"]);
                        w.WritePropertyName("fallback"); w.WriteValue((string)e["fallback"] ?? "");
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }
                else w.WriteNull();
                w.WriteEndObject();
            }
        }
        catch (Exception e) { errors.Add("characters not read from " + characterDbPath + ": " + e.Message); }
        w.WriteEndArray();

        w.WritePropertyName("coms");
        w.WriteStartArray();
        try
        {
            var coms = ReadComs(masterListPath);
            if (coms == null) throw new InvalidDataException("no COMs.list");
            foreach (var token in coms)
            {
                if (!(token is JObject c) || string.IsNullOrEmpty((string)c["ID"])) continue;
                w.WriteStartObject();
                w.WritePropertyName("id"); w.WriteValue((string)c["ID"]);
                w.WritePropertyName("tags"); WriteStrings(w, c["comTags"]);
                w.WritePropertyName("pt"); WriteStrings(w, c["portraitTags"]);
                w.WritePropertyName("ptd"); WriteStrings(w, c["portraitTags_Doer"]);
                w.WritePropertyName("ptr"); WriteStrings(w, c["portraitTags_Receiver"]);
                w.WritePropertyName("solo"); w.WriteValue(c["selfOnly"]?.Type == JTokenType.Boolean && (bool)c["selfOnly"]);
                w.WriteEndObject();
            }
        }
        catch (Exception e) { errors.Add("commands not read from " + masterListPath + ": " + e.Message); }
        w.WriteEndArray();

        w.WritePropertyName("errors");
        w.WriteStartArray();
        for (int i = 0; i < errors.Count; i++) w.WriteValue(errors[i]);
        w.WriteEndArray();
        w.WriteEndObject();
    }

    /// <summary>MasterList.json "COMs.list", streamed: the dump is tens of MB and only the commands are needed.</summary>
    static JArray ReadComs(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("file not found");
        using (var sr = new StreamReader(path, Encoding.UTF8))
        using (var r = new JsonTextReader(sr))
        {
            while (r.Read())
            {
                if (r.TokenType != JsonToken.PropertyName || r.Path != "COMs") continue;
                if (!r.Read()) return null;
                return JToken.ReadFrom(r)["list"] as JArray;
            }
        }
        return null;
    }

    /// <summary>A string array token as written strings (trimmed, empty skipped); anything else = [].</summary>
    static void WriteStrings(JsonTextWriter w, JToken token)
    {
        w.WriteStartArray();
        if (token is JArray array)
            foreach (var v in array)
            {
                var s = v.Type == JTokenType.String ? ((string)v).Trim() : null;
                if (!string.IsNullOrEmpty(s)) w.WriteValue(s);
            }
        w.WriteEndArray();
    }
}

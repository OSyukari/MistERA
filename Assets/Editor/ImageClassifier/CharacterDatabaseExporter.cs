using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class CharacterDatabaseExporter
{
    const string MenuPath = "Tools/MistEra/Export Room Character Prompts";

    [MenuItem(MenuPath, true)]
    static bool CanExport()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling
            || scr_System_Serializer.current?.MasterList == null) return false;
        var campaign = scr_System_CampaignManager.current;
        return campaign != null && campaign.Map != null && campaign.LoadedCharacterIDs.Length > 0
            && campaign.CurrentCampaign != null && campaign.FindInstanceByID(0) != null;
    }

    [MenuItem(MenuPath)]
    static void Export()
    {
        if (!CanExport()) return;
        try
        {
            var campaign = scr_System_CampaignManager.current;
            var player = campaign.FindInstanceByID(0);
            var room = campaign.Map.FindRoomByChara(player.RefID);
            if (room == null) return;
            var roomIDs = new HashSet<int>(room.RoomCharaRefs) { player.RefID };
            var viewed = campaign.CurrentTargetEX ?? campaign.CurrentTarget;
            // A viewed room character wins when several instances share one portrait-template key.
            var targets = campaign.LoadedCharacterIDs.Where(roomIDs.Contains)
                .Select(id => campaign.FindInstanceByID(id)).Where(character => character != null)
                .OrderBy(character => character.RefID == viewed?.RefID ? 1 : 0).ThenBy(character => character.RefID).ToList();
            string path = Path.Combine(scr_System_Serializer.AssetPacksPath, "characterDBs.json");
            var database = File.Exists(path)
                ? JObject.Parse(File.ReadAllText(path, Encoding.UTF8), new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error })
                : new JObject();
            int exported = 0;
            var updated = new HashSet<string>();
            foreach (var target in targets)
            {
                var baseID = UpsertCharacter(database, target);
                if (baseID == null) continue;
                exported++;
                updated.Add(baseID);
            }
            if (exported == 0) return;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, database.ToString(Formatting.Indented) + Environment.NewLine, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            Debug.Log($"Character DB: exported {exported} characters in {room.DisplayNameShort}; updated {updated.Count} portrait-template entries at {path}");
        }
        catch (Exception exception)
        {
            Debug.LogError($"Character DB export failed; existing entries were not overwritten with partial data: {exception}");
        }
    }

    static string UpsertCharacter(JObject database, Character_Trainable target)
    {
        string baseID = target.PortraitManager?.PortraitTemplateID;
        if (string.IsNullOrWhiteSpace(baseID))
        {
            Debug.LogWarning($"Character DB: {target.FullName} has no portrait template ID; skipped.");
            return null;
        }

        // Keep pose-dependent details deferred; exporting naked must not force them into every composition.
        var clothedGroups = new Dictionary<string, List<string>>();
        var nakedGroups = new Dictionary<string, List<string>>();
        var clothedRules = new List<ActorOptionalPromptRule>();
        var nakedRules = new List<ActorOptionalPromptRule>();
        var clothed = LLMUtils.CollectActorTags(target, false, out _, promptGroups: clothedGroups, optionalRules: clothedRules);
        var naked = LLMUtils.CollectActorTags(target, false, out _, promptGroups: nakedGroups, ignoreClothing: true, optionalRules: nakedRules);
        string shortTag = LLMUtils.GetShortTag(target);
        // Match actors_simple's threshold against the complete evaluated prompt, not the partial export base.
        var supportFull = LLMUtils.CollectActorTags(target, false, out _, promptGroups: new Dictionary<string, List<string>>());
        var own = supportFull.Where(tag => !string.Equals(tag, shortTag, StringComparison.OrdinalIgnoreCase)).ToList();
        var support = own.Count > ComfyUISceneImage.ComplexActorMinTags
            ? LLMUtils.CollectShortPrompt(target) : WithShortTag(shortTag, own);
        if (shortTag == ActionScenePromptUtility.Short_Girl || shortTag == ActionScenePromptUtility.Short_Boy
            || shortTag == ActionScenePromptUtility.Short_Futa)
            if (!naked.Contains("nude", StringComparer.OrdinalIgnoreCase)) naked.Add("nude");

        var entry = database[baseID] as JObject ?? new JObject();
        string raceName = target.Race?.DisplayName;
        entry["name"] = string.IsNullOrWhiteSpace(raceName)
            ? target.FullName : $"{target.FullName}\uFF08{raceName}\uFF09";
        entry["short_tag"] = shortTag;
        entry["short_name"] = LLMUtils.GetShortName(target);
        entry["short_prompt"] = string.Join(", ", LLMUtils.CollectShortPrompt(target));
        entry["support_prompt"] = string.Join(", ", support);
        entry["main_naked_prompt"] = string.Join(", ", WithShortTag(shortTag, naked));
        entry["main_clothed_prompt"] = string.Join(", ", WithShortTag(shortTag, clothed));
        var actorTags = LLMUtils.CollectActorPropertyTags(target);
        actorTags.Add(shortTag);
        entry["actor_tags"] = new JArray(actorTags.Distinct(StringComparer.OrdinalIgnoreCase));
        entry["optional_prompts"] = new JObject
        {
            ["naked"] = OptionalSnapshot(nakedGroups, nakedRules),
            ["clothed"] = OptionalSnapshot(clothedGroups, clothedRules),
        };
        if (database.Property(baseID) != null)
            database[baseID] = entry;
        else
        {
            var property = new JProperty(baseID, entry);
            var next = database.Properties().FirstOrDefault(existing => StringComparer.Ordinal.Compare(baseID, existing.Name) < 0);
            if (next == null) database.Add(property);
            else next.AddBeforeSelf(property);
        }
        return baseID;
    }

    static JObject OptionalSnapshot(Dictionary<string, List<string>> groups, List<ActorOptionalPromptRule> rules)
    {
        var result = new JObject();
        foreach (var group in groups)
            if (group.Value.Count > 0)
                result[group.Key] = new JArray(new JObject { ["prompts"] = new JArray(group.Value.Distinct()) });
        foreach (var rule in rules)
            foreach (var group in (rule.promptGroups.Count > 0 ? rule.promptGroups : new List<string> { "__base__" })
                .Where(group => !string.IsNullOrWhiteSpace(group)).Distinct())
            {
                var entries = result[group] as JArray;
                if (entries == null) result[group] = entries = new JArray();
                entries.Add(new JObject
                {
                    ["id"] = rule.id,
                    ["prompts"] = new JArray(rule.prompts.Distinct()),
                    ["requireActorTags"] = new JArray(rule.requireActorTags),
                    ["requireAnyActorTags"] = new JArray(rule.requireAnyActorTags),
                    ["excludeActorTags"] = new JArray(rule.excludeActorTags),
                });
            }
        return result;
    }

    static List<string> WithShortTag(string shortTag, IEnumerable<string> tags)
    {
        return new[] { shortTag }.Concat(tags.Where(tag => !string.Equals(tag, shortTag, StringComparison.OrdinalIgnoreCase))).ToList();
    }
}

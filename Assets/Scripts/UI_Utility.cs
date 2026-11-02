using System.Collections.Generic;
using UnityEngine;
using System;
using TMPro;

public static class UI_Utility
{
    
    public static void Draw(Stats_Base stat, scr_HoverableText text)
    {
        var link = "stats_base_" + stat.stat_base_string;
        var valueMod = stat.FinalValue() - stat.BaseValue;
        var str = LocalizeDictionary.QueryThenParse(link);

        text.SetText(str + " " + stat.BaseValue + (valueMod != 0 ? (valueMod).ToString("+0;-#") : ""), false, link + "_tooltip");
        text.SetExternalTooltip(stat.ModStrings());
    }

    public static void Draw(Stats_Derived_Instance stat, scr_HoverableText text)
    {
        text.SetText(stat.Parent.DisplayName + ":" + stat.FinalValue(), false, stat.Parent.ID + "_tooltip");
        text.SetExternalTooltip(stat.ModStrings());
    }

    public static void Draw(Status_Instance si, TMP_Text text)
    {
        if (si.ID.Contains("chara_status_sexual_") || si.ID.Contains("chara_status_sex_"))
        {
            string s = si.ID.Substring(si.ID.Length - 1);
            text.text = s + " (" + si.Severity + ")";
            if (si.maxed) text.color = scr_System_CentralControl.current.DisplaySetting.TextColor_maxed.Color;
            else if (si.Severity > 0) text.color = scr_System_CentralControl.current.DisplaySetting.TextColor_neutral.Color;
            else text.color = scr_System_CentralControl.current.DisplaySetting.TextColor_disabled.Color;
        }
        else
        {
            text.text = $"{si.SeverityDisplayName}({si.Severity.ToString(si.BaseRef.stringFormat)})";
        }

    }

    public static void Draw(Status_Instance si, scr_HoverableText text)
    {
        if (si.ID.Contains("chara_status_sexual_") || si.ID.Contains("chara_status_sex_"))
        {
            string s = si.ID.Substring(si.ID.Length - 2);
            text.SetText($"{si.SeverityDisplayName}({si.Severity})", false, si.BaseRef.statusID + "_tooltip");
            text.SetExternalTooltip(String.Join(" ", si.ModString) + $"\n{si.ThresholdString}");
        }
        else
        {
            text.SetText($"{si.SeverityDisplayName}({si.Severity.ToString(si.BaseRef.stringFormat)})", false, si.BaseRef.statusID + "_tooltip");
            List<string> tooltips = new List<string>();
            if (si.duration > -1) tooltips.Add($"remaining duration: {si.duration}");
            tooltips.Add($"severity: {si.Severity}, downrank in {si.TickTillDecay}, disappear in {si.TickTillExpire}");
            tooltips.AddRange(si.ModString);
            text.SetExternalTooltip(String.Join("\n", tooltips)+$"\n{si.ThresholdString}");
        }

    }


}

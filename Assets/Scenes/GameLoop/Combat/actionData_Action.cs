using UnityEngine;
using System;
using System.Collections.Generic;

public class actionData_Action : MonoBehaviour
{

    public scr_HoverableText action_name, action_speed, action_props, action_tracking, action_distance, action_result, action_result_tooltip;
    Dictionary<string,string> _cachedDict = new Dictionary<string,string>();    

    string GetString(string s)
    {
        if (_cachedDict.ContainsKey(s)) return _cachedDict[s];
        var ss = LocalizeDictionary.QueryThenParse(s);
        _cachedDict.Add(s, ss);
        return ss;
    }
    public void Start()
    {
        initialized = true;
        _action = GetString("ui_combat_preview_action");
        _speed = GetString("ui_combat_preview_speed");
        _tracking = GetString("ui_combat_preview_tracking");
        _distance = GetString("ui_combat_preview_distance");
        _result = GetString("ui_combat_preview_result");
    }
    string _action, _speed, _tracking, _distance, _result;

    bool initialized = false;
    public void Refresh(CombatActionInstance instance)
    {
        if (!initialized) Start();

        action_name.SetText(_action.Replace("$name$", instance == null || instance.actionRef == null ? " - " : instance.actionRef.Name));
        action_speed.SetText(_speed.Replace("$name$", instance == null || instance.actionRef == null ? " - " : $"{instance.BaseSpeed}{(instance.Speed - instance.BaseSpeed).ToString("+0;-#")}"));

        var attack = instance == null || instance.actionRef == null ? null : instance.actionRef as CombatAction_Attack;
        action_tracking.SetText(_tracking.Replace("$tracking$", attack == null ? " - " : $"{attack.tracking}")
                .Replace("$mov$", instance == null || instance.targetRef == null ? " - " : $"{instance.Handler.ActorStats[instance.targetRef.RefID].Evasion_Pre}"));

        action_distance.SetText(_distance.Replace("$range$", attack == null ? " - " : $"{attack.range}")
            .Replace("$distance$", attack == null || instance.targetRef == null ? " - " : $"{instance.Handler.GetCombatDistance(instance.ownerRef, instance.targetRef)}"));

        if (instance == null)
        {
            action_result.SetText(" - ");
            action_result_tooltip.SetText("");
        }
        else
        {
            action_result.SetText(_result.Replace("$result$", instance.ResultString));// $"ActionResult_{instance.Result}")$"Result: {instance.Result}");
            action_result_tooltip.SetText(instance.ResultTooltip);
        }
    }
}

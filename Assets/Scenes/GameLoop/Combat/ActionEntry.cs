using UnityEngine;
using UnityEngine.UI;
using System;

public class ActionEntry : MonoBehaviour
{

    public RectTransform SelfRect;

    public bool isHostile = false;
    public HorizontalLayoutGroup selfLayout;
    public scr_HoverableText Name, Action, Result;
    public Image selfImage;
    public scr_HoverableText additionalText;

    string s = "";
    public void Initialize(CombatActionInstance inst)
    {
        if (isHostile) selfImage.color = UtilityEX.UI_HostileColor;
        else selfImage.color = UtilityEX.UI_SelfColor;

        Name.SetText(inst.Handler.GetName(inst.ownerRef));

        Action.SetText(inst.Description);

        s = $"BaseSpeed: {inst.BaseSpeed}, Final Speed: {inst.Speed}";
        s += $"\nPrevious [{(inst.action_previous == null ? " - " : inst.action_previous.actionRef.Name)}]";
        s += $"\nSelf Prev [{(inst.self_action_previous == null ? " - " : inst.self_action_previous.actionRef.Name)}]";

        Action.SetExternalTooltip(s);

        Result.SetText($"{inst.ResultString}");
        Result.SetExternalTooltip($"{inst.ResultTooltip}");

        additionalText.SetText(inst.FinalResult);
    }
}

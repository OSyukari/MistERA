using UnityEngine;
using UnityEngine.UI;
using System;
using UnityEngine.EventSystems;

public class ActionEntry : MonoBehaviour, IPointerEnterHandler
{

    public RectTransform SelfRect;

    public bool isHostile = false;
    public HorizontalLayoutGroup selfLayout;
    public scr_HoverableText Name, Action, Result;
    public Image selfImage;
    public scr_HoverableText additionalText;

    scr_Menu_Combat parent;
    CombatActionInstance inst;
    bool pointerEnter = false;
    string s = "";
    public void Initialize(CombatActionInstance inst, scr_Menu_Combat parent, bool OnPointerEnter = false)
    {
        this.pointerEnter = OnPointerEnter;
        this.parent = parent;
        this.inst = inst;
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

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!pointerEnter) return;
//        Debug.Log("onpointenter");
        parent.LoadChara(inst.ownerRef, isHostile ? false : true);
        if (inst.isHostile) parent.LoadChara(inst.targetRef, isHostile ? true : false);

    }
}

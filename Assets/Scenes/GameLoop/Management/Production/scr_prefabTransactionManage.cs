using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class scr_prefabTransactionManage : MonoBehaviour, IPointerEnterHandler
{
    public TMP_Text FactionName;
    public scr_HoverableText ItemName;
    public TMP_Text ItemCount, OrderAmount, pricing;
    public scr_SelectableText Button_orderType, ButtonMinus, ButtonPlus, Btn_action;
    public TMP_Text warningMsg;
    public moveOrderScriptBTN moveBTN;

    public Manageable targetFaction;
    public Manageable.TradeOrder order = null;
    public CanvasGroup CanvasGroup;

    public int CurrentIndex = -1;
    public int SiblingIndex = -1;
    bool _isActive = false;
    public bool isActive
    {
        get
        {
            return _isActive;
        }
        set
        {
            if (value && parent.moveBoxScript != null)
            {
                _isActive = false;
            }
            else _isActive = value;

            CanvasGroup.alpha = _isActive ? 0.5f : 1;

            if (_isActive)
            {
                parent.moveBoxScript = moveBTN;
            }
            else if (parent.moveBoxScript == moveBTN)
            {
                parent.moveBoxScript = null;
            }
        }
    }

    public void NotifyChanged()
    {
        this.SiblingIndex = this.transform.GetSiblingIndex();
        this.CurrentIndex = targetFaction.TradeOrders.IndexOf(this.order);
    }
    public bool ActivateScript()
    {
        if (parent == null) return false;
        this.isActive = true;
        return this.isActive;
    }

    public void DeactivateScript()
    {
        if (parent == null) return;
        this.isActive = false;
    }
    scr_Canvas_Management parent;

    public void RegisterTR(scr_Canvas_Management canvas, Manageable faction, Manageable.TradeOrder orderTR)
    {
        targetFaction = faction;
        this.order = orderTR;
        this.parent = canvas;
        if (targetFaction != null && this.order != null)
        {
            CurrentIndex = faction.TradeOrders.IndexOf(this.order);
        }
        this.SiblingIndex = this.transform.GetSiblingIndex();
    }
    bool isactive = false;
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (parent == null) return;
        if (this.isActive) return;
        if (isactive) return;
        if (parent.moveBoxScript != null)
        {
            var sib = parent.moveBoxScript.SiblingIndex;
            var cur = parent.moveBoxScript.CurrentIndex;
            if (sib == SiblingIndex || cur == CurrentIndex) return;
            if (sib != -1 && cur != -1)
            {
                isactive = true;
                parent.moveBoxScript.InactiveOverride = true;

                this.SiblingIndex = sib;
                this.CurrentIndex = Math.Min(this.targetFaction.TradeOrders.Count, cur);

                this.transform.SetSiblingIndex(this.SiblingIndex);
                this.targetFaction.TradeOrders.Remove(order);
                this.targetFaction.TradeOrders.Insert(this.CurrentIndex, order);
               //NotifyChanged();
                parent.moveBoxScript.NotifyChanged();
                parent.moveBoxScript.InactiveOverride = false;
                isactive = false;
            }
            else Debug.Log($"Inactive moved, self {CurrentIndex} parentIndex {(parent.moveBoxScript == null ? "null" : parent.moveBoxScript.CurrentIndex)}");
        }
    }
}

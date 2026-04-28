using System.Collections;
using System.Collections.Generic;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class scr_prOrderManage : MonoBehaviour, IPointerEnterHandler
{
    public scr_HoverableText itemName;
    public TMP_Text itemCount;
    public TMP_Text orderAmount;
    public scr_SelectableText button_orderType;
    public scr_SelectableText buttonMinus;
    public scr_SelectableText buttonPlus;
    public scr_SelectableText btn_action;
    public TMP_Text warningMsg;
    public moveOrderScriptBTN moveBTN;

    public Manageable targetFaction;
    public Manageable.ProductionOrder order = null;
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
            }else  _isActive = value;

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
    public void RegisterPO(scr_Canvas_Management canvas, Manageable faction, Manageable.ProductionOrder orderPR)
    {
        targetFaction = faction;
        this.order = orderPR;
        this.parent = canvas;
        if (targetFaction != null && this.order != null)
        {
            CurrentIndex = faction.ProductionOrders.IndexOf(this.order);
        }
        this.SiblingIndex = this.transform.GetSiblingIndex();

    }
    public void NotifyChanged()
    {
        this.SiblingIndex = this.transform.GetSiblingIndex();
        this.CurrentIndex = targetFaction.ProductionOrders.IndexOf(this.order);
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
                this.CurrentIndex = Math.Min(this.targetFaction.ProductionOrders.Count, cur);

                this.transform.SetSiblingIndex(this.SiblingIndex);
                this.targetFaction.ProductionOrders.Remove(order);
                this.targetFaction.ProductionOrders.Insert(this.CurrentIndex, order);
                //NotifyChanged();
                parent.moveBoxScript.NotifyChanged();
                parent.moveBoxScript.InactiveOverride = false;
                isactive = false;
            }
            else Debug.Log($"Inactive moved, self {CurrentIndex} parentIndex {(parent.moveBoxScript == null ? "null" : parent.moveBoxScript.CurrentIndex)}");
        }
    }

}

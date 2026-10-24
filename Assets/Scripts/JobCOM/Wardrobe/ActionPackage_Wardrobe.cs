using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Undress / redress through a COM_Wardrobe command: a whole layer (equipRef -1) or a single item, on the receiver
/// (or the doer itself for the self variant). Same command for every item, the item is loaded into the package -
/// like ActionPackage_ItemUse. The clothes only change once the package executes accepted, so the receiver's
/// acceptance check, kojo (accept / refuse / complete) and onlookers' reactions to its comTags all apply.
/// </summary>
public class ActionPackage_Wardrobe : ActionPackage_Interaction
{
    public ActionPackage_Wardrobe() : base()
    {

    }

    public ActionPackage_Wardrobe(Job job, COM_Wardrobe targetCOM, int equipRef, int revealingFilter, List<int> doer, List<int> receiver, int masterRef) : base(job, targetCOM, doer, receiver, masterRef)
    {
        this.equipRef = equipRef;
        this.revealingFilter = revealingFilter;
        RefreshItemKey();
    }

    /// <summary> single item (inventory refID or active outfit piece id), -1 = the whole layer of the command </summary>
    [JsonProperty] protected int equipRef = -1;
    /// <summary> whole-layer undress only: items less revealing than this stay on (Character_Trainable.Undress) </summary>
    [JsonProperty] protected int revealingFilter = (int)Revealing.ShapeReveal;

    [JsonIgnore] public int EquipRef { get { return equipRef; } }
    [JsonIgnore] public COM_Wardrobe WardrobeCOM { get { return targetCOM as COM_Wardrobe; } }

    /// <summary> whose clothes change: the receiver, or the doer itself (self variant) </summary>
    [JsonIgnore]
    public Character_Trainable Target
    {
        get
        {
            if (ReceiverRefs.Count > 0) return scr_System_CampaignManager.current.FindInstanceByID(ReceiverRefs[0]);
            if (DoerRefs.Count > 0) return scr_System_CampaignManager.current.FindInstanceByID(DoerRefs[0]);
            return null;
        }
    }

    [JsonIgnore]
    public Item_Instance Item
    {
        get
        {
            if (equipRef == -1) return null;
            var chara = Target;
            return chara == null || chara.Body == null ? null : chara.Body.ResolveEquip(equipRef);
        }
    }

    [JsonIgnore]
    public override bool PackageRepeat
    {
        get
        {
            return false;
        }
        set
        {
            toggleRepeat = value;
            this.tooltip = new List<string>();
        }
    }

    [JsonIgnore]
    public override string DisplayName
    {
        get
        {
            RefreshItemKey();
            return base.DisplayName.Replace("$item$", ItemName);
        }
    }

    [JsonIgnore]
    string ItemName
    {
        get
        {
            var item = Item;
            if (item != null) return item.DisplayName;
            var com = WardrobeCOM;
            return com == null ? "" : LocalizeDictionary.QueryThenParse($"equip_layer_{com.layer}");
        }
    }

    void RefreshItemKey()
    {
        if (keyReplaceDictionary == null) keyReplaceDictionary = new Dictionary<string, string>();
        keyReplaceDictionary["$item$"] = ItemName;
    }

    protected override bool PreEvaluate()
    {
        bool base_evaluate = base.PreEvaluate();
        if (!base_evaluate) return base_evaluate;

        RefreshItemKey();

        var com = WardrobeCOM;
        var chara = Target;
        if (com == null)
        {
            tooltip.Add("ActionPackage_Wardrobe: command is not a COM_Wardrobe");
            isValid = false;
            return isValid;
        }
        if (chara == null || chara.Body == null)
        {
            tooltip.Add("ActionPackage_Wardrobe: no target");
            isValid = false;
            return isValid;
        }

        if (!HasSomethingToChange(chara, com, out var reason))
        {
            tooltip.Add(LocalizeDictionary.QueryThenParse(reason));
            isValid = false;
        }
        return isValid;
    }

    /// <summary> the command would change something on chara right now; reason = localization key when not </summary>
    bool HasSomethingToChange(Character_Trainable chara, COM_Wardrobe com, out string reason)
    {
        reason = "";
        if (equipRef != -1)
        {
            var item = chara.Body.ResolveEquip(equipRef);
            if (item == null || item.GetComp_Equippable() == null)
            {
                reason = "ui_wardrobe_tooltip_noItem";
                return false;
            }
            bool worn = chara.GetPartByEquipRef(equipRef) != null;
            if (com.isRedress && worn)
            {
                reason = "ui_wardrobe_tooltip_alreadyWorn";
                return false;
            }
            if (!com.isRedress && !worn)
            {
                reason = "ui_wardrobe_tooltip_notWorn";
                return false;
            }
            return true;
        }

        if (com.isRedress)
        {
            if (!chara.CanRedress(com.allLayers ? BodyEquipLayer.None : com.layer))
            {
                reason = "ui_wardrobe_tooltip_nothingToRedress";
                return false;
            }
            return true;
        }

        if (!chara.NeedUndress(KeepLayer(com), (Revealing)revealingFilter))
        {
            reason = "ui_wardrobe_tooltip_nothingToUndress";
            return false;
        }
        return true;
    }

    protected override void Execution(MessageCollect m = null, List<Action> eventCollector = null)
    {
        base.Execution(m, eventCollector);
        if (!executeSuccessful) return;

        var com = WardrobeCOM;
        var chara = Target;
        if (com == null || chara == null || chara.Body == null) return;

        if (equipRef != -1)
        {
            if (com.isRedress) chara.Reequip(chara.Body.ResolveEquip(equipRef));
            else chara.UnequipItem(equipRef, -1, true, true);
        }
        else if (com.isRedress) chara.Redress(com.allLayers ? BodyEquipLayer.None : com.layer);
        else
        {
            chara.Undress(com.allLayers ? BodyEquipLayer.None : com.layer, (Revealing)revealingFilter);
            // the layer left on, as ActionPackage_Undress reports it
            chara.NotifyConsciousClothingChange(KeepLayer(com));
        }
    }

    /// <summary> undress keeps the layers below the command's: Outer -> Inner, Inner -> Skin, Skin / all -> None </summary>
    static BodyEquipLayer KeepLayer(COM_Wardrobe com)
    {
        return com.allLayers ? BodyEquipLayer.None : (BodyEquipLayer)Math.Max(0, (int)com.layer - 1);
    }

    protected override ActionPackage CopyPackage()
    {
        ActionPackage_Wardrobe copy = new ActionPackage_Wardrobe(job, WardrobeCOM, equipRef, revealingFilter, DoerRefs, ReceiverRefs, masterRef);
        copy.SetVariantID(this.validVariant);
        copy.LoggedBegin = this.LoggedBegin;
        copy.duration = this.duration;
        return copy;
    }
}

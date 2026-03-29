using System;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UIElements;

public class scr_add_instance : MonoBehaviour
{
    public RectTransform selfRect;

    public scr_SelectableText selectTarget;

    public scr_HoverableText applyCount;
    public scr_HoverableText expiresIn;

    public scr_SelectableText button_add;
    public scr_SelectableText button_reduce;
    public scr_SelectableText button_remove;

    public AdditiveEntry Entry;

    public void InitializeData(AdditiveEntry entry)
    {
        this.Entry = entry;
        RefreshData();
    }

    public void RefreshData()
    {
        if (Entry.remainingTicks == 0) expiresIn.SetText(LocalizeDictionary.QueryThenParse("ui_management_mealprep_additive_expiresIn_inactive"));
        else if (Entry.remainingTicks < 0) expiresIn.SetText(LocalizeDictionary.QueryThenParse("ui_management_mealprep_additive_expiresIn_alwaysactive"));
        else expiresIn.SetText(LocalizeDictionary.QueryThenParse("ui_management_mealprep_additive_expiresIn_active").Replace("$minutes$", $"{Entry.remainingTicks}"));

        // each button add to list
        if (Entry.targetingType != AdditiveEntry.AdditiveEntryType.Custom)
        {
            selectTarget.SetText(LocalizeDictionary.QueryThenParse($"ui_management_mealprep_additive_applyTo_BTN_{Entry.targetingType}"));
        }
        else
        {
            var names = new List<string>();
            foreach (var i in Entry.targetCharaRefs) 
            {
                var c = scr_System_CampaignManager.current.FindInstanceByID(i);
                if (c == null) continue;
                names.Add(c.FirstName);
            }
            selectTarget.SetText(LocalizeDictionary.QueryThenParse($"ui_management_mealprep_additive_applyTo_BTN_Custom").Replace("$names$", names.Count > 0 ? String.Join(" ",names) : LocalizeDictionary.QueryThenParse("ui_management_mealprep_additive_applyTo_BTN_Custom_none")));
        }

        applyCount.SetText(LocalizeDictionary.QueryThenParse("ui_management_mealprep_additive_dosage").Replace("$dosage$", $"{Entry.usageCount}"));
    }

    public class ButtonValidator_ModCount : ButtonValidator, I_ButtonClickable
    {
        new scr_menu_mealadditives parent;
        bool isAdd; 
        AdditiveEntry entry;
        scr_HoverableText applyCount;
        scr_prefab_additive prefab;
        scr_SelectableText button;
        scr_add_instance selfbox;
        public ButtonValidator_ModCount(scr_menu_mealadditives parent, scr_SelectableText button, scr_HoverableText applyCount, AdditiveEntry entry, scr_prefab_additive prefab, bool isAdd, scr_add_instance selfbox) : base(parent)
        {
            this.parent = parent;
            this.button = button;
            this.prefab = prefab;
            this.isAdd = isAdd;
            this.entry = entry;
            this.applyCount = applyCount;
            this.prefab = prefab;
            this.selfbox = selfbox;
        }

        public override bool IsButtonValid()
        {
            if (prefab == null || !prefab.gameObject.activeInHierarchy) return false;
            if (entry == null) return false;
            if (isAdd) return true;
            else return entry.usageCount > 0;
        }

        public void OnClickButton()
        {
            int modcount = UtilityEX.SHIFT ? 100 : UtilityEX.CTRL ? 10 : 1;
            if (isAdd) entry.usageCount += modcount;
            else entry.usageCount = Math.Max(0, entry.usageCount - modcount);
            selfbox.RefreshData();
        }
    }
    public class ButtonValidator_Remove : ButtonValidator, I_ButtonClickable
    {

        new scr_menu_mealadditives parent;
        AdditiveEntry entry;
        scr_HoverableText applyCount;
        scr_prefab_additive prefab;
        scr_SelectableText button;
        scr_add_instance parentRect;
        public ButtonValidator_Remove(scr_menu_mealadditives parent, scr_SelectableText button, AdditiveEntry entry, scr_prefab_additive prefab, scr_add_instance parentRect) : base(parent)
        {
            this.parent = parent;
            this.button = button;
            this.prefab = prefab;
            this.entry = entry;
            this.prefab = prefab;
            this.parentRect = parentRect;
        }

        public override bool IsButtonValid()
        {
            return prefab != null && prefab.gameObject.activeInHierarchy && prefab.item != null && prefab.faction != null;
        }

        public void OnClickButton()
        {
            parentRect.gameObject.SetActive(false);
            prefab.DeletePrefab(parentRect);
        }
    }

    public class ButtonValidator_Targets : ButtonValidator, I_ButtonClickable
    {

        new scr_menu_mealadditives parent;
        AdditiveEntry entry;
        scr_HoverableText applyCount;
        scr_prefab_additive prefab;
        scr_SelectableText button;
        scr_add_instance parentRect;
        public ButtonValidator_Targets(scr_menu_mealadditives parent, scr_SelectableText button, AdditiveEntry entry, scr_prefab_additive prefab, scr_add_instance parentRect) : base(parent)
        {
            this.parent = parent;
            this.button = button;
            this.prefab = prefab;
            this.entry = entry;
            this.prefab = prefab;
            this.parentRect = parentRect;
        }

        public override bool IsButtonValid()
        {
            if (prefab == null || !prefab.gameObject.activeInHierarchy) return false;
            if (prefab.item == null || prefab.faction == null) return false;
            button.Toggle(true, parent.CurrentlyEditing == parentRect);
            return true;
        }

        public void OnClickButton()
        {
            parent.CurrentlyEditing = parentRect;
        }
    }

}

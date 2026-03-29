using UnityEngine;
using System.Collections.Generic;
using System;

public class scr_prefab_additive : MonoBehaviour
{
    public RectTransform selfRect;
    public scr_HoverableText titleText;
    public RectTransform additiveGrid;
    public RectTransform additiveGrid_none;

    public scr_add_instance prefab_additive;
    public UnityEngine.UI.Image selfImage;

    public scr_SelectableText btn_addEntry;


    public Manageable faction = null;
    public Item_Base item = null;
    scr_menu_mealadditives parent = null;
    public Dictionary<AdditiveEntry, List<int>> trackedButtons = new Dictionary<AdditiveEntry, List<int>>();
    public void Load(scr_menu_mealadditives parent, Manageable faction, Item_Base item)
    {
        this.faction = faction;
        this.item = item;
        this.parent = parent;

        var itemcomp = item.GetCompTemplateByID("ItemComponent_Ingestible");
        var itemcomp_ingest = itemcomp == null ? null : itemcomp.comp_Ingestible;

        List<string> validFoods = new List<string>();

        foreach(var food in faction.Inventory.Contents)
        {
            if (food.isFoodAdditive) continue;
            if (!food.isFoodConsumable) continue;
            if (food.Count < 1) continue;
            if (food.markForDelete) continue;
            var ingest = food.GetComp_Ingestible();
            if (ingest.canMixWith(itemcomp_ingest))
            {
                validFoods.Add(food.Print());
            } 
        }


        this.titleText.SetText(LocalizeDictionary.QueryThenParse( this.titleText.replaceText).Replace("$name$", item.DisplayName).Replace("$count$",$"{faction.Inventory.GetItemCount(item.ID)}").Replace("$food$",$"{validFoods.Count}"));
        this.titleText.SetExternalTooltip(LocalizeDictionary.QueryThenParse("ui_management_mealprep_additive_title_extratooltip").Replace("$names$", validFoods.Count > 0 ? String.Join("\n", validFoods) : LocalizeDictionary.QueryThenParse("none")));

        Utility.DestroyAllChildrenFrom(additiveGrid, 1);

        bool hasentry = false;
        // foreach
        foreach (var add in faction.mealManager.GetAdditivesUsing(item.ID))
        {
            MakePrefab(add);
            hasentry = true;
        }

        additiveGrid_none.gameObject.SetActive(!hasentry);
        parent.RegisterButton(GetHashCode(), this.btn_addEntry, new Button_AddAdditiveInstance(parent, this.btn_addEntry, this));
    }

    public List<scr_add_instance> boxes = new List<scr_add_instance>();

    public void MakePrefab(AdditiveEntry add)
    {
        if (trackedButtons.ContainsKey(add)) return;
        var list = new List<int>();
        trackedButtons.Add(add, list);


        var box = Instantiate(prefab_additive);
        box.selfRect.SetParent(additiveGrid, false);

        list.Add(parent.RegisterButton(box.button_add.GetHashCode(), box.button_add, new scr_add_instance.ButtonValidator_ModCount(parent, box.button_add, box.applyCount, add, this, true, box)));
        list.Add(parent.RegisterButton(box.button_reduce.GetHashCode(), box.button_reduce, new scr_add_instance.ButtonValidator_ModCount(parent, box.button_reduce, box.applyCount, add, this, false, box)));
        list.Add(parent.RegisterButton(box.button_remove.GetHashCode(), box.button_remove, new scr_add_instance.ButtonValidator_Remove(parent, box.button_remove, add, this, box)));
        list.Add(parent.RegisterButton(box.selectTarget.GetHashCode(), box.selectTarget, new scr_add_instance.ButtonValidator_Targets(parent, box.selectTarget, add, this, box)));

        box.InitializeData(add);
        boxes.Add(box);

        additiveGrid_none.gameObject.SetActive(false);
    }

    public void DeletePrefab(scr_add_instance add)
    {
        faction.mealManager.RemoveAdditive(add.Entry);

        if (trackedButtons.TryGetValue(add.Entry, out var list))
        {
            trackedButtons.Remove(add.Entry);
            foreach(var i in list)
            {
                parent.UnregisterButton(i);
            }
        }
        boxes.Remove(add);
    }


    public class Button_AddAdditiveInstance : ButtonValidator, I_ButtonClickable
    {
        new scr_menu_mealadditives parent;
        scr_prefab_additive prefab;
        scr_SelectableText button;
        public Button_AddAdditiveInstance(scr_menu_mealadditives parent, scr_SelectableText button, scr_prefab_additive prefab) : base(parent)
        {
            this.parent = parent;
            this.button = button;
            this.prefab = prefab;
        }

        public override bool IsButtonValid()
        {
            return prefab != null && prefab.gameObject.activeInHierarchy && prefab.item != null && prefab.faction != null; 
        }

        public void OnClickButton()
        {
            var fab = parent.faction.mealManager.AddAdditive(prefab.item.ID, 0);
            prefab.MakePrefab(fab);
        }
    }

}

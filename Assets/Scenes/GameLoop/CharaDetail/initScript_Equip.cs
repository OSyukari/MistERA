using System.Collections.Generic;
using UnityEngine;
using System;
using TMPro;
using System.Linq;

public class initScript_Equip: MonoBehaviour
{
    public RectTransform skinTitle, skinButton;

    public scr_HoverableText currentOutfit;
    public RectTransform rect_wardrobes, rect_wardrobes_outfits;
    public scr_equip_wardrobe prefab_outfit;
    public scr_WardrobeEditor canvas_WardrobeEdit;

    public scr_equip_wardrobe Outfit_Default, Outfit_Home, Outfit_Home_Temp, Outfit_Sleep, Outfit_Sleep_Temp;

    scr_Menu_CharaDetail parent;

    public scr_HoverableText description;

    public void InitializeData(Character_Trainable c, scr_Menu_CharaDetail parent)
    {
        this.parent = parent;

        if (scr_System_CentralControl.current.isSafeMode)
        {
            skinTitle.gameObject.SetActive(false);
            skinButton.gameObject.SetActive(false);
        }

        var outfitName = c.Wardrobe.ActiveOutfit?.DisplayName ?? "";
        currentOutfit.SetText(LocalizeDictionary.QueryThenParse("ui_wardrobe_currentOutfit").Replace("$outfit$", outfitName));

        foreach (var row in rect_wardrobes_outfits.GetComponentsInChildren<scr_equip_wardrobe>(true)) parent.UnregisterOutfit(row);
        foreach (var row in new[] { Outfit_Default, Outfit_Home, Outfit_Home_Temp, Outfit_Sleep, Outfit_Sleep_Temp })
            if (row != null) parent.UnregisterOutfit(row);
        Utility.DestroyAllChildrenFrom(rect_wardrobes_outfits, 0);
        RecreationUtility.RegisterWardrobeOpportunities(c);
        // every key in effect, outfit or not (the reserved ones always) - an empty one can get an outfit set
        foreach (var entry in c.Wardrobe.GetEntries())
        {
            var box = UsePresetOutfitRect(entry.key);
            if (box != null)
            {
                box.gameObject.SetActive(true);
            }
            else
            {
                box = Instantiate(prefab_outfit);
                box.selfRect.SetParent(rect_wardrobes_outfits, false);
            }

            box.InitializeData(c, entry);
            parent.RegisterOutfit(box);
        }
        var newKey = c.Wardrobe.NewPoolKey();
        if (c.Wardrobe.CanEdit(newKey))
        {
            var box = Instantiate(prefab_outfit);
            box.selfRect.SetParent(rect_wardrobes_outfits, false);
            box.InitializeData(c, new WardrobeEntry(newKey, null));
            box.keywords.SetText("ui_wardrobe_createUnclassified");
            box.overrides.SetText("Used when a direct assignment is missing or invalid.");
            parent.RegisterOutfit(box);
        }
    }

    scr_equip_wardrobe UsePresetOutfitRect(string key)
    {
        if (key == OutfitUtility.Key_Default) return Outfit_Default;
        else if (key == OutfitUtility.Key_Home) return Outfit_Home;
        else if (key == OutfitUtility.Key_HomeTemp) return Outfit_Home_Temp;
        else if (key == OutfitUtility.Key_Sleep) return Outfit_Sleep;
        else if (key == OutfitUtility.Key_SleepTemp) return Outfit_Sleep_Temp;
        else return null;
    }


    public class ButtonValidator_CanEditOutfit : ButtonValidator, I_ButtonClickable
    {
        new scr_Menu_CharaDetail parent;
        scr_SelectableText text;
        /// <summary> the wardrobe key to edit - its outfit may not exist yet </summary>
        string key;
        public ButtonValidator_CanEditOutfit(scr_Menu_CharaDetail parent, scr_SelectableText text, string key) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.key = key ?? "";
        }

        public override bool IsButtonValid()
        {
            bool allowed = parent.chara?.Wardrobe?.CanEdit(key) == true;
            if (!allowed) { tooltip = "The responsible faction must be player managed."; return false; }
            var prefab = parent.initScript_Equip.canvas_WardrobeEdit;
            if (prefab == null) { tooltip = "Assign the wardrobe editor canvas prefab."; return false; }
            if (!prefab.IsConfigured(out tooltip)) return false;
            tooltip = "Edit using clothing in the responsible faction's inventory.";
            return true;
        }

        public void OnClickButton()
        {
            if (!IsButtonValid()) return;
            var editor = scr_System_SceneManager.current.LoadCanvasIntoScene(parent, parent.initScript_Equip.canvas_WardrobeEdit)
                .GetComponent<scr_WardrobeEditor>();
            editor.InitializeWithArgument(parent.chara, key, () =>
            {
                parent.initScript_Equip.InitializeData(parent.chara, parent);
                parent.ValidateAll();
            });
        }
    }
}

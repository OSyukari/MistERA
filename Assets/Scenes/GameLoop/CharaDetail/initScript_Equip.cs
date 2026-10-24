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

        currentOutfit.SetText($"Current Outfit: {c.Wardrobe.ActiveOutfit?.DisplayName}");

        Utility.DestroyAllChildrenFrom(rect_wardrobes_outfits, 0);
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

        bool? result = null;
        public override bool IsButtonValid()
        {
            if (result == null)
            {
                result = false;
                tooltip = "not player related cannot edit";
                if (parent.chara != null && parent.chara.FactionManager != null)
                {
                    var kind = OutfitUtility.ParseKey(key, out var factionID, out var id);

                    var player = scr_System_CampaignManager.current.Player;
                    var fm = parent.chara.FactionManager;

                    if (kind != OutfitKeyKind.MemberType || (fm.Faction_Home != null && factionID == fm.Faction_Home.ID) || (id == "membertype_none"))
                    {
                        // not faction related outfit setting (could be activity outfit), allow edit if player is family member
                        // TODO in the future add a "change outfit permission"
                        result = fm.Faction_Home != null && fm.Faction_Home.isManagedChara(player.RefID);
                        tooltip = "family member CAN request change outfit";
                    }
                    else
                    {
                        // we have a work faction here
                        foreach(var w in fm.WorkFactions)
                        {
                            if (!w.isPlayerManagedFaction) continue;
                            if (w.ID != factionID) continue;
                            result = true;
                            tooltip = "workplace manager can edit uniform";
                        }

                        if (result.Value == false)
                        {
                            tooltip = $"{LocalizeDictionary.QueryThenParse(factionID)} not player related cannot edit";
                        }
                    }
                }
            }
            // blocked on purpose: workplace uniform editing is not implemented yet
            result = false;
            return result.Value;
        }

        public void OnClickButton()
        {

        }
    }
}

using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.UI;

public class scr_equip_wardrobe : MonoBehaviour
{
    public OutfitSet outfit;
    public RectTransform selfRect;

    public scr_HoverableText keywords;
    public scr_HoverableText overrides;

    public scr_HoverableText prefab_equip;
    public RectTransform equipRect;

    public scr_HoverableText hairstyle;
    public scr_HoverableText body;

    public RectTransform variantRect;
    public scr_wardrobe_outfitvariant prefab_equipvariant;

    public scr_SelectableText btn_Edit;

    public Image bgimage;

    bool canEdit = false;

    public void InitializeData(Character_Trainable c, OutfitSet wd)
    {
        outfit = wd;

        keywords.SetText($"Keys: {wd.DisplayName}");

        foreach(var i in wd.pieces)
        {
            var box = Instantiate(prefab_equip);
            box.SelfRect.SetParent(equipRect, false);
            box.SetText(i.DisplayName);
        }

        bgimage.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;

        List<string> types = new List<string>();
        if (wd.overrideSkin) types.Add("overrideSkin");
        if (wd.overrideInner) types.Add("overrideInner");
        if (wd.overrideOuter) types.Add("overrideOuter");

        if (wd.limitToLocaleFaction) types.Add("limitToLocaleFaction");
        if (wd.sleepKeepLayers.Count > 0) types.Add($"will wear {String.Join(" ", wd.sleepKeepLayers)} during sleep");
        if (wd.restKeepLayers.Count > 0) types.Add($"will wear {String.Join(" ", wd.restKeepLayers)} when not working");

        if (wd.hairstyleOverride == null) hairstyle.SetText("");
        else hairstyle.SetText("override hairstyle");

        if (wd.bodyTraitOverrides.Count > 0) body.SetText($"body override: {String.Join(" ", wd.bodyTraitOverrides)}");
        else body.SetText("");


        overrides.SetText(String.Join(" ", types));

        foreach(var variant in wd.variants)
        {
            var box = Instantiate(prefab_equipvariant);
            box.selfRect.SetParent(variantRect);
            box.InitializeData(variant);
        }


    }

}

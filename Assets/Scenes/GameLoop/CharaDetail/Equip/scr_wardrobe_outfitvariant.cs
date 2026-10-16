using UnityEngine;

public class scr_wardrobe_outfitvariant : MonoBehaviour
{
    public RectTransform selfRect;

    public scr_HoverableText groupKey;
    public scr_HoverableText requires;

    public scr_HoverableText prefab_equip;
    public RectTransform equipRect;

    public void InitializeData(OutfitVariant var)
    {
        groupKey.SetText($"group: {var.group}");

        var requireString = 
            $"{(var.requireTags_all.Count > 0 ? $" All({string.Join(" ", var.requireTags_all)})" : "")}" +
            $"{(var.requireTags_any.Count > 0 ? $" Any({string.Join("|", var.requireTags_any)})" : "")}" +
            $"{(var.excludeTags_any.Count > 0 ? $" Except({string.Join("|", var.excludeTags_any)})" : "")}";

        requires.SetText(requireString.Length > 0 ? $"requires: {requireString}" : "no requirement");

        foreach (var i in var.pieces)
        {
            var box = Instantiate(prefab_equip);
            box.SelfRect.SetParent(equipRect, false);
            box.SetText(i.DisplayName);
        }
    }
}

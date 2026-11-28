using UnityEngine;

/// <summary>Attach to a supplied clothing/option row prefab. Leave button.optionID at -1; the menu registers it.</summary>
public class scr_WardrobeEditEntry : MonoBehaviour
{
    public scr_HoverableText title;
    public scr_HoverableText details;
    public scr_SelectableText button;
    public bool IsConfigured => title != null && button != null;

    public void InitializeData(string label, string description, string actionLabel)
    {
        title.SetText(label);
        if (details != null) details.SetText(description);
        button.SetText(actionLabel);
    }
}

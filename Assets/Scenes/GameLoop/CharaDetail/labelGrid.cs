using UnityEngine;

public class labelGrid : MonoBehaviour
{
    public string gridLabel = "";
    public RectTransform selfRect;
    public scr_HoverableText title = null;
    public scr_HoverableText none = null;

    private void Start()
    {
        if (title != null && gridLabel != "") title.SetText(LocalizeDictionary.QueryThenParse(gridLabel));
    }

    public void NotifyInsert()
    {
        if (none != null) none.gameObject.SetActive(false);
    }

    public void Clear()
    {
        var startIndex = none == null ? 0 : 1;
        while (selfRect.transform.childCount > startIndex) DestroyImmediate(selfRect.transform.GetChild(startIndex).gameObject);
        if (none != null) none.gameObject.SetActive(true);
    }
}

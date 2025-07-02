using UnityEngine;

public class ActionList : MonoBehaviour
{
    public RectTransform entryList, endOfTurnList;

    protected void Awake()
    {
        //selfRect = GetComponent<RectTransform>();
    }
    public RectTransform selfRect;

    public void NotifyChange(scr_Menu_Combat.CombatUI mode)
    {
        if (!this.gameObject.activeInHierarchy) return;

        for (int i = 0; i < entryList.childCount; i++)
        {
            var comp = entryList.GetChild(i).GetComponent<ActionEntry>();
            if (comp != null) comp.NotifyChange(mode);
        }

        for (int i = 0; i < endOfTurnList.childCount; i++)
        {
            var comp = endOfTurnList.GetChild(i).GetComponent<ActionEntry>();
            if (comp != null) comp.NotifyChange(mode);
        }

        switch (mode)
        {
            case scr_Menu_Combat.CombatUI.Overview:
                selfRect.sizeDelta = new Vector2(600, 0);
                selfRect.anchorMin = new Vector2(0.5f, 0);
                selfRect.anchorMax = new Vector2(0.5f, 1);
                selfRect.pivot = new Vector2(0.5f, 0.5f);
                selfRect.anchoredPosition = new Vector2(0, 0);
                break;
            case scr_Menu_Combat.CombatUI.SkillSelect:
                selfRect.sizeDelta = new Vector2(450, 0);
                selfRect.anchorMin = new Vector2(1, 0);
                selfRect.anchorMax = new Vector2(1, 1);
                selfRect.pivot = new Vector2(1, 0.5f);
                selfRect.anchoredPosition = new Vector2(0, 0);
                break;
        }
    }

}

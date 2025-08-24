using UnityEngine;

public class ActionList : MonoBehaviour
{
    public RectTransform entryList, endOfTurnList, No_EOT_message;

    protected void Awake()
    {
        //selfRect = GetComponent<RectTransform>();
    }
    public RectTransform selfRect;

    public void NotifyChange(scr_Menu_Combat.CombatUI mode)
    {
        if (!this.gameObject.activeInHierarchy) return;

        switch (mode)
        {
            case scr_Menu_Combat.CombatUI.Overview:
                selfRect.sizeDelta = new Vector2(550, 0);
                selfRect.anchorMin = new Vector2(0.5f, 0);
                selfRect.anchorMax = new Vector2(0.5f, 1);
                selfRect.pivot = new Vector2(0.5f, 0.5f);
                selfRect.anchoredPosition = new Vector2(0, 0);
                break;
            case scr_Menu_Combat.CombatUI.SkillSelect:
                selfRect.sizeDelta = new Vector2(550, 0);
                selfRect.anchorMin = new Vector2(1, 0);
                selfRect.anchorMax = new Vector2(1, 1);
                selfRect.pivot = new Vector2(1, 0.5f);
                selfRect.anchoredPosition = new Vector2(0, 0);
                break;
        }
    }

}

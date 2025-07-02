using UnityEngine;
using UnityEngine.UI;

public class ActionEntry : MonoBehaviour
{


    public bool isHostile = false;
    public HorizontalLayoutGroup selfLayout;
    public RectTransform block_Self;
    public RectTransform block_icon;
    public RectTransform block_blank;
    public Image selfImage;

    protected void Awake()
    {
        //selfLayout = this.GetComponent<HorizontalLayoutGroup>();
        //selfImage = this.GetComponent<Image>();
    }
    public void Initialize()
    {
        if (isHostile) selfImage.color = Utility.UI_HostileColor;
        else selfImage.color = Utility.UI_SelfColor;
    }

    public void NotifyChange(scr_Menu_Combat.CombatUI mode)
    {
        if (!this.gameObject.activeInHierarchy) return;
        switch(mode)
        {
            case scr_Menu_Combat.CombatUI.Overview:
                selfLayout.reverseArrangement = isHostile;
                block_blank.gameObject.SetActive(true);
                break;
            case scr_Menu_Combat.CombatUI.SkillSelect:
                selfLayout.reverseArrangement = false;
                block_blank.gameObject.SetActive(false);
                break;
        }
    }
}

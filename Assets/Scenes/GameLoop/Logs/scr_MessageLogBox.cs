using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class scr_MessageLogBox : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public void OnPointerEnter(PointerEventData eventData)
    {

        if (portrait != null && !scr_UpdateHandler.current.Lock)
        {
            //Debug.Log("scr_MessageLogBox OnPointerEnter, refID "+charaRefID);
            scr_System_CampaignManager.current.Log_TrySetChara(portrait, false);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        //if (charaRefID > -1) scr_System_CampaignManager.current.Log_TryClearChar(false);
    }

    protected void Start()
    {
        //selfText2 = GetComponent<TMP_Text>();
    }
    protected TMP_Text selfText2;
    public void Initialize(PortraitManager portrait)
    {
        this.portrait = portrait;
    }

    protected PortraitManager portrait = null;

}

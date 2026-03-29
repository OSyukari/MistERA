using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class scr_childCOMs : MonoBehaviour, IPointerClickHandler
{

    public RectTransform verticalAlignment;
    public scr_HoverableText title;
    public RectTransform comList;
    public CanvasGroup SelfCanvasGroup;
    public List<int> trackedIDs = new List<int>();
    public Image selfImage;
    public ActionPackage ap = null;

    void Awake()
    {
        selfImage.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Opaque.Color;
        this.Active = false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        bool unload = false;
        // if click outside box
        if (eventData.rawPointerPress.GetComponent<scr_childCOMs>() != null) unload = true;
        // inside box
        else if (eventData.button == PointerEventData.InputButton.Right && UtilityEX.isClickBelowDragThreshold(eventData)) unload = true;
        //Debug.Log("scr_Menu_CharaDetail: OnPointerClick! Data["+eventData.pointerPress+"] rawData["+ eventData.rawPointerPress + "]");

        if (unload)
        {
            Active = false;
        }
    }

    public bool Active
    {
        get
        {
            return SelfCanvasGroup.alpha == 1;
        }
        set
        {
            SelfCanvasGroup.alpha = value ? 1 : 0;
            SelfCanvasGroup.interactable = value;
            SelfCanvasGroup.blocksRaycasts = value;
        }
    }
}

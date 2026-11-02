using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System;

public class scr_AttachToUpdateHandler : MonoBehaviour, IPointerClickHandler
{
    //public Image selfImage;
    public CanvasGroup selfCanvasGroup;
    public Image selfImage;
    public void OnPointerClick(PointerEventData eventData)
    {
        Observer_PointerClick?.Invoke(eventData);
    }

    public void Activate()
    {
        selfImage.raycastTarget = true;
        selfCanvasGroup.interactable = true;
        selfCanvasGroup.blocksRaycasts = true;
        selfCanvasGroup.alpha = 1;
    }

    public void Deactivate(bool hideOnly = false)
    {
        if (hideOnly)
        {
            selfCanvasGroup.alpha = 0;
        }
        else
        {
            selfImage.raycastTarget = false;
            selfCanvasGroup.interactable = false;
            selfCanvasGroup.blocksRaycasts = false;
            selfCanvasGroup.alpha = 0;
        }
    }

    public event Action<PointerEventData> Observer_PointerClick;
    // Start is called before the first frame update
    private void Start()
    {
        scr_UpdateHandler.current.AttachUpdateImage(this);
    }
}

using UnityEngine;
using UnityEngine.EventSystems;
using System;

public class scr_AnimatingClickIntercept : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
        Observer_AnimatingClicks?.Invoke();
    }

    public Action Observer_AnimatingClicks;

}

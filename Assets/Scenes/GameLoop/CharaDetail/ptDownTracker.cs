using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UIElements;

public class ptDownTracker : MonoBehaviour, IScrollHandler//, IDragHandler
{
    public scr_Menu_CharaDetail Parent;
    public RectTransform SelfRect, targetRect;
#pragma warning disable CS0436 // Type conflicts with imported type
    public ScrollRect parentScroll;
#pragma warning restore CS0436 // Type conflicts with imported type

    public void NotifyScrollViewUpdate(Vector2 scrollPos)
    {
        //Debug.Log("scroolupdate!!");
        UpdatePosition();
    }

    protected void UpdatePosition()
    {
        var p = scr_System_CampaignManager.current.CurrentTargetEXPortrait;
        if (p != currentPortrait) SetRectPosition(p);
        if (currentPortrait != null && init)
        {
            currentPortrait.SetPortraitOffsets(x + targetRect.anchoredPosition.x, y + targetRect.anchoredPosition.y, s + (targetRect.localScale.x - 1));
            scr_System_CampaignManager.current.UpdateCurrentTargetAnchor(currentPortrait);
        }
    }
    public void OnScroll(PointerEventData eventData)
    {
        parentScroll.OnScroll(eventData);

        Debug.Log("Scrolled!!!");
        UpdatePosition();
    }

    public PortraitManager.CharaPortrait currentPortrait = null;

    public bool init = false;

    public void SetRectPosition(PortraitManager.CharaPortrait p)
    {
        if (targetRect.gameObject.activeInHierarchy) init = true;
        this.currentPortrait = p;

        x = p.portrait_offset_x;
        y = p.portrait_offset_y;
        s = p.portrait_offset_size;
        
    }

    public float x, y, s;

    void Start()
    {
        if (!init && this.currentPortrait != null) SetRectPosition(this.currentPortrait);
    }
}

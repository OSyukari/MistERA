using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// Whatever scr_button_setHighlightCOM.notifyTarget is currently set to needs to receive hover
/// forwarding - implemented by every schedule-picker button class (button_ScheduleCommand,
/// button_ScheduleSandbox, button_ScheduleMemberType, button_setHighlightCOM), not just one of them.
/// </summary>
public interface I_ScheduleHoverNotifiable
{
    void NotifyPointerEnter();
    void NotifyPointerExit();
}

public class scr_button_setHighlightCOM : MonoBehaviour
{
    public TMP_Text description;
    public scr_SelectableText button;

    protected I_ScheduleHoverNotifiable _notifyTarget = null;
    public I_ScheduleHoverNotifiable notifyTarget { set
        {
            _notifyTarget = value;
            if(_notifyTarget != null)
            {
                this.button.AttachOnHoverEnter(OnPointerEnter );
                this.button.AttachOnHoverExit(OnPointerExit );
            }
            else
            {
                this.button.AttachOnHoverExit(null);
                this.button.AttachOnHoverEnter(null);
            }
        }
        get {
            return _notifyTarget;
                    } }

    public void OnPointerEnter()
    {
        if (notifyTarget != null) notifyTarget.NotifyPointerEnter();
    }

    public void OnPointerExit()
    {
        if (notifyTarget != null) notifyTarget.NotifyPointerExit();
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using TMPro;
using UnityEngine.EventSystems;
using System;

public class initScript_prefs_Display: MonoBehaviour
{
    public TMP_Text text_normal, text_hover, text_conflict, text_disabled, text_maxed, text_toggled;
    public RectTransform selfRect;
    public Image bg_normal, bg_transparent;

    void Start()
    {
        TextColorUpdate();
        BGColorUpdate();
    }

    public void TextColorUpdate()
    {
        text_normal.color = scr_System_CentralControl.current.DisplaySetting.TextColor_neutral.Color;
        text_hover.color = scr_System_CentralControl.current.DisplaySetting.TextColor_hover.Color;
        text_conflict.color = scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color;
        text_disabled.color = scr_System_CentralControl.current.DisplaySetting.TextColor_disabled.Color;
        text_maxed.color = scr_System_CentralControl.current.DisplaySetting.TextColor_maxed.Color;
        text_toggled.color = scr_System_CentralControl.current.DisplaySetting.TextColor_toggle.Color;
    }

    public void BGColorUpdate()
    {
        bg_normal.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Opaque.Color;
        bg_transparent.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;
    }

}

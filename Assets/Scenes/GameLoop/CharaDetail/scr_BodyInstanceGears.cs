using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class scr_BodyInstanceGears : MonoBehaviour
{

    public TMP_Text bodyInstance;
    public RectTransform gearTab;
    public Image self_backgroundImage;
    public RectTransform Instantiate(string s)
    {
        //bodyInstance.text = s;
        self_backgroundImage.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;
        return this.gearTab;
    }
}

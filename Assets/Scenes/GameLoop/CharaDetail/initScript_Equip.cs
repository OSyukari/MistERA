using System.Collections.Generic;
using UnityEngine;
using System;
using TMPro;
using System.Linq;

public class initScript_Equip: MonoBehaviour
{
    public RectTransform skinTitle, skinButton;

    public void InitializeData(Character_Trainable c)
    {
        if (scr_System_CentralControl.current.isSafeMode)
        {
            skinTitle.gameObject.SetActive(false);
            skinButton.gameObject.SetActive(false);
        }
    }
}

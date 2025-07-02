using HSVPicker;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class initScript_ColorPicker: MonoBehaviour
{
    public CanvasGroup selfGroup;
    public scr_HoverableText title;

    string _titleString = "";
    string TitleString { get
        {
            if (_titleString == "") _titleString = LocalizeDictionary.QueryThenParse("ui_prefs_colorPicker_Title");
            return _titleString;
        } }

    Color32 targetColor;
    Action<float, float, float, float> OnValueChange;
    Action OnRevert;
    public ColorPicker ColorPicker;
    public void LoadMenu(string optionString, Color32 targetColor, Action<float, float, float, float> OnValueChange, Action OnRevert)
    {
        title.SetText(TitleString.Replace("$optionName$", optionString));
        this.targetColor = targetColor;
        this.OnValueChange = OnValueChange;
        this.OnRevert = OnRevert;

        UpdateColor(targetColor);
        Active = true;
    }


    // Colorpicker works on 0-1 meanwhile unity handles color32 in 0-255

    public void UpdateColor(Color32 c)
    {
        ColorPicker.AssignColor(ColorValues.R, c.r);
        ColorPicker.AssignColor(ColorValues.G, c.g);
        ColorPicker.AssignColor(ColorValues.B, c.b);
        ColorPicker.AssignColor(ColorValues.A, c.a);

        prevColor = ColorPicker.CurrentColor;
    }

    public bool Active { get
        {
            return selfGroup.alpha == 1;
        }set
        {
            if (value)
            {
                selfGroup.alpha = 1;
                selfGroup.interactable = true;
                selfGroup.blocksRaycasts = true;
            }
            else
            {
                selfGroup.alpha = 0;
                selfGroup.interactable = false;
                selfGroup.blocksRaycasts = false;
            }
        }
    }


    Color prevColor;
    public void OnValueChanged(Color color)
    {
        if (!Active) return;
        bool mult = false;
        bool multa = false;
        if ((color.r != prevColor.r || color.g != prevColor.g || color.b != prevColor.b) && color.r <= 1 && color.g <= 1 && color.b <= 1) mult = true;
        if (color.a != prevColor.a && color.a <= 1) multa = true;
        OnValueChange(mult? color.r * 255 : color.r, mult? color.g*255 : color.g, mult? color.b * 255 : color.b, multa? color.a*255:color.a);
    }

    public void Revert()
    {
        if (!Active) return;
        OnRevert();

    }


}

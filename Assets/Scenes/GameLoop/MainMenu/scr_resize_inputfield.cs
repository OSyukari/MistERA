using System;
using System.Linq;
using TMPro;
using UnityEngine;

public class scr_resize_inputfield : MonoBehaviour
{


    public float singleLineHeight;
    public RectTransform targetRect;
    public TMP_InputField inputField;
    public RectTransform SelfRect;
    
    public int lastLineCount = 0;

    private void Start()
    {
        if (scr_System_CentralControl.current.LLMSetting.enabled) OnContentChange();
        else SelfRect.gameObject.SetActive(false);
    }

    public void OnContentChange()
    {
        if (inputField.textComponent.textInfo.lineCount != lastLineCount)
        {
            lastLineCount = inputField.textComponent.textInfo.lineCount;
            //LayoutRebuilder.MarkLayoutForRebuild(targetRect);
            targetRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, (lastLineCount + 2) * singleLineHeight);
        }
        return;
    }
}

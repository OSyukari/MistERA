using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static System.Net.Mime.MediaTypeNames;

public class scr_resize_inputfield : MonoBehaviour
{


    public float singleLineHeight;
    public RectTransform targetRect;
    public TMP_InputField inputField; 
    
    public int lastLineCount = 0;

    private void Start()
    {
        OnContentChange();
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

using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
/// <summary>
/// Detect mouse over text and send Dictionary Query to Canvas for display.
/// 
/// TMPRO guide https://www.youtube.com/watch?v=xm6rVhFqTVU
/// </summary>
public class scr_HoverableText : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    bool updating = false;
    public void LateUpdate()
    {
        if (updating)
        {
            RefreshHover();
            if (!TMP_TextUtilities.IsIntersectingRectTransform(m_TextMeshPro.rectTransform, Input.mousePosition, m_Camera))
            {
                updating = false;
                lastTrackedIndex = -1;
                //Debug.Log("OnPointerExit");
                Handler.NotifyExit(); 
            }
        }
    }

    scr_Canvas_tooltipHandler Handler;
    private Canvas m_Canvas;
    private Camera m_Camera;
    private TextMeshProUGUI m_TextMeshPro;
   // int linkIndex;
    TMP_LinkInfo linkInfo;
    string tooltip;
    string tooltip_custom;
    string tooltip_external = null;
    protected scr_SelectableText button;
    private string holder;
    public void OnPointerEnter(PointerEventData eventData)
    {
        updating = true;

    }

    int lastTrackedIndex = -1;

    private void RefreshHover()
    {
        //Debug.Log("Input Mouse Position : " + Input.mousePosition.ToString());
        int linkIndex = TMP_TextUtilities.FindIntersectingLink(m_TextMeshPro, Input.mousePosition, m_Camera);
        //linkIndex = 0;  // this is bad. but. hey. what can I do ?
        //if (TMP_TextUtilities.FindIntersectingLink(m_TextMeshPro, Input.mousePosition, m_Camera) != -1) Debug.Log("OnPointerEnter, linkIndex [" + linkIndex + "]");
        if (linkIndex != -1 && lastTrackedIndex != linkIndex)
        //if (m_TextMeshPro.textInfo.linkInfo.Length > 0)
        {
            Handler.NotifyExit();
            lastTrackedIndex = linkIndex;

            linkInfo = m_TextMeshPro.textInfo.linkInfo[linkIndex];
           // tooltip = scr_System_tooltipDictionary.current.FindEntry(linkInfo.GetLinkID() as string);
            tooltip = scr_System_Serializer.current.Dictionary.QueryThenParse(linkInfo.GetLinkID() as string);
            //Debug.Log("OnPointerEnter, linkIndex [" + linkIndex + "] linkInfo[" + linkInfo + "] tooltip[" + tooltip + "]");
            if (button) tooltip_custom = button.GetCustomTooltip();
            else tooltip_custom = null;

            string[] s = new string[3];
            s[0] = tooltip;
            s[1] = tooltip_custom;
            s[2] = tooltip_external;

           // holder = String.Join("\n", s);

            holder = ((tooltip != null && tooltip.Length > 0) ? tooltip : "") + ((tooltip_custom != null && tooltip_custom.Length > 0) ? (tooltip != null && tooltip.Length > 0? "\n":"") + tooltip_custom : "" )+ ((tooltip_external != null && tooltip_external.Length > 0) ? ((tooltip != null && tooltip.Length > 0 || (tooltip_custom != null && tooltip_custom.Length > 0)) ? "\n" : "") + tooltip_external : "");

            if (holder.Length > 0)
            {
                Handler.NotifyHover(m_TextMeshPro, holder);
            }
        }
    }

    public void SetExternalTooltip(string s)
    {
        this.tooltip_external = s;
    }

    
    public void OnPointerExit(PointerEventData eventData)
    {
        /*
        updating = false;
        lastTrackedIndex = -1;
        //Debug.Log("OnPointerExit");
        Handler.NotifyExit();*/
    }

    void Awake()
    {
        this.m_TextMeshPro = GetComponent<TextMeshProUGUI>();
        this.button = GetComponent<scr_SelectableText>();
        if (this.GetComponent<scr_SelectableText>() == null) this.m_TextMeshPro.text = replaceText != "" ? scr_System_Serializer.current.Dictionary.QueryThenParse(replaceText) : scr_System_Serializer.current.Dictionary.QueryThenParse(this.m_TextMeshPro.text);
        this.m_TextMeshPro.font = scr_System_CentralControl.current.Font;
        this.m_TextMeshPro.UpdateFontAsset();

/*
        this.m_TextMeshPro.enableAutoSizing = true;
        this.m_TextMeshPro.fontSizeMax = 24;
        this.m_TextMeshPro.fontSizeMin = 8;
        this.m_TextMeshPro.verticalAlignment = VerticalAlignmentOptions.Middle;*/
    }

    void Start()
    {

        m_Canvas = gameObject.GetComponentInParent<Canvas>(true);
        //GetParentCanvasRecursive();
        Handler = m_Canvas.GetComponent<scr_Canvas_tooltipHandler>();
        if (m_Canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            m_Camera = null;
        else
            m_Camera = m_Canvas.worldCamera;
        /*
        this.m_TextMeshPro.text = scr_System_Serializer.current.Dictionary.Parse(this.m_TextMeshPro.text);

        this.m_TextMeshPro.enableAutoSizing = true;
        this.m_TextMeshPro.fontSizeMax = 24;
        this.m_TextMeshPro.fontSizeMin = 8;
        this.m_TextMeshPro.verticalAlignment = VerticalAlignmentOptions.Middle;*/
    }

    void OnEnable()
    {
        //this.m_TextMeshPro.text = scr_System_Serializer.current.Dictionary.Parse(this.m_TextMeshPro.text);
    }

    RectTransform parent;
    private void GetParentCanvasRecursive()
    {
        parent = GetComponent<RectTransform>();
        int counter = 0;
        do
        {
            Debug.Log("Current Recursion Layer :" + parent.name);
            parent = parent.GetComponentInParent<RectTransform>(true);
            m_Canvas = parent.GetComponent<Canvas>();
            counter += 1;
        }
        while (m_Canvas == null && counter < 5);
    }

    public string replaceText = "";

    public void SetText(string text, bool leadingSpace = false, string link = "")
    {
        if (this.m_TextMeshPro == null || this.m_TextMeshPro.text == null)
        {
            return;
        }
        if (text == null) text = "";
        text = scr_System_Serializer.current.Dictionary.QueryThenParse(text);
        //Debug.LogError(text);
        //if (link == "" || link == "trait_neutral" || link.Length < 1)
        //{
        this.m_TextMeshPro.text = "<link=" + link + ">" + (leadingSpace ? " " : "")  + text + "</link>";
        //}
        //else
        //{
           // this.m_TextMeshPro.text = "<link=" + link + ">" + (leadingSpace ? " " : "") + "<u>" + text + "</u></link>";
        //}
       
    }


}

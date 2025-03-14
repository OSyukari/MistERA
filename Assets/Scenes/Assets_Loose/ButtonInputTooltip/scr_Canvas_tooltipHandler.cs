using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class scr_Canvas_tooltipHandler : MonoBehaviour
{
    public RectTransform TextPopup_Prefab_01;

    private Camera m_Camera;
    private Canvas m_Canvas;

    private RectTransform m_TextPopup_RectTransform;
    private TextMeshProUGUI m_TextPopup_TMPComponent;



    void Start()
    {

        m_Canvas = GetComponent<Canvas>();

        // Get a reference to the camera if Canvas Render Mode is not ScreenSpace Overlay.
        if (m_Canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            m_Camera = null;
        else
            m_Camera = m_Canvas.worldCamera;

        m_TextPopup_RectTransform = Instantiate(TextPopup_Prefab_01) as RectTransform;
        m_TextPopup_RectTransform.SetParent(m_Canvas.transform, false);
        m_TextPopup_TMPComponent = m_TextPopup_RectTransform.GetComponentInChildren<TextMeshProUGUI>();
        m_TextPopup_RectTransform.gameObject.SetActive(false);

        m_TextPopup_TMPComponent.text = ""; // initialize to prevent null

        //scr_System_CampaignManager.current.Observer_UpdateNotice += UpdateNotice;
        scr_UpdateHandler.current.Observer_PreUpdateTime += UpdateNotice;
    }

    private void UpdateNotice()
    {
        NotifyExit();
    }

    private void OnDestroy()
    {
        scr_UpdateHandler.current.Observer_PreUpdateTime -= UpdateNotice;
    }
    /*
    private float timer = 0.0f;
    private float waitTime = 0.1f;
    private bool timerStart = false;
    private int linkIndex;
    void LateUpdate()
    {
        if (timerStart == true && target != null && script != null)
        {
            timer += Time.deltaTime;
            if (timer > waitTime)
            {
                timer = 0.0f;
                linkIndex = TMP_TextUtilities.FindIntersectingLink(target, Input.mousePosition, m_Camera);
                if (linkIndex == -1)
                    {
                        timerStart = false;
                        timer = 0.0f;
                        NotifyExit();
                }

            }
        }
    }*/
    float x_offset, y_offset;
    int anchor_x, anchor_y;
    Vector3 worldPointInRectangle;
    //public void NotifyHover(TextMeshProUGUI m_TextMeshPro, TMPro.Examples.TMP_TextSelector_B_edited script, string text)
    public void NotifyHover(TextMeshProUGUI m_TextMeshPro, string text)
    {
        if (text.Length < 1) return;
        else if (text == "\n") return;
        //m_TextPopup_RectTransform.gameObject.SetActive(true);
        m_TextPopup_TMPComponent.text += scr_System_Serializer.current.Dictionary.Parse(text);

        x_offset = 15.0f;
        y_offset = -15.0f;
        anchor_x = 0;
        anchor_y = 1;

        if (Screen.width - x_offset - m_TextPopup_TMPComponent.preferredWidth - Input.mousePosition.x < 50)
        {
            anchor_x = 1;
            x_offset = -x_offset;
        }

        if (Input.mousePosition.y + y_offset - m_TextPopup_TMPComponent.preferredHeight < 50)
        {
            anchor_y = 0;
            y_offset = -y_offset;
        }
        //m_TextPopup_RectTransform.gameObject.SetActive(false);
        //m_TextPopup_RectTransform.position = worldPointInRectangle + new Vector3(x_offset, y_offset);
        m_TextPopup_RectTransform.anchorMin = new Vector2(anchor_x, anchor_y);
        m_TextPopup_RectTransform.anchorMax = new Vector2(anchor_x, anchor_y);
        m_TextPopup_RectTransform.pivot = new Vector2(anchor_x, anchor_y);

        RectTransformUtility.ScreenPointToWorldPointInRectangle(m_TextMeshPro.rectTransform, Input.mousePosition + new Vector3(x_offset, y_offset), m_Camera, out worldPointInRectangle);
        m_TextPopup_RectTransform.position = worldPointInRectangle;
        // check sibling index
        m_TextPopup_RectTransform.SetSiblingIndex(m_Canvas.transform.childCount - 1);
        //m_TextPopup_RectTransform.gameObject.SetActive(true);

        
    }

    private void LateUpdate()
    {
        if (m_TextPopup_TMPComponent.text.Length < 1) m_TextPopup_RectTransform.gameObject.SetActive(false);
        else m_TextPopup_RectTransform.gameObject.SetActive(true);
    }

    public void NotifyExit()
    {

        //m_TextPopup_RectTransform.gameObject.SetActive(false);
        m_TextPopup_TMPComponent.text = "";
    }

}

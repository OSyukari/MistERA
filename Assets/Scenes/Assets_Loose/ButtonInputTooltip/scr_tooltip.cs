using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Text;

// https://stackoverflow.com/questions/10967786/how-to-encode-and-decode-broken-chinese-unicode-characters
public class scr_tooltip : MonoBehaviour
{
    Encoding Windows1252 = Encoding.GetEncoding("Windows-1252");
    Encoding Utf8 = Encoding.UTF8;


    TMP_Text text;
    RectTransform rectTransform;
    // Start is called before the first frame update
    void Start()
    {
        text = GetComponentInChildren<TMP_Text>();
        rectTransform = GetComponent<RectTransform>();
    }

    private void Update()
    {
        if (text.text == null || text.text.Length < 1)
        {
            this.enabled = false;
        }
        else
        {
            this.enabled = true;
        }
    }


    float x_offset, y_offset;
    int anchor_x, anchor_y;
    public void EnableTooltip(string content)
    {

        text.text = content;
        //text.text = Utf8.GetString(Windows1252.GetBytes(content));

        LayoutRebuilder.ForceRebuildLayoutImmediate(text.rectTransform);
        x_offset = 15.0f;
        y_offset = -15.0f;
        anchor_x = 0;
        anchor_y = 1;

        while (Screen.width < text.preferredWidth)
        {
            text.fontSize -= 1;
        }
        while (Screen.height < text.preferredHeight)
        {
            text.fontSize -= 1;
        }

        float i = Screen.width - text.preferredWidth - Input.mousePosition.x;
        if (i < 0)
        {
            //anchor_x = 1;
            x_offset += i;
        }

        float j = Input.mousePosition.y - text.preferredHeight;
        if (j < 0)
        {
            //anchor_y = 0;
            y_offset += j;
        }

        rectTransform.position = Input.mousePosition + new Vector3(x_offset, y_offset);
        rectTransform.anchorMin = new Vector2(anchor_x, anchor_y);
        rectTransform.anchorMax = new Vector2(anchor_x, anchor_y);
        rectTransform.pivot = new Vector2(anchor_x, anchor_y);
        LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
        this.enabled = true;
    }

    public void DisableTooltip()
    {
        text.text = "";
        //this.enabled = false;
    }
}

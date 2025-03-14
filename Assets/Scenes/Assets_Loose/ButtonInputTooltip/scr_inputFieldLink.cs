using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class scr_inputFieldLink:MonoBehaviour
{
    private TextMeshProUGUI parent;
    public TextMeshProUGUI placeholder;

    public void Initialize(TextMeshProUGUI parent, string content="")
    {
        this.parent = parent;
        this.placeholder.text = content;
    }

    public void OnValueChange(string s)
    {
        parent.text = s;
    }


    public void Destroy(string input)
    {
        if(input == null || input == "")
        {

        }
        else
        {
            parent.text = input;
        }

        Destroy(this.gameObject);
    }
}

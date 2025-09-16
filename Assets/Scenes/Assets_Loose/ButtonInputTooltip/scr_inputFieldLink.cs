using UnityEngine;
using TMPro;

public class scr_inputFieldLink:MonoBehaviour
{
    private TextMeshProUGUI parent;
    public TextMeshProUGUI placeholder;
    public TMP_InputField self_inputfield;
    public TMP_Text self_placeholder;
    public TMP_Text self_text;


    protected void Awake()
    {
        var setting = scr_System_CentralControl.current.DisplaySetting;

        this.self_text.color = setting.TextColor_neutral.Color;
        this.self_placeholder.color = setting.TextColor_disabled.Color;

        var color = this.self_inputfield.colors;//.normalColor = 
        color.normalColor = setting.BackgroundColor_Transparent.Color;
        color.selectedColor = setting.BackgroundColor_Transparent.Color;

        this.self_inputfield.selectionColor = setting.BackgroundColor_Transparent.Color;
    }

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

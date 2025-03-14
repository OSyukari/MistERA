using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class scr_SelectableText : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{

    protected TextMeshProUGUI text = null;

    protected scr_Menu parent;

    public bool isButtonToggle = false;
    private bool isToggled = false;
    public bool IsToggled{ get{ return isToggled; } }
    protected bool isValid;
    protected string customTooltip = "";
    public Color32 baseColor, hoverColor, disableColor, errorColor, toggleColor;

    public int optionID = -1;
    public string linkText = "";
    public bool leadingSpace = false;
    public bool showOptionID = false;
    public bool showBrackets = true;
    public bool useDisabledColorWhenUntoggled = false;

    protected Color32 BaseColor { get
        {
            if (isButtonToggle && useDisabledColorWhenUntoggled) return disableColor;
            else return baseColor;
        } }

    protected Color32 ToggledColor { get
        {
            if (isButtonToggle && useDisabledColorWhenUntoggled) return baseColor;
            else return toggleColor;
        } }

    private ActionScript onHoverEnter = null;
    private ActionScript onHoverExit = null;
    private ActionScript onClick = null;

    public delegate void ActionScript();

    protected bool initialized = false;
    public bool forbidNotify = false;
    private void Awake()
    {
        if (!initialized)
        {
            initialized = true;
            text = GetComponent<TextMeshProUGUI>();
            // parent does not exist at this moment ?

            baseColor = scr_System_CentralControl.current.pref.TextColor_neutral;
            hoverColor = scr_System_CentralControl.current.pref.TextColor_hover;
            disableColor = scr_System_CentralControl.current.pref.TextColor_disabled;
            errorColor = scr_System_CentralControl.current.pref.TextColor_conflict;
            toggleColor = scr_System_CentralControl.current.pref.TextColor_toggle;
        }
    }

    public TextMeshProUGUI Text { get {  if (!initialized) Awake();
            return text;
     } }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (isValid)
        {
            if (validator == null || validator.State == ButtonValidator_States.Valid)
            {
                if (isButtonToggle && isToggled) Text.color = ToggledColor;
                else Text.color = BaseColor;
                if (onHoverExit != null) onHoverExit();
            }

            else Text.color = errorColor;
        }
    }

    public void AttachOnHoverEnter(ActionScript script) { this.onHoverEnter = script; }
    public void AttachOnHoverExit(ActionScript script) { this.onHoverExit = script; }
    public void AttachOnClick(ActionScript script) { this.onClick = script; }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (isValid)
        {
            Text.color = hoverColor;
            if (onHoverEnter != null) onHoverEnter();
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.pointerId == -1 && isValid && optionID != -1 && !forbidNotify)
        {
            //Debug.Log("SelectableText Notify Parent : optionID [" + optionID + "]");
            parent.Notify(optionID);
            if (onClick != null) onClick();
        }
    }

    private ButtonValidator validator = null;
    public ButtonValidator Validator { get { return validator; } }
    public void Validate()
    {
        if (!initialized) Awake();
        this.text.text = scr_System_Serializer.current.Dictionary.QueryThenParse(this.text.text);

        if (validator != null)
        {
            isValid = validator.IsButtonValid();
            customTooltip = validator.Tooltip;
            if (isValid)
            {
                if (validator.State == ButtonValidator_States.Valid)
                {
                    if (isButtonToggle && isToggled) Text.color = ToggledColor;
                    else Text.color = BaseColor;
                }else if (validator.State == ButtonValidator_States.Conflict)
                {
                    Text.color = errorColor;
                }
            }
            else
            {
                //Debug.Log("custom tooltip [" + customTooltip+"]");
                Text.color = disableColor;
            }
        }
        else
        {   // validator null always valid
            isValid = true;
            customTooltip = "";
            if (isButtonToggle && isToggled) Text.color = ToggledColor;
            else Text.color = BaseColor;

            //this.text.text = scr_System_Serializer.current.Dictionary.Parse(this.text.text);
        }

    }

    public void Toggle(bool forceValue = false, bool value = false){
        if (isButtonToggle){
            if (forceValue) isToggled = value;
            else isToggled = !isToggled;
        }
    }

    public void SetText(string s, bool newLine = false)
    {
        s = scr_System_Serializer.current.Dictionary.QueryThenParse(s);
        this.Text.text = "<link=" + (linkText.Length > 0 ? linkText : "") + ">" + (leadingSpace ? " " : "") + ((showOptionID) ? "[" + optionID + "] " + s : (showBrackets?"[":"") + s + (showBrackets ? "]" : "")) + "</link>"+(newLine ? "\n ":"");
    }
    /// <summary>
    /// Return tooltop written by validator
    /// </summary>
    /// <returns></returns>
    public string GetCustomTooltip()
    {
        /*
        if (isValid) return (linkText.Length > 0 ? linkText + "\n" + customTooltip : customTooltip);
        else return (linkText.Length > 0 ? linkText + "\n" + "<color=red>" + customTooltip + "</color>" : "<color=red>" + customTooltip + "</color>");
            // "<color=red>" + customTooltip + "</color>";
        */
        if (this.optionID == -1) { return ""; }
        else{ 
            if (validator == null || validator.State == ButtonValidator_States.Valid)
            {
                return validator.Tooltip;
            } else {
                return "<color=red>" + validator.Tooltip + "</color>";
            }
        }

    }

    private string replaceText = "";

    /// <summary>
    /// 
    /// </summary>
    /// <param name="parent">script that dispatch validator, on command re-validate all buttons, and receive button click response</param>
    /// <param name="v">validator for button, dispatched by parent, has pointer to all info he know, return bool and a string for customTooltip</param>
    public void Initialize(scr_Menu parent, ButtonValidator v)
    {
        if (!initialized) Awake();
        replaceText = this.GetComponent<scr_HoverableText>().replaceText;
        //Debug.Log("initiaze button parent[" + parent + "] validator[" + v + "] text ["+this.Text.text+"]");
        if (parent == null || v == null) Debug.Log("button.initialize failed cuz parent["+parent+"] or validator["+ v+"]");
        this.validator = v;
        this.parent = parent;
        this.Text.text = this.replaceText != "" ? scr_System_Serializer.current.Dictionary.QueryThenParse(replaceText) : scr_System_Serializer.current.Dictionary.QueryThenParse(this.Text.text);
        this.Text.text = "<link=" + (linkText.Length > 0?linkText:"") + ">" + (leadingSpace ? " " : "") + ((showOptionID) ? "["+optionID+"] "+this.Text.text : (showBrackets ? "[" : "") + this.Text.text+ (showBrackets ? "]" : "")) + "</link>";
   
        
    }


}

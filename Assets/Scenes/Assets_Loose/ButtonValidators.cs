using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum ButtonValidator_States
{
    /// <summary>
    /// this button is enabled, can be click on, and the content is valid
    /// </summary>
    Valid,
    /// <summary>
    /// this button is disabled and shouldnt be clicked on
    /// </summary>
    Invalid,
    /// <summary>
    /// this button is enabled, can be clicked on, the content is invalid and should be changed
    /// </summary>
    Conflict
}

public abstract class ButtonValidator
{
    /// <summary>
    /// Should modify 2 parameter before return Bool: this.tooltip, this.state
    /// </summary>
    /// <param name="tooltip"></param>
    /// <returns></returns>
    public abstract bool IsButtonValid();

    protected ButtonValidator_States state = ButtonValidator_States.Valid;
    public ButtonValidator_States State { get { return state; }  }

    protected string tooltip = "";
    public string Tooltip { get { return tooltip; } }

    //public virtual bool Clickable { get { return false; } }

    protected scr_Menu parent;
    public ButtonValidator(scr_Menu parent)
    {
        this.parent = parent;
    }

    public virtual void Destroy()
    {

    }

}

public class ButtonValidator_AlwaysTrue : ButtonValidator
{
    public ButtonValidator_AlwaysTrue(scr_Menu parent) : base(parent)
    {
    }

    public override bool IsButtonValid()
    {
        state = ButtonValidator_States.Valid;
        tooltip = "";
        return true;
    }
}

public class ButtonValidator_AlwaysFalse : ButtonValidator
{
    public ButtonValidator_AlwaysFalse(scr_Menu parent) : base(parent)
    {
    }

    public override bool IsButtonValid()
    {
        state = ButtonValidator_States.Invalid;
        tooltip = "This feature is currently on cutting floor.";
        return false;
    }
}

public interface I_ButtonClickable
{
    public void OnClickButton();
}

public interface I_ConflictCatcher{
    public void NotifyConflict(string tooltip);
    public void Reset();
}
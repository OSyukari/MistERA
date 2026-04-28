using System;
using System.Collections.Generic;
using System.Text;


public class ActionPackageOptions
{
    public string optionName = "";
    public Action callback = null;

    public ActionPackageOptions(string optionName, Action callback)
    {
        this.optionName = optionName;
        this.callback = callback;
    }
}


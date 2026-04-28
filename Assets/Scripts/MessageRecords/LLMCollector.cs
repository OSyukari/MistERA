using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;


public class LLMCollector
{
    public MessageJSON json = null;

    public LLMCollector() { }
    public LLMCollector(scr_menu_LLMQuery query) 
    {
        json = query.CurrentResponse.JSON;
    
    }
}


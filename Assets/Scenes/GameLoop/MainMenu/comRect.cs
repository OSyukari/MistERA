using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class comRect : MonoBehaviour
{
    public List<string> accepteTags_any = new List<string>();
    public List<string> accepteTags_all = new List<string>();
    public List<string> excludeTags_any = new List<string>();
    public List<string> excludeTags_all = new List<string>();

    public RectTransform selfRect;

    public bool Match(List<string> comtags)
    {
        if (!Utility.ListContainsStrict(comtags, accepteTags_all)) return false;
        else if (!Utility.ListContainsLoose(comtags, accepteTags_any)) return false;

        else if (Utility.ListContainsLoose(excludeTags_any, comtags)) return false;
        else if (Utility.ListContainsStrict(excludeTags_all, comtags)) return false;

        else return true;

    }
}

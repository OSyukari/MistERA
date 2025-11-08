using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class scr_MessageLogBox : MonoBehaviour
{

    protected void Start()
    {
        //selfText2 = GetComponent<TMP_Text>();
    }
    protected TMP_Text selfText2;
    public void Initialize(PortraitManager portrait)
    {
        this.portrait = portrait;
    }

    protected PortraitManager portrait = null;

}

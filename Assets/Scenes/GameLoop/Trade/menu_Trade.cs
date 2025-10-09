using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class menu_Trade : scr_Menu, IPointerClickHandler
{

    public bool isHostile = false;
    public bool isFriendly = false;

    public bool allowHostile = false;
    public bool allowKill = false;
    public bool allowTransfer = false;
    public bool allowChara = false;

    public scr_charaEntry prefab_chara;
    public scr_itemEntry prefab_item;

    public scr_HoverableText A_Name, B_Name;
    public RectTransform A_listRect, B_listRect;

    protected override void Awake()
    {
        base.Awake();
        this.m_Canvas.overrideSorting = true;
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);
    }

    public bool allowDismember(Character_Trainable c, bool isTeamA)
    {
        if (isTeamA) return false;
        if (!allowKill) return false;
        if (!c.isHumanoid) return true;
        return !scr_System_CentralControl.current.isSafeMode && scr_System_CentralControl.current.ContentSetting.DismemberMode == Dismember_Mode.dead_and_living;
    }

    public bool allowInventory(bool isTeamA)
    {
        if (isTeamA) return false;
        if (isFriendly) return true;
        if (isHostile || allowHostile) return true;
        return false;
    }

    public I_IsJobGiver a = null, b = null;

    List<scr_charaEntry> list_c = new List<scr_charaEntry>();
    List<scr_itemEntry> list_i = new List<scr_itemEntry>();

    public void InitializeWithArgument(I_IsJobGiver a, I_IsJobGiver b, bool allowChara, bool allowHostile, bool allowKill, bool allowTransfer)
    {
        list_c.Clear(); list_i.Clear();

        this.a = a; this.b = b;
        this.allowTransfer = allowTransfer;
        this.allowKill = allowKill;
        this.allowHostile = allowHostile;
        this.allowChara = allowChara;

        this.isHostile = FactionUtility.isFactionHostile(a, b);
        this.isFriendly = FactionUtility.isFactionFriendly(a, b);

        A_Name.SetText(a.FactionDisplayName);
        B_Name.SetText(b.FactionDisplayName);

        var color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;
        if (allowChara)
        {
            foreach(var c in a.ManagedChara)
            {
                var fab = Instantiate(prefab_chara);
                fab.selfRect.SetParent(A_listRect, false);
                fab.InitChara(this, c, true);
                fab.selfImage.color = color;
                list_c.Add(fab);
            }

            foreach (var c in b.ManagedChara)
            {
                var fab = Instantiate(prefab_chara);
                fab.selfRect.SetParent(B_listRect, false);
                fab.InitChara(this, c, false);
                fab.selfImage.color = color;
                list_c.Add(fab);
            }
        }

        foreach(var i in a.Inventory.Contents)
        {
            var fab = Instantiate(prefab_item);
            fab.selfRect.SetParent(A_listRect, false);
            fab.InitItem(this, i, true, b.Inventory, a.Inventory);
            fab.selfImage.color = color;
            list_i.Add(fab);
        }

        foreach (var i in b.Inventory.Contents)
        {
            var fab = Instantiate(prefab_item);
            fab.selfRect.SetParent(B_listRect, false);
            fab.InitItem(this, i, false, a.Inventory, b.Inventory);
            fab.selfImage.color = color;
            list_i.Add(fab);
        }

        ValidateAll();
    }


    public scr_Menu_CharaDetail prefab_Canvas_CharaDetail;
    public override void Initialize()
    {
        base.Initialize();

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            // Debug.Log("Button " + button + " " + button.optionID);
            switch (button.optionID)
            {

                case 9999: // exit
                    button.Initialize(this, button_alwaysValid); break;
                default: break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }
        // build all presetLis

    }
    public override void ValidateAll()
    {
        base.ValidateAll();
    }

    public void OnPointerClick(PointerEventData eventData)
    {

        /*
         * Trade Menu should NOT be able to right click exit
         * 
        // if click outside box
        if ((eventData.rawPointerPress.GetComponent<scr_Canvas_Management>() != null) || (eventData.button == PointerEventData.InputButton.Right && UtilityEX.isClickBelowDragThreshold(eventData)))
        {
            scr_System_SceneManager.current.UnloadLastCanvasFromScene();
        }*/
    }

    public void ResolveAll()
    {
        foreach(var c in list_c)
        {
            c.Resolve();
        }
        foreach(var i in list_i)
        {
            i.Resolve();
        }
    }

    public override void Notify(int optionID)
    {
        //Debug.Log("Parent Notified ! [" + optionID + "]");
        ButtonValidator validator = validatorsByID[optionID];
        I_ButtonClickable button = validator as I_ButtonClickable;
        if (button != null)
        {
            button.OnClickButton();
        }
        else
        {
            switch (optionID)
            {
                case 9999:
                    ResolveAll();
                    scr_System_SceneManager.current.UnloadLastCanvasFromScene(); break;
                default: break;
            }
        }
        ValidateAll();
    }
    protected override void OnDestroy()
    {
        base.OnDestroy();
        scr_System_CampaignManager.current.NotifyUpdate();

    }



    public void RegisterBtn(scr_SelectableText button, ButtonValidator validator)
    {
        int optionID = AssertUniqueHash(button.GetHashCode());

        if (!buttonsByID.ContainsKey(optionID))
        {
            button.Initialize(this, validator);
            button.optionID = optionID;
            buttonsByID.Add(button.optionID, button);
            validatorsByID.Add(button.optionID, button.Validator);
            // button.Validate();
            // return true;
        }
        else
        {
            Debug.LogError($"menu_Trade registerbtn hash collision on {optionID}");
        }
    }

}

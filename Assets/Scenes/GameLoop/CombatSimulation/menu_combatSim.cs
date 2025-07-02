using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System.Linq;


public class menu_combatSim : scr_Menu, IPointerClickHandler
{

    TeamComposition teamA = null;
    TeamComposition teamB = null;

    public RectTransform playerTeamList;
    System.Action successCallback;
    System.Action failCallback;
    public void InitializeWithArgument(List<int> actors, System.Action successCallback = null, System.Action failCallback = null)
    {
        if (!initialized) Initialize();

        this.successCallback = successCallback;
        this.failCallback = failCallback;

        var playerRef = scr_System_CampaignManager.current.Player.RefID;
        // make stuff
        teamA = new TeamComposition();
        teamA.frontline = actors.FindAll(x=>x != playerRef);
        if (actors.Contains(playerRef)) teamA.support.Add(playerRef);

        teamB = new TeamComposition();
        teamB.frontline = new List<int>() { scr_System_CampaignManager.current.Combat.Dummy.RefID };

        Utility.DestroyAllChildrenFrom(ref playerTeamList);

        foreach(var actorRef in teamA.Actors)
        {
            MakeCharaLine(actorRef);
        }

        ValidateAll();
    }

    public void ChangeCharaTeam(ref List<int> team, int charaRef)
    {
        if (teamA.frontline != team) teamA.frontline.Remove(charaRef);
        if (teamA.support != team) teamA.support.Remove(charaRef);

        team.Add(charaRef);
    }

    public scr_teamComp prefab_line;

    protected void MakeCharaLine(int charaRef)
    {
        var c = scr_System_CampaignManager.current.FindInstanceByID(charaRef);

        var Line = Instantiate(prefab_line);

        Line.GetComponent<RectTransform>().SetParent(playerTeamList, false);
        Line.charaName.SetText(c.FirstName);

        RegisterBtn(Line.btn_frontline, new Button_selectTeam(this, Line.btn_frontline,charaRef, ref teamA.frontline));
        RegisterBtn(Line.btn_support, new Button_selectTeam(this, Line.btn_support, charaRef, ref teamA.support));


    }

    protected void RegisterBtn(scr_SelectableText button, ButtonValidator validator)
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
            Debug.LogError($"menu_combatsim registerbtn hash collision on {optionID}");
        }
    }


    protected override void Awake()
    {
        base.Awake();
        this.m_Canvas.overrideSorting = true;
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);
    }

    public override void Initialize()
    {
        base.Initialize();

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            // Debug.Log("Button " + button + " " + button.optionID);
            switch (button.optionID)
            {
                case 9998:
                    button.Initialize(this, new Button_StartCombat(this, button));
                    break;
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
        // if click outside box
        if ((eventData.rawPointerPress.GetComponent<scr_Canvas_Management>() != null) || (eventData.button == PointerEventData.InputButton.Right && Utility.isClickBelowDragThreshold(eventData)))
        {
            scr_System_SceneManager.current.UnloadLastCanvasFromScene();
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
                    if (this.failCallback != null) failCallback.Invoke();
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

    public class Button_StartCombat: ButtonValidator, I_ButtonClickable
    {
        new menu_combatSim parent;
        scr_SelectableText button;
        public Button_StartCombat(menu_combatSim parent, scr_SelectableText button) : base(parent)
        {
            this.parent = parent;
            this.button = button;
        }

        public override bool IsButtonValid()
        {
            if (parent.teamA == null || parent.teamA.Actors.Count < 1)
            {
                this.tooltip = "teamA is empty";
                return false;
            }
            else if (parent.teamA.frontline.Count < 1)
            {
                this.tooltip = "teamA must have frontline member";
                return false;
            }
            else if (parent.teamB == null || parent.teamB.Actors.Count < 1)
            {
                this.tooltip = "teamB is empty";
                return false;
            }
            else if (parent.teamB.frontline.Count < 1)
            {
                this.tooltip = "teamB must have frontline member";
                return false;
            }

            return true;

        }

        public void OnClickButton()
        {
            if (parent.successCallback != null) parent.successCallback.Invoke();
            scr_System_CampaignManager.current.StartCombat(parent.teamA, parent.teamB);
            scr_System_SceneManager.current.UnloadLastCanvasFromScene();
        }
    }


    public class Button_selectTeam : ButtonValidator, I_ButtonClickable
    {
        new menu_combatSim parent;
        scr_SelectableText button;
        List<int> team;
        int charaRef;
        public Button_selectTeam(menu_combatSim parent, scr_SelectableText button, int charaRef, ref List<int> team) : base(parent)
        {
            this.parent = parent;
            this.button = button;
            this.charaRef = charaRef;
            this.team = team;
        }

        public override bool IsButtonValid()
        {
            if (team.Contains(charaRef))
            {
                button.SetText(" O ");
                return false;
            }
            else
            {
                button.SetText(" X ");
                return true;
            }
        }

        public void OnClickButton()
        {
            parent.ChangeCharaTeam(ref team, charaRef);
        }
    }
}

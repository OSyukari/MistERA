using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.EventSystems;


public class menu_combatSim : scr_Menu, IPointerClickHandler
{
    public List<string> combatSimTargets = new List<string>();
    public List<string> randomWeaponLists = new List<string>();

    TeamComposition teamA = null;
    TeamComposition teamB = null;

    public scr_HoverableText DummyNames;
    public RectTransform playerTeamList;
    System.Action successCallback;
    System.Action failCallback;

    CombatManager combatManager;

    public void InitializeWithArgument(List<int> actors, System.Action successCallback = null, System.Action failCallback = null)
    {
        combatManager = scr_System_CampaignManager.current.Combat;
        if (!initialized) Initialize();

        this.successCallback = successCallback;
        this.failCallback = failCallback;

        var playerRef = scr_System_CampaignManager.current.Player.RefID;
        // make stuff
        teamA = new TeamComposition();
        teamA.frontline = actors.FindAll(x=>x != playerRef);
        if (actors.Contains(playerRef)) teamA.support.Add(playerRef);

        teamB = new TeamComposition();
        teamB.frontline = new List<int>();

        for(int i = combatSimTargets.Count - 1; i >= 0; i--)
        {

            var team = scr_System_Serializer.current.MasterList.Encounters.GetByID(combatSimTargets[i]);
            if (team == null) combatSimTargets.RemoveAt(i);

        }

        SetCombatSimTargets(0);
        
        Utility.DestroyAllChildrenFrom( playerTeamList);

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

    protected void MakeCharaLine(Character_Trainable c)
    {
        var Line = Instantiate(prefab_line);

        Line.GetComponent<RectTransform>().SetParent(playerTeamList, false);
        Line.charaName.SetText(c.FirstName);

        RegisterBtn(Line.btn_frontline, new Button_selectTeam(this, Line.btn_frontline,c.RefID, ref teamA.frontline));
        RegisterBtn(Line.btn_support, new Button_selectTeam(this, Line.btn_support, c.RefID, ref teamA.support));


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
                case 9996:
                    button.Initialize(this, new Button_toggleDummy(this, button, true));
                    break;
                case 9997:
                    button.Initialize(this, new Button_toggleDummy(this, button, false));
                    break;
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
        if ((eventData.rawPointerPress.GetComponent<scr_Canvas_Management>() != null) || (eventData.button == PointerEventData.InputButton.Right && UtilityEX.isClickBelowDragThreshold(eventData)))
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
        List<string> a, b;
        public Button_StartCombat(menu_combatSim parent, scr_SelectableText button) : base(parent)
        {
            this.parent = parent;
            this.button = button;
            a = new List<string>();
            b = new List<string>();
        }

        public override bool IsButtonValid()
        {
            this.tooltip = "";
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

            a.Clear();
            b.Clear();
            bool hasweapon_a = true;
            foreach (var i in parent.teamA.Actors)
            {
                a.Add(i.FirstName);
                hasweapon_a = hasweapon_a && !i.Inventory.HasWeapon();
            }
            foreach (var i in parent.teamB.Actors) b.Add(i.CallName);


            this.tooltip = LocalizeDictionary.QueryThenParse("ui_combatSim_starting")
                .Replace("$names_a$", String.Join("|", a))
                .Replace("$names_b$", String.Join("|", b));// $"Starting Combat between {} and {String.Join("|", b)}\n";

            if (!hasweapon_a)
            {
                this.tooltip += $"\n{LocalizeDictionary.QueryThenParse("ui_combatSim_randWeapon")}";
            }
            return true;
        }

        public void OnClickButton()
        {
            if (parent.successCallback != null) parent.successCallback.Invoke();

            foreach(var i in parent.teamA.Actors)
            {
                if (!i.Inventory.HasWeapon())
                {
                    var weapon = WorldManager.Instantiate(Utility.GetRandomElement(parent.randomWeaponLists));
                    i.Inventory.AddItem(weapon);
                }
            }
            
            scr_System_CampaignManager.current.StartCombat(parent.teamA, parent.teamB, "exp_event_Utnapishtim_combat_training_end", "exp_event_Utnapishtim_combat_training_end", "exp_event_Utnapishtim_combat_training_end");
            scr_System_SceneManager.current.UnloadLastCanvasFromScene();
        }
    }

    public int CurrentDummy = 0;
    protected void SetCombatSimTargets(int i)
    {
        if (i < 0) i = this.combatSimTargets.Count - 1;
        else if (i >= this.combatSimTargets.Count) i = 0;
        CurrentDummy = i;

        var nextTarget = this.combatSimTargets[CurrentDummy];


        SetCombatSimTargets(nextTarget);

    }

    protected void SetCombatSimTargets(string nextTarget)
    {
        //Debug.Log($"getting combat dummy {nextTarget}");
        teamB.Clear();
        var team = scr_System_Serializer.current.MasterList.Encounters.GetByID(nextTarget);
        var tooltips = new List<string>();  
        foreach(var name in team.frontline)
        {
            var chara = scr_System_CampaignManager.current.Combat.GetCombatDummy(name, teamB.ActorRefs);
            teamB.frontline.Add(chara.RefID);
            teamB.NotifyAddActor();
            List<string> inv = new List<string>();
            foreach (var i in chara.Inventory.Contents) inv.Add(i.Print());
            tooltips.Add($"{chara.CallName} {chara.BaseID}: {String.Join("|", inv)}");
        }
        foreach (var name in team.support)
        {
            var chara = scr_System_CampaignManager.current.Combat.GetCombatDummy(name, teamB.ActorRefs);
            teamB.support.Add(chara.RefID);
            teamB.NotifyAddActor();
            List<string> inv = new List<string>();
            foreach (var i in chara.Inventory.Contents) inv.Add(i.Print());
            tooltips.Add($"{chara.CallName} {chara.BaseID}: {String.Join("|", inv)}");
        }
        DummyNames.SetText(team.Name);
        DummyNames.SetExternalTooltip(String.Join("\n", tooltips));

        ValidateAll();
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

    public class Button_toggleDummy : ButtonValidator, I_ButtonClickable
    {
        new menu_combatSim parent;
        scr_SelectableText button;
        bool prev;
        public Button_toggleDummy(menu_combatSim parent, scr_SelectableText button, bool prev) : base(parent)
        {
            this.parent = parent;
            this.button = button;
            this.prev = prev;
        }
        public override bool IsButtonValid()
        {
            return parent.combatSimTargets.Count > 1;
        }

        public void OnClickButton()
        {
            parent.SetCombatSimTargets(prev ? parent.CurrentDummy - 1 : parent.CurrentDummy + 1);
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System;
using Cysharp.Threading.Tasks.Triggers;
using UnityEngine.UIElements;

public class scr_Menu_Combat : scr_Menu
{


    public enum CombatUI
    {
        Overview,
        SkillSelect,
        Animating
    }

    public List<CanvasGroup> group_Overview;
    public List<CanvasGroup> group_Select;
    public List<CanvasGroup> group_Animating;
    public ActionList actionList;
    public scr_AnimatingClickIntercept AnimatingClick;
    public scr_HoverableText turnCounter;

    protected CombatUI _selfMode = CombatUI.Overview;

    List<CombatActionInstance> currentActionsBackup;

    public RectTransform DebugPanel;

    public CombatUI CurrentMode
    {
        get
        {
            return _selfMode;
        }
        set
        {
            _selfMode = value;
            switch(_selfMode)
            {
                case CombatUI.Animating:
                    foreach (var group in group_Select) viewDisable(group);
                    foreach (var group in group_Overview) viewDisable(group);
                    foreach (var group in group_Animating) viewEnable(group);
                    break;
                case CombatUI.Overview:
                    foreach (var group in group_Animating) viewDisable(group);
                    foreach (var group in group_Select) viewDisable(group);
                    foreach (var group in group_Overview) viewEnable(group);
                    break;
                case CombatUI.SkillSelect:
                    foreach (var group in group_Animating) viewDisable(group);
                    foreach (var group in group_Overview) viewDisable(group);
                    foreach (var group in group_Select) viewEnable(group);
                    break;
            }
        }
    }

    public RectTransform SkillsTab;
    menu_SkillSelect _menu_SkillSelect = null;
    menu_SkillSelect menu_SkillSelect { get { return _menu_SkillSelect; } set { 
        //if (this._menu_SkillSelect != null) this._menu_SkillSelect.Notify(9999);
        this._menu_SkillSelect = value;
        } }
    public void OpenSkillSelectMenu(Character_Trainable c, int actionIndex, CombatActionInstance innerInstance)
    {
        // innerInstance can be null

        CurrentMode = CombatUI.SkillSelect;

        // backup of currently active actions list
        currentActionsBackup = new List<CombatActionInstance>(currentActiveCombat.ActionsOngoing);

        menu_SkillSelect = scr_System_SceneManager.current.LoadCanvasIntoScene(this, SkillsTab).GetComponent<menu_SkillSelect>();
        menu_SkillSelect.InitializeWithArgument(this, c, actionIndex, innerInstance);
    }
    public void OpenEOTSelectMenu(Character_Trainable c, CombatActionInstance innerInstance)
    {
        // innerInstance can be null

        CurrentMode = CombatUI.SkillSelect;

        // backup of currently active actions list
        currentActionsBackup = new List<CombatActionInstance>(currentActiveCombat.ActionsOngoing);

        menu_SkillSelect = scr_System_SceneManager.current.LoadCanvasIntoScene(this, SkillsTab).GetComponent<menu_SkillSelect>();
        menu_SkillSelect.InitializeWithArgument_EOT(this, c, currentActiveCombat.EOTIndex/-10, innerInstance);
    }

    public void SetEOTAction(Character_Trainable c, CombatActionInstance cai)
    {
        currentActiveCombat.SetEOTCounter(c, cai);
    }

    public void FinalizeSkillSelect(bool abort = false)
    {
        CurrentMode = CombatUI.Overview;
        // disable view

        menu_SkillSelect = null;

        // if abort, revert action list and recalculate
        if (abort) currentActiveCombat.InsertAction(null, this.currentActionsBackup);
        else OnInstanceUpdate();
        // else, overwrite action list

    }

    private void viewEnable(CanvasGroup group, bool toggleActivate = true)
    {
        if (toggleActivate) group.gameObject.SetActive(true);

        group.alpha = 1;
        group.interactable = true;
        group.blocksRaycasts = true;
    }

    private void viewDisable(CanvasGroup group, bool toggleActivate = true)
    {
        group.alpha = 0;
        group.interactable = false;
        group.blocksRaycasts = false;
        if (toggleActivate) group.gameObject.SetActive(false);
    }

    protected override void Awake()
    {
        base.Awake();

        scr_System_CampaignManager.current.Observer_CurrentViewMode += OnViewModeChange;
        scr_System_CampaignManager.current.Observer_UpdateNotice += OnUpdateNotice;

    }

    protected override void Start()
    {
        base.Start();
        AnimatingClick.Observer_AnimatingClicks += Click;
        if (_turnCount == string.Empty) _turnCount = LocalizeDictionary.QueryThenParse("ui_combat_title2");
    }

    protected void Click()
    {
        Debug.Log("Clicked!");
    }

    HashSet<int> TemporaryButtonRefs = new HashSet<int>();

    bool forceReload = false;
    public void NotifyForceReload()
    {
        forceReload = true;
    }

    Action PostValidateCallback = null;
    Action SkipValidateCallback = null;

    public Button_ModActionCount MakeModCountButton(scr_prefab_actortab tab, scr_SelectableText btn, bool isReduce)
    {
        btn.Initialize(this, new Button_ModActionCount(this, tab, currentActiveCombat, isReduce));
        btn.optionID = AssertUniqueHash(btn.GetHashCode());
        this.buttonsByID.Add(btn.optionID, btn);
        this.validatorsByID.Add(btn.optionID, btn.Validator);

        TemporaryButtonRefs.Add(btn.optionID);

        return btn.Validator as Button_ModActionCount;
    }

    public RectTransform prefab_actionButton;
    public Button_OpenActionSelect MakeActionButton(Character_Trainable c, int actionIndex)
    {
        RectTransform r = Instantiate(prefab_actionButton);
        scr_SelectableText btn = r.GetComponent<scr_SelectableText>();
//Button_OpenActionSelect(scr_Menu_Combat parent, CombatInstance instance, Character_Trainable c, scr_SelectableText text, int actionIndex) : base(parent)
        btn.Initialize(this, new Button_OpenActionSelect(this, currentActiveCombat, c, btn,r, actionIndex));
        btn.SetText("Action");
        btn.optionID = AssertUniqueHash(r.GetHashCode());
        this.buttonsByID.Add(btn.optionID, btn);
        this.validatorsByID.Add(btn.optionID, btn.Validator);

        TemporaryButtonRefs.Add(btn.optionID);

        return btn.Validator as Button_OpenActionSelect;
    }

    public Button_OpenActionSelect MakeEOTActionButton(Character_Trainable c, scr_SelectableText btn)
    {
        //Button_OpenActionSelect(scr_Menu_Combat parent, CombatInstance instance, Character_Trainable c, scr_SelectableText text, int actionIndex) : base(parent)
        btn.Initialize(this, new Button_OpenEOTActionSelect(this, currentActiveCombat, c, btn));
        btn.SetText("Action");
        btn.optionID = AssertUniqueHash(btn.GetHashCode());
        this.buttonsByID.Add(btn.optionID, btn);
        this.validatorsByID.Add(btn.optionID, btn.Validator);

        TemporaryButtonRefs.Add(btn.optionID);

        return btn.Validator as Button_OpenActionSelect;
    }

    public bool isActive = false;
    public CanvasGroup self;
    private void OnViewModeChange(ViewMode vm, bool lockView)
    {
        switch (vm)
        {
            case ViewMode.View_Combat:
                //enable self
                isActive = true;
                viewEnable(self, false);
                CurrentMode = CombatUI.Overview;
                LoadCombatInstance(scr_System_CampaignManager.current.Combat.PlayerCombatInstance);
                this.DebugPanel.gameObject.SetActive(scr_System_CampaignManager.current.DebugMode);
                break;
            default:
                //disable self
                UnloadCombatInstance();
                isActive = false;
                viewDisable(self, false);
                break;
        }
        ValidateAll();
    }

    public CombatInstance currentActiveCombat = null;
    public RectTransform charaList_teamA, charaList_teamB, centerActionList;
    public scr_prefab_actortab prefab_Actor;

    protected void UnloadCombatInstance()
    {
        if (currentActiveCombat != null ) currentActiveCombat.Observer_InstanceUpdate -= OnInstanceUpdate;
        currentActiveCombat = null;

        foreach(var i in TemporaryButtonRefs)
        {
            buttonsByID.Remove(i);
            validatorsByID.Remove(i);
        }
        TemporaryButtonRefs.Clear();
    }

    public scr_bgImageSwapper background;
    public scr_CharPortraitBox left, right;
    protected void LoadCombatRoom(string imagePath)
    {
        if (imagePath != "")
        {
            background.image.color = background.activeColor;
            if (background.co != null)
            {
                StopCoroutine(background.co);
                background.co = null;
            }
            background.co = StartCoroutine(background.roomchange(imagePath));
        }
        else
        {
            background.image.color = background.disabledColor;
        }
    }

    public void LoadChara(Character_Trainable c, bool isLeft)
    {
        if (c == null) return;
        if (isLeft) left.InitializeWithArgument(c);
        else right.InitializeWithArgument(c);
    }

    protected void LoadCombatInstance(CombatInstance instance)
    {
        if (this.currentActiveCombat == instance) return;
        if (instance == null) return;
        if (this.currentActiveCombat != null) UnloadCombatInstance();
        actorTabs.Clear();

        LoadCombatRoom(instance.backgroundImgPath);

        this.currentActiveCombat = instance;
        this.currentActiveCombat.Observer_InstanceUpdate += OnInstanceUpdate;

        Utility.DestroyAllChildrenFrom (charaList_teamA);
        Utility.DestroyAllChildrenFrom (charaList_teamB);

        //scrollLeft = left.GetComponent<ScrollRect>();
        //scrollRight = right.GetComponent<ScrollRect>();
        // for each team, draw their character box in respective transform
        // for enemy team, decide on their action and draw action


        // for self team, draw selectable action button
        // on click, swap panel
        OnInstanceUpdate();
    }
    public Dictionary<int, scr_prefab_actortab> actorTabs = new Dictionary<int, scr_prefab_actortab>();

    //public RectTransform left, right;
    //protected ScrollRect scrollLeft, scrollRight;
    string _turnCount = string.Empty;
    int turnCount = -1;
    private void OnInstanceUpdate()
    {
        if (CurrentMode == CombatUI.SkillSelect) return;

        //Debug.Log($"OnInstanceUpdate");

        
        turnCounter.SetText(_turnCount.Replace("$count$", $"{currentActiveCombat.CurrentRound + 1}"));

        foreach (var c in currentActiveCombat.teamA.Actors)
        {
            if (actorTabs.ContainsKey(c.RefID)) continue;
            var rect = Instantiate(prefab_Actor);
            rect.SelfRect.SetParent(charaList_teamA, false);
            rect.Load(this,currentActiveCombat.ActorStats[c.RefID], c, false);
            actorTabs.Add(c.RefID, rect);
        }

        foreach (var c in currentActiveCombat.teamB.Actors)
        {
            if (actorTabs.ContainsKey(c.RefID)) continue;
            var rect = Instantiate(prefab_Actor);
            rect.SelfRect.SetParent(charaList_teamB, false);
            rect.Load(this, currentActiveCombat.ActorStats[c.RefID], c, true);
            actorTabs.Add(c.RefID, rect);
        }

        centerActionList.anchoredPosition = new Vector2(0, -1000);

        charaList_teamA.anchoredPosition = new Vector2(0, -1000);
        charaList_teamB.anchoredPosition = new Vector2(0, -1000);

        //if (scrollLeft != null) scrollLeft.normalizedPosition = Vector3.zero;
        //else Debug.LogError("nope");
        //if (scrollRight != null) scrollRight.normalizedPosition = Vector3.zero;
        //else Debug.LogError("nope");

        //left.horizontalScroller.ScrollPageUp();// = Vector3.zero;
        //right.normalizedPosition = Vector3.zero;

        Utility.DestroyAllChildrenFrom(InTurn_Actions);
        Utility.DestroyAllChildrenFrom(EOT_Actions, 1);

        bool resetOverride = currentActiveCombat.CurrentRound != turnCount;

        foreach (var i in actorTabs.Values)
        {
            if (resetOverride) i.overrideCount = 2;
            i.UpdateContent(currentActiveCombat);
        }

        turnCount = currentActiveCombat.CurrentRound;

        var act = currentActiveCombat.RoundActions(currentActiveCombat.CurrentRound);
        while (act != null)
        {
            if (!act.Hidden)
            {
                ActionEntry entry = Instantiate(prefab_ActionEntry);
                entry.SelfRect.SetParent(InTurn_Actions, false);
                entry.isHostile = currentActiveCombat.teamB.hasActor(act.ownerRef.RefID);
                entry.Initialize(act, this, true);
            }
            act = act.Next ;
        }
        var eot = currentActiveCombat.RoundEndActions(currentActiveCombat.CurrentRound);
        bool hasEOT = false;
        while (eot != null)
        {
            hasEOT = true;
            ActionEntry entry = Instantiate(prefab_ActionEntry);
            entry.SelfRect.SetParent(EOT_Actions, false);
            entry.isHostile = currentActiveCombat.teamB.hasActor(eot.ownerRef.RefID);
            entry.Initialize(eot, this, true);
            eot = eot.Next;
        }
        actionList.No_EOT_message.gameObject.SetActive(!hasEOT);

        msg_turnstart.SetText(String.Join("\n", currentActiveCombat.TurnStartMessages));
        msg_turnend.SetText(String.Join("\n", currentActiveCombat.TurnEndMessages));

        ValidateAll();
    }

    public scr_HoverableText msg_turnstart, msg_turnend;
    public RectTransform InTurn_Actions, EOT_Actions;
    public ActionEntry prefab_ActionEntry;

    private void OnUpdateNotice(bool b)
    {
        this.OnViewModeChange(scr_System_CampaignManager.current.CurrentViewMode, false);
    }

    public override void ValidateAll()
    {
        //Debug.Log("validateall!");
        if (!isActive) return;
        if (SkipValidateCallback != null)
        {
            //Debug.Log("SkipValidateCallback!");
            var callback = SkipValidateCallback;
            SkipValidateCallback = null;
            callback.Invoke();
        }
        else
        {
            base.ValidateAll();
            if (PostValidateCallback != null)
            {
                //Debug.Log("PostValidateCallback!");
                var callback = PostValidateCallback;
                PostValidateCallback = null;
                callback.Invoke();
            }
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
                default: break;
            }
        }
        if (PostValidateCallback != null)
        {
            var v = PostValidateCallback;
            PostValidateCallback = null;
            v.Invoke();
        }
        else ValidateAll();
    }

    public override void Initialize()
    {
        base.Initialize();

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {

            switch (button.optionID)
            {
                case -9996:
                    button.Initialize(this, new Button_Debug_EndCombat(this, button, CombatResult.Victory)); break;
                case -9997:
                    button.Initialize(this, new Button_Debug_EndCombat(this, button, CombatResult.Draw)); break;
                case -9998:
                    button.Initialize(this, new Button_Debug_EndCombat(this, button, CombatResult.Defeat)); break;
                case -9999:
                    // execute round
                    button.Initialize(this, new Button_FinalizeRound(this, button));
                    break;
                default:
                    break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }

        // build all presetList
        ValidateAll();
    }

    public class Button_ModActionCount : ButtonValidator, I_ButtonClickable
    {
        new scr_Menu_Combat parent;
        scr_prefab_actortab tab;
        bool reduce;
        CombatInstance instance;
        public Button_ModActionCount(scr_Menu_Combat parent, scr_prefab_actortab tab, CombatInstance instance,  bool reduce = false) : base(parent)
        {
            this.parent = parent;
            this.tab = tab;
            this.reduce = reduce;
            this.instance = instance;
        }

        public override bool IsButtonValid()
        {
            if (parent.currentActiveCombat != null && parent.currentActiveCombat.Ongoing && parent.currentActiveCombat.TurnEnded) return false;
            if (!tab.CanModCount()) return false;
            if (reduce) return tab.overrideCount > 2;
            else return tab.overrideCount < parent.currentActiveCombat.roundMaxAction;
        }

        public void Callback()
        {
            parent.currentActiveCombat.RemoveActionsOngoing(tab.c, tab.overrideCount - 1);
        }

        public void OnClickButton()
        {
            if (reduce)
            {
                tab.ReduceActionCount(instance);//parent.PostValidateCallback = Callback;
            }
            else tab.AddActionCount(instance);
        }
    }

    public class Button_OpenActionSelect : ButtonValidator, I_ButtonClickable
    {
        new scr_Menu_Combat parent;
        CombatInstance instance;
        Character_Trainable c;
        scr_SelectableText text;
        int index;
        CombatActionInstance inst;
        public RectTransform selfRect;

        bool isActive = false;
        /// <summary>
        /// Anything lt this number should be displayed, else hidden
        /// </summary>
        int displayIndex = 2;
        public Button_OpenActionSelect(scr_Menu_Combat parent, CombatInstance instance, Character_Trainable c, scr_SelectableText text, RectTransform selfRect, int actionIndex = 0) : base(parent) 
        {
            this.parent = parent;
            this.instance = instance;
            this.c = c;
            this.text = text;
            this.selfRect = selfRect;
            this.index = actionIndex;   // need index to calculate base speed value

            isActive = instance.teamA.hasActor(c.RefID);
        }

        public override bool IsButtonValid()
        {
            if (parent.currentActiveCombat != null && parent.currentActiveCombat.Ongoing && parent.currentActiveCombat.TurnEnded) return false;
            if (index < 2 || index < displayIndex)
            {
                text.gameObject.SetActive(true);

                if (this.inst != null && this.inst.actionRef != null) text.SetText($"{index + 1}: {inst.actionRef.Name}");
                else text.SetText($"{index + 1}: - ");
            }
            else text.gameObject.SetActive(false);

            return isActive && text.gameObject.activeInHierarchy;
        }

        public void ResetAction(CombatActionInstance instance, int displayIndex)
        {
            this.displayIndex = displayIndex;
            //Debug.Log($"Button_OpenActionSelect ResetAction [{(instance == null ? "null" : instance.actionRef.ID)}] index [{index}] displayIndex [{displayIndex}]");
            this.inst = instance;
            this.IsButtonValid();
        }

        public void OnClickButton()
        {
            parent.OpenSkillSelectMenu(c, index, inst);
        }
    }

    public class Button_OpenEOTActionSelect : ButtonValidator, I_ButtonClickable
    {
        new scr_Menu_Combat parent;
        CombatInstance instance;
        Character_Trainable c;
        scr_SelectableText text;

        bool isActive = false;
        /// <summary>
        /// Anything lt this number should be displayed, else hidden
        /// </summary>
        int displayIndex = 2;
        bool teamA = false;
        public Button_OpenEOTActionSelect(scr_Menu_Combat parent, CombatInstance instance, Character_Trainable c, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.instance = instance;
            this.c = c;
            this.text = text;
            teamA = instance.teamA.hasActor(c.RefID);
            isActive = instance.teamA.hasActor(c.RefID);
        }

        public override bool IsButtonValid()
        {
            if (parent.currentActiveCombat != null && parent.currentActiveCombat.Ongoing && parent.currentActiveCombat.TurnEnded) return false;
            
            if (instance.roundMaxAction > 2 && instance.ActorStats[c.RefID].CanPush && (instance.MaxActionsByCharaRef(c.RefID) < 3)
                && ((teamA && instance.roundMaxAction_B > 2) || instance.roundMaxAction_A > 2))
            {
                text.gameObject.SetActive(true);
                var counter = instance.GetEOTCounter(c);
                text.SetText($"{LocalizeDictionary.QueryThenParse("combat_action_EOTaction")} {(counter == null ? " - " : counter.actionRef.Name)}");
            }
            else
            {
                text.gameObject.SetActive(false);
            }

            return isActive && text.gameObject.activeInHierarchy;
        }

        public void OnClickButton()
        {
            parent.OpenEOTSelectMenu(c, instance.GetEOTCounter(c));
        }
    }

    public class Button_FinalizeRound : ButtonValidator, I_ButtonClickable
    {
        new scr_Menu_Combat parent;
        RectTransform selfRect;
        scr_SelectableText text;
        public Button_FinalizeRound(scr_Menu_Combat parent, scr_SelectableText selfRect) :base(parent)
        {
            this.parent = parent;
            this.text = selfRect;
            this.selfRect = selfRect.SelfRect;
        }
        public override bool IsButtonValid()
        {
            if (parent.CurrentMode == CombatUI.SkillSelect) selfRect.gameObject.SetActive(false);
            else if (parent.currentActiveCombat == null) selfRect.gameObject.SetActive(false);
            else
            {
                selfRect.gameObject.SetActive(true);
                if (parent.currentActiveCombat.Ongoing) text.SetText(parent.currentActiveCombat.TurnEnded ? LocalizeDictionary.QueryThenParse("ui_combat_menu_continue") : LocalizeDictionary.QueryThenParse("ui_combat_menu_execute"));
                else text.SetText(LocalizeDictionary.QueryThenParse("ui_combat_menu_exit"));
            }
            return selfRect.gameObject.activeInHierarchy;
        }
        public void OnClickButton()
        {
            // finalize combat and run
            if (parent.currentActiveCombat.Ongoing) parent.currentActiveCombat.Run();
            else
            {
                parent.UnloadCombatInstance();
                scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Room);
            }
        }
    }

    public class Button_Debug_EndCombat : ButtonValidator, I_ButtonClickable
    {
        new scr_Menu_Combat parent;
        RectTransform selfRect;
        scr_SelectableText text;
        CombatResult targetResult;
        public Button_Debug_EndCombat(scr_Menu_Combat parent, scr_SelectableText selfRect, CombatResult targetResult) : base(parent)
        {
            this.parent = parent;
            this.text = selfRect;
            this.selfRect = selfRect.SelfRect;
            this.targetResult = targetResult;
        }
        public override bool IsButtonValid()
        {
            if (!selfRect.gameObject.activeInHierarchy) return false;
            if (parent.currentActiveCombat == null) return false;
            if (parent.currentActiveCombat.Result != CombatResult.Ongoing) return false;
            return true;
        }
        public void OnClickButton()
        {
            // finalize combat and run
            parent.currentActiveCombat.Result = targetResult;
        }
    }
}


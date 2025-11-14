using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class menu_SkillSelect : scr_Menu, IPointerClickHandler
{

    public RectTransform SkillsRect;
    public ItemActions prefab_ItemActions;
    scr_Menu_Combat parent;
    Character_Trainable c;
    CombatActionInstance editing;
    int actionIndex = -1;
    public SkillSelect SkillTab;

    bool isEOTAction = false;

    //public scr_HoverableText actionDescription;
    CombatInstance Combat = null;
    protected override void OnDestroy()
    {
        Combat.Observer_SnapshotUpdate -= AttachRefresh;
        base.OnDestroy();
    }

    int RoundIndex { get
        {
            return Combat == null ? -1 : this.Combat.CurrentRound;
        } }

    public void OnPointerClick(PointerEventData eventData)
    {
        // if click outside box
        if ((eventData.rawPointerPress.GetComponent<scr_Canvas_Management>() != null) || (eventData.button == PointerEventData.InputButton.Right && UtilityEX.isClickBelowDragThreshold(eventData)))
        {

            Notify(-9998);

        }
    }
    List<CombatActionInstance> wipActionsList;

    public void InitializeWithArgument_EOT(scr_Menu_Combat parent, Character_Trainable c, int actionIndex, CombatActionInstance innerInstance)
    {
        this.isEOTAction = true;
        //actionDescription.SetText($"RoundIndex {RoundIndex} ActionIndex {this.actionIndex}");
        _initialize(parent, c, actionIndex, innerInstance);
    }

    protected void _initialize(scr_Menu_Combat parent, Character_Trainable c, int actionIndex, CombatActionInstance innerInstance)
    {
        if (!initialized) Initialize();
        this.parent = parent;
        this.c = c;
        this.Combat = parent.currentActiveCombat;
        Utility.DestroyAllChildrenFrom(SkillsRect, 1);
        this.editing = innerInstance;
        this.currentInstance = innerInstance;
        this.wipActionsList = new List<CombatActionInstance>(Combat.ActionsOngoing);

        SkillTab.originalAction.SetText($"{(this.isEOTAction ? "EX" : actionIndex + 1)}: {(this.editing == null || this.editing.actionRef == null ? "-" : this.editing.actionRef.Name)}  ");

        name_self.SetText(_name_self.Replace("$name$", c.FirstName));

        if (editing != null) Target = editing.targetRef;
        else name_target.SetText(_name_target.Replace("$name$", Combat.GetName(Target)));
        //Target = editing == null ? null : editing.targetRef;
        if (editing != null) this.wipActionsList.Remove(editing);
        else
        {
            Left.Refresh(c, Combat);
            Right.Refresh(Target, Combat);
            ActionData.Refresh(null);
        }
        Combat.Observer_SnapshotUpdate += AttachRefresh;
        MakeItemRectAlwaysValid();
        foreach (var kvp in c.Body.CombatActions) if (kvp.Value.Count > 0) MakeItemRect(kvp.Key, kvp.Value);
        foreach (var kvp in c.Inventory.CombatActions) if (kvp.Value.Count > 0) MakeItemRect(kvp.Key, kvp.Value);
        targetingRect_A.GetComponent<Image>().color = UtilityEX.UI_SelfColor;
        targetingRect_B.GetComponent<Image>().color = UtilityEX.UI_HostileColor;
        foreach (var chara in Combat.teamA.Actors) MakeTargetButton(chara, false, targetingRect_A);
        foreach (var chara in Combat.teamB.Actors) MakeTargetButton(chara, true, targetingRect_B);
        Mode = MenuMode.SkillMenu;

        /// Redraw actions list
        UpdateActionInstance();
        ValidateAll();
    }

    /// <summary>
    /// InnerInstance must not be null.<br/>
    /// Insert required data into instance
    /// </summary>
    /// <param name="parent"></param>
    /// <param name="c"></param>
    /// <param name="actionIndex"></param>
    /// <param name="innerInstance"></param>
    public void InitializeWithArgument(scr_Menu_Combat parent, Character_Trainable c, int actionIndex, CombatActionInstance innerInstance)
    {
        this.isEOTAction = false;
        this.actionIndex = actionIndex;
       // actionDescription.SetText($"RoundIndex {RoundIndex} ActionIndex {this.actionIndex}");
        _initialize(parent, c, actionIndex, innerInstance);
    }

    public RectTransform prefab_targetButton;
    protected void MakeTargetButton(Character_Trainable chara, bool isHostile, RectTransform targetingRect)
    {
        RectTransform r = Instantiate(prefab_targetButton);
        scr_SelectableText btn = r.GetComponent<scr_SelectableText>();
        r.SetParent(targetingRect, false);
        btn.Initialize(this, new Button_SetTarget(this, btn, chara, isHostile));
        btn.optionID = AssertUniqueHash(chara.RefID);
        btn.SetText($"{Combat.GetName(chara)}");

        this.buttonsByID.Add(btn.optionID, btn);
        this.validatorsByID.Add(btn.optionID, btn.Validator);

        btn.Validate();
    }
    public RectTransform AlwaysValidRect;
    protected void MakeItemRectAlwaysValid() 
    {
        List<CombatAction> actions = c.Body.AlwaysValidActions;
        if (actions.Count < 1) return;

        bool existNonHide = actions.Any(x => !x.HideInSelect);
        //Debug.Log($"MakeItemRect {displayName} entrycount {actions.Count} existNonHide {existNonHide}");
        if (!existNonHide) return;

        //Utility.DestroyAllChildrenFrom(box.ActionsRect);
        foreach (var action in actions)
        {
            if (action.HideInSelect) continue;
            MakeActionButton(AlwaysValidRect, action, null);
        }
    }
    protected void MakeItemRect(I_CombatItem item, List<CombatAction> actions)
    {
        if (actions.Count < 1 || !actions.Any(x => !x.HideInSelect)) return;

        //Debug.Log($"MakeItemRect {displayName}");
        ItemActions box = Instantiate(prefab_ItemActions);
        box.SelfRect.SetParent(SkillsRect, false);
        box.Title.SetText(item.DisplayName);
        box.Title.SetExternalTooltip(item.Tooltip);

        //Utility.DestroyAllChildrenFrom(box.ActionsRect);
        foreach(var action in actions)
        {
            if (action.HideInSelect) continue;
            MakeActionButton(box.ActionsRect, action, item);
        }
    }

    protected void MakeActionButton(RectTransform parentRect, CombatAction action, I_CombatItem item)
    {
        RectTransform r = Instantiate(prefab_text_linkbutton);
        scr_SelectableText btn = r.GetComponent<scr_SelectableText>();
        r.SetParent(parentRect, false);

        if (actionIndex < 0)
        {

        }

        //Button_OpenActionSelect(scr_Menu_Combat parent, CombatInstance instance, Character_Trainable c, scr_SelectableText text, int actionIndex) : base(parent)
        var CAI = new CombatActionInstance(Combat, c, item, action, null, Combat.BaseSpeed[c.RefID], RoundIndex, actionIndex, this.isEOTAction);
        btn.Initialize(this, new Button_SetAction(this, btn, CAI));
        btn.optionID = AssertUniqueHash(r.GetHashCode());

        if (CAI != null)
        { 
            btn.SetText(CAI.actionRef.ID);
        }
        else btn.SetText(" - ");

        this.buttonsByID.Add(btn.optionID, btn);
        this.validatorsByID.Add(btn.optionID, btn.Validator);

        btn.Validate();
    }

    protected void OpenTargetingMenu()
    {
        Mode = MenuMode.TargetingMenu;
    }
    protected override void Awake()
    {
        base.Awake();
        this.m_Canvas.overrideSorting = true;
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);
        _name_self = LocalizeDictionary.QueryThenParse("ui_combat_result_preview_source");
        _name_target = LocalizeDictionary.QueryThenParse("ui_combat_result_preview_target");
    }

    public override void Initialize()
    {
        base.Initialize();

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {

             //Debug.Log("Button " + button + " " + button.optionID);
            switch (button.optionID)
            {
                case -9996:
                    button.Initialize(this, new Button_SetAction(this, button, null));
                    button.SetText(" - ");
                    break;
                case -9997:
                    button.Initialize(this, new Button_SetTarget(this, button, null, false));
                    button.SetText(" - ");
                    break;
                case -9998: // exit
                    button.Initialize(this, button_alwaysValid); break;
                case -9999:
                    button.Initialize(this, new Button_SelectTargetMenu(this, button)); break;
                default:
                    break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }
        }
        /*
        // CASE -9999
        actionData_Target.Initialize(this, new Button_SelectTargetMenu(this, actionData_Target));
        buttonsByID.Add(actionData_Target.optionID, actionData_Target);
        validatorsByID.Add(actionData_Target.optionID, actionData_Target.Validator);
        */
        // CASE -9997
    }
    protected void FinalizeSkillSelect(bool abort = false)
    {
        if (this.isEOTAction)
        {
            if (abort) parent.SetEOTAction(c, editing);
            else parent.SetEOTAction(c, currentInstance);
        }

        parent.FinalizeSkillSelect(abort);
        
        scr_System_SceneManager.current.UnloadLastCanvasFromScene();
    }

    public RectTransform targetingRect_A, targetingRect_B;

    public Character_Trainable Target
    {
        get
        {
            return parent.CurrentTarget;
        }
        set
        {
            parent.CurrentTarget = value;
            name_target.SetText(_name_target.Replace("$name$", Combat.GetName(value)));
        }
    }
    public enum MenuMode 
    { 
        SkillMenu,
        TargetingMenu
    }

    MenuMode _mode = MenuMode.SkillMenu;
    public MenuMode Mode
    { get
        {
            return _mode;
        }
        set
        {
            _mode = value;
            switch (_mode)
            {
                case MenuMode.SkillMenu:
                    foreach (var group in tabs_targetSelect) viewDisable(group);
                    foreach (var group in tabs_skillsSelect) viewEnable(group);
                    break;
                case MenuMode.TargetingMenu:
                    foreach (var group in tabs_skillsSelect) viewDisable(group);
                    foreach (var group in tabs_targetSelect) viewEnable(group);
                    break;
            }
        }
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

    public List<CanvasGroup> tabs_skillsSelect, tabs_targetSelect;

    CombatActionInstance currentInstance;
    protected void SetSkill(CombatActionInstance instance)
    {
       // Debug.Log($"setskill {(instance == null ? "null" : instance.Description)} isEOT {(instance == null ? false: instance.isEOTAction)} == {isEOTAction}");
        currentInstance = instance;
        //if (instance == null) Debug.LogError("setting null skill");
        UpdateActionInstance();
    }

    bool newlyAdded = false;
    /// <summary>
    /// </summary>
    /// 
    public scr_SelectableText actionData_Target;
    public actionData_Actor Left, Right;
    public actionData_Action ActionData;
    public scr_HoverableText name_self;
    public scr_SelectableText name_target;

    string _name_self, _name_target;

    protected void UpdateActionInstance()
    {
        //Debug.Log("UpdateUI");

        if (currentInstance != null)
        {
            currentInstance.targetRef = Target;
            if (!currentInstance.Validate())
            {
                Debug.Log("cai failed validation skip adding");
                return;
            }
        }
        if (this.isEOTAction) parent.SetEOTAction(c, currentInstance);
        else Combat.InsertAction(currentInstance, wipActionsList);

        SkillTab.newAction.SetText($"{(this.isEOTAction ? "EX" : actionIndex + 1)}: {(this.currentInstance == null || this.currentInstance.actionRef == null ? "-" : this.currentInstance.actionRef.Name)}  ");

        OnInstanceUpdate();
    }

    public void AttachRefresh(CombatActionInstance i)
    {
        if (currentInstance == null || i != currentInstance) return;
        Left.Refresh(i.ownerRef, Combat);
        Right.Refresh(i.targetRef, Combat);
        ActionData.Refresh(this.currentInstance);
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
                case -9998: FinalizeSkillSelect(abort:true); break;
                default: break;
            }
        }
        ValidateAll();
    }

    public RectTransform InTurn_Actions, EOT_Actions, No_EOT_message;
    public ActionEntry prefab_ActionEntry;

    /// <summary>
    /// Redraw Combat Instance Action List
    /// </summary>
    private void OnInstanceUpdate()
    {

        Utility.DestroyAllChildrenFrom(InTurn_Actions);
        Utility.DestroyAllChildrenFrom(EOT_Actions, 1);

        int counter = 5;
        var act = Combat.RoundActions(RoundIndex);
        while (act != null && counter > 0)
        {
            if (!act.Hidden)
            {
                ActionEntry entry = Instantiate(prefab_ActionEntry);
                entry.SelfRect.SetParent(InTurn_Actions, false);
                entry.isHostile = Combat.teamB.hasActor(act.ownerRef.RefID);
                entry.Initialize(act, this.parent);
            }
            act = act.Next;
           // counter--;
        }

        var eot = Combat.RoundEndActions(Combat.CurrentRound);
        bool hasEOT = false;
        while (eot != null)
        {
            hasEOT = true;
            ActionEntry entry = Instantiate(prefab_ActionEntry);
            entry.SelfRect.SetParent(EOT_Actions, false);
            entry.isHostile = Combat.teamB.hasActor(eot.ownerRef.RefID);
            entry.Initialize(eot, this.parent);
            eot = eot.Next;
        }
        No_EOT_message.gameObject.SetActive(!hasEOT);
    }

    public class Button_SetTarget: ButtonValidator, I_ButtonClickable
    {
        new menu_SkillSelect parent;
        scr_SelectableText text;
        Character_Trainable c;
        bool isHostile;
        public Button_SetTarget(menu_SkillSelect parent, scr_SelectableText text, Character_Trainable c, bool isHostile) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.c = c;
            this.isHostile = isHostile;
            //text.AttachOnHoverEnter(OnHoverEnter);
        }
        public override bool IsButtonValid()
        {
            if (!text.gameObject.activeInHierarchy) return false;
            //if (parent.currentInstance == null || parent.currentInstance.actionRef == null) return false;
            return true;
        }
        public void OnClickButton()
        {
            // finalize
            parent.Target = c;
            parent.Mode = MenuMode.SkillMenu;
            parent.UpdateActionInstance();
        }
    }


    public class Button_SelectTargetMenu : ButtonValidator, I_ButtonClickable
    {
        new menu_SkillSelect parent;
        scr_SelectableText text;

        public Button_SelectTargetMenu(menu_SkillSelect parent, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.text = text;
        }

        public override bool IsButtonValid()
        {
            return this.text.gameObject.activeInHierarchy;
        }

        public void OnClickButton()
        {
            parent.OpenTargetingMenu();
        }
    }

    public class Button_SetAction : ButtonValidator, I_ButtonClickable
    {
        new menu_SkillSelect parent;
        scr_SelectableText text;
        CombatActionInstance actionInstance;

        string baseTooltip;
        public Button_SetAction(menu_SkillSelect parent, scr_SelectableText text, CombatActionInstance actionInstance) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.actionInstance = actionInstance;
            this.tooltip = this.actionInstance == null ? "" : this.actionInstance.ActionRefTooltip;
            this.text.AttachOnHoverEnter(OnHoverEnter);
        }

        public override bool IsButtonValid()
        {
            if (!this.text.gameObject.activeInHierarchy) return false;
            else if (actionInstance == null) return true;
            else if (actionInstance.actionRef.requireTarget && parent.Target == null)
            {
                return false;
            }
            return true;
        }

        public void OnHoverEnter()
        {
            parent.SetSkill(actionInstance);
        }

        public void OnClickButton()
        {
            // if require targeting, open targeting menu

            // targeting menu LMB confirm, RMB cancel.
            // make a canvas with transparent background, on click self destroy

            // else confirm.
            parent.FinalizeSkillSelect();
            
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class scr_Menu_Combat : scr_Menu
{
    public enum CombatUI
    {
        Overview,
        SkillSelect
    }

    public List<CanvasGroup> group_Overview;
    public List<CanvasGroup> group_Select;
    public ActionList actionList;

    [SerializeField] protected CombatUI _selfMode = CombatUI.Overview;

    public CombatUI CurrentMode
    {
        get
        {
            return _selfMode;
        }
        set
        {
            if (_selfMode != value)
            {

                _selfMode = value;
                switch (_selfMode)
                {
                    case CombatUI.Overview:
                        foreach (var group in group_Select) viewDisable(group);
                        foreach (var group in group_Overview) viewEnable(group);
                        break;
                    case CombatUI.SkillSelect:
                        foreach (var group in group_Overview) viewDisable(group);
                        foreach (var group in group_Select) viewEnable(group);
                        break;
                }
                actionList.NotifyChange(_selfMode);
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

    protected override void Awake()
    {
        base.Awake();

        scr_System_CampaignManager.current.Observer_CurrentViewMode += OnViewModeChange;
        scr_System_CampaignManager.current.Observer_UpdateNotice += OnUpdateNotice;

    }

    public bool isActive = false;
    public CanvasGroup self;
    public RectTransform backgroundRect;
    private void OnViewModeChange(ViewMode vm, bool lockView)
    {
        switch (vm)
        {
            case ViewMode.View_Combat:
                //enable self
                isActive = true;
                viewEnable(self, false);
                CurrentMode = CombatUI.Overview;
                backgroundRect.gameObject.SetActive(true);
                LoadCombatInstance(scr_System_CampaignManager.current.Combat.PlayerCombatInstance);
                break;
            default:
                //disable self
                isActive = false;
                viewDisable(self, false);
                backgroundRect.gameObject.SetActive(false);
                break;
        }
        ValidateAll();
    }

    CombatInstance currentActiveCombat = null;
    public RectTransform charaList_teamA, charaList_teamB;
    public scr_prefab_actortab prefab_Actor;
    protected void LoadCombatInstance(CombatInstance instance)
    {
        if (this.currentActiveCombat == instance || instance == null) return;
        this.currentActiveCombat = instance;

        Utility.DestroyAllChildrenFrom (ref charaList_teamA);
        Utility.DestroyAllChildrenFrom (ref charaList_teamB);

        // for each team, draw their character box in respective transform
        // for enemy team, decide on their action and draw action

        foreach(var actor in currentActiveCombat.teamA.Actors)
        {
            var rect = Instantiate(prefab_Actor);
            rect.SelfRect.SetParent(charaList_teamA, false);
            var c = scr_System_CampaignManager.current.FindInstanceByID(actor);
            rect.Load(c);
        }

        foreach(var actor in currentActiveCombat.teamB.Actors)
        {
            var rect = Instantiate(prefab_Actor);
            rect.SelfRect.SetParent(charaList_teamB, false);
            var c = scr_System_CampaignManager.current.FindInstanceByID(actor);
            rect.Load(c);
        }
        // for self team, draw selectable action button
        // on click, swap panel
    }




    private void OnUpdateNotice(bool b)
    {
        this.OnViewModeChange(scr_System_CampaignManager.current.CurrentViewMode, false);
    }

    public override void ValidateAll()
    {
        if (!isActive) return;
        
        base.ValidateAll();
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
        ValidateAll();
    }

    public override void Initialize()
    {
        base.Initialize();

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {

            switch (button.optionID)
            {

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
}


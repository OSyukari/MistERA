using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System;

public class scr_Panel_SexComTarget : scr_Menu, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public enum SubPanel { None, SexCom, Equipment, History, Internals, Schedule }
    public enum InternalsTab { Internals, Wombs }

    private const int ID_ToggleSexCom = -3;
    private const int ID_ToggleEquipment = -4;
    private const int ID_ToggleHistory = -5;
    private const int ID_ToggleInternals = -6;
    private const int ID_TabInternals = -7;
    private const int ID_TabWombs = -8;
    private const int ID_Hide = -9;
    private const int ID_ToggleSchedule = -10;

    public RectTransform child;     // SexCom content root
    private Image image_bg;

    //private float update;

    private Dictionary<ActionPackage_Sex, RectTransform> indexSexRelations;
    private List<ActionPackage_Sex> markforDelete;
    private Dictionary<int, RectTransform> indexActorRef;

    // side panels (current target data)
    public RectTransform topBar;
    public RectTransform panel_Equipment, panel_History, panel_Internals, panel_Schedules;
    public RectTransform content_Equipment, content_History, content_Internals, content_Wombs;
    public RectTransform prefab_BodyInstanceGear, prefab_Equipment;
    public TextMeshProUGUI prefab_TextBox, prefab_ButtonBox;
    public scr_memoryDaySplit prefab_DaySplit;
    public scr_memoryBox prefab_MemoryEntry;
    public scr_Panel_BodyDetail prefab_PanelInternal;
    public scr_panel_wombdata prefab_PanelWomb;

    private bool hovered = false;
    private bool forceShown = false;    // post-update reminder, sex only
    private bool userHidden = false;    // hide button override, cleared on toggle / pointer exit / update
    private SubPanel activePanel = SubPanel.None;
    private InternalsTab internalsTab = InternalsTab.Internals;

    // target refID each data panel was last built for, -1 = stale. Rebuild is lazy: only when shown.
    private Dictionary<SubPanel, int> builtForRef = new Dictionary<SubPanel, int>()
    {
        { SubPanel.Equipment, -1 },
        { SubPanel.History, -1 },
        { SubPanel.Internals, -1 },
        { SubPanel.Schedule, -1 },
    };

    public List<scr_ScheduleBox> Schedules;
    protected override void Awake()
    {
        base.Awake();
        image_bg = this.GetComponent<Image>();
        //update = 0.0f;
        indexSexRelations = new Dictionary<ActionPackage_Sex, RectTransform>();
        indexActorRef = new Dictionary<int, RectTransform>();
        markforDelete = new List<ActionPackage_Sex>();
        swapButtonScr = swapButtonBox.GetComponent<scr_SelectableText>();
        scr_System_CampaignManager.current.Observer_PlayerJob += OnPlayerJobChange;
        scr_System_CampaignManager.current.Observer_CurrentViewMode += OnViewModeChange;
        //scr_UpdateHandler.current.Observer_PostUpdateTime_4 += InternalUpdate;
        scr_System_CampaignManager.current.Observer_UpdateNotice += OnCentralUpdate;
        scr_System_CampaignManager.current.Observer_CurrentTarget += OnCurrentTargetChange;

        ApplyVisibility();
    }

    protected override void Start()
    {
        if(!initialized) Initialize();
        ApplyVisibility();
    }

    private void OnViewModeChange(ViewMode vm, bool lockView)
    {
        if (vm == ViewMode.View_Room)
        {
            // back in room = update completed: reset hide override, reload data, sex reminder
            userHidden = false;
            InvalidateDataPanels();
            RefreshSexState();
            if (sexJob != null)
            {
                activePanel = SubPanel.SexCom;
                forceShown = true;
            }
        }
        ApplyVisibility();
    }

    /// <summary>
    /// Re-read player sex job. Tears down sex rows when sex ended, and prunes departed actors from COM doer/receiver.
    /// </summary>
    private void RefreshSexState()
    {
        bool wasInSex = sexJob != null;
        sexJob = scr_System_CampaignManager.current.Player.CurrentJob as Job_Sex_Group;
        if (sexJob == null)
        {
            forceShown = false;
            TearDownSexRows();
        }
        else
        {
            if (!wasInSex)
            {
                activePanel = SubPanel.SexCom;
                forceShown = true;
            }

            // rows only sync while visible, so keep the pending selection valid even when the panel is hidden
            COMmanager.SexComDoers.RemoveAll(r => !sexJob.actorRefID.Contains(r));
            COMmanager.SexComReceivers.RemoveAll(r => !sexJob.actorRefID.Contains(r));
        }
    }

    private void TearDownSexRows()
    {
        if (indexActorRef.Count > 0 || indexSexRelations.Count > 0)
        {
            List<int> ints = new List<int>();
            foreach (var refID in indexActorRef.Keys)
            {
                ints.Add(refID);
            }
            indexActorRef.Clear();
            foreach (var ii in ints)
            {
                DestroyActor(ii);
            }

            while (doerReceiverList.transform.childCount > 0)
            {
                DestroyImmediate(doerReceiverList.transform.GetChild(0).gameObject);
            }
            while (relationsList.transform.childCount > 0)
            {
                DestroyImmediate(relationsList.transform.GetChild(0).gameObject);
            }

            foreach(var i in indexSexRelations)
            {
                if (i.Value != null) Destroy(i.Value.gameObject);
            }
            indexSexRelations.Clear();

        }
    }

    private void OnCentralUpdate(bool b)
    {
        if (scr_System_CampaignManager.current.CurrentViewMode != ViewMode.View_Room) return;
        RefreshSexState();
        InvalidateDataPanels();
        ApplyVisibility();
    }

    private Job_Sex_Group sexJob = null;

    private void OnPlayerJobChange(int i, Job j)
    {
        //Debug.LogError("ONPLAYERJOBCHANGE SUBSCRIBER CALLED");

        if (scr_System_CampaignManager.current.CurrentViewMode != ViewMode.View_Room) return;
        if (!this.gameObject.activeInHierarchy) return;
        RefreshSexState();
        ApplyVisibility();
    }

    private void OnCurrentTargetChange(int refID, bool forceUpdate)
    {
        // data panels detect stale target themselves, only the visible one rebuilds
        ApplyVisibility();
    }

    private void InvalidateDataPanels()
    {
        builtForRef[SubPanel.Equipment] = -1;
        builtForRef[SubPanel.History] = -1;
        builtForRef[SubPanel.Internals] = -1;
        builtForRef[SubPanel.Schedule] = -1;
    }

    private static void SetActive(Component c, bool value)
    {
        if (c != null && c.gameObject.activeSelf != value) c.gameObject.SetActive(value);
    }

    /// <summary>
    /// Single place deciding what is shown. Every state change calls this.
    /// </summary>
    private void ApplyVisibility()
    {
        bool roomView = scr_System_CampaignManager.current.CurrentViewMode == ViewMode.View_Room
            && !(scr_UpdateHandler.current != null && scr_UpdateHandler.current.Updating);
        bool inSex = sexJob != null;
        if (!inSex && activePanel == SubPanel.SexCom) activePanel = SubPanel.None;
        if (activePanel == SubPanel.None) lastCenteredPanel = SubPanel.None;

        bool barVisible = roomView && (hovered || (inSex && forceShown));
        bool contentVisible = barVisible && !userHidden;

        SetActive(topBar, barVisible);
        SetActive(child, contentVisible && activePanel == SubPanel.SexCom);
        SetActive(panel_Equipment, contentVisible && activePanel == SubPanel.Equipment);
        SetActive(panel_History, contentVisible && activePanel == SubPanel.History);
        SetActive(panel_Internals, contentVisible && activePanel == SubPanel.Internals);
        SetActive(panel_Schedules, contentVisible && activePanel == SubPanel.Schedule);
        SetActive(content_Internals, internalsTab == InternalsTab.Internals);
        SetActive(content_Wombs, internalsTab == InternalsTab.Wombs);

        if (contentVisible)
        {
            if (activePanel == SubPanel.SexCom) SyncSexRows();
            else if (activePanel != SubPanel.None) RebuildIfStale(activePanel);
        }

        ValidateAll();
    }

    public void TogglePanel(SubPanel p)
    {
        activePanel = activePanel == p ? SubPanel.None : p;
        userHidden = false;
        ApplyVisibility();
    }

    public void SetInternalsTab(InternalsTab tab)
    {
        internalsTab = tab;
        ApplyVisibility();
    }

    public void ToggleHide()
    {
        userHidden = !userHidden;
        ApplyVisibility();
    }


    public RectTransform cycleRect;
    public scr_HoverableText cycle_total, cycle_current, cycle_ovum, cycle_fertility;

    private void RebuildIfStale(SubPanel p)
    {
        Character_Trainable c = scr_System_CampaignManager.current.CurrentTarget;
        if (c == null || builtForRef[p] == c.RefID) return;
        builtForRef[p] = c.RefID;

        switch (p)
        {
            case SubPanel.Equipment:
                Utility.DestroyAllChildrenFrom(content_Equipment);
                SideRectUtility.LoadEquipmentData(c, InstantiateGear, InstantiateEquip, InstantiateBox_Text, InstantiateBox_Button);
                break;
            case SubPanel.History:
                Utility.DestroyAllChildrenFrom(content_History);
                SideRectUtility.LoadHistoryLogsData(c, content_History, AddDaySplit, AddMemoryEntry);
                break;
            case SubPanel.Internals:
                Utility.DestroyAllChildrenFrom(content_Internals);
                Utility.DestroyAllChildrenFrom(content_Wombs, 1);
                SideRectUtility.LoadCycleData(c, cycleRect, cycle_total, cycle_current, cycle_ovum, cycle_fertility);
                SideRectUtility.LoadBodyInternalData(c, AddBodyDetail, AddWombDetail);
                break;
            case SubPanel.Schedule:
                SideRectUtility.LoadScheduleData(c, Schedules);
                break;
        }
    }

    private RectTransform InstantiateGear()
    {
        var rect = Instantiate(prefab_BodyInstanceGear);
        rect.SetParent(content_Equipment, false);
        return rect;
    }

    private RectTransform InstantiateEquip(RectTransform parent)
    {
        var rect = Instantiate(prefab_Equipment);
        rect.SetParent(parent, false);
        return rect;
    }

    private bool InstantiateBox_Text(RectTransform parent, string content, bool dimColor = false)
    {
        return InstantiateBox(prefab_TextBox, parent, content, dimColor);
    }

    private bool InstantiateBox_Button(RectTransform parent, string content, bool dimColor = false)
    {
        return InstantiateBox(prefab_ButtonBox, parent, content, dimColor);
    }

    private bool InstantiateBox(TextMeshProUGUI prefab, RectTransform parent, string content, bool dimColor)
    {
        TextMeshProUGUI text = Instantiate(prefab);
        text.text = content;
        text.GetComponent<RectTransform>().SetParent(parent, false);
        if (dimColor)
        {
            text.color = scr_System_CentralControl.current.DisplaySetting.TextColor_disabled.Color;
        }
        return true;
    }

    private scr_memoryDaySplit AddDaySplit()
    {
        return Instantiate(prefab_DaySplit);
    }

    public Scrollbar horizontal;
    public RectTransform horizontalViewRect;    // RectTransform of the scrollview holding the panel toggle buttons, size only
    private SubPanel lastCenteredPanel = SubPanel.None;

    /// <summary>
    /// Set the toggle bar scrollbar value so the active panel's button is centered. Only on active panel change.
    /// Content width is derived from scrollbar size (= view / content).
    /// </summary>
    private void CenterTabButton(SubPanel panel, RectTransform button)
    {
        if (panel == SubPanel.None || panel == lastCenteredPanel) return;
        if (horizontal == null || horizontalViewRect == null || !button.gameObject.activeInHierarchy) return;   // retry on next validate
        if (horizontal.size >= 1f) return;  // nothing to scroll (or scrollbar not yet updated), retry on next validate
        lastCenteredPanel = panel;

        float viewWidth = horizontalViewRect.rect.width;
        float scrollable = viewWidth / horizontal.size - viewWidth;
        if (scrollable <= 0) return;

        // button center relative to view's left edge, then shift by current scroll offset to get its position in content
        float buttonInView = horizontalViewRect.InverseTransformPoint(button.TransformPoint(button.rect.center)).x - horizontalViewRect.rect.xMin;
        float buttonInContent = buttonInView + horizontal.value * scrollable;

        horizontal.value = Mathf.Clamp01((buttonInContent - viewWidth / 2) / scrollable);
    }
    private scr_memoryBox AddMemoryEntry()
    {
        return Instantiate(prefab_MemoryEntry);
    }

    private scr_Panel_BodyDetail AddBodyDetail()
    {
        var box = Instantiate(prefab_PanelInternal);
        box.selfRect.SetParent(content_Internals, false);
        return box;
    }

    private scr_panel_wombdata AddWombDetail()
    {
        var box = Instantiate(prefab_PanelWomb);
        box.selfRect.SetParent(content_Wombs, false);
        return box;
    }

    private void DestroyActor(int refID)
    {
        ButtonValidator val = validatorsByID[refID * 3];
        validatorsByID.Remove(refID * 3);
        val.Destroy();

        scr_SelectableText scr = buttonsByID[refID * 3];
        buttonsByID.Remove(refID * 3);
        DestroyImmediate(scr);

        ButtonValidator val2 = validatorsByID[refID * 3 + 1];
        validatorsByID.Remove(refID * 3 + 1);
        val2.Destroy();

        scr_SelectableText scr2 = buttonsByID[refID * 3 + 1];
        buttonsByID.Remove(refID * 3 + 1);
        DestroyImmediate(scr2);

        ButtonValidator val3 = validatorsByID[refID * 3 + 2];
        validatorsByID.Remove(refID * 3 + 2);
        val3.Destroy();

        scr_SelectableText scr3 = buttonsByID[refID * 3 + 2];
        buttonsByID.Remove(refID * 3 + 2);
        DestroyImmediate(scr3);
    }

    public RectTransform prefab_SexRelations, relationsList;
    public RectTransform prefab_COMdoerReceiver, doerReceiverList;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (hovered) return;    // re-entering self from a child
        hovered = true;
        if (sexJob != null) activePanel = SubPanel.SexCom;
        ApplyVisibility();
    }
    public void OnPointerClick(PointerEventData eventData)
    {
        if ((activePanel == SubPanel.None || userHidden) && scr_System_CampaignManager.current.CurrentTarget != null)
        {
            scr_System_CampaignManager.current.NotifyCurrentTargetClick();//.PortraitManager.ActivityClick();
        }
        /*
        if (activePanel == SubPanel.SexCom && child.gameObject.activeInHierarchy && scr_System_CampaignManager.current.CurrentTarget != null)
        {
            scr_System_CampaignManager.current.NotifyCurrentTargetClick();//.PortraitManager.ActivityClick();
        }*/
    }
    public void removeAP()
    {
        SyncSexRows();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!eventData.fullyExited) return;     // moved onto a child
        hovered = false;
        forceShown = false;
        userHidden = false;
        ApplyVisibility();
    }

    /// <summary>
    /// Sync sex relation / actor rows with the current sex job. Only called while SexCom content is shown.
    /// </summary>
    private void SyncSexRows()
    {
        Job_Sex_Group jSexDebug = sexJob;

        if (child.gameObject.activeInHierarchy && jSexDebug != null)
        {

            //Debug.LogError("sexcompanel turned on actor check "+)

            foreach (ActionPackage_Sex rel in indexSexRelations.Keys)
            {
                if (rel == null) continue;
                if (!jSexDebug.CurrentPackages.Contains(rel)) markforDelete.Add(rel);
            }

            foreach (var rel in markforDelete)
            {
                var rell = rel;
                var box = indexSexRelations[rell];
                var script = box.GetComponent<scr_box_sexRelation>();
                box.gameObject.SetActive(false);
                indexSexRelations.Remove(rel);

                buttonsByID.Remove(script.removeButton.optionID);
                validatorsByID.Remove(script.removeButton.optionID);

                if (box != null) Destroy(box.gameObject);
            }

            markforDelete.Clear();

            foreach (ActionPackage p in jSexDebug.ActivePackages)
            {
                var rel = p as ActionPackage_Sex;
                if (rel == null) continue;
                if (!indexSexRelations.ContainsKey(rel))
                {
                    RectTransform box = Instantiate(prefab_SexRelations);
                    scr_box_sexRelation scr = box.GetComponent<scr_box_sexRelation>();
                    box.SetParent(relationsList, false);
                    scr.Initialize(rel);
                    indexSexRelations.Add(rel, box);

                    scr.removeButton.Initialize(this, new ButtonValidator_RemoveAP(this, rel, jSexDebug, COMmanager));
                    scr.removeButton.optionID = -scr.GetInstanceID();
                    buttonsByID.Add(scr.removeButton.optionID, scr.removeButton);
                    validatorsByID.Add(scr.removeButton.optionID, scr.removeButton.Validator);

                    scr.removeButton.Validate();

                }
            }

            List<int> actorsMarkForDelete = new List<int>();
            foreach (int actorRef in indexActorRef.Keys)
            {
                if (!jSexDebug.actorRefID.Contains(actorRef)) actorsMarkForDelete.Add(actorRef);
            }
            foreach (int actorRef in actorsMarkForDelete)
            {
                RectTransform box = indexActorRef[actorRef];
                indexActorRef.Remove(actorRef);
                DestroyActor(actorRef);
                if (box != null) Destroy(box.gameObject);

                // Also drop them from the pending doer/receiver selection - otherwise a departed actor's
                // ref lingers here even after their row is gone, and the next COM built from this panel
                // (com.MakePackage(job, COMmanager.SexComDoers, COMmanager.SexComReceivers, 0)) would still
                // target someone no longer in the job.
                COMmanager.SexComDoers.Remove(actorRef);
                COMmanager.SexComReceivers.Remove(actorRef);
            }

            foreach (int actorRef in jSexDebug.actorRefID)
            {
                if (!indexActorRef.ContainsKey(actorRef))
                {
                    RectTransform box = Instantiate(prefab_COMdoerReceiver);
                    scr_box_SexCOM_doerreceiver scr = box.GetComponent<scr_box_SexCOM_doerreceiver>();

                    scr.TargetBox.Initialize(this, new ButtonValidator_COMChara(this, actorRef, scr));
                    scr.TargetBox.optionID = actorRef * 3;
                    buttonsByID.Add(scr.TargetBox.optionID, scr.TargetBox);
                    validatorsByID.Add(scr.TargetBox.optionID, scr.TargetBox.Validator);

                    box.SetParent(doerReceiverList, false);

                    //scr.TargetBox.text = scr_System_CampaignManager.current.FindInstanceByID(actorRef).FirstName +" "+ scr_System_CentralControl.current.GetGenderSymbol(actorRef);
                    
                    scr.DoerBox.Initialize(this, new ButtonValidator_COMdoer(this, actorRef, scr.DoerBox, jSexDebug, COMmanager));
                    scr.DoerBox.optionID = actorRef*3+1;
                    buttonsByID.Add(scr.DoerBox.optionID, scr.DoerBox);
                    validatorsByID.Add(scr.DoerBox.optionID, scr.DoerBox.Validator);

                    indexActorRef.Add(actorRef, box);

                    scr.ReceiverBox.Initialize(this, new ButtonValidator_COMreceiver(this, actorRef, scr.ReceiverBox, jSexDebug, COMmanager));
                    scr.ReceiverBox.optionID = actorRef * 3 + 2;
                    buttonsByID.Add(scr.ReceiverBox.optionID, scr.ReceiverBox);
                    validatorsByID.Add(scr.ReceiverBox.optionID, scr.ReceiverBox.Validator);

                    scr.DoerBox.Validate();
                    scr.ReceiverBox.Validate();
                }

                indexActorRef[actorRef].SetSiblingIndex(jSexDebug.actorRefID.IndexOf(actorRef));
            }

           

            //COMmanager.notifyActorsChange();

        }

    }

    public RectTransform swapButtonBox;
    private scr_SelectableText swapButtonScr;


    public RectTransform box_mcNotDoer;
    public TMP_Text namebox_mc, namebox_doers, namebox_receivers;
    private void RefreshVerb()
    {
        if (COMmanager.SexComDoers.Contains(0))
        {
            box_mcNotDoer.gameObject.SetActive(false);
        }
        else
        {
            box_mcNotDoer.gameObject.SetActive(true);
            namebox_mc.text = scr_System_CampaignManager.current.FindInstanceByID(0).FirstName;
        }


        string doers = "";
        string receivers = "";
        foreach (int i in COMmanager.SexComDoers) doers += scr_System_CampaignManager.current.FindInstanceByID(i).FirstName+"\n";
        foreach (int i in COMmanager.SexComReceivers) receivers += scr_System_CampaignManager.current.FindInstanceByID(i).FirstName+"\n";

        if (doers == "") namebox_doers.text = "no one";
        else namebox_doers.text = doers;

        if (receivers == "") namebox_receivers.text = "no one";
        else namebox_receivers.text = receivers;
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
                case -2:
                    button.Initialize(this, new ButtonValidator_swapCOMactors(this, COMmanager, button));
                    break;
                case -1: break;
                case ID_ToggleSexCom:
                    if (scr_System_CentralControl.current.isSafeMode) button.SelfRect.gameObject.SetActive(false);
                    else button.Initialize(this, new ButtonValidator_TogglePanel(this, SubPanel.SexCom, button));
                    break;
                case ID_ToggleEquipment:
                    if (scr_System_CentralControl.current.isSafeMode) button.SelfRect.gameObject.SetActive(false);
                    else button.Initialize(this, new ButtonValidator_TogglePanel(this, SubPanel.Equipment, button));
                    break;
                case ID_ToggleHistory:
                    button.Initialize(this, new ButtonValidator_TogglePanel(this, SubPanel.History, button));
                    break;
                case ID_ToggleInternals:
                    if (scr_System_CentralControl.current.isSafeMode) button.SelfRect.gameObject.SetActive(false);
                    else button.Initialize(this, new ButtonValidator_TogglePanel(this, SubPanel.Internals, button));
                    break;
                case ID_TabInternals:
                    if (scr_System_CentralControl.current.isSafeMode) button.SelfRect.gameObject.SetActive(false);
                    else button.Initialize(this, new ButtonValidator_InternalsTab(this, InternalsTab.Internals, button));
                    break;
                case ID_TabWombs:
                    if (scr_System_CentralControl.current.isSafeMode) button.SelfRect.gameObject.SetActive(false);
                    else button.Initialize(this, new ButtonValidator_InternalsTab(this, InternalsTab.Wombs, button));
                    break;
                case ID_ToggleSchedule:
                    button.Initialize(this, new ButtonValidator_TogglePanel(this, SubPanel.Schedule, button));
                    break;
                case ID_Hide:
                    button.Initialize(this, new ButtonValidator_HidePanel(this, button));
                    break;
                default:
                    button.Initialize(this, button_alwaysValid);
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



    public scr_panel_COMmanager COMmanager;
    public RectTransform prefab_Canvas_charaDetail;
    public class ButtonValidator_COMChara : ButtonValidator, I_ButtonClickable
    {
        int actorRef;
        scr_box_SexCOM_doerreceiver box;
        Character_Trainable chara;
        new scr_Panel_SexComTarget parent;
        public ButtonValidator_COMChara(scr_Menu parent, int actorRef, scr_box_SexCOM_doerreceiver box) : base(parent)
        {
            this.actorRef = actorRef;
            this.box = box;
            this.parent = parent as scr_Panel_SexComTarget;
            this.chara = scr_System_CampaignManager.current.FindInstanceByID(actorRef);
            //box.TargetBox.showBrackets = false;
            box.SetChara(chara);
        }

        public void OnClickButton()
        {
            scr_Menu_CharaDetail detail = scr_System_SceneManager.current.LoadCanvasIntoScene(parent, parent.prefab_Canvas_charaDetail).GetComponent<scr_Menu_CharaDetail>();
            detail.InitializeWithArgument(actorRef);
        }

        public override bool IsButtonValid()
        {
            box.TargetBox.SetText(chara.FirstName + " " + scr_System_CentralControl.current.GetGenderSymbol(actorRef));
            box.Refresh();
            return true;
        }
    }

    public class ButtonValidator_COMdoer : ButtonValidator, I_ButtonClickable
    {
        int actorRef = -1;
        scr_SelectableText text;
        Job job;
        scr_panel_COMmanager COMmanager;

        new scr_Panel_SexComTarget parent;
        public ButtonValidator_COMdoer(scr_Menu parent, int actorRef, scr_SelectableText text, Job job, scr_panel_COMmanager COMmanager) : base(parent)
        {
            this.actorRef = actorRef;
            this.text = text;
            this.job = job;
            this.COMmanager = COMmanager;
            this.parent = parent as scr_Panel_SexComTarget;
        }

        public override bool IsButtonValid()
        {
            if (COMmanager.SexComDoers.Contains(actorRef)) text.SetText("O");
            else text.SetText("--");

            return true;
        }

        public void OnClickButton()
        {
            if (COMmanager.SexComDoers.Contains(actorRef))  COMmanager.SexComDoers.Remove(actorRef); 
            else
            {
                COMmanager.SexComDoers.Add(actorRef); 
                if (scr_System_CampaignManager.current.DisplayPortrait(actorRef)) scr_System_CampaignManager.current.ChangeCurrentTarget(actorRef);
            }

            if (COMmanager.SexComReceivers.Contains(actorRef)) COMmanager.SexComReceivers.Remove(actorRef); 
            

            COMmanager.notifyActorsChange();
        }

        public override void Destroy()
        {
            this.text = null;
            this.job = null;
            this.COMmanager = null;
            this.parent = null;
            base.Destroy();
        }
    }

    public class ButtonValidator_COMreceiver : ButtonValidator, I_ButtonClickable
    {
        int actorRef = -1;
        scr_SelectableText text;
        Job job;
        scr_panel_COMmanager COMmanager;

        new scr_Panel_SexComTarget parent;
        public ButtonValidator_COMreceiver(scr_Menu parent, int actorRef, scr_SelectableText text, Job job, scr_panel_COMmanager COMmanager) : base(parent)
        {
            this.actorRef = actorRef;
            this.text = text;
            this.job = job;
            this.COMmanager = COMmanager;
            this.parent = parent as scr_Panel_SexComTarget;
        }

        public override bool IsButtonValid()
        {
            if (COMmanager.SexComReceivers.Contains(actorRef)) text.SetText("O");
            else text.SetText("--");

            return true;
        }

        public void OnClickButton()
        {
            if (COMmanager.SexComReceivers.Contains(actorRef)) COMmanager.SexComReceivers.Remove(actorRef); 
            else
            {
                COMmanager.SexComReceivers.Add(actorRef); 
                if (scr_System_CampaignManager.current.DisplayPortrait(actorRef)) scr_System_CampaignManager.current.ChangeCurrentTarget(actorRef);
            }

            if (COMmanager.SexComDoers.Contains(actorRef)) COMmanager.SexComDoers.Remove(actorRef); 
            COMmanager.notifyActorsChange();
        }

        public override void Destroy()
        {
            this.text = null;
            this.job = null;
            this.COMmanager = null;
            this.parent = null;
            base.Destroy();
        }
    }

    public class ButtonValidator_swapCOMactors : ButtonValidator, I_ButtonClickable
    {
        scr_panel_COMmanager COMmanager;
        new scr_Panel_SexComTarget parent;
        //scr_SelectableText button;

        public ButtonValidator_swapCOMactors(scr_Menu parent, scr_panel_COMmanager COMmanager, scr_SelectableText button) : base(parent)
        {
            this.COMmanager = COMmanager;
            this.parent = parent as scr_Panel_SexComTarget;
            //this.button = button;
        }

        public override bool IsButtonValid()
        {
            return COMmanager.SexComDoers!= null && COMmanager.SexComReceivers != null;
        }

        public void OnClickButton()
        {
            List<int> temp = COMmanager.SexComDoers;

            COMmanager.SexComDoers = COMmanager.SexComReceivers;
            COMmanager.SexComReceivers = temp;
            temp = null;

            COMmanager.notifyActorsChange();
        }
    }

    public class ButtonValidator_TogglePanel : ButtonValidator, I_ButtonClickable
    {
        SubPanel panel;
        scr_SelectableText button;
        new scr_Panel_SexComTarget parent;

        public ButtonValidator_TogglePanel(scr_Menu parent, SubPanel panel, scr_SelectableText button) : base(parent)
        {
            this.panel = panel;
            this.button = button;
            this.parent = parent as scr_Panel_SexComTarget;
            button.isButtonToggle = true;
        }

        public override bool IsButtonValid()
        {
            button.Toggle(true, parent.activePanel == panel);
            if (parent.activePanel == panel) parent.CenterTabButton(panel, button.SelfRect);
            if (panel == SubPanel.SexCom && parent.sexJob == null)
            {
                state = ButtonValidator_States.Invalid;
                return false;
            }
            state = ButtonValidator_States.Valid;
            return true;
        }

        public void OnClickButton()
        {
            parent.TogglePanel(panel);
        }
    }

    public class ButtonValidator_InternalsTab : ButtonValidator, I_ButtonClickable
    {
        InternalsTab tab;
        scr_SelectableText button;
        new scr_Panel_SexComTarget parent;

        public ButtonValidator_InternalsTab(scr_Menu parent, InternalsTab tab, scr_SelectableText button) : base(parent)
        {
            this.tab = tab;
            this.button = button;
            this.parent = parent as scr_Panel_SexComTarget;
            button.isButtonToggle = true;
        }

        public override bool IsButtonValid()
        {
            button.Toggle(true, parent.internalsTab == tab);
            state = ButtonValidator_States.Valid;
            return true;
        }

        public void OnClickButton()
        {
            parent.SetInternalsTab(tab);
        }
    }

    public class ButtonValidator_HidePanel : ButtonValidator, I_ButtonClickable
    {
        scr_SelectableText button;
        new scr_Panel_SexComTarget parent;

        public ButtonValidator_HidePanel(scr_Menu parent, scr_SelectableText button) : base(parent)
        {
            this.button = button;
            this.parent = parent as scr_Panel_SexComTarget;
            button.isButtonToggle = true;
        }

        public override bool IsButtonValid()
        {
            button.Toggle(true, parent.userHidden);
            state = ButtonValidator_States.Valid;
            return true;
        }

        public void OnClickButton()
        {
            parent.ToggleHide();
        }
    }

    public class ButtonValidator_RemoveAP : ButtonValidator, I_ButtonClickable
    {
        Job job;
        scr_panel_COMmanager COMmanager;
        ActionPackage ap;

        new scr_Panel_SexComTarget parent;
        public ButtonValidator_RemoveAP(scr_Menu parent, ActionPackage ap, Job job, scr_panel_COMmanager COMmanager) : base(parent)
        {
            this.ap = ap;
            this.job = job;
            this.COMmanager = COMmanager;
            this.parent = parent as scr_Panel_SexComTarget;
        }

        public override bool IsButtonValid()
        {
            return true;
        }

        public void OnClickButton()
        {
            //job.CurrentPackages.Remove(ap);
            job.RemovePackage(ap, true);
            COMmanager.notifyActorsChange();
            parent.removeAP();  // let parent take care of self removal
        }

        public override void Destroy()
        {
            this.job = null;
            this.COMmanager = null;
            this.parent = null;
            base.Destroy();
            
        }
    }

}

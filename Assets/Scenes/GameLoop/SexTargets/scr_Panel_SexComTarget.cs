using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System;

public class scr_Panel_SexComTarget : scr_Menu, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public RectTransform child;
    public bool inside = false;
    private Image image_bg;

    //private float update;

    private Dictionary<ActionPackage_Sex, RectTransform> indexSexRelations;
    private List<ActionPackage_Sex> markforDelete;
    private Dictionary<int, RectTransform> indexActorRef;

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

        turnOff();
    }

    protected override void Start()
    {
        if(!initialized) Initialize();
        turnOff();
    }

    private void OnTimeChange(TimeSpan t)
    {
        if (sexJob != null && inside) turnOn(sexJob);
    }

    private void OnViewModeChange(ViewMode vm, bool lockView)
    {
        if (sexJob != null && vm != ViewMode.View_Room)
        {
            //image_bg.raycastTarget = false;
            turnOff();
        }
        else if (sexJob != null && vm == ViewMode.View_Room)
        {
            //image_bg.raycastTarget = true;
            turnOn(sexJob);
        }
    }

    private void InternalUpdate()
    {
        sexJob = scr_System_CampaignManager.current.Player.CurrentJob as Job_Sex_Group;
        if (sexJob == null)
        {
            //image_bg.raycastTarget = false;
            turnOff();

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
        else
        {
            //image_bg.raycastTarget = true;
            turnOn(sexJob);
            ValidateAll();
        }
    }

    private void OnCentralUpdate(bool b)
    {
        InternalUpdate();
    }

    private Job_Sex_Group sexJob = null;

    private void OnPlayerJobChange(int i, Job j)
    {
        //Debug.LogError("ONPLAYERJOBCHANGE SUBSCRIBER CALLED");
        InternalUpdate();
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
        if (scr_System_CampaignManager.current.CurrentViewMode == ViewMode.View_Room && sexJob != null && !inside)
        {
            turnOn(sexJob);
            ValidateAll();
        }
    }
    public void OnPointerClick(PointerEventData eventData)
    {
        if (scr_System_CampaignManager.current.CurrentViewMode == ViewMode.View_Room && sexJob == null && scr_System_CampaignManager.current.CurrentTarget != null)
        {
            //Debug.Log("click!");
            scr_System_CampaignManager.current.NotifyCurrentTargetClick();//.PortraitManager.ActivityClick();
        }
    }
    public void removeAP()
    {
        turnOn(sexJob);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (sexJob != null && inside) turnOff();
    }

    private void turnOn(Job sexJob)
    {
        inside = true;
        //this.gameObject.SetActive(true);
        child.gameObject.SetActive(true);

        Job_Sex_Group jSexDebug = sexJob as Job_Sex_Group;

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

        ValidateAll();
        

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

    private void turnOff()
    {

        inside = false;
        child.gameObject.SetActive(false);
        //this.gameObject.SetActive(false);

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

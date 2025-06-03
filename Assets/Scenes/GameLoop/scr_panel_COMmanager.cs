using System.Collections.Generic;
using UnityEngine;
using System;
using TMPro;
using System.Linq;

public class scr_panel_COMmanager : scr_Menu
{

    public TMP_Text title_interaction, title_sex, title_inventory;
    public scr_HoverableText title_furnitures;
    public TMP_Text ongoingCOMs;
    public enum COMFilter

    {
        Debug,
        DeterministicRolls,
        Sex_Touch,
        Sex_Undress,
        Sex_Oral,
        Sex_Breast,
        Sex_Vaginal,
        Sex_Anal,
        Sex_Service,
        Inv_Ingest,
        Inv_Equip,
        Act_Room,
        Act_Work
    }

    public enum COMTabs
    {
        Touch,
        Interaction,
        Sex,
        Inventory,
        Combat
    }

    public Dictionary<string, int> indexCOM;

    

    List<int> trackedJobRefs;

    public void notifyActorsChange()
    {
        ValidateAll();
    }

    Dictionary<COMFilter, bool> filterState;
    protected void ChangeCOMFilter(COMFilter filter, bool value)
    {
        if (!filterState.ContainsKey(filter)) filterState.Add(filter, value);
        filterState[filter] = value;
    }
    protected bool GetCOMFilter(COMFilter filter)
    {
        if (!filterState.ContainsKey(filter)) filterState.Add(filter, true);
        return filterState[filter];
    }

    private void OnPlayerJobChange(int i, Job j)
    {
        if (j is Job_Sex_Group )
        {
            ChangeCurrentTab(COMTabs.Sex);

        }
        //UpdateJobCOM();
        if(scr_System_CampaignManager.current.CurrentViewMode == ViewMode.View_Room) ValidateAll();
    }

    public List<string> SexComDoersNames
    {
        get
        {
            List<string> names = new List<string>();
            foreach (int i in SexComDoers) names.Add(scr_System_CampaignManager.current.FindInstanceByID(i).FirstName);
            return names;
        }
    }
    public List<string> SexComReceiversNames
    {
        get
        {
            List<string> names = new List<string>();
            foreach (int i in SexComReceivers) names.Add(scr_System_CampaignManager.current.FindInstanceByID(i).FirstName);
            return names;
        }
    }


    private void OnCurrentRoomChange(Room_Instance r)
    {
        ValidateAll();
    }

    private void OnCurrentTargetChange(int refID)
    {
        //UpdateJobCOM();
        if (refID == 0) ChangeCurrentTab(COMTabs.Interaction);

        UpdateEquipBox();
        ValidateAll();
    }

    private void RefreshTitle(int refID)
    {
        List<int> members = new List<int>() { refID };
        members.AddRange(scr_System_CampaignManager.current.PlayerPartyMembers);
        members = members.Distinct().ToList();
        members.Remove(0);

        List<string> names = new List<string>();
        foreach (var mb in members) names.Add(scr_System_CampaignManager.current.FindInstanceByID(mb).FirstName);
        string name = String.Join(",", names);

        if (title_interaction.gameObject.activeInHierarchy)
        {
            //if (members.Count > 0) title_interaction.text = scr_System_Serializer.current.Dictionary.Parse("%%comManager_title_interact_target%%").Replace("$name$", scr_System_CampaignManager.current.FindInstanceByID(refID).FirstName);
            if (members.Count > 0) title_interaction.text = scr_System_Serializer.current.Dictionary.Parse("%%comManager_title_interact_target%%").Replace("$name$", name);
            else title_interaction.text = scr_System_Serializer.current.Dictionary.Parse("%%comManager_title_interact_self%%");
        }
        if (title_furnitures.gameObject.activeInHierarchy && scr_System_CampaignManager.current.CurrentRoom != null)
        {
            List<string> aps = new List<string>();
            foreach(var ap in scr_System_CampaignManager.current.GetRegisteredAPByRoom(scr_System_CampaignManager.current.CurrentRoom.RefID, false))
            {
                if (ap.job.isPlayerRelatedJob) continue;
                if (ap.isTemporaryAP) continue;
                aps.Add(ap.DescriptionText());
            }
            title_furnitures.SetText(scr_System_CampaignManager.current.CurrentRoom.DisplayableFurnitureNames_withLink);
            ongoingCOMs.text = String.Join(", ", aps);
        }
        if (title_inventory.gameObject.activeInHierarchy)
        {
            //if (refID > 0) title_inventory.text = scr_System_Serializer.current.Dictionary.Parse("%%comManager_title_inventory_target%%").Replace("$name$", scr_System_CampaignManager.current.FindInstanceByID(refID).FirstName);
            if (refID > 0) title_inventory.text = scr_System_Serializer.current.Dictionary.Parse("%%comManager_title_inventory_target%%").Replace("$name$", name);
            else title_inventory.text = scr_System_Serializer.current.Dictionary.Parse("%%comManager_title_inventory_self%%");
        }
        if (title_sex.gameObject.activeInHierarchy)
        {

            if (scr_System_CampaignManager.current.displaySex)
            {

                Box_SexCOMs.gameObject.SetActive(true);
                Box_MassageCOMs.gameObject.SetActive(false);
                Box_TouchCOMs.gameObject.SetActive(false);
                Box_ServiceCOMs.gameObject.SetActive(false);
                string doers, receivers, body;
                if (SexComDoers.Count > 0) doers = String.Join(", ", SexComDoersNames);
                else doers = " - ";

                if (SexComReceivers.Count > 0) receivers = String.Join(", ", SexComReceiversNames);
                else receivers = " - ";

                if (!SexComDoers.Contains(0))
                {
                    body = scr_System_Serializer.current.Dictionary.Parse("%%comManager_title_sex_observer%%");
                    //body = body.Replace("$player$", scr_System_CampaignManager.current.FindInstanceByID(0).FirstName);
                }
                else
                {
                    body = scr_System_Serializer.current.Dictionary.Parse("%%comManager_title_sex_participant%%");
                }

                title_sex.text = body.Replace("$doer$",doers).Replace("$receiver$", receivers);
            }
            else
            {

                Box_SexCOMs.gameObject.SetActive(false);
                Box_MassageCOMs.gameObject.SetActive(true);
                Box_TouchCOMs.gameObject.SetActive(true);
                Box_ServiceCOMs.gameObject.SetActive(true);
                if (refID > 0) title_sex.text = scr_System_Serializer.current.Dictionary.Parse("%%comManager_title_skinship_target%%").Replace("$name$", scr_System_CampaignManager.current.FindInstanceByID(refID).FirstName);
                else title_sex.text = scr_System_Serializer.current.Dictionary.Parse("%%comManager_title_skinship_self%%");
            }

        }

        UpdateEquipBox();
    }

    protected void UpdateEquipBox()
    {
        bool playerBox = true, targetBox = true;

        if (title_sex.gameObject.activeInHierarchy)
        {
            playerBox = true;

            if (scr_System_CampaignManager.current.CurrentTargetRef > 0) targetBox = true;
            else targetBox = false;
        }
        else
        {
            playerBox = false;
            targetBox = false;
        }

        if (playerBox)
        {
            player_UndressBox.gameObject.SetActive(true);
            undress_player_name.text = scr_System_CampaignManager.current.Player.FirstName;
            RefreshEquips(ref managedPlayerEquipRefs, scr_System_CampaignManager.current.Player, player_UndressEquipList);
        }
        else
        {
            RefreshEquips(ref managedPlayerEquipRefs, null, player_UndressEquipList);
            player_UndressBox.gameObject.SetActive(false);
        }

        if (targetBox)
        {
            target_UndressBox.gameObject.SetActive(true);
            undress_target_name.text = scr_System_CampaignManager.current.CurrentTarget.FirstName;
            RefreshEquips(ref managedTargetEquipRefs, scr_System_CampaignManager.current.CurrentTarget, target_UndressEquipList);
        }
        else
        {
            RefreshEquips(ref managedTargetEquipRefs, null, target_UndressEquipList);
            target_UndressBox.gameObject.SetActive(false);
        }


    }

    private void OnNotifyUpdate(bool value)
    {
        ValidateAll();   
    }
    public RectTransform prefab_equippedItem;
    List<int> managedPlayerEquipRefs = new List<int>();
    List<int> managedTargetEquipRefs = new List<int>();
    private void RefreshEquips(ref List<int> managedList, Character_Trainable chara, RectTransform gridList)
    {
        // player
        foreach (var i in managedList) DestroyUndressButton(i);
        managedList.Clear();

        if (chara == null) return;

        managedList.AddRange(chara.EquippedItemRefs);
        managedList.AddRange(chara.Inventory.ContentRefs);

        managedList = managedList.Distinct().ToList();
        managedList.Sort();

        var newList = new List<int>();
        foreach(var i in managedList)
        {
            newList.Add(MakeUndressButton(gridList, prefab_equippedItem, chara.RefID, i));
        }
        managedList = newList;
    }

    private void DestroyUndressButton(int buttonRef)
    {

        scr_SelectableText text = buttonsByID[buttonRef];
        buttonsByID.Remove(buttonRef);
        ButtonValidator validator = validatorsByID[buttonRef];
        validatorsByID.Remove(buttonRef);

        validator.Destroy();
        text.gameObject.SetActive(false);
        Destroy(text.gameObject);

    }
    private int MakeUndressButton(RectTransform parent, RectTransform prefab, int charaRef, int equipRef)
    {


        RectTransform r = Instantiate(prefab);
        r.SetParent(parent, false);
        scr_SelectableText comp = r.GetComponent<scr_SelectableText>();

        comp.Initialize(this, new ButtonValidator_equipSingle(this, charaRef, equipRef, comp));
        comp.optionID = AssertUniqueHash(comp.GetHashCode());

        //Debug.Log("Making Undress button with CompID " + comp.optionID);

        buttonsByID.Add(comp.optionID, comp);
        validatorsByID.Add(comp.optionID, comp.Validator);

        comp.Validate();

        return comp.optionID;
    }

    
    List<scr_SelectableText> filters_sextouch;

    protected override void Awake()
    {
        base.Awake();

        scr_System_CampaignManager.current.Observer_PlayerJob += OnPlayerJobChange;
        scr_System_CampaignManager.current.Observer_CurrentTarget += OnCurrentTargetChange;
        scr_System_CampaignManager.current.Observer_UpdateNotice += OnNotifyUpdate;

        forbidCOMRepeatList = new Dictionary<string, Dictionary<int, bool>>();
        currentTab = COMTabs.Interaction;
        trackedJobRefs = new List<int>();

        filters_sextouch = new List<scr_SelectableText>();
        filters_sextouch.AddRange(filters_Sex);
        filters_sextouch.AddRange(filters_Touch);

        filterState = new Dictionary<COMFilter, bool>();
        this.buttonsByID = new Dictionary<int, scr_SelectableText>();
        validatorsByID = new Dictionary<int, ButtonValidator>();
        indexCOM = new Dictionary<string, int>();

        SexComReceivers = new List<int>();
        SexComDoers = new List<int>();

        managedTargetEquipRefs = new List<int>();
        managedPlayerEquipRefs = new List<int>();

    }

    COMTabs currentTab;
    public override void Initialize()
    {
        base.Initialize();
        
        //scr_System_CampaignManager.current.Observer_CurrentRoom += OnCurrentRoomChange;
        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {

            switch (button.optionID)
            {

                case -1: break;
                //case 1: button.Initialize(this, new ButtonValidator_InspectChara(this, button)); break;
                case -2: button.Initialize(this, new ButtonValidator_PartyInvite(this, button)); break;

                //case 106: button.Initialize(this, new ButtonValidator_FixClothes(this, button)); break;



                case -6400: button.Initialize(this, new ButtonValidator_ChangeCOMTab(this, button,COMTabs.Interaction, panel_InteractionCOMs, filters_Interact)); break;
                case -6401: button.Initialize(this, new ButtonValidator_ChangeCOMTab(this, button, COMTabs.Sex, panel_SexCOMs, filters_sextouch, Box_UndressCOMs)); break;
                case -6402: button.Initialize(this, new ButtonValidator_ChangeCOMTab(this, button, COMTabs.Inventory, panel_InventoryCOMs, filters_Inventory)); break;
                case -6403: button.Initialize(this, new ButtonValidator_ChangeCOMTab(this, button, COMTabs.Combat, panel_CombatCOMs, filters_Combat)); break;

                case -6500: button.Initialize(this, new ButtonValidator_ChangeDebugFilter(this, COMFilter.Debug, button)); break;
                case -6511: button.Initialize(this, new ButtonValidator_ChangeDeterministicRollsFilter(this, COMFilter.DeterministicRolls, button)); break;
                case -6509: button.Initialize(this, new ButtonValidator_ChangeCOMFilter(this, COMFilter.Sex_Touch, button)); break;
                case -6510: button.Initialize(this, new ButtonValidator_ChangeCOMFilter(this, COMFilter.Sex_Service, button)); break;
                case -6501: button.Initialize(this, new ButtonValidator_ChangeCOMFilter(this, COMFilter.Sex_Oral, button)); break;
                case -6502: button.Initialize(this, new ButtonValidator_ChangeCOMFilter(this, COMFilter.Sex_Breast, button)); break;
                case -6503: button.Initialize(this, new ButtonValidator_ChangeCOMFilter(this, COMFilter.Sex_Vaginal, button)); break;
                case -6504: button.Initialize(this, new ButtonValidator_ChangeCOMFilter(this, COMFilter.Sex_Anal, button)); break;
                case -6505: button.Initialize(this, new ButtonValidator_ChangeCOMFilter(this, COMFilter.Inv_Ingest, button)); break;
                case -6506: button.Initialize(this, new ButtonValidator_ChangeCOMFilter(this, COMFilter.Inv_Equip, button)); break;
                case -6507: button.Initialize(this, new ButtonValidator_ChangeCOMFilter(this, COMFilter.Act_Room, button)); break;
                case -6508: button.Initialize(this, new ButtonValidator_ChangeCOMFilter(this, COMFilter.Act_Work, button)); break;

                case -6600: button.Initialize(this, new ButtonValidator_FixClothes(this, button, true)); break;
                case -6601: button.Initialize(this, new ButtonValidator_RedressLayers(this, button, true, 1)); break;
                case -6602: button.Initialize(this, new ButtonValidator_UndressLayers(this, button, true, 1)); break;
                case -6603: button.Initialize(this, new ButtonValidator_UndressAll(this, button, true, 1)); break;
                case -6700: button.Initialize(this, new ButtonValidator_FixClothes(this, button, false)); break;
                case -6701: button.Initialize(this, new ButtonValidator_RedressLayers(this, button, false, 1)); break;
                case -6702: button.Initialize(this, new ButtonValidator_UndressLayers(this, button, false, 1)); break;
                case -6703: button.Initialize(this, new ButtonValidator_UndressAll(this, button, false, 1)); break;

                //case -7400: button.Initialize(this, new ButtonValidator_InitSexDebug(this, button)); break;
                //case 7401: //hypnos;
                 //   button.Initialize(this, new ButtonValidator_AddStatusDebug(this, button, Character_Status_Keyword.hypno)); break;
                //case -7408://skip day
                 //   button.Initialize(this, new ButtonValidator_AlwaysTrue(this)); break;
                case -7402: //timestop
                    button.Initialize(this, new ButtonValidator_DebugTimeStop(this, button)); break;
                case -7403: //xray
                    button.Initialize(this, new ButtonValidator_ToggleXrayDebug(this, button)); break;
                case -7404://rapedrug
                    button.Initialize(this, new ButtonValidator_IngestItemDebug(this, button, "consumable_drug_rapedrug","stomach")); break;
                case -7405://sleeping stomach
                    button.Initialize(this, new ButtonValidator_IngestItemDebug(this, button, "consumable_drug_sleepingpill", "stomach")); break;
                case -7406://ovulate
                    button.Initialize(this, new ButtonValidator_IngestItemDebug(this, button, "consumable_drug_ovulationpill", "stomach")); break;
                case -7407://alcohol stomach
                    button.Initialize(this, new ButtonValidator_IngestItemDebug(this, button, "consumable_alcohol_wine", "stomach")); break;
                case -7409:  //sterile egg stomach
                    button.Initialize(this, new ButtonValidator_IngestItemDebug(this, button, "consumable_tlic_sterileeggs", "stomach")); break;
                case -7410:  //sterile egg anal
                    button.Initialize(this, new ButtonValidator_IngestItemDebug(this, button, "consumable_tlic_sterileeggs", "anus")); break;
                case -7411:  //add trash in room
                    button.Initialize(this, new ButtonValidator_DebugAddItemToRoom(this, button, "item_trash")); break;

                case -7500: button.Initialize(this, new ButtonValidator_DebugAddCharaToParty(this, button, "Campaign2_Chara_Amelie")); break;
                case -7501: button.Initialize(this, new ButtonValidator_DebugAddCharaToParty(this, button, "Campaign2_Chara_Elena")); break;
                case -7502: button.Initialize(this, new ButtonValidator_DebugAddCharaToParty(this, button, "Campaign2_Chara_Hiyori")); break;
                case -7503: button.Initialize(this, new ButtonValidator_DebugAddCharaToParty(this, button, "Campaign2_Chara_Olivia")); break;
                case -7504: button.Initialize(this, new ButtonValidator_DebugAddCharaToParty(this, button, "Campaign2_Chara_couple1_Female")); break;
                case -7505: button.Initialize(this, new ButtonValidator_DebugAddCharaToParty(this, button, "Campaign2_Chara_couple1_Male")); break;
                case -7506: button.Initialize(this, new ButtonValidator_DebugAddCharaToParty(this, button, "Campaign2_Chara_HalfasOne")); break;
                case -7507: button.Initialize(this, new ButtonValidator_DebugAddCharaToParty(this, button, "Campaign2_Chara_HalfasTwo")); break;
                case -7508: button.Initialize(this, new ButtonValidator_DebugAddCharaToParty(this, button, "Campaign2_Chara_HalfasThree")); break;

                case -7600: button.Initialize(this, new ButtonValidator_DebugAddCharaToParty(this, button, "creature_animal_pig")); break;
                case -7601: button.Initialize(this, new ButtonValidator_DebugAddCharaToParty(this, button, "creature_animal_horse")); break;
                case -7602: button.Initialize(this, new ButtonValidator_DebugAddCharaToParty(this, button, "creature_animal_warg")); break;
                case -7603: button.Initialize(this, new ButtonValidator_DebugAddCharaToParty(this, button, "creature_humanoid_goblin")); break;
/*
                case -7700: button.Initialize(this, new ButtonValidator_ModCharaPersonality(this, "mood", 1)); break;
                case -7701: button.Initialize(this, new ButtonValidator_ModCharaPersonality(this, "mood", -1)); break;
                case -7702: button.Initialize(this, new ButtonValidator_ModCharaPersonality(this, "stress", 1)); break;
                case -7703: button.Initialize(this, new ButtonValidator_ModCharaPersonality(this, "stress", -1)); break;
                case -7704: button.Initialize(this, new ButtonValidator_ModCharaPersonality(this, "lust", 1)); break;
                case -7705: button.Initialize(this, new ButtonValidator_ModCharaPersonality(this, "lust", -1)); break;
                case -7706: button.Initialize(this, new ButtonValidator_ModCharaPersonality(this, "trust", 10)); break;
                case -7707: button.Initialize(this, new ButtonValidator_ModCharaPersonality(this, "trust", -10)); break;
                case -7708: button.Initialize(this, new ButtonValidator_ModCharaPersonality(this, "fear", 10)); break;
                case -7709: button.Initialize(this, new ButtonValidator_ModCharaPersonality(this, "fear", -10)); break;
                case -7710: button.Initialize(this, new ButtonValidator_ModCharaPersonality(this, "goodwill", 10)); break;
                case -7711: button.Initialize(this, new ButtonValidator_ModCharaPersonality(this, "goodwill", -10)); break;
                case -7712: button.Initialize(this, new ButtonValidator_ModCharaPersonality(this, "badwill", 10)); break;
                case -7713: button.Initialize(this, new ButtonValidator_ModCharaPersonality(this, "badwill", -10)); break;
                case -7714: button.Initialize(this, new ButtonValidator_ModCharaPersonality(this, "desire", 10)); break;
                case -7715: button.Initialize(this, new ButtonValidator_ModCharaPersonality(this, "desire", -10)); break;
                case -7716: button.Initialize(this, new ButtonValidator_ModCharaPersonality(this, "pride", 10)); break;
                case -7717: button.Initialize(this, new ButtonValidator_ModCharaPersonality(this, "pride", -10)); break;
                case -7718: button.Initialize(this, new ButtonValidator_ModCharaPersonality(this, "corruption", 10)); break;
                case -7719: button.Initialize(this, new ButtonValidator_ModCharaPersonality(this, "corruption", -10)); break;
           */     default:
                    //button.Initialize(this, button_alwaysValid);
                    break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }

        //UpdateJobCOM();
        // build all presetList
        ValidateAll();
    }

    protected override void OnEnable()
    {
        base.OnEnable();

        ValidateAll();
        //RefreshTitle(scr_System_CampaignManager.current.CurrentTargetRef);
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

    public RectTransform panel_InteractionCOMs, panel_SexCOMs, panel_InventoryCOMs, panel_CombatCOMs;
    public RectTransform Box_SexCOMs, Box_InteractionCOMs, Box_UndressCOMs, Box_FurnitureCOMs, Box_FurnitureCOMsList, Box_TouchCOMs, Box_MassageCOMs, Box_ServiceCOMs, buttonPrefab_COM, Box_InitiateSex;
    public RectTransform Box_Filters;
    public List<scr_SelectableText> filters_Sex, filters_Touch, filters_Interact, filters_Inventory, filters_Combat;

    public scr_IndividualCOMBox prefab_IndividualCOMBox;

    protected List<scr_SelectableText> currentTab_Filters { get
        {
            switch (currentTab)
            {
                case COMTabs.Interaction: return filters_Interact;
                case COMTabs.Sex: 
                    if (scr_System_CampaignManager.current.displaySex) return filters_Sex;
                    else return filters_Touch;
                case COMTabs.Inventory: return filters_Inventory;
                case COMTabs.Combat: return filters_Combat;
                default: return null;
            }
        } }

    bool displayInventory
    {
        get
        {
            return true;
        }
    }

    bool displayCombat
    {
        get
        {
            return true;
        }
    }

    private List<int> tempList;

    public override void ValidateAll()
    {
        COMRepeat_Reset();
        UpdateJobCOM();
        //scr_System_CampaignManager.current.NotifyUpdate();
        RefreshTitle(scr_System_CampaignManager.current.CurrentTargetRef);
        base.ValidateAll();
    }

    Job_Sex_Group currentSexJob;
    /*
     UndressCOMs
    - undress outer
    - undress inner
    - undress skin

    - foreach inner that can be lifted, addcom lift
    - foreach skin that can be lifted, addcom lift
     */

    Dictionary<int, scr_IndividualCOMBox> furnitureRectList = new Dictionary<int, scr_IndividualCOMBox>();

    public TMP_Text undress_player_name, undress_target_name;
    public RectTransform player_UndressBox, player_UndressEquipList, target_UndressBox, target_UndressEquipList;

    private void UpdateJobCOM()
    {
        foreach(var kvpair in furnitureRectList)
        {
            Job_Furniture jf = (scr_System_CampaignManager.current.FindJobInstanceByID(kvpair.Key) as Job_Furniture);
            kvpair.Value.title.text = jf.DisplayName;
            kvpair.Value.tooltip.text = jf.ContainerTooltip;
        }


        tempList = new List<int>();
        tempList.Add(scr_System_CampaignManager.current.jobRef_playerCOM);
        string s3 = "Tracking player interactionjob " + scr_System_CampaignManager.current.jobRef_playerCOM;
        /**/
        if (scr_System_CampaignManager.current.Player.CurrentJob != null)
        {
            s3 += "\nTracking player current job " + scr_System_CampaignManager.current.Player.CurrentJob.RefID;
            tempList.Add(scr_System_CampaignManager.current.Player.CurrentJob.RefID);
        }


        var currentTarget = scr_System_CampaignManager.current.CurrentTarget;
        if (currentTarget != null && currentTarget.RefID != 0 && currentTarget.InteractionJob != null)
        {   
            tempList.Add(currentTarget.InteractionJob.RefID);
            s3 += "\nTracking CurrentTarget interact job " + currentTarget.InteractionJob.RefID;
        }


        //Debug.Log("UPDATEJOBCOM currentRoom ["+ scr_System_CampaignManager.current.CurrentRoom .DisplayName+ "] isJobsnull? ["+(scr_System_CampaignManager.current.CurrentRoom.Jobs == null) +"]");
        foreach (var job in scr_System_CampaignManager.current.CurrentRoom.Jobs)
        {
            //Debug.LogError("UPDATEJOBCOM JOB " + job.DisplayName);
            tempList.Add(job.RefID);
            s3 += "\nTracking Roomjobref " + job.RefID;
        }

        if (scr_System_CentralControl.current.LogPrefs.DLog_Jobs) Debug.Log("All tracked job : [" + String.Join("] [", trackedJobRefs) + "]\n"+s3);

        foreach(int jobRef in tempList)
        {
            
            if (!trackedJobRefs.Contains(jobRef) && scr_System_CampaignManager.current.FindJobInstanceByID(jobRef) != null)
            {

                //Debug.Log("tracking jobRef " + jobRef);

                trackedJobRefs.Add(jobRef);
                Job j = scr_System_CampaignManager.current.FindJobInstanceByID(jobRef);


                if (j is Job_CharaCOM)
                {
                    Job_CharaCOM jobChara = j as Job_CharaCOM;

                   // Debug.Log("Making Chara Job ");
                    foreach (COM c in jobChara.allusableCOMs)
                    {
                        //Debug.Log("Making Chara COM " + c.ID);
                        if (c.comTags.Contains("initSex") || c.comTags.Contains("endSex"))
                        {
                            MakeCOMButton(Box_InitiateSex, buttonPrefab_COM, jobChara, c, true, true);
                        }
                        else if (c.comTags.Contains("massage") && !c.comTags.Contains("sex"))
                        {
                            MakeCOMButton(Box_MassageCOMs, buttonPrefab_COM, jobChara, c, true, true);
                        }
                        else if (c.comTags.Contains("touch") && !c.comTags.Contains("sex"))
                        {
                            MakeCOMButton(Box_TouchCOMs, buttonPrefab_COM, jobChara, c, true, false);
                        }
                        else if (c.comTags.Contains("service") && !c.comTags.Contains("sex"))
                        {
                            MakeCOMButton(Box_ServiceCOMs, buttonPrefab_COM, jobChara, c, true, true);
                        }
                        else if (c.comTags.Contains("interaction") || c.comTags.Contains("action"))
                        {
                            MakeCOMButton(Box_InteractionCOMs, buttonPrefab_COM, jobChara, c, true, true);
                        }
                        else
                        {
                            Debug.Log("Aborting Chara COM " + c.ID);
                        }
                    }

                }
                else if (j is Job_PlayerCOM)
                {
                    Job_PlayerCOM jPlayer = j as Job_PlayerCOM;

                    //Debug.Log("Making Player Job ");

                    foreach (COM c in jPlayer.allusableCOMs)
                    {
                        if (c.comTags.Contains("interaction") || c.comTags.Contains("action"))
                        {
                           // Debug.Log("Making Player COM " + c.ID);
                            MakeCOMButton(Box_InteractionCOMs, buttonPrefab_COM, jPlayer, c, true, true);
                        }
                        else
                        {
                            Debug.Log("Aborting Player COM " + c.ID);
                        }
                    }
                }
                else if (j is Job_Sex_Group)
                {

                    Job_Sex_Group jdebug = j as Job_Sex_Group;
                    Debug.Log("Making Sex Job " + jdebug.RefID+" registeredactors "+String.Join("|", jdebug.actorRefID));
                    currentSexJob = jdebug;
                    foreach (COM c in jdebug.allusableCOMs)
                    {
                        MakeCOMButton(Box_SexCOMs, buttonPrefab_COM, jdebug, c);
                    }

                    SexComDoers.Clear();
                    SexComReceivers.Clear();

                    if (currentSexJob.actorRefID.Count > 0) SexComDoers.Add(currentSexJob.actorRefID[0]);
                    if (currentSexJob.actorRefID.Count > 1) SexComReceivers.Add(currentSexJob.actorRefID[1]);

                }
                else if (j is Job_Furniture)
                {

                    // Debug.Log("Making Furniture Job ");
                    Job_Furniture jfurn = j as Job_Furniture;

                    if ((j as Job_Furniture).allusableCOMs.Count > 0)
                    {
                        if (jfurn.isContainer)
                        {
                            scr_IndividualCOMBox newScript = Instantiate(prefab_IndividualCOMBox);
                            newScript.title.text = jfurn.DisplayName;


                            RectTransform newRect = newScript.GetComponent<RectTransform>();
                            newRect.SetParent(Box_FurnitureCOMsList, false);

                            foreach (var ap in j.MakePackages(scr_System_CampaignManager.current.Player, true))
                            {
                                MakeCOMButton(newScript.list, buttonPrefab_COM, j, ap.targetCOM, ap.targetCOM.COMRepeat, false, ap);
                            }
                            furnitureRectList.Add(jfurn.RefID, newScript);
                        }
                        else
                        {
                            var packages = (j as Job_Furniture).MakePackagesJoinable(scr_System_CampaignManager.current.Player);
                            if (packages.Count > 0)
                            {
                                foreach(var ap in packages)
                                {
                                    MakeCOMButton(Box_FurnitureCOMs, buttonPrefab_COM, ap, false, true);
                                }
                            }
                            else
                            {
                                foreach (var ap in j.MakePackages(scr_System_CampaignManager.current.Player, true))
                                {
                                    MakeCOMButton(Box_FurnitureCOMs, buttonPrefab_COM, j, ap.targetCOM, ap.targetCOM.COMRepeat, false, ap);
                                }
                            }
                        }
                    }

                    /// use a different method
                    /*
                    foreach (var ap in j.JoinablePackages(0))
                    {
                        MakeCOMButton(Box_FurnitureCOMs, buttonPrefab_COM, ap,false, false);
                    }*/
                }
            }
            else
            {
                //Debug.Log(jobRef+" already tracked");
                Job j = scr_System_CampaignManager.current.FindJobInstanceByID(jobRef);
                if(j is Job_Furniture)
                {
                    foreach (var ap in j.JoinablePackages(0))
                    {
                        MakeCOMButton(Box_FurnitureCOMs, buttonPrefab_COM, ap, false, false);
                    }
                }

            }
        }

        for(int i = trackedJobRefs.Count - 1; i >= 0; i--)
        {
            if (!tempList.Contains(trackedJobRefs[i]))
            {
                DestroyCOMButton(trackedJobRefs[i]);
                if (furnitureRectList.ContainsKey(trackedJobRefs[i])) DestroyCOMBox(trackedJobRefs[i]);

                foreach (KeyValuePair<string, Dictionary<int, bool>> kvp in forbidCOMRepeatList)
                {
                    if (kvp.Value.ContainsKey(trackedJobRefs[i])) kvp.Value.Remove(trackedJobRefs[i]);
                }
                trackedJobRefs.RemoveAt(i);
            }
        }


        if (scr_System_CentralControl.current.LogPrefs.DLog_CurrentRoomJob)
        {
            string s2 = "Current Jobs in room: \n";
            foreach (var job in scr_System_CampaignManager.current.CurrentRoom.Jobs)
            {
                if (job is Job_Furniture) continue;
                s2 += "[" + job.RefID + "]:";
                foreach (COM c in job.allusableCOMs)
                {
                    s2 += c.ID + " ";
                }
                s2 += "]\n";
            }

            string s = "Current furniture in room: \n";
            foreach (var furniture in scr_System_CampaignManager.current.CurrentRoom.Furnitures)
            {
                s += "[" + furniture.DisplayName +"|"+furniture.JobGiver.RefID+ "]:";
                if (furniture.JobGiver != null)
                {
                    foreach (COM c in furniture.JobGiver.allusableCOMs) s += c.ID + " ";
                }
                s += "]\n";
            }
            Debug.Log(s2 +"\n"+s);


        }
    }

    private void DestroyCOMBox(int jobRef)
    {
        RectTransform rect = furnitureRectList[jobRef].GetComponent<RectTransform>();
        furnitureRectList.Remove(jobRef);
        DestroyImmediate(rect.gameObject);
    }

    Dictionary<string, Dictionary<int, bool>> forbidCOMRepeatList = null;
    public bool COMRepeat_Get(string comID, int jobID, bool value)
    {
        if (!forbidCOMRepeatList.ContainsKey(comID)) return true;
        if (!forbidCOMRepeatList[comID].ContainsKey(jobID)) return true;

        bool returnValue = false;

        List<int> keysList = forbidCOMRepeatList[comID].Keys.ToList();
        for (int i = 0; i < keysList.Count; i++)
        {
            if (returnValue) break;
            if (forbidCOMRepeatList[comID][keysList[i]]) returnValue = (keysList[i] == jobID);
        }

        forbidCOMRepeatList[comID][jobID] = value;

        if (returnValue) return returnValue;
        else return keysList[keysList.Count - 1] == jobID;
    }
    public void COMRepeat_Register(string comID, int jobID)
    {
        if (!forbidCOMRepeatList.ContainsKey(comID)) forbidCOMRepeatList.Add(comID, new Dictionary<int, bool>());
        if (!forbidCOMRepeatList[comID].ContainsKey(jobID)) forbidCOMRepeatList[comID].Add(jobID, false);
        //Debug.Log("COMREPEAT registering COM[" + comID + "] from job[" + jobID + "]"+forbidCOMRepeatListContent);
    }

    public string forbidCOMRepeatListContent { get
        {
            string s = "";
            foreach(KeyValuePair<string, Dictionary<int, bool>> kvp in forbidCOMRepeatList)
            {
                s += "\n[" + kvp.Key + "] -";
                foreach (KeyValuePair<int, bool> kkvp in kvp.Value) s += " " + kkvp.Key + "|" + kkvp.Value;
            }
            return s; 
        } }
    public void COMRepeat_Reset()
    {
        foreach (var kvp in forbidCOMRepeatList.Keys.ToList())
        {
            foreach(var kkvp in forbidCOMRepeatList[kvp].Keys.ToList())
            {
                forbidCOMRepeatList[kvp][kkvp] = false;
            }
        }
    }

    private void ChangeCurrentTab(COMTabs tab)
    {
        currentTab = tab;
        ValidateAll();
    }

    protected bool ValidateCOMByTags(COM com)
    {
        if (com.comTags.Contains("initSex") || com.comTags.Contains("endSex")) return scr_System_CampaignManager.current.displaySex || GetCOMFilter(COMFilter.Sex_Touch);
        if (com.comTags.Contains("sex") && !scr_System_CampaignManager.current.displaySex) return false;
        if (com.comTags.Contains("service") && !GetCOMFilter(COMFilter.Sex_Service)) return false;
        if (com.comTags.Contains("vagina") && !GetCOMFilter(COMFilter.Sex_Vaginal)) return false;
        if (com.comTags.Contains("anus") && !GetCOMFilter(COMFilter.Sex_Anal)) return false;
        if (com.comTags.Contains("oral") && !GetCOMFilter(COMFilter.Sex_Oral)) return false;
        if (com.comTags.Contains("breast") && !GetCOMFilter(COMFilter.Sex_Breast)) return false;
        if (com.comTags.Contains("furniture") && !GetCOMFilter(COMFilter.Act_Room)) return false;
        
        if (com.comTags.Contains("touch") && !GetCOMFilter(COMFilter.Sex_Touch)) return false;
        if ((com.comTags.Contains("touch") || com.comTags.Contains("massage") || com.comTags.Contains("service")) && scr_System_CampaignManager.current.displaySex && !com.comTags.Contains("sex")) return false;
        return true ;
    }

    private void MakeCOMButton(RectTransform parent, RectTransform prefab, Job job, COM com, bool comRepeat = false, bool hidingOverride = false, ActionPackage ap = null)
    {
        var key = "|jobRef|"+ (job == null ? "": job.RefID) + "|comID|" + (com == null? "": com.ID) + "|doers|" + (ap == null? "": ap.DoerRefs.Sum().ToString());
        if (!indexCOM.ContainsKey(key))
        {
            int hash = AssertUniqueHash( job.RefID + com.GetHashCode() + (ap != null ? ap.DoerRefs.Sum() : 0));
            if (!comRepeat)
            {
                //Debug.Log("registering comRepeat button for " + com.ID);
                COMRepeat_Register(com.ID, job.RefID);
            }

            if (buttonsByID.ContainsKey(hash))
            {
                int previousJob = (validatorsByID[hash] as ButtonValidator_validateCOM).ChangeValidatorReference(job, job.RefID);
                var tempList = indexCOM.Keys.ToList();
                var removeList = tempList.FindAll(x => x.Contains("|jobRef|" + previousJob + "|comID|" + com.ID));
                foreach (var i in removeList) indexCOM.Remove(i);
               // indexCOM.Remove(previousJob + "|" + com.ID + "|" + ap.DoerRefs.Sum().ToString());
                indexCOM.Add(key, buttonsByID[hash].optionID);
            }
            else
            {
                RectTransform r = Instantiate(prefab);
                r.SetParent(parent, false);
                scr_SelectableText comp = r.GetComponent<scr_SelectableText>();

                if (ap != null) comp.Initialize(this, new ButtonValidator_validateCOM(this, ap, comp, comRepeat, hidingOverride));
                else comp.Initialize(this, new ButtonValidator_validateCOM(this, job.RefID, com.ID, comp, comRepeat, hidingOverride));
                comp.linkText = com.ID;
                comp.SetText(com.displayName);

                comp.optionID = hash;
                comp.showBrackets = true;

                buttonsByID.Add(comp.optionID, comp);
                validatorsByID.Add(comp.optionID, comp.Validator);

                comp.Validate();

                indexCOM.Add(key, comp.optionID);
            }
        }
    }

    private void MakeCOMButton(RectTransform parent, RectTransform prefab, ActionPackage ap, bool comRepeat = false, bool hidingOverride = false)
    {
        /*
        take in AP by reference. 
        during validation, make copy of said AP and revalidate
        during execution, modify original AP and send request
        -> above 2 made inside buttonvalidator
        */
        var com = ap.targetCOM;
        var job = ap.job;
        if (job == null || com == null) 
        {
           // Debug.Log("AP " + ap.DisplayName + " makeCOMButton Error, exiting");
            return;
        }
        var key = "|jobRef|"+ (job == null ? "": job.RefID) + "|comID|" + (com == null? "": com.ID) + "|doers|" + ap.DoerRefs.Sum().ToString();
        if (!indexCOM.ContainsKey(key))
        {
            //Debug.Log("AP validator making AP with key ["+key+"]");
            int hash = AssertUniqueHash(job.RefID + com.GetHashCode() + ap.DoerRefs.Sum());
            if (!comRepeat) COMRepeat_Register(com.ID, job.RefID);
            
            RectTransform r = Instantiate(prefab);
            r.SetParent(parent, false);
            scr_SelectableText comp = r.GetComponent<scr_SelectableText>();

            comp.Initialize(this, new ButtonValidator_validateAP(this, ap, comp, comRepeat, hidingOverride));
            comp.linkText = com.ID;
            comp.SetText(com.displayName);

            comp.optionID = hash;
            comp.showBrackets = true;

            buttonsByID.Add(comp.optionID, comp);
            validatorsByID.Add(comp.optionID, comp.Validator);

            comp.Validate();

            indexCOM.Add(key, comp.optionID);
        
        }
        else if (validatorsByID.ContainsKey(indexCOM[key]) && (validatorsByID[indexCOM[key]] as ButtonValidator_validateAP) != null)
        {
            //Debug.Log("AP validator remaking AP validator by injection");
            var validator = validatorsByID[indexCOM[key]] as ButtonValidator_validateAP;
            validator.RemakePackage(ap);
        }
        else Debug.LogError("AP validator remaking AP non unique key ["+key+"], failed validator injection");
        
    }

    private void DestroyCOMButton(int jobRef)
    {
        List<string> removeList = new List<string>();

        foreach(string key in indexCOM.Keys)
        {
            if (key.Contains("|jobRef|" + jobRef + "|")) removeList.Add(key);
        }
        foreach (var kvp in removeList)
        {
            scr_SelectableText text = buttonsByID[indexCOM[kvp]];
            buttonsByID.Remove(indexCOM[kvp]);
            ButtonValidator validator = validatorsByID[indexCOM[kvp]];
            validatorsByID.Remove(indexCOM[kvp]);

            validator.Destroy();
            text.gameObject.SetActive(false);
            Destroy(text.gameObject);
            indexCOM.Remove(kvp);
        }
    }

    public class ButtonValidator_validateAP : ButtonValidator, I_ButtonClickable
    {
        new scr_panel_COMmanager parent;
        int jobRefID;
        string comID;

        scr_SelectableText text;

        COM com 
        { 
            get { 
                if (job == null) return null;
                else return job.allusableCOMs.Find(x => x.ID == comID);
            } 
        }
        Job job { get { return scr_System_CampaignManager.current.FindJobInstanceByID(jobRefID); } }

        ActionPackage package_cache = null;
        ActionPackage package
        {
            get
            {
                if (package_cache == null) RemakePackage();
                return package_cache;
            }
        }



        public ActionPackage cachedAP = null;
        bool hidingOverride = false;

        bool COMRepeat;

        public ButtonValidator_validateAP(scr_Menu parent, ActionPackage AP, scr_SelectableText text, bool COMRepeat = false, bool hidingOverride = false):base(parent)
        {
            this.parent = parent as scr_panel_COMmanager;
            this.jobRefID = AP.job.RefID;
            this.comID = AP.targetCOM.ID;
            this.text = text;
            this.COMRepeat = COMRepeat;
            this.hidingOverride = hidingOverride;
            RemakePackage(AP);
        }
        
        public void RemakePackage(ActionPackage injectAP = null)
        {
            if (injectAP != null) this.cachedAP = injectAP;
            if (cachedAP == null) this.package_cache = null;
            else this.package_cache = cachedAP.Copy();
        }

        public override bool IsButtonValid()
        {
            bool returnVal = true;
            bool display = true;
            tooltip = "";
            
            package_cache = null;

            if (package == null || package.Duration < 1)
            {
                text.gameObject.SetActive(false);
                return false;
            }
            else if (!job.ExecutingPackages.Contains(cachedAP))
            {
                tooltip += "AP no longer exist in job";
                text.gameObject.SetActive(false);
                return false;

            }

            else if (scr_System_CampaignManager.current.DebugMode)
            { 
                tooltip += "AP " + package.DisplayName + " isSex[" + (package as ActionPackage_Sex != null) + "] isInteract [" + (package as ActionPackage_Interaction != null) + "]\n";
            }



            if (parent.ValidateCOMByTags(com))
            {
                if (!com.ValidateJob(job, out var msg))
                {
                    returnVal = false;
                    tooltip += msg + "]\n";
                }
                else
                {
                    if (package is ActionPackage_Sex)
                    {
                        //Debug.Log("package apsex");
                        if (scr_System_CampaignManager.current.displaySex) package.ResetRequest(parent.SexComDoers, parent.SexComReceivers, 0);// (package as ActionPackage_Sex).ReInitializeCOM(job, com, parent.SexComDoers, parent.SexComReceivers, 0, false);
                        else package.ResetRequest(new List<int>() { scr_System_CampaignManager.current.Player.RefID }, scr_System_CampaignManager.current.CurrentTargetRef > 0 ? new List<int>() { job.targetActorRef } : new List<int>(), 0);//(package as ActionPackage_Sex).ReInitializeCOM(job, com, new List<int>() { scr_System_CampaignManager.current.Player.RefID },
                                                                                                                                                                                                                                              //scr_System_CampaignManager.current.CurrentTargetRef > 0 ? new List<int>() { job.targetActorRef } : new List<int>() { }, 0, false);
                    }
                    else if (package is ActionPackage_Interaction || package is ActionPackage_ProductionOrder)
                    {
                        var doers = new List<int>(package.DoerRefs);
                        var receivers = new List<int>(package.ReceiverRefs);
                        List<int> targets = new List<int>();
                        var currentref = scr_System_CampaignManager.current.CurrentTargetRef;
                        if (currentref > 0 && !doers.Contains(currentref) && !receivers.Contains(currentref)) targets.Add(currentref);
                        targets.AddRange(scr_System_CampaignManager.current.PlayerPartyMembers);
                        targets = targets.Distinct().ToList();
                        targets.Remove(0);
                        targets.RemoveAll(x => doers.Contains(x) || receivers.Contains(x));

                        if (doers.Count < 1) doers.Add(0);
                        else if (!doers.Contains(0) && !receivers.Contains(0)) receivers.Add(0);
                        receivers.AddRange(targets);

                        package.ResetRequest(doers, receivers, 0);
                        //package.ReInitializeCOM(job, com, doers, receivers, 0, false);
                    }
                    else
                    {
                        Debug.LogError("COMMANAGER package typecheck failed");
                    }


                    tooltip += "doer[" + String.Join("|", package.DoerRefs) + "] receiver [" + String.Join("|", package.ReceiverRefs) + "]\n";
                    //text.SetText(package.DisplayName);

                    if (package.Validate())
                    {
                        returnVal = returnVal && true;
                        tooltip += "Time cost [" + package.Duration + "] minutes, Resources cost [" + package.ResourceCost + "]\n";
                        tooltip += package.GetSuccessRateString();
                    }
                    else
                    {
                        returnVal = false;
                        tooltip += "package did not pass internal validation\n";
                    }
                    package.tooltip.RemoveAll(x => x == "" || x.Length < 1);
                    tooltip += "\n" + String.Join("\n", package.tooltip);

                    if (job.targetActorRef != scr_System_CampaignManager.current.CurrentTargetRef) returnVal = false;
                }
            }
            else
            {
                returnVal = false;
                tooltip += "package did not pass external validation, validateByTag[" + parent.ValidateCOMByTags(com) + "]\n";
                //this.text.gameObject.SetActive(false);
                display = false;
            }

            if (package.DoerRefs.Contains(0)) text.SetText(package.DisplayName);
            else 
            {
                var tempList = new List<string>();
                foreach (var i in package.doer) tempList.Add(i.FirstName);
                text.SetText(scr_System_Serializer.current.Dictionary.QueryThenParse(package.JoinAPDescriptorKey).Replace("$apName$", package.DisplayName).Replace("$target$", String.Join(" ", tempList)));
            }

            if (!COMRepeat)
            {
                // tooltip += "inside comrepeat loop\n";
                bool comRepeatGet = parent.COMRepeat_Get(this.comID, job.RefID, returnVal);

                //Debug.Log("COMREPEAT Get COM[" + comID + "] from job[" + job.RefID + "] with value [" + returnVal + "] and in list [" + parent.forbidCOMRepeatList[comID][job.RefID] + "] returnValue ["+ comRepeatGet + "]" + parent.forbidCOMRepeatListContent);
                
                if (comRepeatGet) display = display && true;

                //else if (!com.HideWhenInvalid) this.text.gameObject.SetActive(true);
                else display = display && false;

            }


            if (returnVal) display = display && true;
            else if (!(com.HideWhenInvalid || hidingOverride)) display = display && true;
            else if (com.comTags.Contains("player")) display = display && true;
            else if (com.comTags.Contains("initSex") || com.comTags.Contains("endSex")) display = display && true;
            else display = display && false;

            if (display) text.gameObject.SetActive(true);
            else text.gameObject.SetActive(false);

            return returnVal;
        }

        public override void Destroy()
        {
            this.text = null;
            this.parent = null;
        }

        public void OnClickButton()
        {
            //Debug.Log("Adding package to job [" + job.GetJobDescription(0) + "] with actors [" + String.Join(" ", package.actorRefs)+"], doers["+ String.Join(" ", package.DoerRefs)+"] receivers["+ String.Join(" ", package.ReceiverRefs) + "]");
            scr_System_CampaignManager.current.Player.ChangeCurrentJob(job);
            
            // modify cachedAP doers and receivers
            cachedAP.ResetRequest(package.DoerRefs, package.ReceiverRefs, package.masterRef);
            // need to reset EP. Re-request should trigger EP rebuild on next update

            scr_System_CampaignManager.current.FreeUpdate(-1, this.text.Text.text);
        }
    }

    public class ButtonValidator_validateCOM : ButtonValidator, I_ButtonClickable
    {
        new scr_panel_COMmanager parent;
        int jobRefID;
        string comID;

        scr_SelectableText text;

        COM com 
        { 
            get { 
                if (job == null) return null;
                return job.allusableCOMs.Find(x => x.ID == comID);
            } 
        }
        Job job { get { return scr_System_CampaignManager.current.FindJobInstanceByID(jobRefID); } }

        ActionPackage package_cache = null;
        ActionPackage package
        {
            get
            {
                if (package_cache == null) package_cache = RemakePackage();
                return package_cache;
            }
        }

        List<int> cachedDoers = new List<int>(), cachedReceivers = new List<int>();
        bool hidingOverride = false;

        bool COMRepeat;

        public ButtonValidator_validateCOM(scr_Menu parent, ActionPackage AP, scr_SelectableText text, bool COMRepeat = false, bool hidingOverride = false):base(parent)
        {
            this.package_cache = RemakePackage(AP);
            this.parent = parent as scr_panel_COMmanager;
            this.jobRefID = AP.job.RefID;
            this.comID = AP.targetCOM.ID;
            this.text = text;
            this.COMRepeat = COMRepeat;
            this.hidingOverride = hidingOverride;
        }
        public ButtonValidator_validateCOM(scr_Menu parent, int jobRefID, string comID, scr_SelectableText text, bool COMRepeat = false, bool hidingOverride = false) : base(parent)
        {
            this.parent = parent as scr_panel_COMmanager;
            this.jobRefID = jobRefID;
            this.comID = comID;
            this.text = text;
            this.COMRepeat = COMRepeat;
            this.hidingOverride = hidingOverride;
        }

        private ActionPackage RemakePackage(ActionPackage injectAP = null)
        {
            // if (com == null) return null;
            

            if(injectAP == null && com != null)
            {
                if (com.ActionPackageClass == "ActionPackage_Sex" && com is COM_Sex)
                {
                    injectAP = com.MakePackage(job, parent.SexComDoers, parent.SexComReceivers, 0);
                }
                else if (!com.comTags.Contains("furniture"))
                {
                    List<int> targets = new List<int>() { job.targetActorRef };
                    if (com.requirements.TreatReceiverAsDoer) targets.AddRange(scr_System_CampaignManager.current.PlayerPartyMembers);
                    targets = targets.Distinct().ToList();
                    injectAP = com.MakePackage(job, new List<int>() { scr_System_CampaignManager.current.Player.RefID }, targets, 0);
                }
                else
                {
                    Debug.LogError("remake package called on invalid AP");
                    injectAP = null;
                }
            }

            if (injectAP != null)
            {
                cachedDoers = new List<int>(injectAP.DoerRefs);
                cachedReceivers = new List<int>(injectAP.ReceiverRefs);
            }
            else
            {
                cachedDoers.Clear();
                cachedReceivers.Clear();
            }
            return injectAP;

            
        }

        /// <summary>
        /// return previous jobRefID
        /// </summary>
        /// <param name="job"></param>
        /// <param name="jobRef"></param>
        public int ChangeValidatorReference(Job job, int jobRef)
        {
            int previous = jobRefID;
            this.jobRefID = jobRef;
            return previous;
        }

        public override bool IsButtonValid()
        {
            bool returnVal = true;
            //returnVal = returnVal && (com != null) && (com.IsActorValid(0, scr_System_CampaignManager.current.CurrentTarget));
            bool display = true;
            //if (com.comTags.Contains("sex") && )
            tooltip = "";



            if (package == null)
            {
                Debug.LogError("COMVALIDATOR ISBUTTON VALID ERROR PACKAGE NULL");
                return false;
            }
            else
            {
                if (scr_System_CampaignManager.current.DebugMode)
                {
                    tooltip += "AP " + package.DisplayName + " isSex[" + (package as ActionPackage_Sex != null) + "] isInteract [" + (package as ActionPackage_Interaction != null) + "]\n";
                }
            }

            if (parent.ValidateCOMByTags(com))
            {
                if (com.ValidateJob(job, out var msg))
                {
                    /*
if(package is ActionPackage_ProductionOrder)
{
    Debug.LogError("COMMANAGER package typecheck ActionPackage_ProductionOrder");
}
else */
                    if (package is ActionPackage_Sex)
                    {
                        //Debug.Log("package apsex");
                        if (scr_System_CampaignManager.current.displaySex) package.ResetRequest(parent.SexComDoers, parent.SexComReceivers, 0);// (package as ActionPackage_Sex).ReInitializeCOM(job, com, parent.SexComDoers, parent.SexComReceivers, 0, false);
                        else
                        {
                            package.ResetRequest(new List<int>() { scr_System_CampaignManager.current.Player.RefID }, scr_System_CampaignManager.current.CurrentTargetRef > 0 ? new List<int>() { job.targetActorRef } : new List<int>() { }, 0);
                            /*
                            (package as ActionPackage_Sex).ReInitializeCOM(job, com, new List<int>() { scr_System_CampaignManager.current.Player.RefID },
                            scr_System_CampaignManager.current.CurrentTargetRef > 0 ? new List<int>() { job.targetActorRef } : new List<int>() { }, 0, false);
                            */
                        }
                    }
                    else if (package is ActionPackage_Interaction || package is ActionPackage_ProductionOrder)
                    {
                        var doers = new List<int>(cachedDoers);
                        var receivers = new List<int>(cachedReceivers);
                        List<int> targets = new List<int>();
                        var currentref = scr_System_CampaignManager.current.CurrentTargetRef;
                        if (currentref > 0 && !doers.Contains(currentref) && !receivers.Contains(currentref)) targets.Add(currentref);
                        targets.AddRange(scr_System_CampaignManager.current.PlayerPartyMembers);
                        targets = targets.Distinct().ToList();
                        targets.Remove(0);
                        targets.RemoveAll(x => doers.Contains(x) || receivers.Contains(x));

                        if (doers.Count < 1) doers.Add(0);
                        else if (!doers.Contains(0) && !receivers.Contains(0)) receivers.Add(0);
                        receivers.AddRange(targets);

                        package.ResetRequest(doers, receivers, 0);
                        // package.ReInitializeCOM(job, com, doers, receivers, 0, false);
                    }
                    else
                    {
                        Debug.LogError("COMMANAGER package typecheck failed");
                    }


                    tooltip += "doer[" + String.Join("|", package.DoerRefs) + "] receiver [" + String.Join("|", package.ReceiverRefs) + "]\n";
                    //text.SetText(package.DisplayName);

                    if (package.Validate())
                    {
                        returnVal = returnVal && true;
                        tooltip += "Time cost [" + package.Duration + "] minutes, Resources cost [" + package.ResourceCost + "]\n";
                        tooltip += package.GetSuccessRateString();
                    }
                    else
                    {
                        returnVal = false;
                        tooltip += "package did not pass internal validation\n";
                    }
                    package.tooltip.RemoveAll(x => x == "" || x.Length < 1);
                    tooltip += "\n" + String.Join("\n", package.tooltip);

                    if (job.targetActorRef != scr_System_CampaignManager.current.CurrentTargetRef) returnVal = false;
                }
                else
                {
                    returnVal = false;
                    tooltip += msg + "\n";
                }
            }
            else
            {
                returnVal = false;
                tooltip += "package did not pass external validation, validateByTag["+parent.ValidateCOMByTags(com)+"]\n";
                //this.text.gameObject.SetActive(false);
                display = display && false;
            }

            text.SetText(package.DisplayName);

            if (!COMRepeat)
            {
                // tooltip += "inside comrepeat loop\n";
                bool comRepeatGet = parent.COMRepeat_Get(this.comID, job.RefID, returnVal);

                //Debug.Log("COMREPEAT Get COM[" + comID + "] from job[" + job.RefID + "] with value [" + returnVal + "] and in list [" + parent.forbidCOMRepeatList[comID][job.RefID] + "] returnValue ["+ comRepeatGet + "]" + parent.forbidCOMRepeatListContent);
                
                if (comRepeatGet) display = display && true;

                //else if (!com.HideWhenInvalid) this.text.gameObject.SetActive(true);
                else display = display && false;

            }


            if (returnVal) display = display && true;
            else if (!(com.HideWhenInvalid || hidingOverride)) display = display && true;
            else if (com.comTags.Contains("player")) display = display && true;
            else if (com.comTags.Contains("initSex") || com.comTags.Contains("endSex")) display = display && true;
            else display = display && false;

            if (display) text.gameObject.SetActive(true);
            else text.gameObject.SetActive(false);

            return returnVal;
        }

        public override void Destroy()
        {
            this.text = null;
            this.parent = null;
        }

        public void OnClickButton()
        {
            //Debug.Log("Adding package to job [" + job.GetJobDescription(0) + "] with actors [" + String.Join(" ", package.actorRefs)+"], doers["+ String.Join(" ", package.DoerRefs)+"] receivers["+ String.Join(" ", package.ReceiverRefs) + "]");
            scr_System_CampaignManager.current.Player.ChangeCurrentJob(job);
            var ppp = package.Copy();
            job.AddPackage(new List<ActionPackage>() { ppp }, true); 
            scr_System_CampaignManager.current.FreeUpdate(-1, this.text.Text.text);
        }
    }

    public class ButtonValidator_ToggleXrayDebug : ButtonValidator, I_ButtonClickable
    {
        new scr_panel_COMmanager parent;
        scr_SelectableText text;
        public ButtonValidator_ToggleXrayDebug(scr_panel_COMmanager parent, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.text = text;

            text.toggleColor = text.baseColor;
            text.baseColor = text.disableColor;
        }

        public override bool IsButtonValid()
        {
            return false;
            if (parent.GetCOMFilter(COMFilter.Debug)) text.gameObject.SetActive(true);
            else text.gameObject.SetActive(false);

            return true;
        }

        public void OnClickButton()
        {
            if (scr_System_CampaignManager.current.debug_xray == true) scr_System_CampaignManager.current.debug_xray = false;
            else scr_System_CampaignManager.current.debug_xray = true;
        }
    }
    
    public List<int> SexComDoers;
    public List<int> SexComReceivers;

    public RectTransform prefab_Canvas_charaDetail;
    public class ButtonValidator_InspectChara : ButtonValidator, I_ButtonClickable
    {
        new scr_panel_COMmanager parent;
        scr_SelectableText text;
        public ButtonValidator_InspectChara(scr_Menu parent, scr_SelectableText text) : base(parent)
        {
            this.parent = parent as scr_panel_COMmanager;
            this.text = text;
        }

        public override bool IsButtonValid()
        {
            int charaRefID = scr_System_CampaignManager.current.CurrentTargetRef;
            //Debug.Log("isbuttonvalid " + charaRefID + " " + scr_System_CentralControl.current.CanHaveSex(0, charaRefID));
            if (charaRefID > -1) text.SetText("%%comManager_btn_inspect%%");

            return (charaRefID >= 0);
        }

        public void OnClickButton()
        {

            scr_Menu_CharaDetail detail = scr_System_SceneManager.current.LoadCanvasIntoScene(parent.prefab_Canvas_charaDetail, parent.m_Canvas.GetComponent<RectTransform>()).GetComponent<scr_Menu_CharaDetail>();
            detail.InitializeWithArgument(scr_System_CampaignManager.current.CurrentTargetRef);

        }
    }
    public class ButtonValidator_InitSexDebug : ButtonValidator, I_ButtonClickable
    {
        new scr_panel_COMmanager parent;
        scr_SelectableText text;
        public ButtonValidator_InitSexDebug(scr_panel_COMmanager parent, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.text = text;

            txt_on = scr_System_Serializer.current.Dictionary.QueryThenParse("com_special_initSex_start");
            txt_off = scr_System_Serializer.current.Dictionary.QueryThenParse("com_special_initSex_end");
            sleep_on = scr_System_Serializer.current.Dictionary.QueryThenParse("com_special_initSex_sleeping_start");
            sleep_off = scr_System_Serializer.current.Dictionary.QueryThenParse("com_special_initSex_sleeping_end");
            timestop_on = scr_System_Serializer.current.Dictionary.QueryThenParse("com_special_initSex_timestop_start");
            timestop_off = scr_System_Serializer.current.Dictionary.QueryThenParse("com_special_initSex_timestop_end");
        }

        string txt_tooltip, txt_on, txt_off, sleep_on, sleep_off, timestop_on, timestop_off;
        string endString;
        string endString_saved = "debugsexnotcalled";
        public override bool IsButtonValid()
        {
            if (!scr_System_CampaignManager.current.DebugMode)
            {
                text.gameObject.SetActive(false);
                return false;
            }

            if (parent.GetCOMFilter(COMFilter.Debug)) text.gameObject.SetActive(true);
            else text.gameObject.SetActive(false);

            
            int charaRefID = scr_System_CampaignManager.current.CurrentTargetRef;
            int jobRef = scr_System_CampaignManager.current.FindInstanceByID(0).CurrentJobRefID;
            Job job = scr_System_CampaignManager.current.FindJobInstanceByID(jobRef);

            // current job is not sex
            if ((job as Job_Sex_Group) == null)
            {
                if (scr_System_Time.current.TimeStop)
                {
                    endString = timestop_off;
                    this.text.SetText(timestop_on, false);
                }
                else if (scr_System_CampaignManager.current.CurrentTarget != null && ( scr_System_CampaignManager.current.CurrentTarget.Stats.isConsciousnessUnconscious ))
                {
                    this.text.SetText(sleep_on, false);
                    endString = sleep_off;
                }
                else
                {
                    this.text.SetText(txt_on);
                    endString = txt_off;
                }
                if (charaRefID > 0 && scr_System_CentralControl.current.CanHaveSex(0, charaRefID))
                {
                    return true;
                }
                else
                {
                    return false;
                }


                // current job is sex
            }else{
                this.text.SetText(endString_saved);
                return true;
            }
            //Debug.Log("isbuttonvalid " + charaRefID + " " + scr_System_CentralControl.current.CanHaveSex(0, charaRefID));
           }

        public void OnClickButton()
        {
            //Debug.Log("can have sex : [" + scr_System_CampaignManager.current.FindInstanceByID(0).FirstName + "] and [" + scr_System_CampaignManager.current.FindInstanceByID(scr_System_CampaignManager.current.CurrentTarget).FirstName + "]");

            var txt = this.text.Text.text;

            Job job = scr_System_CampaignManager.current.FindJobInstanceByID(scr_System_CampaignManager.current.FindInstanceByID(0).CurrentJobRefID);

            if ((job as Job_Sex_Group) == null)
            {
                endString_saved = endString;
                scr_System_CampaignManager.current.Register(new Job_Sex_Group(scr_System_CampaignManager.current.CharaInCurrentRoom, scr_System_CampaignManager.current.CurrentRoom));
            }
            else
            {

                parent.currentSexJob = null;
                (job as Job_Sex_Group).EndJob();
                //parent.RefreshTitle(scr_System_CampaignManager.current.CurrentTargetRef);
                //parent.UpdateJobCOM();
            }

            scr_System_CampaignManager.current.FreeUpdate(-1, txt);

            //scr_System_CampaignManager.current.UpdateScene();
            //scr_System_CampaignManager.current.FindInstanceByID(scr_System_CampaignManager.current.CurrentTarget).currentJobRefID = refe;
            //parent.UpdateJobCOM(refe);
        }
    }


    

    public class ButtonValidator_IngestItemDebug : ButtonValidator, I_ButtonClickable
    {
        string itemBaseID;
        string ingestTag;
        Item_Base baseItem;
        new scr_panel_COMmanager parent;
        scr_SelectableText text;
        public ButtonValidator_IngestItemDebug(scr_panel_COMmanager parent, scr_SelectableText text, string itemBaseID, string ingestTag) : base(parent)
        {
            this.itemBaseID = itemBaseID;
            this.ingestTag = ingestTag;
            baseItem = scr_System_Serializer.current.GetByNameOrID_Item_Base(itemBaseID);

            this.parent = parent;
            this.text = text;
        }

        public override bool IsButtonValid()
        {
            if (!parent.GetCOMFilter(COMFilter.Debug)) text.gameObject.SetActive(false);
            else text.gameObject.SetActive(true);

            if (scr_System_CampaignManager.current.CurrentTargetRef < 0) return false;
            //Debug.Log("ButtonValidator_IngestItemDebug looking for bodytag [" + ingestTag + "] baseItem isnull ["+(baseItem == null)+"] hastag ["+ scr_System_CampaignManager.current.FindInstanceByID(scr_System_CampaignManager.current.CurrentTarget).Body.HasBodyTag(new List<string>() { ingestTag }) + "]");
            this.tooltip = $"baseItem {(baseItem == null ? "null" : baseItem.DisplayName)}, requiredtag {ingestTag} targethastag? {scr_System_CampaignManager.current.CurrentTarget.Body.HasBodyTag(new List<string>() { ingestTag })}";
            return baseItem != null && scr_System_CampaignManager.current.CurrentTarget.Body.HasBodyTag(new List<string>() { ingestTag });
        }

        public void OnClickButton()
        {
            Character_Trainable chara = scr_System_CampaignManager.current.CurrentTarget;

            Debug.Log("Debug Feeding Ingestible Item [" + baseItem.DisplayName + "] to [" + chara.FirstName + "]");

            
            Item_Instance i = WorldManager.Instantiate(baseItem.id, baseItem.displayName);

            if(!chara.Body.ConsumeIngestible(i, ingestTag))
            {
                scr_System_CampaignManager.current.Unregister(i);
            }
        }
    }

    public class ButtonValidator_ChangeCOMFilter:ButtonValidator, I_ButtonClickable
    {
        COMFilter filter;
        new scr_panel_COMmanager parent;
        scr_SelectableText text;
        public ButtonValidator_ChangeCOMFilter(scr_panel_COMmanager parent, COMFilter filter, scr_SelectableText text) :base(parent)
        {
            this.filter = filter;
            this.parent = parent;
            this.text = text;

            text.isButtonToggle = true;
            text.useDisabledColorWhenUntoggled = true;
        }

        public override bool IsButtonValid()
        {
            if (parent.GetCOMFilter(filter)) text.Toggle(true, true);
            else text.Toggle(true, false);

            return true;
        }

        public void OnClickButton()
        {
            parent.ChangeCOMFilter(filter, !parent.GetCOMFilter(filter));
        }
    }

    public class ButtonValidator_ChangeDebugFilter : ButtonValidator, I_ButtonClickable
    {
        COMFilter filter;
        new scr_panel_COMmanager parent;
        scr_SelectableText text;

        public ButtonValidator_ChangeDebugFilter(scr_panel_COMmanager parent, COMFilter filter, scr_SelectableText text) : base(parent)
        {
            this.filter = filter;
            this.parent = parent;
            this.text = text;

            text.isButtonToggle = true;
            text.useDisabledColorWhenUntoggled = true;
        }

        public override bool IsButtonValid()
        {
            var currentVal = scr_System_CampaignManager.current.DebugMode;
            parent.ChangeCOMFilter(filter, currentVal);

            if (currentVal) text.Toggle(true, true);
            else text.Toggle(true, false);

            return true;
        }

        public void OnClickButton()
        {
            var resultValue = !parent.GetCOMFilter(filter);
            parent.ChangeCOMFilter(filter, resultValue);
            scr_System_CampaignManager.current.DebugMode = resultValue;
        }
    }

    public class ButtonValidator_ChangeDeterministicRollsFilter : ButtonValidator, I_ButtonClickable
    {
        COMFilter filter;
        new scr_panel_COMmanager parent;
        scr_SelectableText text;
        public ButtonValidator_ChangeDeterministicRollsFilter(scr_panel_COMmanager parent, COMFilter filter, scr_SelectableText text) : base(parent)
        {
            this.filter = filter;
            this.parent = parent;
            this.text = text;

            text.isButtonToggle = true;
            text.useDisabledColorWhenUntoggled = true;
        }

        public override bool IsButtonValid()
        {
            var currentVal = scr_System_CampaignManager.current.DeterministicRolls;
            parent.ChangeCOMFilter(filter, currentVal);
            if (currentVal) text.Toggle(true, true);
            else text.Toggle(true, false);
            return true;
        }

        public void OnClickButton()
        {
            var resultValue = !parent.GetCOMFilter(filter);
            parent.ChangeCOMFilter(filter, resultValue);
            scr_System_CampaignManager.current.DeterministicRolls = resultValue;
        }
    }

    public class ButtonValidator_PartyInvite : ButtonValidator, I_ButtonClickable
    {

        new scr_panel_COMmanager parent;
        scr_SelectableText text;

        public ButtonValidator_PartyInvite(scr_panel_COMmanager parent, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            invite = scr_System_Serializer.current.Dictionary.QueryThenParse("comManager_com_follow_request");
            invite_off = scr_System_Serializer.current.Dictionary.QueryThenParse("comManager_com_follow_stop");
            carry = scr_System_Serializer.current.Dictionary.QueryThenParse("comManager_com_followCarry_request");
            carry_off = scr_System_Serializer.current.Dictionary.QueryThenParse("comManager_com_followCarry_stop");
        }

        string invite, invite_off, carry, carry_off;

        public override bool IsButtonValid()
        {
            this.tooltip = "";
            int currentRef = scr_System_CampaignManager.current.CurrentTargetRef;
            var targ = scr_System_CampaignManager.current.CurrentTarget;
            if (currentRef > 0)
            {
                if (targ.isRestrained)
                {
                    this.tooltip = "target is being restrained";
                    return false;
                }
                else if (scr_System_CampaignManager.current.party.HasMember(currentRef))
                {
                    if (!targ.canAct) this.text.SetText(carry_off);
                    else this.text.SetText(invite_off);
                }
                else
                {
                    if (!targ.canAct) this.text.SetText(carry);
                    else this.text.SetText(invite);
                }
                return true;
            }
            return false;
        }

        public void OnClickButton()
        {
            int currentRef = scr_System_CampaignManager.current.CurrentTargetRef;
            if (currentRef > 0)
            {
                if (scr_System_CampaignManager.current.party.HasMember(currentRef))
                {
                    scr_System_CampaignManager.current.party.RemoveFromParty(currentRef);
                }
                else
                {
                    scr_System_CampaignManager.current.party.AddToParty(currentRef);
                }
            }
            scr_System_CampaignManager.current.NotifyUpdate();
        }
    }

    public class ButtonValidator_AddStatus : ButtonValidator, I_ButtonClickable
    {
        new scr_panel_COMmanager parent;
        scr_SelectableText text;
        string statusString;
        public ButtonValidator_AddStatus(scr_panel_COMmanager parent, scr_SelectableText text, string StatusID, string buttonText) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.statusString = StatusID;
            text.SetText(buttonText);
        }
        Character_Trainable chara;

        public override bool IsButtonValid()
        {
            if (scr_System_CampaignManager.current.CurrentTargetRef < 0) return false;
            chara = scr_System_CampaignManager.current.CurrentTarget;
            if (chara == null) return false;
            return !chara.Stats.HasStatusByStringMatch(statusString);
            // unique id = statusID + itemRefID
        }

        public void OnClickButton()
        {
            scr_System_CampaignManager.current.CurrentTarget.Stats.AddOrModStatus(statusString);
        }
    }

    
    
    public class ButtonValidator_ChangeCOMTab : ButtonValidator, I_ButtonClickable
    {
        COMTabs target;
        new scr_panel_COMmanager parent;
        scr_SelectableText text;
        RectTransform targetCOMs;
        List<scr_SelectableText> filters;
        RectTransform targetCOMs_extra;

        public ButtonValidator_ChangeCOMTab(scr_panel_COMmanager parent, scr_SelectableText text, COMTabs target, RectTransform targetCOMs, List<scr_SelectableText> filters, RectTransform targetCOMs_Extra = null) : base(parent)
        {
            this.target = target;
            this.parent = parent;
            this.text = text;
            this.targetCOMs = targetCOMs;
            this.filters = filters;
            this.targetCOMs_extra = targetCOMs_Extra;
            //text.toggleColor = text.baseColor;
            //text.baseColor = text.disableColor;
        }

        public override bool IsButtonValid()
        {
            if (parent.currentTab == target) text.Toggle(true, true);
            else text.Toggle(true, false);

            if (parent.currentTab != target)
            {
                
                targetCOMs.gameObject.SetActive(false);
                if (targetCOMs_extra != null) targetCOMs_extra.gameObject.SetActive(false);
            }
            else
            {
                targetCOMs.gameObject.SetActive(true);
                if (targetCOMs_extra != null) targetCOMs_extra.gameObject.SetActive(true);
            }

            foreach (scr_SelectableText s in filters)
            {
                if (!parent.currentTab_Filters.Contains(s)) s.gameObject.SetActive(false);
                else s.gameObject.SetActive(true);
            }

            switch (target)
            { 
                case COMTabs.Interaction:
                    if (scr_System_CampaignManager.current.CurrentTargetRef > 0) text.SetText("%%comManager_tab_interact%%");
                    else text.SetText("%%comManager_tab_act%%");
                    break;
                case COMTabs.Sex:
                    if (scr_System_CampaignManager.current.displaySex) text.SetText("%%comManager_tab_sex%%");
                    else text.SetText("%%comManager_tab_skinship%%");
                    break;
                //case COMTabs.Inventory:
                //    text.SetText("%%comManager_tab_inventory%%");
                //    break;
                default:
                    break;
            }

            if (target == COMTabs.Interaction && scr_System_CampaignManager.current.Player.CurrentJob is Job_Sex_Group) return false;
            return true;
        }

        public void OnClickButton()
        {
            parent.ChangeCurrentTab(target);
            //scr_System_CampaignManager.current.NotifyUpdate();
        }
    }

    public class ButtonValidator_DebugAddItemToRoom : ButtonValidator, I_ButtonClickable
    {
        new scr_panel_COMmanager parent;
        scr_SelectableText text;
        string itemID;
        Item_Base item;

        public ButtonValidator_DebugAddItemToRoom(scr_panel_COMmanager parent, scr_SelectableText text, string itemID):base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.itemID = itemID;
            item = scr_System_Serializer.current.GetByNameOrID_Item_Base(itemID);

        }

        public override bool IsButtonValid()
        {
            return (item != null);
        }

        public void OnClickButton()
        {
            scr_System_CampaignManager.current.CurrentRoom.AddItem(WorldManager.Instantiate(itemID));
            //scr_System_CampaignManager.current.NotifyUpdate();
        }
    }

    public class ButtonValidator_DebugAddCharaToParty : ButtonValidator, I_ButtonClickable
    {
        new scr_panel_COMmanager parent;
        scr_SelectableText text;
        string baseID;
        Item_Base item;
        Character_Trainable innerChara;
        public ButtonValidator_DebugAddCharaToParty(scr_panel_COMmanager parent, scr_SelectableText text, string charaBaseID) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.baseID = charaBaseID;
            innerChara = scr_System_Serializer.current.index_Characters_Bases.baseCharacters.Find(x=>x.BaseID == baseID);
        }

        public override bool IsButtonValid()
        {
            this.tooltip = "";
#if UNITY_EDITOR
            if (innerChara != null) this.text.SetText("AddChara " + innerChara.FirstName);
            else this.text.SetText("AddChara missingData");

            if (scr_System_CampaignManager.current.HasInstanceCharaWithBaseID(baseID) != null)
            {
                this.tooltip = "Chara already exist";
                return false;
            }
            if (scr_System_CampaignManager.current.ColdLoad)
            {
                this.tooltip = "still loading";
                return false;
            }
            if (scr_System_CampaignManager.current.Player.FactionManager.CurrentlyActiveFaction == null)
            {
                return false;
            }
            if (!scr_System_CampaignManager.current.Player.FactionManager.CurrentlyActiveFaction.ManagedRefs.Contains(0))
            {
                this.tooltip = "can only be used when player is in a player-managed faction";
                return false;
            }
            return true;
#else
            return false;
#endif
        }

        public void OnClickButton()
        {
            
            var c = scr_System_CampaignManager.current.InstantiateCharacter_FromBaseID(baseID, scr_System_CampaignManager.current.CurrentRoom);
            scr_System_CampaignManager.current.party.AddToParty(c);

            var addTofaction = scr_System_CampaignManager.current.Player.FactionManager.CurrentlyActiveFaction;

            c.FactionManager.SetTempHomeFaction(addTofaction.ID);

            //scr_System_CampaignManager.current.CurrentRoom.AddItem(WorldManager.Instantiate(itemID));
            scr_System_CampaignManager.current.NotifyUpdate();
        }
    }
    public class ButtonValidator_DebugTimeStop : ButtonValidator, I_ButtonClickable
    {

        scr_SelectableText text;

        public ButtonValidator_DebugTimeStop(scr_Menu parent, scr_SelectableText text) : base(parent)
        {
            this.text = text;
            on = scr_System_Serializer.current.Dictionary.QueryThenParse("com_special_timestop_begin");
            off = scr_System_Serializer.current.Dictionary.QueryThenParse("com_special_timestop_end");
        }

        string on, off;

        public override bool IsButtonValid()
        {
            if (scr_System_Time.current.TimeStop)
            {
                text.SetText(off);
                text.Toggle(true, true);
            }
            else
            {
                text.SetText(on);
                text.Toggle(true, false);
            }
            return true;
        }

        public void OnClickButton()
        {
            scr_System_CampaignManager.current.ToggleTimeStop();
            //scr_System_CampaignManager.current.NotifyUpdate();
        }
    }

    public class ButtonValidator_UndressAll : ButtonValidator, I_ButtonClickable
    {
        new scr_panel_COMmanager parent;
        scr_SelectableText text;
        int revealingScoreFilter;
        bool isPlayer;
        public ButtonValidator_UndressAll(scr_panel_COMmanager parent, scr_SelectableText text, bool isPlayer, int revealingScoreFilter) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.revealingScoreFilter = revealingScoreFilter;
            this.isPlayer = isPlayer;
        }
        Character_Trainable chara;

        public override bool IsButtonValid()
        {
            if (!text.gameObject.activeInHierarchy) return false;

            //if (scr_System_CampaignManager.current.CurrentTargetRef < 0) return false;
            chara = ( isPlayer ? scr_System_CampaignManager.current.Player : scr_System_CampaignManager.current.CurrentTarget );
            if (chara == null) return false;

            if (chara.Body.HasEquipByFilter(BodyEquipLayer.None + 1, revealingScoreFilter)) return true;
            return false;
        }

        public void OnClickButton()
        {
            chara.Undress(BodyEquipLayer.None, (Revealing)revealingScoreFilter);
            //Debug.Log("Chara [" + chara.FirstName + "] undress inventory [" + String.Join(" ", chara.inventory_ref) + "]");

            scr_System_CampaignManager.current.NotifyUpdate();
        }
    }

    public class ButtonValidator_UndressLayers : ButtonValidator, I_ButtonClickable
    {
        new scr_panel_COMmanager parent;
        scr_SelectableText text;
        BodyEquipLayer layer;
        int revealingScoreFilter;
        bool isPlayer;
        public ButtonValidator_UndressLayers(scr_panel_COMmanager parent, scr_SelectableText text, bool isPlayer, int revealingScoreFilter) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.revealingScoreFilter = revealingScoreFilter;

            this.isPlayer = isPlayer;
        }
        Character_Trainable chara;

        public override bool IsButtonValid()
        {
            if (!text.gameObject.activeInHierarchy) return false;

            chara = (isPlayer ? scr_System_CampaignManager.current.Player : scr_System_CampaignManager.current.CurrentTarget);
            if (chara == null) return false;

            for (int i = (int)BodyEquipLayer.Outer; i > (int)BodyEquipLayer.None; i--)
            {
                layer = (BodyEquipLayer)i;

                if (chara.Body.HasEquipByFilter(BodyEquipLayer.Outer) || chara.Body.HasEquipByFilter(layer, revealingScoreFilter) )
                {
                    this.text.SetText("%%comManager_com_undress_" + layer.ToString() + "%%");
                    return true;
                }
            }

            this.text.SetText("%%comManager_com_undress_" + layer.ToString() + "%%");
            return false;
        }

        public void OnClickButton()
        {
            chara.Undress(layer, (Revealing)revealingScoreFilter);
            //Debug.Log("Chara [" + chara.FirstName + "] undress inventory [" + String.Join(" ", chara.inventory_ref) + "]");

            scr_System_CampaignManager.current.NotifyUpdate();
        }
    }

    public class ButtonValidator_RedressLayers : ButtonValidator, I_ButtonClickable
    {
        new scr_panel_COMmanager parent;
        scr_SelectableText text;
        BodyEquipLayer layer;
        int revealingScoreFilter;
        bool isPlayer;
        public ButtonValidator_RedressLayers(scr_panel_COMmanager parent, scr_SelectableText text, bool isPlayer, int revealingScoreFilter) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.revealingScoreFilter = revealingScoreFilter;

            this.isPlayer = isPlayer;
        }
        Character_Trainable chara;

        public override bool IsButtonValid()
        {
            if (!text.gameObject.activeInHierarchy) return false;

            chara = (isPlayer ? scr_System_CampaignManager.current.Player : scr_System_CampaignManager.current.CurrentTarget);
            if (chara == null) return false;

            if (chara.CurrentJob is Job_Sex_Group)
            {
                this.tooltip = "cannot redress during sex";
                return false;
            }

            for (int ii = (int)BodyEquipLayer.Skin; ii <= (int)BodyEquipLayer.Outer; ii++)
            {
                layer = (BodyEquipLayer)ii;

                if (chara.Inventory.Contents.Count < revealingScoreFilter) continue;

                var list = chara.Inventory.Contents;
                foreach (var i in list)
                {
                    ItemComponent_Equippable comp = i.GetComp("ItemComponent_Equippable") as ItemComponent_Equippable;
                    if (comp != null && comp.equipLayer == layer)
                    {
                        this.text.SetText("%%comManager_com_redress_" + layer.ToString() + "%%");
                        return true;
                    }
                }
            }

            this.text.SetText("%%comManager_com_redress_" + layer.ToString() + "%%");
            return false;
        }

        public void OnClickButton()
        {
            chara.Redress(layer);
            //Debug.Log("Chara [" + chara.FirstName + "] redress inventory [" + String.Join(" ", chara.inventory_ref) + "]");

            scr_System_CampaignManager.current.NotifyUpdate();
        }
    }

    public class ButtonValidator_FixClothes : ButtonValidator, I_ButtonClickable
    {
        new scr_panel_COMmanager parent;
        scr_SelectableText text;
        bool isPlayer;
        public ButtonValidator_FixClothes(scr_panel_COMmanager parent, scr_SelectableText text, bool isPlayer) : base(parent)
        {
            this.parent = parent;
            this.text = text;

            this.isPlayer = isPlayer;
        }
        Character_Trainable chara;

        public override bool IsButtonValid()
        {
            if (!text.gameObject.activeInHierarchy) return false;

            chara = (isPlayer ? scr_System_CampaignManager.current.Player : scr_System_CampaignManager.current.CurrentTarget);
            if (chara == null) return false;


            if (chara.CurrentJob is Job_Sex_Group)
            {
                this.tooltip = "cannot redress during sex";
                return false;
            }

            if (chara.Inventory.Contents.Count < 1)
            {
                this.tooltip += "all clothes are dressed";
                return false;
            }

            return true;
        }

        public void OnClickButton()
        {

            chara.Redress();
            //Debug.Log("Chara [" + chara.FirstName + "] redress inventory [" + String.Join(" ", chara.inventory_ref) + "]");

            scr_System_CampaignManager.current.NotifyUpdate();
        }
    }

    public class ButtonValidator_equipSingle : ButtonValidator, I_ButtonClickable
    {

        int charaRef = -1;
        int equipRef = -1;
        new scr_panel_COMmanager parent;
        scr_SelectableText text;
        Character_Trainable chara;
        Item_Instance item = null;
        ItemComponent_Equippable eq = null;
        public ButtonValidator_equipSingle(scr_Menu parent, int charaRef, int equipRef, scr_SelectableText text) : base(parent)
        {
            this.parent = parent as scr_panel_COMmanager;
            this.charaRef = charaRef;
            this.chara = scr_System_CampaignManager.current.FindInstanceByID(charaRef);
            this.equipRef = equipRef;
            this.text = text;
            item = scr_System_CampaignManager.current.FindItemInstanceByID(equipRef);
            if (item != null) eq = item.GetComp_Equippable();

            text.isButtonToggle = true;
            text.useDisabledColorWhenUntoggled = true;
        }
        bool isEquipped, isVisible, allowDuringSex;
        public override bool IsButtonValid()
        {

            //if (!text.gameObject.activeInHierarchy) return false;
            if (eq == null || item == null) return false;
            //Debug.LogError("Chara " + chara.FullName + " inventory " + chara.inventory_ref.ToArray().ToString());
            //Debug.LogError("EquipRef " + equipRef +" displayName "+item.DisplayName);

            text.SetText(item.DisplayName);

            var equippedPart = chara.GetPartByEquipRef(equipRef);

            isEquipped = !(chara.Inventory.Contains(item) || equippedPart == null);
            isVisible = (!isEquipped || equippedPart.GetRevealingScore(eq.equipLayer) <= 1);
            allowDuringSex = !(chara.CurrentJob is Job_Sex_Group) || (int)eq.revealing <= 0;

            bool returnVal = true;
            // always allow unequip
            if (isEquipped) text.Toggle(true, true);
            else
            {   // only allow re-equip during sex if satisfy condition
                if (allowDuringSex) text.Toggle(true, false);
                else
                {
                    tooltip = "cannot be redressed during sex";
                    returnVal = false;
                    text.Toggle(true, false);
                }
            }

            if (!isVisible) text.gameObject.SetActive(false);
            else text.gameObject.SetActive(true);

            return returnVal;
        }




        public void OnClickButton()
        {
            if (chara.Inventory.Contains(item)) chara.Reequip(item);
            else chara.UnequipItem(equipRef, -1, true, true);
            scr_System_CampaignManager.current.NotifyUpdate();
        }
    }


    public class ButtonValidator_ModCharaPersonality : ButtonValidator, I_ButtonClickable
    {

        string targetStat;
        new scr_panel_COMmanager parent;
        int modValue;
        public ButtonValidator_ModCharaPersonality(scr_panel_COMmanager parent, string key, int value) : base(parent)
        {
            this.parent = parent;
            this.targetStat = key;
            this.modValue = value;
        }

        public override bool IsButtonValid()
        {
            return scr_System_CampaignManager.current.CurrentTargetRef > 0;
        }

        public void OnClickButton()
        {
            if (targetStat == "pride") scr_System_CampaignManager.current.CurrentTarget.Relationships._Pride += modValue;
            else if (targetStat == "corruption") scr_System_CampaignManager.current.CurrentTarget.Relationships._Corruption += modValue;
            else if (targetStat == "mood") scr_System_CampaignManager.current.CurrentTarget.Stats.Mood.DebugSeverityMod += modValue;
            else if (targetStat == "stress") scr_System_CampaignManager.current.CurrentTarget.Stats.Stress.DebugSeverityMod += modValue;
            else if (targetStat == "lust") scr_System_CampaignManager.current.CurrentTarget.Stats.Lust.DebugSeverityMod += modValue;
            else if (targetStat == "trust") scr_System_CampaignManager.current.CurrentTarget.Relationships.FindRelationshipWith(0).ModRelationValue(RelationshipScoreType.Trust, modValue);
            else if (targetStat == "fear") scr_System_CampaignManager.current.CurrentTarget.Relationships.FindRelationshipWith(0).ModRelationValue(RelationshipScoreType.Fear, modValue);
            else if (targetStat == "goodwill") scr_System_CampaignManager.current.CurrentTarget.Relationships.FindRelationshipWith(0).ModRelationValue(RelationshipScoreType.Goodwill, modValue);
            else if (targetStat == "badwill") scr_System_CampaignManager.current.CurrentTarget.Relationships.FindRelationshipWith(0).ModRelationValue(RelationshipScoreType.Badwill, modValue);
            else if (targetStat == "desire") scr_System_CampaignManager.current.CurrentTarget.Relationships.FindRelationshipWith(0).ModRelationValue(RelationshipScoreType.Desire, modValue);
            else { }

            scr_System_CampaignManager.current.NotifyUpdate();
        }
    }
}

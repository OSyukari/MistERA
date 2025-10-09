using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using System;


public class scr_Canvas_Management : scr_Menu, IPointerClickHandler
{

    public List<Manageable> factions = new List<Manageable>();
    protected Manageable currentFaction = null;
    public Manageable CurrentFaction { get { return currentFaction; } set { currentFaction = value; } }
    public scr_HoverableText factionName;

    public TMP_Text production_results;
    public RectTransform inventoryList;
    public TMP_Text chara_warnings;
    public RectTransform list_factionWork, list_assignCOM, list_CharaNeeds;

    public initScript_ManagementOverview overviewScript;

    public void InitializeWithArgument(Manageable targetFaction = null)
    {
        if (!initialized) Initialize();

        factions.Clear();
        if (targetFaction != null)
        {
            factions.Add(targetFaction);
        }

        var ms = scr_System_CampaignManager.current.Player.FactionManager.ManagerFactions;
        foreach (var m in ms)
        {
            if (m != null && !factions.Contains(m)) factions.Add(m);
        }


        foreach (var i in factions)
        {
            if (i != null)
            {
                LoadFactionData(i);
                break;
            }
        }
    }

    protected override void Awake()
    {
        base.Awake();
        homef = LocalizeDictionary.QueryThenParse("management_faction_home_nameplate");
        workf = LocalizeDictionary.QueryThenParse("management_faction_work_nameplate");
        otherf = LocalizeDictionary.QueryThenParse("management_faction_others_nameplate");
        charaLocAP = LocalizeDictionary.QueryThenParse("ui_management_jobs_currentInfo");
        this.m_Canvas.overrideSorting = true;
        factions = new List<Manageable>();
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);
    }

    string homef, workf, otherf;
    string charaLocAP;

    public void LoadFactionData(Manageable m)
    {
        if (CurrentFaction != null && CurrentFaction == m) return;
        if (m == null) return;

        CurrentFaction = m;

        if (m is Manageable_HomeFaction) factionName.SetText(homef.Replace("$name$", m.FactionDisplayName), false, "management_faction_home_tooltip");
        else if (m is Manageable_WorkFaction) factionName.SetText(workf.Replace("$name$", m.FactionDisplayName), false, "management_faction_work_tooltip");
        else factionName.SetText(otherf.Replace("$name$", m.FactionDisplayName));


        currentTab = Tab_Overview;
        initialized_faction_overview = false;
        initialized_faction_productions = false;
        initialized_faction_charaList = false;

        foreach (var temp in m.printDebugInfo_RoomOwners())
        {
            writeLine(temp, list_PrivateRooms);
        }

        Tab_Overview.gameObject.SetActive(true);
        Initialize_FactionOverview();

        ValidateAll();
    }





    public RectTransform currentTab;


    /////// OVERVIEW TAB
    public RectTransform Tab_Overview;
    private bool initialized_faction_overview = false;
    private void Initialize_FactionOverview()
    {
        if (initialized_faction_overview) return;
        else initialized_faction_overview = true;

        overviewScript.Initialize(CurrentFaction);

    }


    /////// PRODUCTION TAB
    public RectTransform Tab_Production;
    private bool initialized_faction_productions = false;

    public scr_prOrderManage prefab_POEntry;
    public scr_prefabTransactionManage prefab_TAEntry;
    public RectTransform list_orders, list_trades;

    private void Initialize_FactionProduction()
    {
        if (initialized_faction_productions) return;
        else initialized_faction_productions = true;

       // if (loadedOrders == null) loadedOrders = new List<Manageable.ProductionOrder>();

        RefreshPOList();
        RefreshTAList();
    }

    private void CalculateProductionWarning()
    {
        //production_results.text = currentFaction.printDebugInfo_Orders();
        currentFaction.RefreshProductionAlertMSG();

        if(inventoryList.gameObject.activeInHierarchy)
        {
            Utility.DestroyAllChildrenFrom( inventoryList);
            foreach (var entry in CurrentFaction.Inventory.ContentsPrintable)
            {
                var text = Instantiate(prefab_text_link).GetComponent<scr_HoverableText>();
                text.SetText(entry.Print());
                text.SetExternalTooltip(entry.Tooltip);
                text.transform.SetParent(inventoryList.transform, false);
            }
        }
        //CurrentFaction.Inventory.PrintContent(ref inventoryList, prefab_text_link);
        //inventoryListing.text = CurrentFaction.Inventory.PrintContent(true, true);
    }

   // List<Manageable.ProductionOrder> loadedOrders = null;
    Dictionary<Manageable.ProductionOrder, button_ManageProductionOrder_RemoveCount> loadOrders_Removal = new Dictionary<Manageable.ProductionOrder, button_ManageProductionOrder_RemoveCount>();
    public void RefreshPOList()
    {
        //Debug.LogError("REFRESHING PO LIST!");
        foreach (var order in currentFaction.ProductionOrders) if (!loadOrders_Removal.ContainsKey(order)) MakePOButton(order);

        //foreach(var order in loadedOrders_daily) if (!currentFaction.ProductionOrdersDaily.Contains(order)) DeletePOButton(order)
    }

    Dictionary<Manageable.TradeOrder, button_ManageTradeOrder_RemoveCount> loadTrades_Removal = new Dictionary<Manageable.TradeOrder, button_ManageTradeOrder_RemoveCount>();
    public void RefreshTAList()
    {
        foreach (var trade in currentFaction.TradeOrders) if (!loadTrades_Removal.ContainsKey(trade)) MakeTOButton(trade);
    }

    private void MakeTOButton(Manageable.TradeOrder order)
    {
        //TODO
        int recipeHash = AssertUniqueHash(order.GetHashCode()) * 4;
        scr_prefabTransactionManage entry = Instantiate(prefab_TAEntry);
        entry.ItemName.SetText(order.Display);
        entry.ItemName.SetExternalTooltip(order.Tooltip);
        entry.ItemCount.text = currentFaction.Inventory.GetItemCount(order.Entry.itemID).ToString();
        entry.FactionName.text = order.TargetFaction == currentFaction ? " - " : order.TargetFaction.FactionDisplayName;
        entry.pricing.text = " - ";
        RectTransform rect = entry.GetComponent<RectTransform>();

        RegisterButton(recipeHash + 1, entry.ButtonPlus, new button_ManageTradeOrder_AddCount(this, entry.OrderAmount, order));
        RegisterButton(recipeHash + 2, entry.Button_orderType, new button_ManageTradeOrder_ChangeType(this, entry.Button_orderType, order));
        RegisterButton(recipeHash + 3, entry.ButtonMinus, new button_ManageTradeOrder_ReduceCount(this, entry.OrderAmount, order));
        var remover = new button_ManageTradeOrder_RemoveCount(this, recipeHash, order, entry.warningMsg, entry);
        RegisterButton(recipeHash, entry.Btn_action, remover);

        rect.SetParent(list_trades, false);
        loadTrades_Removal.Add(order, remover);
    }

    private void MakePOButton(Manageable.ProductionOrder order)
    {
        int recipeHash = AssertUniqueHash(order.GetHashCode()) * 4;
        //if (loadOrders_Hash.ContainsKey(order)) return;

        scr_prOrderManage entry = Instantiate(prefab_POEntry);
        entry.itemName.SetText(order.Recipe.DisplayName);
        entry.itemName.SetExternalTooltip(order.Recipe.Tooltip);
        entry.itemCount.text = currentFaction.Inventory.GetItemCount(order.Recipe.outputItemBaseID).ToString();
        entry.orderAmount.text = order.CountABS.ToString();
        RectTransform rect = entry.GetComponent<RectTransform>();

        //entry.expectedWorkLoad.text = (order.Recipe.workAmount).ToString();

        RegisterButton(recipeHash + 1, entry.buttonPlus, new button_ManageProductionOrder_AddCount(this, entry.orderAmount, order));
        RegisterButton(recipeHash + 2, entry.button_orderType, new button_ManageProductionOrder_ChangeType(this, entry.button_orderType, order));
        RegisterButton(recipeHash + 3, entry.buttonMinus, new button_ManageProductionOrder_ReduceCount(this, entry.orderAmount, order));
        // the following validator also responsible for displaying warning message
        var remover = new button_ManageProductionOrder_RemoveCount(this, recipeHash, order, entry.warningMsg, entry);
        RegisterButton(recipeHash, entry.btn_action, remover);    

        rect.SetParent(list_orders, false);
        loadOrders_Removal.Add(order, remover);
    }

    /// <summary>
    /// THIS IS NOT BEING USED AT ALL RIGHT ???
    /// </summary>
    /// <param name="order"></param>
    public void DestroyPOMButton(Manageable.ProductionOrder po, int recipeHash)
    {
        //// ??????
        DestroyCOMButton(recipeHash);
        DestroyCOMButton(recipeHash + 1);
        DestroyCOMButton(recipeHash + 2);
        DestroyCOMButton(recipeHash + 3);
        loadOrders_Removal.Remove(po);
    }

    public void DestroyTOMButton(Manageable.TradeOrder to, int recipeHash)
    {
        //// ??????
        DestroyCOMButton(recipeHash);
        DestroyCOMButton(recipeHash + 1);
        DestroyCOMButton(recipeHash + 2);
        DestroyCOMButton(recipeHash + 3);
        loadTrades_Removal.Remove(to);
    }

    private void DestroyCOMButton(int optionID)
    {
        scr_SelectableText text = buttonsByID[optionID];
        buttonsByID.Remove(optionID);
        ButtonValidator validator = validatorsByID[optionID];
        validatorsByID.Remove(optionID);

        validator.Destroy();
        text.gameObject.SetActive(false);
        Destroy(text.gameObject);
    }

    private bool RegisterButton(int optionID, scr_SelectableText button, ButtonValidator validator)
    {
        if (!buttonsByID.ContainsKey(optionID))
        {
            button.Initialize(this, validator);
            button.optionID = optionID;
            buttonsByID.Add(button.optionID, button);
            validatorsByID.Add(button.optionID, button.Validator);
            button.Validate();
            return true;
        }
        else return false;
    }

    /////// EXPS TAB
    public RectTransform Tab_Expeditions;
    bool initialized_faction_expsList = false;
    public initScript_Expeditions Script_Expeditions;
    private void Initialize_FactionExpsList()
    {
        if (initialized_faction_expsList) return;
        else initialized_faction_expsList = true;

        Script_Expeditions.Initialize(this, currentFaction);

    }



    /////// CHARA TAB

    public RectTransform Tab_Jobs;
    public TMP_Text chara_fullname, charaGender, charaGenderSeparator;
    public scr_HoverableText chara_Race, chara_RaceTemplate;
    public scr_SelectableText chara_HomeFaction, chara_TempHomeFaction;
    public TMP_Text chara_location_ap;
    public RectTransform chara_schedulebox; 
    public Image chara_scheduleBoxBG;

    public List<RectTransform> chara_scheduleCOMboxes;
    public RectTransform list_chara, list_prisoner;
    public scr_SelectableText prefab_charaNameButton;

    private List<Character_Trainable> charaInFaction;
    public Character_Trainable currentChara;

    public delegate void Initializer();

    private bool initialized_faction_charaList = false;
    private void Initialize_FactionCharaList()
    {
        if (initialized_faction_charaList) return;
        else initialized_faction_charaList = true;

        UnloadButton(tempCharaRefIDStorage);
        Utility.DestroyAllChildrenFrom(list_chara);
        Utility.DestroyAllChildrenFrom(list_prisoner);
        tempCharaRefIDStorage.Clear();

        if (currentFaction == null) return;

        charaInFaction = currentFaction.ManagedChara;
        if (charaInFaction == null || charaInFaction.Count < 1) return;

        SetCurrentChara(scr_System_CampaignManager.current.Player);

        
        foreach (Character_Trainable chara in currentFaction.ManagedChara_Members)
        {
            MakeCharaButton(list_chara, prefab_charaNameButton, chara);
        }
        foreach (Character_Trainable chara in currentFaction.ManagedChara_Prisoners)
        {
            MakeCharaButton(list_prisoner, prefab_charaNameButton, chara);
        }

    }
    List<int> tempCharaRefIDStorage = new List<int>();

    private void MakeCharaButton(RectTransform parent, scr_SelectableText prefab, Character_Trainable chara)
    {

        scr_SelectableText comp = Instantiate(prefab);
        RectTransform r = comp.GetComponent<RectTransform>();
        r.SetParent(parent, false);


        comp.Initialize(this, new ButtonValidator_charaSelect(this, comp, chara));
        comp.SetText(chara.FirstName);

        comp.optionID = AssertUniqueHash( chara.GetHashCode());

        buttonsByID.Add(comp.optionID, comp);
        validatorsByID.Add(comp.optionID, comp.Validator);

        tempCharaRefIDStorage.Add(comp.optionID);

        comp.Validate();
    }

    public void SetCurrentChara(int charaRef)
    {
        Character_Trainable c = charaInFaction.Find(x => x.RefID == charaRef);
        if (c != null) SetCurrentChara(c);
    }

    COM currentHighlightJobCOM = null;
    public List<int> _currentHighlightHours = new List<int>();
    public List<int> CurrentHighlightHours { get
        {
            return _currentHighlightHours;
        } 
        set
        {
            _currentHighlightHours = value;
            ValidateAll();
        }
    }


    public COM CurrentHighlightJOBCOM { get { return currentHighlightJobCOM; } }



    public void SetCurrentChara(Character_Trainable c)
    {
        // destroy previous
        Utility.DestroyAllChildrenFrom( list_factionWork);
        Utility.DestroyAllChildrenFrom( list_CharaNeeds);

        bool safe = scr_System_CentralControl.current.isSafeMode;

        // set current
        currentChara = c;

        chara_fullname.text = c.FullName;
        if (safe)
        {
            charaGenderSeparator.gameObject.SetActive(false);
            charaGender.gameObject.SetActive(false);
        }
        else
        {
            charaGenderSeparator.gameObject.SetActive(true);
            charaGender.gameObject.SetActive(true);
            charaGender.SetText(LocalizeDictionary.QueryThenParse(currentChara.Appearance.ToString()));

        }
        chara_Race.SetText(currentChara.Race.DisplayName, false, currentChara.Race.ID + "_tooltip");
        chara_RaceTemplate.SetText(currentChara.RaceTemplate.DisplayName, false, currentChara.RaceTemplate.ID + "_tooltip");

        chara_location_ap.SetText(charaLocAP.Replace("$location$", scr_System_CampaignManager.current.Map.FindRoomByChara(currentChara.RefID).DisplayName).Replace("$jobdescription$", currentChara.GetJobDescription()));

        chara_HomeFaction.SetText(currentChara.FactionManager.Faction_Home == null ? " - " : currentChara.FactionManager.Faction_Home.FactionDisplayName);
        chara_TempHomeFaction.SetText(currentChara.FactionManager.Faction_Home_Temporary == null ? " - " : currentChara.FactionManager.Faction_Home_Temporary.FactionDisplayName);



        // int currentHour = scr_System_Time.current.getCurrentTime().Hour;

        foreach (Manageable faction in c.FactionManager.WorkFactions)
        {
            var newLine = Instantiate(prefab_text_linkbutton);
            var text = newLine.GetComponent<scr_SelectableText>();
            text.SetText(faction.FactionDisplayName);
            if (faction == currentFaction) text.Text.color = text.baseColor;
            else text.Text.color = text.disableColor;// (true,true);
            newLine.SetParent(list_factionWork, false);
        }

        if (c.hasSleepNeed)
        {
            var newLine = Instantiate(prefab_text_linkbutton);
            var text = newLine.GetComponent<scr_SelectableText>();
            text.showBrackets = false;
            text.forbidNotify = true;
            text.SetText("sleep");
            newLine.SetParent(list_CharaNeeds, false);
        }

        foreach(var need in c.Stats.Needs)
        {
            var newLine = Instantiate(prefab_text_linkbutton);
            var text = newLine.GetComponent<scr_SelectableText>();
            text.showBrackets = false;
            text.forbidNotify = true;
            text.SetText(need.DisplayName);
            newLine.SetParent(list_CharaNeeds, false);
        }
    }
    protected void RefreshCurrentChara()
    {
        if (!chara_schedulebox.gameObject.activeInHierarchy) return;
        List<string> warnings = new List<string>();
        currentChara.FactionManager.ValidateSchedule(ref warnings);

        Manageable.Job_Schedule jbsch = currentFaction.GetSchedule(currentChara);
        if (chara_schedulebox.transform.childCount >= 24 && jbsch != null)
        {

            for (int i = 0; i < 24; i++)
            {
                scr_ScheduleBox box = chara_schedulebox.transform.GetChild(i).GetComponent<scr_ScheduleBox>();
                if (box != null)
                {
                    box.Refresh();
                }
            }
        }

        chara_warnings.text = String.Join("\n", warnings);
    }

    public RectTransform list_PrivateRooms;

    private void writeLine(string s, RectTransform parent)
    {
        RectTransform targetRect = Instantiate(prefab_text_standard);
        targetRect.GetComponent<TMP_Text>().text = s;

        targetRect.SetParent(parent, false);
    }

    public scr_menu_AddProductionOrder canvas_AddPO;
    public scr_Menu_AddTrade canvas_AddTR;

    public override void Initialize()
    {
        base.Initialize();

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            // Debug.Log("Button " + button + " " + button.optionID);
            switch (button.optionID)
            {
                case 10: // faction left
                    button.Initialize(this, new button_FactionSwitch(this, true)); break;
                case 11: // faction right
                    button.Initialize(this, new button_FactionSwitch(this, false)); break;
                case 1: // overview tab
                    button.Initialize(this, new button_ChangeTab(this, button, Tab_Overview, Initialize_FactionOverview)); break;
                case 2: // productions tab
                    button.Initialize(this, new button_ChangeTab(this, button, Tab_Production, Initialize_FactionProduction)); break;
                case 3: // jobs tab
                    button.Initialize(this, new button_ChangeTab(this, button, Tab_Jobs, Initialize_FactionCharaList)); break;
                case 4: // expeditions tab
                    button.Initialize(this, new button_ChangeTab(this, button, Tab_Expeditions, Initialize_FactionExpsList)); break;
                case 12:
                    button.Initialize(this, new button_modifyLinkFaction(this)); break;
                case 20:    // add production order
                    button.Initialize(this, new Button_LoadCanvas_AddPO(this)); break;
                case 21:    // add trade order
                    button.Initialize(this, new Button_LoadCanvas_AddTR(this)); break;
                case 31: // chara detail tab
                    button.Initialize(this, new button_CharaDetail(this)); break;
                case 32: // chara edit schedule
                    button.Initialize(this, new button_EditSchedule(this, button, chara_schedulebox, chara_scheduleBoxBG, chara_scheduleCOMboxes)); break;
                case 40:  // add party 
                    button.Initialize(this, new ButtonValidator_partyCreate(this, button)); break;
                case 41:  // edit party members 
                    button.Initialize(this, new ButtonValidator_partyEditMembers(this, button)); break;
                case 42:  // edit party inventory 
                    button.Initialize(this, new ButtonValidator_AlwaysFalse(this)); break;
                case 43:
                    button.Initialize(this, new ButtonValidator_partyEditExpeditions(this, button)); break;
                case 44:
                    button.Initialize(this, new initScript_Expeditions.ButtonValidator_StartExp(this, button)); break;
                case 45:
                    button.Initialize(this, new initScript_Expeditions.ButtonValidator_AllowPassNightToggle(this, button, Script_Expeditions.ExpeditionConfig.Duration)); break;
                case 46:
                    button.Initialize(this, new initScript_Expeditions.ButtonValidator_RecurringToggle(this, button, Script_Expeditions.ExpeditionConfig.CooldownTime, Script_Expeditions.OnEndEdit_Recurring)); break;
                case 47:
                    button.Initialize(this, new initScript_Expeditions.ButtonValidator_EditCamp(this, button)); break;
                case 9999: // exit
                    button.Initialize(this, button_alwaysValid); break;
                default:
                    button.Initialize(this, button_alwaysValid); break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }
        // build all presetLis

    }

    public void UnloadButton(List<int> buttons)
    {
        foreach(var i in buttons)
        {
            this.buttonsByID.Remove(i);
            this.validatorsByID.Remove(i);
        }
    }

    public void MakeButton_PartyMembers(Character_Trainable c, scr_SelectableText button)
    {
        button.Initialize(this, new ButtonValidator_partyMemberSelect(this, button, c));
        button.SetText(c.FirstName);
        button.optionID = AssertUniqueHash(c.GetHashCode());

        buttonsByID.Add(button.optionID, button);
        validatorsByID.Add(button.optionID, button.Validator);

        button.Validate();
    }
    public void MakeButton_PartyMemberComp(Character_Trainable c, scr_SelectableText button, Manageable_Party.PartyComposition comp)
    {
        button.Initialize(this, new initScript_Expeditions.ButtonValidator_partyMemberTeamComp(this, button, c, comp));
        button.SetText(LocalizeDictionary.QueryThenParse($"PartyComposition_{comp}"));
        button.optionID = AssertUniqueHash(c.GetHashCode());

        buttonsByID.Add(button.optionID, button);
        validatorsByID.Add(button.optionID, button.Validator);

        button.Validate();
    }
    public void MakeButton_Party(Manageable_Party p, scr_SelectableText button, bool isKidnap = false)
    {
        button.Initialize(this, new ButtonValidator_partySelect(this, button, p, isKidnap));
        button.SetText(p.FactionDisplayName);
        button.optionID = AssertUniqueHash(p.GetHashCode());

        buttonsByID.Add(button.optionID, button);
        validatorsByID.Add(button.optionID, button.Validator);

        button.Validate();
    }
    public void MakeButton_Expedition(Expedition exp, scr_SelectableText button, scr_partyBTN box)
    {
        button.Initialize(this, new initScript_Expeditions.ButtonValidator_selectExp(this, button, exp, box));
        button.SetText(exp.DisplayName);
        button.optionID = AssertUniqueHash(exp.GetHashCode());

        buttonsByID.Add(button.optionID, button);
        validatorsByID.Add(button.optionID, button.Validator);

        button.Validate();
    }

    public Manageable_Party currentParty = null;
    public void LoadParty(Manageable_Party p, bool iskidnap = false)
    {
        currentParty = p;
        Script_Expeditions.Draw(p, iskidnap);
    }


    public void UpdatePartyNames()
    {
        foreach(var i in this.validatorsByID)
        {
            if (!(i.Value is ButtonValidator_partySelect)) continue;
            (i.Value as ButtonValidator_partySelect).OnClickButton();
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
                case 9999: scr_System_SceneManager.current.UnloadLastCanvasFromScene(); break;
                default: break;
            }
        }
        ValidateAll();
    }

    public override void ValidateAll()
    {
        CalculateProductionWarning();
        base.ValidateAll();
        RefreshCurrentChara();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        /*
        while (list_Jobs.transform.childCount > 0)
        {
            DestroyImmediate(list_Jobs.transform.GetChild(0).gameObject);
        }*/
        //Debug.LogError("CANVAS MANAGEMENT ONDESTROY");
        scr_System_CampaignManager.current.NotifyUpdate();

    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // if click outside box
        if ((eventData.rawPointerPress.GetComponent<scr_Canvas_Management>() != null) || (eventData.button == PointerEventData.InputButton.Right && UtilityEX.isClickBelowDragThreshold(eventData)))
        {
            if (chara_scheduleCOMboxes[0].gameObject.activeInHierarchy)
            {
                (validatorsByID[32] as I_ButtonClickable).OnClickButton();
            }
            else scr_System_SceneManager.current.UnloadLastCanvasFromScene();
        }
    }


    public scr_SelectableText MakeEventResolveButton(ExpeditionMessageEntry m, out scr_resolveEv mb)
    {
        mb = Instantiate(this.Script_Expeditions.prefab_resolveEventBtn);
        var b = mb.button;
        b.Initialize(this, new initScript_Expeditions.ButtonValidator_ResolveEvent(this, b, m));
        b.SetText( m.unresolved.DisplayName );
        b.optionID = AssertUniqueHash(m.GetHashCode());

        buttonsByID.Add(b.optionID, b);
        validatorsByID.Add(b.optionID, b.Validator);

        b.Validate();
        return b;
    }
    

    public class button_ChangeTab : ButtonValidator, I_ButtonClickable
    {
        RectTransform target;
        new scr_Canvas_Management parent;
        scr_SelectableText text;
        Initializer init;
       // scr_PointerEnterNotifier pointerhandler;
        public button_ChangeTab(scr_Canvas_Management parent, scr_SelectableText text, RectTransform target, Initializer init = null) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.target = target;
            this.init = init;
           // pointerhandler = text.GetComponent<scr_PointerEnterNotifier>();
           // pointerhandler.Initialize(parent, text.optionID);
        }

        public override bool IsButtonValid()
        {

            if (parent.currentTab != target)
            {
                text.Toggle(true, false);
                target.gameObject.SetActive(false);
                //foreach (scr_SelectableText s in filters) if (!parent.currentTab_Filters.Contains(s)) s.gameObject.SetActive(false);

            }
            else
            {
                text.Toggle(true, true);
                target.gameObject.SetActive(true);
                //foreach (scr_SelectableText s in filters) s.gameObject.SetActive(true);


            }
            return true;
        }

        public void OnClickButton()
        {
            parent.currentTab = target;
            target.gameObject.SetActive(true);
            if (init != null) init();
        }
    }

    public class button_FactionSwitch : ButtonValidator , I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        bool left;
        public button_FactionSwitch(scr_Canvas_Management parent, bool left = false) : base(parent)
        {
            this.parent = parent;
            this.left = left;
        }

        public override bool IsButtonValid()
        {
            return parent.factions.Count > 1;
        }
        public void OnClickButton()
        {
            var index = parent.factions.IndexOf(parent.CurrentFaction);
            Manageable targetF = null;

            if (left) targetF = index - 1 >= 0 ? parent.factions[index - 1] : parent.factions[parent.factions.Count - 1];
            else targetF = index + 1 >= parent.factions.Count ? parent.factions[0] : parent.factions[index + 1];

            parent.LoadFactionData(targetF);
        }

    }

    public scr_Menu_CharaDetail prefab_Canvas_CharaDetail;
    public class button_CharaDetail : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        public button_CharaDetail(scr_Canvas_Management parent) : base(parent)
        {
            this.parent = parent;
        }

        public override bool IsButtonValid()
        {
            return parent.currentChara != null && parent.currentChara.RefID >= 0;
        }

        public void OnClickButton()
        {
            //, parent.transform.parent.GetComponent<RectTransform>()
            scr_Menu_CharaDetail detail = scr_System_SceneManager.current.LoadCanvasIntoScene(parent.prefab_Canvas_CharaDetail.GetComponent<RectTransform>(), parent.transform.parent.GetComponent<RectTransform>()).GetComponent<scr_Menu_CharaDetail>();
            detail.InitializeWithArgument(parent.currentChara.RefID);

            //scr_System_SceneManager.current.UnloadLastCanvasFromScene(typeof(scr_Canvas_Management));
        }
    }
        

    public class button_ManageProductionOrder_Add :ButtonValidator, I_ButtonClickable
    {

        new scr_Canvas_Management parent;
        public button_ManageProductionOrder_Add(scr_Canvas_Management parent, scr_SelectableText text, TMP_Text count) : base(parent)
        {
            this.parent = parent;
        }

        public override bool IsButtonValid()
        {

            return true;
        }

        public void OnClickButton()
        {

        }
    }

    public class button_ManageProductionOrder_AddCount : ButtonValidator, I_ButtonClickable
    {

        new scr_Canvas_Management parent;
        Manageable.ProductionOrder order;
        TMP_Text text;
        public button_ManageProductionOrder_AddCount(scr_Canvas_Management parent, TMP_Text text, Manageable.ProductionOrder order) : base(parent)
        {
            this.parent = parent;
            this.order = order;
            this.text = text;
        }

        public override bool IsButtonValid()
        {
            if (!parent.currentFaction.HasProductionOrder(order))
            {
                tooltip = "This Production Order no longer exists.";
                return false;
            }
            text.text = order.CountABS.ToString();
            return true;
        }

        public void OnClickButton()
        {
            if (UtilityEX.SHIFT) order.AddCount(100);
            else if (UtilityEX.CTRL) order.AddCount(10);
            else order.AddCount(1);
            //text.text = order.Count.ToString();
            //expectedWork.text = ((int)Math.Ceiling(order.Count * order.Recipe.workAmount / 60f)).ToString();
        }
    }

    public class button_ManageProductionOrder_ChangeType : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        Manageable.ProductionOrder order;
        scr_SelectableText text;
        public button_ManageProductionOrder_ChangeType(scr_Canvas_Management parent, scr_SelectableText text, Manageable.ProductionOrder order) : base(parent)
        {
            this.parent = parent;
            this.order = order;
            this.text = text;
        }
        public override bool IsButtonValid()
        {
            this.text.SetText(LocalizeDictionary.QueryThenParse(order.orderType.ToString()));
            return true;
        }
        public void OnClickButton()
        {
            order.orderType = 1 - order.orderType;
        }
    }

    public class button_ManageProductionOrder_ReduceCount : ButtonValidator, I_ButtonClickable
    {

        new scr_Canvas_Management parent;
        Manageable.ProductionOrder order;
        TMP_Text text;
        public button_ManageProductionOrder_ReduceCount(scr_Canvas_Management parent, TMP_Text text, Manageable.ProductionOrder order) : base(parent)
        {
            this.parent = parent;
            this.order = order;
            this.text = text;
        }

        public override bool IsButtonValid()
        {
            if (!parent.currentFaction.HasProductionOrder(order))
            {
                tooltip = "This Production Order no longer exists.";
                return false;
            }
            text.text = order.CountABS.ToString();
            if (order.CountABS > 0) return true;
            else return false;
        }

        public void OnClickButton()
        {
            //parent.currentFaction.AddProductionOrder(order.Recipe, -1);
            if (UtilityEX.SHIFT) order.AddCount(-100);
            else if (UtilityEX.CTRL) order.AddCount(-10);
            else order.AddCount(-1);

            //expectedWork.text = ((int) Math.Ceiling( order.Count * order.Recipe.workAmount / 60f)).ToString();
        }
    }

    public class button_ManageProductionOrder_RemoveCount : ButtonValidator, I_ButtonClickable
    {

        new scr_Canvas_Management parent;
        Manageable.ProductionOrder order;
        TMP_Text warning;
        scr_prOrderManage parentRect;

        Color32 conflictColor;
        string alert_hours, alert_items;
        int buttonID;
        public button_ManageProductionOrder_RemoveCount(scr_Canvas_Management parent, int buttonID, Manageable.ProductionOrder order, TMP_Text warning, scr_prOrderManage parentRect) : base(parent)
        {
            this.parent = parent;
            this.order = order;
            this.buttonID = buttonID;
            this.parentRect = parentRect;
            this.warning = warning;
           // this.tooltip = "Delete This Order\nThis functionality is currently disabled";
            this.conflictColor = scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color;
            alert_hours = LocalizeDictionary.QueryThenParse("ui_management_production_missingHours");
            alert_items = LocalizeDictionary.QueryThenParse("ui_management_production_missingResource");
        }

        public override bool IsButtonValid()
        {
            // modify warning message
            this.warning.text = "";
            var texts = new List<string>();
            if (this.order.Count > 0)
            {
                foreach (var i in order.Recipe.itemRequirements) if (!parent.CurrentFaction.resourceWarnings.ContainsKey(i.itemID) || parent.CurrentFaction.resourceWarnings[i.itemID] < 0) texts.Add(alert_items.Replace("$itemname$", i.Print));
                if (!parent.CurrentFaction.productionWarnings.ContainsKey(order.Recipe.jobKeyword) || parent.CurrentFaction.productionWarnings[order.Recipe.jobKeyword] < 0) texts.Add(alert_hours.Replace("$comname$", "tag_"+order.Recipe.jobKeyword ));
                this.warning.text = texts.Count > 0 ? Utility.WrapTextColor( String.Join(" ", texts), conflictColor): "";
            }

            if (!parent.currentFaction.HasProductionOrder(order))
            {
                tooltip = "This Production Order no longer exists.";
                return false;
            }
            return true;
        }

        public void OnClickButton()
        {
            parent.currentFaction.RemoveProductionOrder(order);
            parentRect.gameObject.SetActive(false);
            parent.DestroyPOMButton(order, buttonID);
            DestroyImmediate(parentRect.gameObject);
            //text.text = order.Count.ToString();
        }
    }

    public class button_EditSchedule : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        RectTransform scheduleRect;
        List<RectTransform> comRects;
        scr_SelectableText button;
        Image background;
        bool isActive = false;
        public button_EditSchedule(scr_Canvas_Management parent, scr_SelectableText button, RectTransform scheduleRect, Image scheduleRectBG, List<RectTransform> comRects) : base(parent)
        {
            this.parent = parent;
            this.scheduleRect = scheduleRect;
            this.comRects = comRects;
            this.background = scheduleRectBG;
            isActive = false;
            this.button = button;
            foreach (var i in comRects) i.gameObject.SetActive(false);
        }

        public override bool IsButtonValid()
        {
            tooltip = "";
            if (parent.currentChara == null) return false;
            else if (!parent.currentFaction.isCharaManager(0))
            {
                tooltip = "Player is not manager in faction [" + parent.currentFaction.ID + "]";
                return false;
            }
            else if (parent.currentChara.RefID == 0)
            {
                tooltip = "Cannot assign schedule to player";
                return false;
            }
            return true;
        }

        public void OnClickButton()
        {
            if (isActive)
            {
                isActive = !isActive;
                //parent.RefreshCurrentChara();
                parent.currentHighlightJobCOM = null;
                parent.SetCurrentChara(parent.currentChara);
               // parent.ValidateAll();
            }
            else
            {
                isActive = !isActive;
            }
            button.Toggle(true, isActive);
            button.Validate();
            foreach (var i in comRects) i.gameObject.SetActive(isActive);
            for (var i = 0; i < parent.chara_schedulebox.childCount; i++)
            {
                parent.chara_schedulebox.GetChild(i).GetComponent<scr_ScheduleBox>().SetActive(isActive);
            }
            background.gameObject.SetActive(isActive);
        }
    }

    public class button_setHighlightCOM : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        scr_SelectableText button;

        TMP_Text description;
        scr_SelectableText buttonText;

        Manageable.JobPostPreset preset = null;
        Manageable presetOwnerFaction = null;
        bool unset = false;


        public button_setHighlightCOM(scr_Canvas_Management parent, scr_button_setHighlightCOM box, scr_SelectableText buttonText, Manageable.JobPostPreset preset, Manageable presetOwnerFaction) : base(parent)
        {
            this.buttonText = buttonText;
            this.parent = parent;
            this.button = box.button;
            button.isButtonToggle = true;
            this.description = box.description;

            this.preset = preset;
            this.presetOwnerFaction = presetOwnerFaction;


            // button.SetText(preset.jobPostID);
            var strs = new List<string>();
            if (preset != null)
            {
                foreach (var cid in preset.workCommands)
                {
                    //Debug.Log($"preset workcommand {cid}");
                    var c_com = scr_System_Serializer.current.GetByNameOrID_COM(cid);
                    if (c_com != null) strs.Add(c_com.DisplayName());
                    else Debug.LogError($"CANNOT FIND WORK PRESET COMMAND {cid}");
                }
                description.text = LocalizeDictionary.QueryThenParse("management_jobpost_description_desc")
                    .Replace("$description$", String.Join(",", strs))
                    .Replace("$hour$", preset.activeHours.Count.ToString())
                    .Replace("$payout$", preset.PrintPayout)
                    .Replace("$additionalDescription$", "");

            }
            else description.text = "error";
            box.notifyTarget = this;

            buttonText.linkText = "";
            buttonText.SetTextPreInit($"{this.presetOwnerFaction.FactionDisplayName} : {preset.Name}");
        }

        COM highlightCOM = null;
        public button_setHighlightCOM(scr_Canvas_Management parent, scr_button_setHighlightCOM box, scr_SelectableText buttonText,  COM highlightCOM) : base(parent)
        {
            this.buttonText = buttonText;
            this.parent = parent;
            this.button = box.button;
            button.isButtonToggle = true;
            this.description = box.description;

            this.highlightCOM = highlightCOM;
            if (highlightCOM == null) description.text = "";

            buttonText.linkText = (highlightCOM != null ? highlightCOM.ID : "");
            buttonText.SetTextPreInit(highlightCOM != null ? highlightCOM.DisplayName() : "None");
        }

        public override bool IsButtonValid()
        {
            tooltip = "";
            if (this.preset != null)
            {
                // check if preset hours are occupied by any other faction else than self and/or home
                // if occupied, false.
                // else, true

                // on click:
                // non home will register work faction and overwrite home
                // home will wipe all other home preset and write self (ensuring no 2 job)
                // one faction only one preset
                // different faction preset can coexist as long as schedule no conflict
                var returnVal = true;
                var chara = parent.currentChara;
                bool ttip_overwrite_home = false;
                bool ttip_external = false;

                // self = home | job
                // target = null | home | job

                // home -> null, job -> null, home -> home, job -> home, allow
                // home -> job, job -> job, DISALLOW
                // nothing happens

                // if this is currently set, flag toggled and allow click (to cancel it)
                // if this is not currently set and there is conflict, disallow clicking

                foreach (var hour in preset.activeHours)
                {
                    var f = chara.CurrentJobScheduleFaction(hour);
                    if (f == null)
                    {
                        // allow set
                        unset = false;
                    }
                    else if (f == this.presetOwnerFaction)  // same faction overwrite ?
                    {
                        // same faction
                        var existingJobID = this.presetOwnerFaction.GetSchedule(chara).Get(hour).jobID;
                        if (existingJobID == this.preset.jobPostID)
                        {
                            // chara has already assigned identical jobpost, allow unset
                            unset = true;
                            button.Toggle(true, true);
                            break;
                        }
                    }
                    else if (chara.FactionManager.HomeFactions.Contains(f)) // target is home and self is obviously not home
                    {
                        // allow overwriting homefaction
                        unset = false;
                        ttip_overwrite_home = true;
                    }
                    else
                    {
                        returnVal = false;
                        tooltip += $"job preset conflict with existing schedule from [{f.FactionDisplayName}]";
                        break;
                    }
                }

                if (returnVal)
                {
                    tooltip += "will overwrite previous job setting, if any\n";
                    if (!chara.FactionManager.HomeFactions.Contains(presetOwnerFaction)) tooltip += "chara will be added to job faction\n";
                    List<string> s = new List<string>();
                    if (!unset) chara.FactionManager.ValidateSchedule(ref s, preset.activeHours);
                    this.tooltip += String.Join("\n", s)+"\n";
                }
                if (ttip_overwrite_home) tooltip += "will overwrite home faction job setting\n";
                if (unset)
                {
                    tooltip += "will unset\n";
                    button.Toggle(true, true);
                }
                else if (returnVal) button.Toggle(true, false);

                return returnVal;
            }
            else
            {
                if (this.highlightCOM == null)
                {
                    return false;
                }
                if (parent.currentHighlightJobCOM == highlightCOM)
                {
                    button.Toggle(true, true);
                }
                else
                {
                    button.Toggle(true, false);
                }
                description.text = parent.CurrentFaction.GetJobCOMAlertInfo(highlightCOM, true);

                return true;
            }
        }

        public void OnClickButton()
        {
            if (this.preset == null)
            {
                if (parent.currentHighlightJobCOM != highlightCOM) parent.currentHighlightJobCOM = highlightCOM;
                else parent.currentHighlightJobCOM = null;
            }
            else
            {
                var chara = parent.currentChara;
                
                for(int i = 0; i < 24; i++)
                {   // first wipe. one faction can only assign a single job preset to a chara, so we first wipe existing
                    chara.FactionManager.SetSchedule(presetOwnerFaction, i, null);//.SetWorkHours(chara, i, null);
                }

                if(!unset)
                {   // then, if not unset, set this preset
                    chara.FactionManager.SetSchedule(presetOwnerFaction, this.preset);
                }
                else
                {
                    chara.FactionManager.SetSchedule(presetOwnerFaction, null);
                }
               // for (int i = 0; i < 24; i++)
               // {
                    // first wipe
                //    presetOwnerFaction.SetWorkHours(chara, i, null);
                    //parent.currentChara.FactionManager.SetSchedule(presetOwnerFaction, i, null);
                //}
                //c.FactionManager.SetSchedule(parent.CurrentFaction, index, parent.CurrentHighlightJOBCOM);
                parent.NotifyScheduleChanged();
            }

        }

        public void NotifyPointerEnter()
        {
            if (this.preset != null) parent.CurrentHighlightHours = this.preset.activeHours;
            else parent.CurrentHighlightHours = null;

            //Debug.Log("NOTIFY POINTER ENTER");

            parent.ValidateAll();
        }
        public void NotifyPointerExit()
        {
            //Debug.Log("NOTIFY POINTER EXIT");

            parent.CurrentHighlightHours = null;
            parent.ValidateAll();
        }
    }

    public scr_button_setHighlightCOM prefab_setHighlightCOM;

    public enum JobAssignmentTab
    {
        singleCOM,
        jobPost,
        externalJob
    }
    List<int> tempListHash = new List<int>();

    public void OnChildActive(JobAssignmentTab tabID, RectTransform rectTransform)
    {
        switch(tabID)
        {
            case JobAssignmentTab.singleCOM:

                List<COM> list = new List<COM>();
                list.AddRange(currentFaction.JobPosts);
                list.RemoveAll(x => x == null);


                foreach (COM c in list)
                {
                    int hash = AssertUniqueHash( c.ID.GetHashCode() );

                    scr_button_setHighlightCOM scr = Instantiate(prefab_setHighlightCOM);
                    RectTransform r = scr.GetComponent<RectTransform>();
                    r.SetParent(rectTransform, false);
                    scr_SelectableText comp = scr.button;

                    comp.Initialize(this, new button_setHighlightCOM(this, scr, comp, c));
                    comp.optionID = hash;

                    buttonsByID.Add(comp.optionID, comp);
                    validatorsByID.Add(comp.optionID, comp.Validator);

                    comp.Validate();

                    tempListHash.Add(hash);
                    
                }


                break;
            case JobAssignmentTab.jobPost:
                var list2 = currentFaction.JobPostsPresets;

                
                foreach (var c in list2)
                {


                    int hash = AssertUniqueHash(c.jobPostID.GetHashCode());

                    scr_button_setHighlightCOM scr = Instantiate(prefab_setHighlightCOM);
                    RectTransform r = scr.GetComponent<RectTransform>();
                    r.SetParent(rectTransform, false);
                    //scr.description.text = c.jobPostID;

                    scr_SelectableText comp = scr.button;
                    comp.Initialize(this, new button_setHighlightCOM(this, scr, comp, c, currentFaction));
                    comp.optionID = hash;
                    buttonsByID.Add(comp.optionID, comp);
                    validatorsByID.Add(comp.optionID, comp.Validator);
                    comp.Validate();
                    tempListHash.Add(hash);
                    /*
                    scr_SelectableText comp = scr.button;

                    comp.Initialize(this, new button_setHighlightCOM(this, comp, c, scr.description));
                    comp.linkText = (c != null ? c.jobPostID : "");
                    comp.SetText(c != null ? c.DisplayName() : "None");

                    comp.optionID = hash;

                    buttonsByID.Add(comp.optionID, comp);
                    validatorsByID.Add(comp.optionID, comp.Validator);

                    comp.Validate();

                    tempListHash.Add(hash);
                    */
                }

                break;
            case JobAssignmentTab.externalJob: 

                foreach(var faction in currentFaction.ConnectedFactions)
                {
                    foreach(var c in faction.JobPostsPresets)
                    {
                        int hash = AssertUniqueHash(c.jobPostID.GetHashCode());

                        scr_button_setHighlightCOM scr = Instantiate(prefab_setHighlightCOM);
                        RectTransform r = scr.GetComponent<RectTransform>();
                        r.SetParent(rectTransform, false);
                        //scr.description.text = c.jobPostID;

                        scr_SelectableText comp = scr.button;
                        comp.Initialize(this, new button_setHighlightCOM(this, scr, comp, c, faction));
                        comp.optionID = hash;
                        buttonsByID.Add(comp.optionID, comp);
                        validatorsByID.Add(comp.optionID, comp.Validator);
                        comp.Validate();
                        tempListHash.Add(hash);
                    }
                }
                break;
        }

    }

    public void OnChildDisable(JobAssignmentTab tabID, RectTransform rect)
    {
        foreach(var hash in tempListHash)
        {
            scr_SelectableText text = buttonsByID[hash];
            buttonsByID.Remove(hash);
            ButtonValidator validator = validatorsByID[hash];
            validatorsByID.Remove(hash);

            if(validator != null) validator.Destroy();
            if (text != null)
            {
                text.gameObject.SetActive(false);
                Destroy(text.gameObject);
            }
        }

        tempListHash.Clear();
    }

    public void NotifyScheduleChanged()
    {
        
        ValidateAll();
    }

    public class Button_LoadCanvas_AddPO : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        public Button_LoadCanvas_AddPO(scr_Canvas_Management parent) : base(parent)
        {
            this.parent = parent;
        }

        public void OnChildExit()
        {
            //Debug.LogError("UNIMPLEMENTED");
            this.parent.RefreshPOList();
        }

        public override bool IsButtonValid()
        {
            return parent.canvas_AddPO != null;
        }
        public void OnClickButton()
        {
            scr_menu_AddProductionOrder cvs = scr_System_SceneManager.current.LoadCanvasIntoScene(parent.canvas_AddPO.GetComponent<RectTransform>(), parent.transform.parent.GetComponent<RectTransform>()).GetComponent<scr_menu_AddProductionOrder>(); 
            cvs.InitializeWithArgument(this.parent.CurrentFaction, OnChildExit);
        }
    }

    public class Button_LoadCanvas_AddTR : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        public Button_LoadCanvas_AddTR(scr_Canvas_Management parent) : base(parent)
        {
            this.parent = parent;
        }

        public override bool IsButtonValid()
        {
            return parent.canvas_AddTR != null;
        }

        protected void OnChildExit()
        {
            //Debug.LogError("UNIMPLEMENTED");
            this.parent.RefreshTAList();
        }

        public void OnClickButton()
        {
            scr_Menu_AddTrade cvs = scr_System_SceneManager.current.LoadCanvasIntoScene(parent.canvas_AddTR.GetComponent<RectTransform>(), parent.transform.parent.GetComponent<RectTransform>()).GetComponent<scr_Menu_AddTrade>();
            cvs.InitializeWithArgument(this.parent.CurrentFaction, OnChildExit);
        }
    }



    public class button_ManageTradeOrder_AddCount : ButtonValidator, I_ButtonClickable
    {

        new scr_Canvas_Management parent;
        Manageable.TradeOrder order;
        TMP_Text text;
        public button_ManageTradeOrder_AddCount(scr_Canvas_Management parent, TMP_Text text, Manageable.TradeOrder order) : base(parent)
        {
            this.parent = parent;
            this.order = order;
            this.text = text;
        }

        public override bool IsButtonValid()
        {
            if (!parent.currentFaction.HasTradeOrder(order))
            {
                tooltip = "This Trade Order no longer exists.";
                return false;
            }
            text.text = order.CountABS.ToString();
            return true;
        }

        public void OnClickButton()
        {
            if (UtilityEX.SHIFT) order.AddCount(100);
            else if (UtilityEX.CTRL) order.AddCount(10);
            else order.AddCount(1);
            //text.text = order.Count.ToString();
            //expectedWork.text = ((int)Math.Ceiling(order.Count * order.Recipe.workAmount / 60f)).ToString();
        }
    }

    public class button_ManageTradeOrder_ChangeType : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        Manageable.TradeOrder order;
        scr_SelectableText text;
        public button_ManageTradeOrder_ChangeType(scr_Canvas_Management parent, scr_SelectableText text, Manageable.TradeOrder order) : base(parent)
        {
            this.parent = parent;
            this.order = order;
            this.text = text;
        }
        public override bool IsButtonValid()
        {
            this.text.SetText(LocalizeDictionary.QueryThenParse(order.orderType.ToString()));
            return true;
        }
        public void OnClickButton()
        {
            order.orderType = 1 - order.orderType;
        }
    }

    public class button_ManageTradeOrder_ReduceCount : ButtonValidator, I_ButtonClickable
    {

        new scr_Canvas_Management parent;
        Manageable.TradeOrder order;
        TMP_Text text;
        public button_ManageTradeOrder_ReduceCount(scr_Canvas_Management parent, TMP_Text text, Manageable.TradeOrder order) : base(parent)
        {
            this.parent = parent;
            this.order = order;
            this.text = text;
        }

        public override bool IsButtonValid()
        {
            if (!parent.currentFaction.HasTradeOrder(order))
            {
                tooltip = "This Production Order no longer exists.";
                return false;
            }
            text.text = order.CountABS.ToString();
            if (order.CountABS > 0) return true;
            else return false;
        }

        public void OnClickButton()
        {
            //parent.currentFaction.AddProductionOrder(order.Recipe, -1);
            if (UtilityEX.SHIFT) order.AddCount(-100);
            else if (UtilityEX.CTRL) order.AddCount(-10);
            else order.AddCount(-1);

            //expectedWork.text = ((int) Math.Ceiling( order.Count * order.Recipe.workAmount / 60f)).ToString();
        }
    }

    public class ButtonValidator_charaSelect : ButtonValidator, I_ButtonClickable
    {
        //Character_Trainable target;

        int charaRefID;
        new scr_Canvas_Management parent;
        scr_SelectableText text;

        public ButtonValidator_charaSelect(scr_Canvas_Management parent, scr_SelectableText text, Character_Trainable chara) : base(parent)
        {
            this.charaRefID = chara.RefID;
            this.text = text;
            this.parent = parent;
            this.text.AttachOnHoverEnter(OnPointerEnter);
        }

        public override bool IsButtonValid()
        {
            //if (text == null) Debug.LogError("text null");
            if (parent.currentChara != null && parent.currentChara.RefID == charaRefID) text.Toggle(true, true);
            else text.Toggle(true, false);

            return true;
        }

        public void OnClickButton()
        {
            parent.SetCurrentChara(charaRefID);
            //parent.ValidateAll();
        }



        public void OnPointerEnter()
        {
            // OnPointerEnter is not hooked into page refresh, so we need to tie it manually
            parent.SetCurrentChara(charaRefID);
            parent.ValidateAll();
        }
    }
    public class button_ManageTradeOrder_RemoveCount : ButtonValidator, I_ButtonClickable
    {

        new scr_Canvas_Management parent;
        Manageable.TradeOrder order;
        TMP_Text warning;
        scr_prefabTransactionManage parentRect;

        Color32 conflictColor;
        string alert_hours, alert_items;
        int buttonID;
        public button_ManageTradeOrder_RemoveCount(scr_Canvas_Management parent, int buttonID, Manageable.TradeOrder order, TMP_Text warning, scr_prefabTransactionManage parentRect) : base(parent)
        {
            this.parent = parent;
            this.order = order;
            this.buttonID = buttonID;
            this.parentRect = parentRect;
            this.warning = warning;
            // this.tooltip = "Delete This Order\nThis functionality is currently disabled";
            this.conflictColor = scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color;
            alert_hours = LocalizeDictionary.QueryThenParse("ui_management_production_missingHours");
            alert_items = LocalizeDictionary.QueryThenParse("ui_management_production_missingResource");
        }

        public override bool IsButtonValid()
        {
            // modify warning message
            this.warning.text = "";
            var texts = new List<string>();
            if (this.order.Count > 0)
            {
                if (order.Cost.itemID != "") if(!parent.CurrentFaction.resourceWarnings.ContainsKey(order.Cost.itemID) || parent.CurrentFaction.resourceWarnings[order.Cost.itemID] < 0) texts.Add(alert_items.Replace("$itemname$", order.Cost.Print));
                //if (!parent.CurrentFaction.productionWarnings.ContainsKey(order.Recipe.jobKeyword) || parent.CurrentFaction.productionWarnings[order.Recipe.jobKeyword] < 0) texts.Add(alert_hours.Replace("$comname$", order.Recipe.jobKeyword));
                this.warning.text = texts.Count > 0 ? Utility.WrapTextColor(String.Join(" ", texts), conflictColor) : "";
            }

            if (!parent.currentFaction.HasTradeOrder(order))
            {
                tooltip = "This Production Order no longer exists.";
                return false;
            }
            return true;
        }

        public void OnClickButton()
        {
            parent.currentFaction.RemoveTradeOrder(order);
            parentRect.gameObject.SetActive(false);
            parent.DestroyTOMButton(order, buttonID);
            DestroyImmediate(parentRect.gameObject);
            //text.text = order.Count.ToString();
        }
    }

    public scr_Menu_addlinkfaction canvas_AddLink;
    public class button_modifyLinkFaction : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        public button_modifyLinkFaction(scr_Canvas_Management parent) : base(parent)
        {
            this.parent = parent;

        }

        public override bool IsButtonValid()
        {
            var mainExit = parent.CurrentFaction.MainExit;
            this.tooltip = "current faction main exit: " + (mainExit == null ? "null" : ((mainExit.parentFloor == null ? "nullFloor" : mainExit.parentFloor.displayName) +" "+ parent.CurrentFaction.MainExit.DisplayName));

            if (parent.canvas_AddLink == null) return false;
            if (scr_System_CampaignManager.current.Factions.Count < 2)
            {
                this.tooltip += "\n\nNo faction can be added";
                return false;
            }
            return true;
            //return parent.canvas_AddTR != null;
        }

        protected void OnChildExit()
        {
            //Debug.LogError("UNIMPLEMENTED");
            parent.overviewScript.Initialize(parent.CurrentFaction);
        }

        public void OnClickButton()
        {
            scr_Menu_addlinkfaction cvs = scr_System_SceneManager.current.LoadCanvasIntoScene(parent.canvas_AddLink.GetComponent<RectTransform>(), parent.transform.parent.GetComponent<RectTransform>()).GetComponent<scr_Menu_addlinkfaction>();
            cvs.InitializeWithArgument(this.parent.CurrentFaction, OnChildExit);
        }
    }

    public class ButtonValidator_partySelect : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        scr_SelectableText text;
        Manageable_Party party;
        bool isKidnap;
        public ButtonValidator_partySelect(scr_Canvas_Management parent, scr_SelectableText text, Manageable_Party party, bool isKidnap) : base(parent)
        {
            this.isKidnap = isKidnap;
            this.text = text;
            this.parent = parent;
            this.party = party;
            this.text.AttachOnHoverEnter(OnPointerEnter);
        }

        public override bool IsButtonValid()
        {
            //if (text == null) Debug.LogError("text null");
            if (!text.gameObject.activeInHierarchy) return false;


            if (parent.currentParty == this.party) text.Toggle(true, true);
            else text.Toggle(true, false);
            return true;
        }

        public void OnClickButton()
        {
            text.SetText(party.FactionDisplayName);
        }

        public void OnPointerEnter()
        {
            // OnPointerEnter is not hooked into page refresh, so we need to tie it manually
            parent.LoadParty(party, isKidnap);// (charaRefID);
            parent.ValidateAll();
        }

    }

    public class ButtonValidator_partyMemberSelect : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        scr_SelectableText text;
        Character_Trainable c;
        public ButtonValidator_partyMemberSelect(scr_Canvas_Management parent, scr_SelectableText text, Character_Trainable c) : base(parent)
        {
            this.text = text;
            this.parent = parent;
            this.c = c;

            text.isButtonToggle = true;
        }

        bool isJoin = false;

        public override bool IsButtonValid()
        {
            //if (text == null) Debug.LogError("text null");
            if (!text.gameObject.activeInHierarchy) return false;
            if (parent.currentParty == null) return false;
            if (!parent.currentParty.isPlayerFaction) return false;
            if (parent.currentParty.ManagedRefs.Contains(c.RefID)) isJoin = false;
            else isJoin = true;

            text.Toggle(true, !isJoin);

            return true;
        }

        public void OnClickButton()
        {
            if (isJoin) parent.currentParty.AddToFaction(c, Manageable_GuestStatus.Member);
            else parent.currentParty.RemoveFromFaction(c);
            parent.Script_Expeditions.Initialize(parent, parent.CurrentFaction, true);
        }
    }

    public class ButtonValidator_partyEditMembers : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        scr_SelectableText text;
        public ButtonValidator_partyEditMembers(scr_Canvas_Management parent, scr_SelectableText text) : base(parent)
        {
            this.text = text;
            this.parent = parent;
            text.isButtonToggle = true;
        }

        bool isEditing = false;

        public override bool IsButtonValid()
        {
            //if (text == null) Debug.LogError("text null");
            if (!text.gameObject.activeInHierarchy) return false;
            if (parent.currentParty == null || parent.CurrentFaction == null) return false;
            if (parent.currentParty.isActive) return false;
            if (!parent.currentParty.isPlayerFaction) return false;

            if (parent.Script_Expeditions.CurrentMode == initScript_Expeditions.PartyEditUI.MembersEdit) isEditing = true;
            else isEditing = false;

            text.Toggle(true, isEditing);
            return true;
        }

        public void OnClickButton()
        {
            if (!isEditing) parent.Script_Expeditions.CurrentMode = initScript_Expeditions.PartyEditUI.MembersEdit;
            else parent.Script_Expeditions.CurrentMode = initScript_Expeditions.PartyEditUI.Neutral;
        }
    }
    public class ButtonValidator_partyEditExpeditions : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        scr_SelectableText text;
        public ButtonValidator_partyEditExpeditions(scr_Canvas_Management parent, scr_SelectableText text) : base(parent)
        {
            this.text = text;
            this.parent = parent;
        }

        public override bool IsButtonValid()
        {
            //if (text == null) Debug.LogError("text null");
            if (!text.gameObject.activeInHierarchy) return false;
            if (parent.currentParty == null || parent.CurrentFaction == null || parent.currentParty.Job == null)
            {
                text.SetText(LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_").Replace("$expName$","-"));
                return false;
            }
            if (!parent.currentParty.isPlayerFaction) return false;
            text.SetText(parent.currentParty.Job.DisplayName+ (parent.currentParty.isActive ? " "+parent.currentParty.Job.RemainingTime : ""));
            return !parent.currentParty.isActive;
        }

        public void OnClickButton()
        {
            parent.Script_Expeditions.CurrentMode = initScript_Expeditions.PartyEditUI.ExpeditionEdit;
        }
    }

    public class ButtonValidator_partyCreate : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        scr_SelectableText text;
        public ButtonValidator_partyCreate(scr_Canvas_Management parent, scr_SelectableText text) : base(parent)
        {
            this.text = text;
            this.parent = parent;
        }

        public override bool IsButtonValid()
        {
            //if (text == null) Debug.LogError("text null");
            if (!text.gameObject.activeInHierarchy) return false;
            return parent.CurrentFaction != null;
        }

        public void OnClickButton()
        {
            parent.CurrentFaction.CreateParty();
            parent.Script_Expeditions.Initialize(parent, parent.CurrentFaction);
        }
    }

    public scr_SelectExp expSelectPage;
    

    public Expedition currentExpedition = null;
    
}


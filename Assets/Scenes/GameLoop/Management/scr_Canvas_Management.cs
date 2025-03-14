using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using TMPro;
using UnityEngine.EventSystems;
using System;
using System.Diagnostics;


public class scr_Canvas_Management : scr_Menu, IPointerClickHandler
{

    public List<Manageable> factions = new List<Manageable>();
    protected Manageable currentFaction = null;
    public Manageable CurrentFaction { get { return currentFaction; } set { currentFaction = value; } }
    public scr_HoverableText factionName;

    public TMP_Text production_results, production_warnings, chara_warnings;
    public RectTransform list_factionWork, list_assignCOM;

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
            if (m != null) factions.Add(m);
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
        homef = scr_System_Serializer.current.Dictionary.QueryThenParse("management_faction_home_nameplate");
        workf = scr_System_Serializer.current.Dictionary.QueryThenParse("management_faction_work_nameplate");
        otherf = scr_System_Serializer.current.Dictionary.QueryThenParse("management_faction_others_nameplate");
        charaLocAP = scr_System_Serializer.current.Dictionary.QueryThenParse("ui_management_jobs_currentInfo");
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
    public RectTransform list_orders;

    private void Initialize_FactionProduction()
    {
        if (initialized_faction_productions) return;
        else initialized_faction_productions = true;

        if (loadedOrders == null) loadedOrders = new List<Manageable.ProductionOrder>();
        else
        {

        }
        if (loadedOrders_Rect == null) loadedOrders_Rect = new Dictionary<Manageable.ProductionOrder, RectTransform>();
        else
        {

        }

        RefreshPOList();

    }

    private void CalculateProductionWarning()
    {
        production_results.text = currentFaction.printDebugInfo_Orders();
        production_warnings.text = currentFaction.printDebugInfo_Jobs();
    }

    List<Manageable.ProductionOrder> loadedOrders_daily = null;
    List<Manageable.ProductionOrder> loadedOrders = null;
    Dictionary<Manageable.ProductionOrder, RectTransform> loadedOrders_Rect = null;
    private void RefreshPOList()
    {
        foreach (var order in currentFaction.ProductionOrders) if (!loadedOrders.Contains(order)) MakePOButton(order, loadedOrders);

        //foreach(var order in loadedOrders_daily) if (!currentFaction.ProductionOrdersDaily.Contains(order)) DeletePOButton(order)
    }

    private void MakePOButton(Manageable.ProductionOrder order, List<Manageable.ProductionOrder> manageList)
    {
        int recipeHash = (order.Recipe.RecipeUID.GetHashCode()) * 4;
        if (loadedOrders_Rect.ContainsKey(order)) return;

        scr_prOrderManage entry = Instantiate(prefab_POEntry);
        entry.itemName.SetText(order.Recipe.DisplayName, false, order.Recipe.RecipeUID);
        entry.itemName.SetExternalTooltip(order.RecipeItem.Tooltip);
        entry.itemCount.text = currentFaction.Inventory.GetItemCount(order.Recipe.outputItemBaseID).ToString();
        entry.orderAmount.text = order.CountABS.ToString();
        RectTransform rect = entry.GetComponent<RectTransform>();

        //entry.expectedWorkLoad.text = (order.Recipe.workAmount).ToString();

        RegisterButton(recipeHash + 1, entry.buttonPlus, new button_ManageProductionOrder_AddCount(this, entry.orderAmount, order));
        RegisterButton(recipeHash + 2, entry.button_orderType, new button_ManageProductionOrder_ChangeType(this, entry.button_orderType, order));
        RegisterButton(recipeHash + 3, entry.buttonMinus, new button_ManageProductionOrder_ReduceCount(this, entry.orderAmount, order));
        RegisterButton(recipeHash, entry.btn_action, new button_ManageProductionOrder_RemoveCount(this, order));

        rect.SetParent(list_orders, false);
        loadedOrders_Rect.Add(order, rect);
    }

    /// <summary>
    /// THIS IS NOT BEING USED AT ALL RIGHT ???
    /// </summary>
    /// <param name="order"></param>
    private void DestroyPOMButton(Manageable.ProductionOrder order)
    {
        //// ??????
        int recipeHash = (order.Recipe.RecipeUID.GetHashCode()) * 4;
        DestroyCOMButton(recipeHash);
        DestroyCOMButton(recipeHash + 1);
        DestroyCOMButton(recipeHash + 2);
        DestroyCOMButton(recipeHash + 2);

        if (loadedOrders_Rect.ContainsKey(order))
        {
            var box = loadedOrders_Rect[order];
            loadedOrders_Rect.Remove(order);
            box.gameObject.SetActive(false);
            Destroy(box.gameObject);
        }
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

    /////// CHARA TAB

    public RectTransform Tab_Jobs;
    public TMP_Text chara_fullname, charaGender;
    public scr_HoverableText chara_Race, chara_RaceTemplate;
    public scr_SelectableText chara_HomeFaction, chara_TempHomeFaction;
    public TMP_Text chara_location_ap;
    public RectTransform chara_schedulebox, chara_scheduleCOMbox;
    public RectTransform list_chara;
    public scr_SelectableText prefab_charaNameButton;

    private List<Character_Trainable> charaInFaction;
    public Character_Trainable currentChara;

    public delegate void Initializer();

    private bool initialized_faction_charaList = false;
    private void Initialize_FactionCharaList()
    {
        if (initialized_faction_charaList) return;
        else initialized_faction_charaList = true;

        while (list_chara.transform.childCount > 0) DestroyImmediate(list_chara.transform.GetChild(0).gameObject);
        if (charaInFaction != null && charaInFaction.Count > 0) foreach (var c in charaInFaction) DestroyCharaButton(c);


        if (currentFaction == null) return;

        charaInFaction = currentFaction.ManagedChara_Members;
        if (charaInFaction == null || charaInFaction.Count < 1) return;



        SetCurrentChara(scr_System_CampaignManager.current.Player);

        if (charaInFaction.Count > 0) foreach (Character_Trainable chara in charaInFaction) MakeCharaButton(list_chara, prefab_charaNameButton, chara);
    }

    private void DestroyCharaButton(Character_Trainable chara)
    {
        buttonsByID.Remove(chara.GetHashCode());
        validatorsByID.Remove(chara.GetHashCode());
    }

    private void MakeCharaButton(RectTransform parent, scr_SelectableText prefab, Character_Trainable chara)
    {

        scr_SelectableText comp = Instantiate(prefab);
        RectTransform r = comp.GetComponent<RectTransform>();
        r.SetParent(parent, false);


        comp.Initialize(this, new ButtonValidator_charaSelect(this, comp, chara));
        comp.SetText(chara.FirstName);

        comp.optionID = chara.GetHashCode();

        buttonsByID.Add(comp.optionID, comp);
        validatorsByID.Add(comp.optionID, comp.Validator);

        comp.Validate();
    }

    public void SetCurrentChara(int charaRef)
    {
        Character_Trainable c = charaInFaction.Find(x => x.RefID == charaRef);
        if (c != null) SetCurrentChara(c);
    }

    COM currentHighlightJobCOM = null;
    public COM CurrentHighlightJOBCOM { get { return currentHighlightJobCOM; } }



    public void SetCurrentChara(Character_Trainable c)
    {
        // destroy previous
        while (list_factionWork.transform.childCount > 0)
        {
            DestroyImmediate(list_factionWork.transform.GetChild(0).gameObject);
        }

        // set current

        currentChara = c;

        chara_fullname.text = c.FullName;
        charaGender.SetText(scr_System_Serializer.current.Dictionary.QueryThenParse(currentChara.Appearance.ToString()), false);
        chara_Race.SetText(currentChara.Race.DisplayName, false, currentChara.Race.ID + "_tooltip");
        chara_RaceTemplate.SetText(currentChara.RaceTemplate.DisplayName, false, currentChara.RaceTemplate.ID + "_tooltip");

        chara_location_ap.SetText(charaLocAP.Replace("$location$", scr_System_CampaignManager.current.Map.FindRoomByChara(currentChara.RefID).DisplayName).Replace("$jobdescription$", currentChara.GetJobDescription()));

        chara_HomeFaction.SetText(currentChara.FactionManager.Faction_Home == null ? " - " : currentChara.FactionManager.Faction_Home.FactionDisplayName);
        chara_TempHomeFaction.SetText(currentChara.FactionManager.Faction_Home_Temporary == null ? " - " : currentChara.FactionManager.Faction_Home_Temporary.FactionDisplayName);


        Manageable.Job_Schedule jbsch = currentFaction.GetSchedule(c);
        // int currentHour = scr_System_Time.current.getCurrentTime().Hour;

        foreach (Manageable faction in c.FactionManager.WorkFactions)
        {
            TMP_Text newLine = Instantiate(prefab_text_standard).GetComponent<TMP_Text>();
            newLine.text = faction.ID;
            newLine.rectTransform.SetParent(list_factionWork, false);
            if (faction == currentFaction) newLine.color = Color.cyan;
        }

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

        RefreshCurrentChara();
    }
    public void RefreshCurrentChara()
    {
        List<string> warnings = new List<string>();
        currentChara.FactionManager.ValidateSchedule(ref warnings);

        chara_warnings.text = String.Join("\n", warnings);
    }

    public RectTransform list_PrivateRooms;

    private void writeLine(string s, RectTransform parent)
    {
        RectTransform targetRect = Instantiate(prefab_text_standard);
        targetRect.GetComponent<TMP_Text>().text = s;

        targetRect.SetParent(parent, false);
    }

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
                case 31: // chara detail tab
                    button.Initialize(this, new button_CharaDetail(this)); break;
                case 32: // chara edit schedule
                    button.Initialize(this, new button_EditSchedule(this, button, chara_schedulebox, chara_scheduleCOMbox)); break;
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
        base.ValidateAll();
        CalculateProductionWarning();
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
        if (eventData.rawPointerPress.GetComponent<scr_Canvas_Management>() != null) scr_System_SceneManager.current.UnloadLastCanvasFromScene();
        // inside box
        else if (eventData.button == PointerEventData.InputButton.Right && Utility.isClickBelowDragThreshold(eventData)) scr_System_SceneManager.current.UnloadLastCanvasFromScene();
        //Debug.Log("scr_Menu_CharaDetail: OnPointerClick! Data["+eventData.pointerPress+"] rawData["+ eventData.rawPointerPress + "]");
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
        }



        public void OnPointerEnter()
        {
            // OnPointerEnter is not hooked into page refresh, so we need to tie it manually
            parent.SetCurrentChara(charaRefID);
            parent.ValidateAll();
        }
    }

    public class button_ChangeTab : ButtonValidator, I_ButtonClickable
    {
        RectTransform target;
        new scr_Canvas_Management parent;
        scr_SelectableText text;
        Initializer init;
        scr_PointerEnterNotifier pointerhandler;
        public button_ChangeTab(scr_Canvas_Management parent, scr_SelectableText text, RectTransform target, Initializer init = null) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.target = target;
            this.init = init;
            pointerhandler = text.GetComponent<scr_PointerEnterNotifier>();
            pointerhandler.Initialize(parent, text.optionID);
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
            this.tooltip = "Add 1 to this Order";
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
            order.AddCount(1);
            //order.AddCount(1);
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
            this.text.SetText(scr_System_Serializer.current.Dictionary.QueryThenParse(order.orderType.ToString()));
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
            this.tooltip = "Reduce 1 from this Order";
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
            order.AddCount(-1);
            
            //expectedWork.text = ((int) Math.Ceiling( order.Count * order.Recipe.workAmount / 60f)).ToString();
        }
    }

    public class button_ManageProductionOrder_RemoveCount : ButtonValidator, I_ButtonClickable
    {

        new scr_Canvas_Management parent;
        Manageable.ProductionOrder order;
        public button_ManageProductionOrder_RemoveCount(scr_Canvas_Management parent, Manageable.ProductionOrder order) : base(parent)
        {
            this.parent = parent;
            this.order = order;
            this.tooltip = "Delete This Order\nThis functionality is currently disabled";
        }

        public override bool IsButtonValid()
        {
            return false;
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
            //order.AddCount(-1);
            //text.text = order.Count.ToString();
        }
    }

    public class button_EditSchedule : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        RectTransform scheduleRect, comRect;
        scr_SelectableText button;
        bool isActive = false;
        public button_EditSchedule(scr_Canvas_Management parent, scr_SelectableText button, RectTransform scheduleRect, RectTransform comRect) : base(parent)
        {
            this.parent = parent;
            this.scheduleRect = scheduleRect;
            this.comRect = comRect;
            isActive = false;
            this.button = button;
            this.comRect.gameObject.SetActive(false);
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
                parent.AssignCOM_Deactivate();
                parent.RefreshCurrentChara();
                parent.currentHighlightJobCOM = null;
            }
            else
            {
                isActive = !isActive;

            }
            button.Toggle(true, isActive);
            comRect.gameObject.SetActive(isActive);

            if (scheduleRect.transform.childCount >= 24)
            {
                for (int i = 0; i < 24; i++)
                {
                    scr_ScheduleBox box = scheduleRect.transform.GetChild(i).GetComponent<scr_ScheduleBox>();
                    if (box == null) continue;
                    box.SetActive(isActive);

                    box.Refresh();
                    
                }
            }

            if (isActive) parent.AssignCOM_Activate();
        }
    }

    public class button_setHighlightCOM : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        scr_SelectableText button;
        COM highlightCOM;
        TMP_Text description;
        public button_setHighlightCOM(scr_Canvas_Management parent, scr_SelectableText button, COM highlightCOM, TMP_Text description) : base(parent)
        {
            this.parent = parent;
            this.button = button;
            this.highlightCOM = highlightCOM;
            button.isButtonToggle = true;
            this.description = description;
            if (highlightCOM == null) description.text = "";
        }

        public override bool IsButtonValid()
        {
            if (parent.currentHighlightJobCOM == highlightCOM)
            {
                button.Toggle(true, true);
            }
            else
            {
                button.Toggle(true, false);
            }
            if (highlightCOM != null) description.text = parent.CurrentFaction.GetJobAlertInfo(highlightCOM);

            return true;
        }

        public void OnClickButton()
        {
            if (parent.currentHighlightJobCOM != highlightCOM) parent.currentHighlightJobCOM = highlightCOM;
            else parent.currentHighlightJobCOM = null;
        }
    }



    List<COM> tempListCOM = new List<COM>();
    public scr_button_setHighlightCOM prefab_setHighlightCOM;
    public void AssignCOM_Activate()
    {
        List<COM> list = new List<COM>();
        list.Add(null);
        list.AddRange(currentFaction.JobPosts);

        foreach (COM c in list)
        {
            int hash = 10000 + (c != null? c.ID.GetHashCode():0);

            if (buttonsByID.ContainsKey(hash))
            {
                UnityEngine.Debug.LogError("canvas management assign com makebutton error duplicate id hash");
            }
            else
            {
                scr_button_setHighlightCOM scr = Instantiate(prefab_setHighlightCOM);
                RectTransform r = scr.GetComponent<RectTransform>();
                r.SetParent(list_assignCOM, false);
                scr_SelectableText comp = scr.button;

                comp.Initialize(this, new button_setHighlightCOM(this, comp, c, scr.description));
                comp.linkText = (c != null? c.ID : "");
                comp.SetText(c != null? c.DisplayName():"None");

                comp.optionID = hash;

                buttonsByID.Add(comp.optionID, comp);
                validatorsByID.Add(comp.optionID, comp.Validator);

                comp.Validate();

                tempListCOM.Add(c);
            }
        }
    }

    public void NotifyScheduleChanged()
    {
        ValidateAll();
    }

    public void AssignCOM_Deactivate()
    {
        for(int i = tempListCOM.Count - 1; i >=0;i--)
        {
            COM c = tempListCOM[i];
            int hash = 10000 + (c != null ? c.ID.GetHashCode() : 0);

            scr_SelectableText text = buttonsByID[hash];
            buttonsByID.Remove(hash);
            ButtonValidator validator = validatorsByID[hash];
            validatorsByID.Remove(hash);

            validator.Destroy();
            text.gameObject.SetActive(false);
            Destroy(text.gameObject);
            tempListCOM.RemoveAt(i);
        }

        while (list_assignCOM.transform.childCount > 0) DestroyImmediate(list_assignCOM.transform.GetChild(0).gameObject);
    }


}


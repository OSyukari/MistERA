using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;

public class canvas_videoEdit : scr_Menu
{
    public scr_inputFieldLink titleText;
    KojoRecording comp = null;

    Item_Instance originalItem = null;
    I_IsJobGiver factionOwner;
    public void InitializeWithArgument(Item_Instance instance, I_IsJobGiver factionOwner)
    {
        if (!initialized) Initialize();

        originalItem = instance;
        this.factionOwner = factionOwner;
        if (instance != null && instance.Comp_Records != null && instance.Comp_Records.Records != null)
        {
            var cmp = instance.Comp_Records.Records;

            var string1 = JsonConvert.SerializeObject(cmp, UtilityEX.SerializerSettings);
            comp = JsonConvert.DeserializeObject<KojoRecording>(string1, UtilityEX.SerializerSettings);
            LoadRecords(comp);
        }
        else
        {
            Debug.LogError("error loading itemcomp");
        }
        titleText.self_inputfield.text = instance.DisplayName;

        ValidateAll();
    }

    public filter_actor prefab_actor;
    Dictionary<int, filter_actor> actors = new Dictionary<int, filter_actor>();

    public RectTransform RectList_Options;


    public void OnTitleChange()
    {
        if (this.validatorsByID.TryGetValue(9997, out var btn))
        {
            btn.IsButtonValid();
        }
    }

    void LoadRecords(KojoRecording comp)
    {
        if (comp == null) return;
        comp.Initialize();



        var replaceStrings = new Dictionary<string, string>();


        foreach (var rec in comp.MessageCountByActor)
        {
            var name = rec.Value.Name;
            var old = rec.Value.firstNameOriginal;
            if (name != old)
            {
                if (!replaceStrings.ContainsKey(old)) replaceStrings.Add(old, name);
            }
        }


        foreach(var actorsetting in comp.ActorSettings)
        {
            var box = Instantiate(prefab_actor);
            box.innerActorRecord = actorsetting;
            box.selfRect.SetParent(RectList_Options, false);

            box.actorName.SetText(LocalizeDictionary.QueryThenParse("recording_edit_canvas_actor_originalName")
                .Replace("$name$", LocalizeDictionary.QueryThenParse(actorsetting.firstNameOriginal)));

            box.overwriteName.self_inputfield.text = actorsetting.Name;

            RegisterButton(box.btn_removeAP_related_include, new button_filter_ap(this, box.btn_removeAP_related_include, box, FilterMode.Include));
            RegisterButton(box.btn_removeAP_related_only, new button_filter_ap(this, box.btn_removeAP_related_only, box, FilterMode.Only));
            RegisterButton(box.btn_removeMessage_related_only, new button_filter_msg(this, box.btn_removeMessage_related_only, box, FilterMode.Only));
            RegisterButton(box.btn_removeMessage_related_include, new button_filter_msg(this, box.btn_removeMessage_related_include, box, FilterMode.Include));


            actors.Add(actorsetting.refID, box);
        }

        foreach(var msgcol in comp.collect)
        {
            // parse each
            // no need to store any other info
            msgcol.Value.FlushCollectedLogsIntoUI(msgcol.Key, this, replaceStrings);

        }
    }


    public scr_actionHolder actionHolder;

    public scr_actionHolder RegisterAPRecord(ActionPackageRecords sourceAP)
    {
        // actor ap log
        var box = sourceAP.mcol != null && sourceAP.mcol.hasMessageChecks ? Instantiate(actionHolder) : null;

        if (box != null)
        {
            box.selfRect.SetParent(RectList_Messages, false);

            RegisterButton(box.toggleVisibility, new button_toggleAP(this, box.toggleVisibility, box));

            sourceAP.RecordBox = box;
            actionHolder.ap = sourceAP;

            apTracker.Add(box);
        }


        foreach (var kvp in actors)
        {
            if (sourceAP != null && sourceAP.hasActor(kvp.Key))
            {
                if (sourceAP.isSingleActor()) kvp.Value.ap_related_only.Add(sourceAP);
                else kvp.Value.ap_related_include.Add(sourceAP);
            }
        }

        return box;
    }

    public void ParseEntry(I_Records record, MessageCollect parent, DateTime timestamp, Dictionary<string, string> replaceStrings, ActionPackageRecords sourceAP = null, RectTransform parentRect = null)
    {
        if (record == null) return;


        if (parentRect != null)
        {
            PrintEntry_1(record, replaceStrings, parentRect, parentRect, sourceAP);
            // Export(box, parent, record, timestamp, sourceAP);
        }
        else
        {

            scr_videoEdit_message_record box = null;

            box = Instantiate(prefab_message_holder);
            parentRect = sourceAP == null || sourceAP.RecordBox == null ? RectList_Messages : sourceAP.RecordBox.messageList;
            box.selfRect.SetParent(parentRect, false);

            PrintEntry_1(record, replaceStrings, parentRect, box.innerObject, sourceAP);
            Export(box, parent, record, timestamp, sourceAP);
        }
    }

    void Export(scr_videoEdit_message_record box, MessageCollect parent, I_Records record, DateTime timestamp, ActionPackageRecords sourceAP = null)
    {
        if (box != null)
        {
            foreach (var kvp in actors)
            {
                if (record.IsRelevantActor(kvp.Key))
                {
                    if (record.IsSingleActor) kvp.Value.msg_related_only.Add(box);
                    else kvp.Value.msg_related_include.Add(box);
                }
                box.ap = sourceAP;
            }

            box.source_timestamp = timestamp;
            box.source = parent;
            box.rec = record;

            RegisterButton(box.toggleVisibility, new button_toggleVisibility(this, box.toggleVisibility, box));
            boxTracker.Add(box);
        }
    }


    public RectTransform prefab_LogEntry;
    public scr_HoverableText prefab_LogLine;
    public scr_menu_question prefab_question;
    public scr_menu_inputField prefab_inputField;
    public scr_menu_LLMQuery prefab_llm;

    public RectTransform RectList_Messages;
    public scr_videoEdit_message_record prefab_message_holder;

    void PrintEntry_1(I_Records record, Dictionary<string, string> replaceStrings, RectTransform parentRect, RectTransform textRect, ActionPackageRecords sourceAP = null)
    {
        if (record is DescriptionCollector)
        {
            var desc = record as DescriptionCollector;
            if (desc == null) return;

            var log = new Message_Text(desc, true, false, replaceStrings);
            if (desc.message_excludeRelated != "" && desc.message_excludeRelated != desc.message) log.AddMessage(desc.message_excludeRelated, true);
            PrintEntry_2(log, sourceAP, textRect);
        }
        else if (record is KojoCollector)
        {
            var desc = record as KojoCollector;
            // RefID -1 no display
            // RefID -2 
            if (desc == null) return;

            if (desc.collect.message != null && desc.collect.message.Length > 0)
            {
                var log = new Message_Text(desc.collect, false, desc.tooltip, replaceStrings);
                PrintEntry_2(log, sourceAP, textRect);
            }
            foreach (var n in desc.collect.nexts)
            {
                var box2 = Instantiate(prefab_message_holder);
                box2.selfRect.SetParent(parentRect, false);
                var log = new Message_Text(n, false, desc.tooltip, replaceStrings);
                PrintEntry_2(log, sourceAP, box2.innerObject);
            }
        }
        else if (record is QuestionBoxCollector)
        {
            var desc = record as QuestionBoxCollector;
            // RefID -1 no display
            // RefID -2 
            if (desc == null) return;

            MessageLog log = new Message_Question_Record(desc, replaceStrings);
            PrintEntry_2(log, sourceAP, textRect);
        }
        else
        {
            Debug.LogError("unknown record type");
        }

    }
    void PrintEntry_2( MessageLog current, ActionPackageRecords sourceAP, RectTransform parent)
    {
        if (current is Message_Text)
        {
            var txt = current as Message_Text;
            RectTransform msgbox = Instantiate(prefab_LogEntry);
            //if (current.PortraitRef == -1000) msgbox = Instantiate(prefab_SeparationEntry);
            txt.animateAllOverride = true;
            msgbox.SetParent(parent, false);
            (current as Message_Text).Draw(true, msgbox.GetComponent<scr_MessageLogBox>(), this.prefab_LogLine);
            // if (waiting) Debug.Log("waiting!");
        }
        else if (current is Message_Question)
        {
            var question = Instantiate(prefab_question);
            question.transform.SetParent(parent, false);
            (current as Message_Question).Draw(true, this.m_Canvas, question);
        }
        else if (current is Message_InputField)
        {
            var question = Instantiate(prefab_inputField);
            question.transform.SetParent(parent, false);
            (current as Message_InputField).Draw(true, this.m_Canvas, question);
        }
        else if (current is Message_LLMQuery)
        {
            var query = Instantiate(prefab_llm);
            query.transform.SetParent(parent, false);
            (current as Message_LLMQuery).Draw(true, this.m_Canvas, query);
        }
        else if (current is Message_Question_Record)
        {
            var question = Instantiate(prefab_question);
            question.transform.SetParent(parent, false);
            (current as Message_Question_Record).Draw(true, this.m_Canvas, question);
        }

    }
    private bool RegisterButton(scr_SelectableText button, ButtonValidator validator)
    {
        var optionID = button.GetHashCode();
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

    protected override void Awake()
    {
        base.Awake();
        this.m_Canvas.overrideSorting = true;
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);

    }
    public override void Initialize()
    {
        base.Initialize();

        bool safe = scr_System_CentralControl.current.isSafeMode;

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            // Debug.Log("Button " + button + " " + button.optionID);
            switch (button.optionID)
            {

                case 9999: // exit
                    button.Initialize(this, button_alwaysValid); break;
                case 9998: // exit
                    button.Initialize(this, new button_saveRecord(this, button));break;
                case 9997: // exit
                    button.Initialize(this, new button_exportRecord(this, button)); break;
                case 9800: // reset all filters
                    button.Initialize(this, new button_resetAllFilters(this, button)); break;
                case -1: break;

                default:
                    button.Initialize(this, button_alwaysValid); break;
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

    protected override void OnDestroy()
    {
        //scr_System_CampaignManager.current.CurrentTargetEX = null;
        base.OnDestroy();

    }
    public override void ValidateAll()
    {
        base.ValidateAll();
        Recalculate();
    }

    void Recalculate()
    {

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
                    scr_System_SceneManager.current.UnloadLastCanvasFromScene();
                    break;
                default: break;
            }
        }
        ValidateAll();
    }

    public enum FilterMode
    {
        Only,
        Include
    }

    List<scr_actionHolder> apTracker = new List<scr_actionHolder>();
    List<scr_videoEdit_message_record> boxTracker = new List<scr_videoEdit_message_record>();
    // save and wipe change in current copy comp
    void SaveRecording()
    {
        // save actor name override
        foreach (var actorrec in actors)
        {
            actorrec.Value.innerActorRecord.firstNameOverwrite = actorrec.Value.overwriteName.self_inputfield.text;
        }

        foreach(var msg in comp.collect)
        {
            // cleanup AP
            var existingAPs = msg.Value.apRecords;
            
            for(int i = existingAPs.Count - 1; i >= 0; i--)
            {
                if (existingAPs[i].Disable)
                {
                    existingAPs.RemoveAt(i);
                    continue;
                }
            }
        }

        foreach(var registeredBox in this.boxTracker)
        {
            if (registeredBox.Activate) continue;
            if (registeredBox.source != null) registeredBox.source.PurgeEntry(registeredBox.rec);
            if (registeredBox.ap != null && registeredBox.ap.mcol != null) registeredBox.ap.mcol.PurgeEntry(registeredBox.rec);
        }
    }

    public class button_saveRecord : ButtonValidator, I_ButtonClickable
    {
        new canvas_videoEdit parent;
        scr_SelectableText text;
        public button_saveRecord(canvas_videoEdit parent, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.text = text;
        }

        public override bool IsButtonValid()
        {
            return true;

        }
        public void OnClickButton()
        {
            parent.SaveRecording();

            // original item load new recording and discard
            parent.originalItem.Comp_Records.LoadRecords(parent.comp);
            parent.originalItem.nameOverwrite = parent.titleText.self_inputfield.text;

            scr_System_CampaignManager.current.AddLog(-1, $"successfully saved recording {parent.originalItem.DisplayName}");

            parent.Notify(9999);
        }
    }
    public class button_toggleVisibility : ButtonValidator, I_ButtonClickable
    {
        new canvas_videoEdit parent;
        scr_SelectableText text;
        scr_videoEdit_message_record rec;
        public button_toggleVisibility(canvas_videoEdit parent, scr_SelectableText text, scr_videoEdit_message_record rec) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.rec = rec;
        }
        public override bool IsButtonValid()
        {
            if (rec == null) return false;

            text.SetText(rec.Activate ? " O " : " X ");
            
            return true;
        }
        public void OnClickButton()
        {
            rec.Activate = !rec.Activate;
        }
    }
    public class button_toggleAP : ButtonValidator, I_ButtonClickable
    {
        new canvas_videoEdit parent;
        scr_SelectableText text;
        scr_actionHolder rec;
        public button_toggleAP(canvas_videoEdit parent, scr_SelectableText text, scr_actionHolder rec) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.rec = rec;
        }
        public override bool IsButtonValid()
        {
            if (rec == null) return false;

            text.SetText(rec.Activate ? " O " : " X ");

            return true;
        }
        public void OnClickButton()
        {
            rec.Activate = !rec.Activate;
        }
    }

    public class button_resetAllFilters : ButtonValidator, I_ButtonClickable
    {
        new canvas_videoEdit parent;
        scr_SelectableText text;
        public button_resetAllFilters(canvas_videoEdit parent, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.text = text;
        }
        public override bool IsButtonValid()
        {
            // reactivate all
            foreach (var box in parent.boxTracker) if (!box.Activate) return true;
            foreach (var box in parent.apTracker) if (!box.Activate) return true;
            return false;
        }

        public void OnClickButton()
        {
            // reactivate all
            foreach(var box in parent.boxTracker) box.Activate = true;
            foreach (var box in parent.apTracker) box.Activate = true;

        }
    }


    public class button_exportRecord : ButtonValidator, I_ButtonClickable
    {
        new canvas_videoEdit parent;
        scr_SelectableText text;
        public button_exportRecord(canvas_videoEdit parent, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.text = text;
        }

        public override bool IsButtonValid()
        {
            tooltip =  $"{errorTTIP}{(errorTTIP.Length > 0 ? "\n\n":"")}";
            if (parent.originalItem == null || parent.originalItem.Comp_Records == null || parent.originalItem.Comp_Records.storeItemID == "")
            {
                tooltip += $"cannot create new tape, item does not exist {parent.originalItem == null || parent.originalItem.Comp_Records == null} or cannot be stored into other recordings {parent.originalItem == null || parent.originalItem.Comp_Records == null || parent.originalItem.Comp_Records.storeItemID == ""}";
                return false;
            }
            var itemID = parent.originalItem.Comp_Records.storeItemID;
            var reqItem = Masterlist_Items.GetByID(itemID);
            if (reqItem == null)
            {
                tooltip += $"cannot find target item {itemID}";
                return false;
            }
            if (parent.factionOwner == null || parent.factionOwner.Inventory == null || parent.factionOwner.Inventory.GetItemCount(itemID) < 1)
            {
                tooltip += $"cannot create new tape, faction inventory null {parent.factionOwner == null || parent.factionOwner.Inventory == null} or does not have {reqItem.DisplayName}";
                return false;
            }

            var newname = (parent.titleText.self_inputfield.text == parent.originalItem.DisplayName ?
                    $"{parent.titleText.self_inputfield.text}_Copy" : parent.titleText.self_inputfield.text);

            tooltip += $"will consume 1 instance of {reqItem.DisplayName} for export\nwill create item {newname}";

            return true;

        }

        string errorTTIP = "";

        public void OnClickButton()
        {
            parent.SaveRecording();

            var itemID = parent.originalItem.Comp_Records.storeItemID;
            // make new item with this comp
            var consumeItem = parent.factionOwner.Inventory.RemoveItem(itemID, 1);
            if (consumeItem.Count < 1)
            {
                errorTTIP = $"failed to remove item {itemID}";
                this.state = ButtonValidator_States.Conflict;
                return;
            }
            else
            {
                foreach (var item in consumeItem) scr_System_CampaignManager.current.Unregister(item);
            }

            var createItem = WorldManager.Instantiate(parent.originalItem.BaseID);
            if (createItem == null || createItem.Comp_Records == null)
            {
                // 
                errorTTIP = $"failed to create recording {parent.originalItem.BaseID}";
                this.state = ButtonValidator_States.Conflict;
                return;
            }
            else
            {
                parent.comp.parentRecordingRef = parent.originalItem.RefID;
                // store into createitem
                createItem.Comp_Records.LoadRecords(parent.comp);
                createItem.nameOverwrite = parent.titleText.self_inputfield.text == parent.originalItem.DisplayName ? 
                    $"{parent.titleText.self_inputfield.text}_Copy" : parent.titleText.self_inputfield.text;
                parent.factionOwner.Inventory.AddItem(createItem);
                this.state = ButtonValidator_States.Valid;
                errorTTIP = $"successfully created new item!";
                scr_System_CampaignManager.current.AddLog(-1, $"successfully created new item {createItem.DisplayName} to {parent.factionOwner.FactionDisplayName}");
                parent.Notify(9999);
            }
        }
    }

    public class button_filter_ap : ButtonValidator, I_ButtonClickable
    {
        new canvas_videoEdit parent;
        filter_actor sourceFilter;
        scr_SelectableText text;
        FilterMode mode;
        bool activated = false;
        List<ActionPackageRecords> targetList = null;
        public button_filter_ap(canvas_videoEdit parent, scr_SelectableText text, filter_actor sourceFilter, FilterMode mode) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.sourceFilter = sourceFilter;
            this.mode = mode;

            switch (mode)
            {
                case FilterMode.Only:
                    targetList =  sourceFilter.ap_related_only ;
                    break;

                case FilterMode.Include:
                    targetList =  sourceFilter.ap_related_include ;
                    break;
            }
        }

        public override bool IsButtonValid()
        {
            if (targetList != null && targetList.Count > 0)
            {
                text.useDisabledColorWhenUntoggled = false;

                bool allInactive = true;
                foreach (var i in targetList)
                {
                    allInactive = allInactive && i.Disable;
                }
                tooltip = $"AP {targetList.Count}";
                activated = allInactive;
                text.Toggle(true, allInactive);
                return true;
            }
            else
            {
                text.useDisabledColorWhenUntoggled = true;

                tooltip = $"AP 0";
                text.Toggle(true, false);
                return false;
            }

        }
        public void OnClickButton()
        {
            activated = !activated;
            foreach (var i in targetList)
            {
                i.Disable = activated;
            }
        }
    }


    public class button_filter_msg : ButtonValidator, I_ButtonClickable
    {
        new canvas_videoEdit parent;
        filter_actor sourceFilter;
        scr_SelectableText text;
        FilterMode mode;
        bool activated = false;
        List<scr_videoEdit_message_record> list_include = null;
        List<scr_videoEdit_message_record> list_only = null;
        public button_filter_msg(canvas_videoEdit parent, scr_SelectableText text, filter_actor sourceFilter, FilterMode mode) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.sourceFilter = sourceFilter;
            this.mode = mode;

            list_only = sourceFilter.msg_related_only;
            list_include = sourceFilter.msg_related_include;
        }

        public override bool IsButtonValid()
        {
            if (mode == FilterMode.Include && ((list_include != null && list_include.Count > 0) || (list_only != null && list_only.Count > 0)))
            {
                text.useDisabledColorWhenUntoggled = false;

                bool allInactive = true;
                int targetCount = 0;

                if (list_include != null)
                {
                    targetCount += list_include.Count;
                    foreach (var i in list_include) allInactive = allInactive && !i.Activate;
                }
                if (list_only != null)
                {
                    targetCount += list_only.Count;
                    foreach (var i in list_only) allInactive = allInactive && !i.Activate;
                }
                tooltip = $"MSG {targetCount}";
                activated = allInactive;
                text.Toggle(true, allInactive);
                return true;
            }
            else if (mode == FilterMode.Only && list_only != null && list_only.Count > 0)
            {
                text.useDisabledColorWhenUntoggled = false;
                bool allInactive = true;
                foreach (var i in list_only) allInactive = allInactive && !i.Activate;
                tooltip = $"MSG {list_only.Count}";
                activated = allInactive;
                text.Toggle(true, allInactive);
                return true;
            }
            else
            {
                text.useDisabledColorWhenUntoggled = true;

                tooltip = $"MSG 0";
                text.Toggle(true, false);
                return false;
            }
            
        }
        public void OnClickButton()
        {
            if (mode == FilterMode.Include) foreach(var i in list_include) i.Activate = activated;
            foreach (var i in list_only) i.Activate = activated;
            activated = !activated;
        }
    }
}

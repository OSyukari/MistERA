using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public class canvas_videoEdit : scr_Menu
{
    public scr_inputFieldLink titleText;
    KojoRecording comp = null;

    RecordingEvaluatorInstance evalInstance;

    // -- evalInstance data --
    public scr_HoverableText names_actors_main; // print actors_main data
    public scr_HoverableText names_actors_supporting; // print actors_rivals data
    public scr_HoverableText actors_tags;   // print actors_main and actors_rivals's features names combined
    public scr_HoverableText content_tags;  // print active Goals.keys displayname
    public TMP_Dropdown validNames; // insert namingTemplate content as entry
    public scr_HoverableText score_req, score_current;
    public scr_HoverableText duration_req, duration_current;
    // -----------------------

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
            BuildEvaluatorSelectionButtons();
        }
        else
        {
            Debug.LogError("error loading itemcomp");
        }
        titleText.self_inputfield.text = instance.DisplayName;

        recalculate = true;
        ValidateAll();
    }

    // TODO: assign a real button prefab + a parent RectTransform in the Inspector once the
    // evaluator-selection UI is actually designed. Using scr_SelectableText directly as a
    // minimal placeholder - same component every other button in this canvas already uses.
    public scr_SelectableText prefab_selectEvaluator;
    public RectTransform RectList_Evaluators;

    void BuildEvaluatorSelectionButtons()
    {
        if (prefab_selectEvaluator == null || RectList_Evaluators == null) return;

        foreach (var evaluator in scr_System_Serializer.current.MasterList.ErAV.recordingEvaluators)
        {
            var btn = Instantiate(prefab_selectEvaluator);
            btn.transform.SetParent(RectList_Evaluators, false);

            RegisterButton(btn, new button_selectEvaluator(this, btn, evaluator));
            btn.SetText(evaluator.DisplayName);
        }
    }

    public filter_actor prefab_actor;
    Dictionary<ActorRecord, filter_actor> actors = new Dictionary<ActorRecord, filter_actor>();

    public RectTransform RectList_Options;


    public void OnTitleChange()
    {
        if (this.validatorsByID.TryGetValue(9997, out var btn))
        {
            btn.IsButtonValid();
        }
    }

    /// <summary>
    /// index 0 is always the "keep current name" fallback and does nothing.
    /// TODO: apply the selected namingTemplate entry (validNames.options[index]) to titleText.
    /// </summary>
    public void OnValidNameChange(int index)
    {
        if (index == 0) return;
        if (evalInstance == null || validNames == null) return;
        if (index < 0 || index >= validNames.options.Count) return;

        var evaluatorName = evalInstance.Evaluator != null ? evalInstance.Evaluator.DisplayName : "";
        var selectedName = validNames.options[index].text;

        titleText.self_inputfield.text = LocalizeDictionary.QueryThenParse("ui_videoEdit_titleWithEvaluator")
            .Replace("$evaluator$", evaluatorName)
            .Replace("$name$", selectedName);
        OnTitleChange();
    }

    void LoadRecords(KojoRecording comp)
    {
        if (comp == null) return;
        comp.Initialize();

        // eval defaults to whichever evaluator this recording was last saved with (comp.evaluatorID),
        // if any, and otherwise stays null until the player picks one via BuildEvaluatorSelectionButtons -
        // the draft-pick + Actions dictionary still get built regardless.
        evalInstance = new RecordingEvaluatorInstance(comp, false);

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

            RegisterButton(box.btn_setActorMain, new button_setActorStatus(this, box.btn_setActorMain, box, ActorStatus.Main));
            RegisterButton(box.btn_setActorSupport, new button_setActorStatus(this, box.btn_setActorSupport, box, ActorStatus.Support));
            RegisterButton(box.btn_setActorNone, new button_setActorStatus(this, box.btn_setActorNone, box, ActorStatus.None));

            actors.Add(actorsetting, box);
        }

        var disabledColor = scr_System_CentralControl.current.DisplaySetting.TextColor_disabled.Color;

        foreach(var msgcol in comp.collect)
        {
            // parse each
            // no need to store any other info
            var box = Instantiate(actionHolder);
            box.selfRect.SetParent(RectList_Messages, false);
            box.mcol = msgcol.Value;
            RegisterButton(box.toggleVisibility, new button_toggleAP(this, box.toggleVisibility, box));
            msgcol.Value.FlushCollectedLogsIntoUI(msgcol.Key, this, replaceStrings, box);
            apTracker.Add(box);
            mcolDict.Add(msgcol.Value, box);

            box.time.SetText($"time: {msgcol.Value.Duration}");
            box.time.SetColor(disabledColor);
            box.score.SetText($"score: {0}");
            box.score.SetColor(disabledColor);

        }

        BuildNonActorMessages();
    }

    /// <summary>
    /// Per apTracker box, looks only at box.mcol.apRecords (never messages). An AP counts as
    /// "from a non-actor" when none of its Doers/Receivers/Master match any comp.ActorSettings
    /// entry, reusing ActionPackageRecords.hasActor - the same predicate RegisterAPRecord2 already
    /// uses to build the per-actor ap_related_only/include lists, so "known actor" means the same
    /// thing here as it does everywhere else in this canvas.
    ///
    /// nonActorAP_Only: box has at least one AP, and every AP on it is from a non-actor. A box with
    /// zero APs is skipped entirely (per spec - "if there is no ap, then it does not add").
    /// nonActorAP_Include: box has at least one AP that is from a non-actor (other APs on the same
    /// box may still be from known actors). Superset of nonActorAP_Only by construction.
    /// </summary>
    public List<scr_actionHolder> nonActorAP_Only = new List<scr_actionHolder>();
    public List<scr_actionHolder> nonActorAP_Include = new List<scr_actionHolder>();
    void BuildNonActorMessages()
    {
        nonActorAP_Only.Clear();
        nonActorAP_Include.Clear();

        foreach (var box in apTracker)
        {
            if (box.mcol == null || box.mcol.apRecords == null || box.mcol.apRecords.Count < 1) continue;

            bool anyNonActorAP = false;
            bool allNonActorAP = true;
            foreach (var ap in box.mcol.apRecords)
            {
                bool isNonActorAP = !comp.ActorSettings.Any(known => ap.hasActor(known));
                anyNonActorAP = anyNonActorAP || isNonActorAP;
                allNonActorAP = allNonActorAP && isNonActorAP;
            }

            if (anyNonActorAP) nonActorAP_Include.Add(box);
            if (allNonActorAP) nonActorAP_Only.Add(box);
        }
    }

    Dictionary<MessageCollect, scr_actionHolder> mcolDict = new Dictionary<MessageCollect, scr_actionHolder>();

    public scr_actionHolder actionHolder;
    public void RegisterAPRecord2(ActionPackageRecords sourceAP, DateTime source_timestamp, MessageCollect source, scr_actionHolder rect)
    {
        // actor ap log
        //sourceAP.RecordBox = rect.titles;

        //var box = sourceAP.mcol != null && sourceAP.mcol.hasMessageChecks ? Instantiate(actionHolder) : null;
        var record = evalInstance.BuildAP(sourceAP, source_timestamp, source);
        record.UI = rect;
        /*
        if (box != null)
        {
            var record = evalInstance.BuildAP(sourceAP, source_timestamp, source);
            //box.holder = record;
            //box.selfRect.SetParent(RectList_Messages, false);
            record.UI = rect;
            //RegisterButton(box.toggleVisibility, new button_toggleAP(this, box.toggleVisibility, box));
            //sourceAP.RecordBox = box;
        }
        */

        foreach (var kvp in actors)
        {
            if (sourceAP != null && sourceAP.hasActor(kvp.Key))
            {
                if (sourceAP.isSingleActor() && !kvp.Value.ap_related_only.Contains(rect)) kvp.Value.ap_related_only.Add(rect);
                else if (!kvp.Value.ap_related_include.Contains(rect)) kvp.Value.ap_related_include.Add(rect);
            }
        }
    }

    public scr_actionHolder RegisterAPRecord(ActionPackageRecords sourceAP, DateTime source_timestamp, MessageCollect source, RectTransform rect)
    {
        // actor ap log
        var box = sourceAP.mcol != null && sourceAP.mcol.hasMessageChecks ? Instantiate(actionHolder) : null;

        if (box != null)
        {
            var record = evalInstance.BuildAP(sourceAP, source_timestamp, source);
            box.holder = record;

            box.selfRect.SetParent(RectList_Messages, false);
            //record.UI = box;

            RegisterButton(box.toggleVisibility, new button_toggleAP(this, box.toggleVisibility, box));

            //sourceAP.RecordBox = box;

            apTracker.Add(box);
        }


        foreach (var kvp in actors)
        {
            if (sourceAP != null && sourceAP.hasActor(kvp.Key))
            {
               // if (sourceAP.isSingleActor()) kvp.Value.ap_related_only.Add(sourceAP);
               // else kvp.Value.ap_related_include.Add(sourceAP);
            }
        }

        return box;
    }

    public void ParseEntry(I_Records record, MessageCollect parent, DateTime timestamp, Dictionary<string, string> replaceStrings, RectTransform parentRect)
    {
        if (record == null) return;


        if (parentRect != null)
        {
            PrintEntry_1(record, replaceStrings, null);
            // Export(box, parent, record, timestamp, sourceAP);
        }
        else
        {
            /*
            scr_videoEdit_message_record box = null;

            box = Instantiate(prefab_message_holder);
            parentRect = sourceAP == null || sourceAP.RecordBox == null ? RectList_Messages : sourceAP.RecordBox.messageList;
            box.selfRect.SetParent(parentRect, false);

            PrintEntry_1(record, replaceStrings, parentRect, box.innerObject, sourceAP);
            Export(box, parent, record, timestamp, sourceAP);*/
        }
    }

    void Export2(scr_actionHolder box, I_Records record)
    {
        if (box != null)
        {
            foreach (var kvp in actors)
            {
                if (record.IsRelevantActor(kvp.Key))
                {
                    if (record.IsSingleActor && !kvp.Value.msg_related_only.Contains(box)) kvp.Value.msg_related_only.Add(box);
                    else if (!kvp.Value.msg_related_include.Contains(box)) kvp.Value.msg_related_include.Add(box);
                }
            }
        }
    }


    public RectTransform prefab_LogEntry;
    public scr_HoverableText prefab_LogLine;
    public scr_menu_question prefab_question;
    public scr_menu_inputField prefab_inputField;

    public RectTransform RectList_Messages;
    public scr_videoEdit_message_record prefab_message_holder;

    public void PrintEntry_1(I_Records record, Dictionary<string, string> replaceStrings, scr_actionHolder textRect)
    {
        Export2(textRect, record);
        if (record is DescriptionCollector)
        {
            var desc = record as DescriptionCollector;
            if (desc == null) return;

            var log = new Message_Text(desc, true, false, replaceStrings);
            if (desc.message_excludeRelated != "" && desc.message_excludeRelated != desc.message) log.AddMessage(desc.message_excludeRelated, true);
            PrintEntry_2(log, textRect);
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
                PrintEntry_2(log, textRect);
            }
            foreach (var n in desc.collect.nexts)
            {
                //var box2 = Instantiate(prefab_message_holder);
                //box2.selfRect.SetParent(parentRect, false);
                var log = new Message_Text(n, false, desc.tooltip, replaceStrings);
                PrintEntry_2(log, textRect);
                //PrintEntry_2(log, sourceAP, box2.innerObject);
            }
        }
        else if (record is QuestionBoxCollector)
        {
            var desc = record as QuestionBoxCollector;
            // RefID -1 no display
            // RefID -2 
            if (desc == null) return;

            MessageLog log = new Message_Question_Record(desc, replaceStrings);
            PrintEntry_2(log, textRect);
        }
        else
        {
            Debug.LogError("unknown record type");
        }
    }
    void PrintEntry_2( MessageLog current, scr_actionHolder parent)
    {
        if (current is Message_Text)
        {
            var txt = current as Message_Text;
            RectTransform msgbox = Instantiate(prefab_LogEntry);
            //if (current.PortraitRef == -1000) msgbox = Instantiate(prefab_SeparationEntry);
            txt.animateAllOverride = true;
            msgbox.SetParent(parent.messageList, false);
            (current as Message_Text).Draw(true, msgbox.GetComponent<scr_MessageLogBox>(), this.prefab_LogLine);
            // if (waiting) Debug.Log("waiting!");
        }
        else if (current is Message_Question)
        {
            var question = Instantiate(prefab_question);
            question.transform.SetParent(parent.messageList, false);
            (current as Message_Question).Draw(true, this.m_Canvas, question);
        }
        else if (current is Message_InputField)
        {
            var question = Instantiate(prefab_inputField);
            question.transform.SetParent(parent.messageList, false);
            (current as Message_InputField).Draw(true, this.m_Canvas, question);
        }
        else if (current is Message_Question_Record)
        {
            var question = Instantiate(prefab_question);
            question.transform.SetParent(parent.messageList, false);
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
                case 9801: // bulk delete/restore messages that include a non-actor (含非演员)
                    button.Initialize(this, new button_filter_nonActor(this, button, FilterMode.Include)); break;
                case 9802: // bulk delete/restore messages that are exclusively non-actor (仅含非演员)
                    button.Initialize(this, new button_filter_nonActor(this, button, FilterMode.Only)); break;
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

        if (recalculate)
        {
            this.evalInstance.InactiveBlocks.Clear();
            foreach (var box in apTracker) if (!box.Activate) this.evalInstance.InactiveBlocks.Add(box.mcol);
            this.evalInstance.ValidateAPs(true);
            UpdateMessageCollectScores();
        }
        base.ValidateAll();
        EvalUpdate();
    }


    const string EvalUpdate_Empty = " - ";

    /// <summary>
    /// Refreshes every UI element bound to evalInstance data. Called on every ValidateAll.
    /// Any field whose backing data isn't available (no recording loaded, no evaluator
    /// selected yet, requirement not set, etc.) shows " - " instead.
    /// </summary>
    void EvalUpdate()
    {
        var eval = evalInstance != null ? evalInstance.Evaluator : null;

        if (names_actors_main != null)
        {
            var namesText = evalInstance != null && evalInstance.actors_main.actors.Count > 0
                ? evalInstance.actors_main.GetActorsName()
                : EvalUpdate_Empty;
            names_actors_main.SetText(LocalizeDictionary.QueryThenParse("ui_videoEdit_actorsMain").Replace("$names$", namesText));
        }

        if (names_actors_supporting != null)
        {
            var namesText = evalInstance != null && evalInstance.actors_rivals.actors.Count > 0
                ? evalInstance.actors_rivals.GetActorsName()
                : EvalUpdate_Empty;
            names_actors_supporting.SetText(LocalizeDictionary.QueryThenParse("ui_videoEdit_actorsSupporting").Replace("$names$", namesText));
        }

        if (actors_tags != null)
        {
            List<string> tagNames = new List<string>();
            if (evalInstance != null)
            {
                foreach (var f in evalInstance.actors_main.features) tagNames.Add(LocalizeDictionary.QueryThenParse(f.featureID, f.featureID));
                foreach (var f in evalInstance.actors_rivals.features) tagNames.Add(LocalizeDictionary.QueryThenParse(f.featureID, f.featureID));
            }
            var tagsText = tagNames.Count > 0 ? string.Join("、", tagNames) : EvalUpdate_Empty;
            actors_tags.SetText(LocalizeDictionary.QueryThenParse("ui_videoEdit_actorsTags").Replace("$tags$", tagsText));
        }

        if (content_tags != null)
        {
            var names = evalInstance != null ? evalInstance.ActiveGoalDisplayNames : null;
            var tagsText = names != null && names.Count > 0 ? string.Join("、", names) : EvalUpdate_Empty;
            content_tags.SetText(LocalizeDictionary.QueryThenParse("ui_videoEdit_contentTags").Replace("$tags$", tagsText));
        }

        if (validNames != null)
        {
            validNames.ClearOptions();

            // index 0 is always the no-op "keep current name" fallback, regardless of evaluator state
            List<string> options = new List<string> { LocalizeDictionary.QueryThenParse("ui_videoEdit_keepCurrentName") };

            if (eval != null && eval.namingTemplate != null && eval.namingTemplate.Count > 0)
            {
                foreach (var key in eval.namingTemplate)
                {
                    var basestr = LocalizeDictionary.QueryThenParse(key, key)
                        .Replace("$actor$", evalInstance.actors_main.RandomName)
                        .Replace("$rivals$", evalInstance.actors_rivals.RandomName)
                        .Replace("$location$", RecordingEvaluatorInstance.MostCommonLocation(evalInstance.Actions.Values));
                    options.Add(basestr);
                }
            }

            validNames.AddOptions(options);
            validNames.SetValueWithoutNotify(0);
        }

        string scoreReqText = eval != null && eval.minimumScoreRequirement > 0f ? eval.minimumScoreRequirement.ToString("0") : EvalUpdate_Empty;
        if (score_req != null)
        {
            score_req.SetText(LocalizeDictionary.QueryThenParse("ui_videoEdit_scoreReq").Replace("$score$", scoreReqText));
        }
        if (score_current != null)
        {
            if (evalInstance == null)
            {
                score_current.SetText(EvalUpdate_Empty);
                score_current.SetExternalTooltip("");
            }
            else if (eval != null)
            {
                score_current.SetText(LocalizeDictionary.QueryThenParse("ui_videoEdit_score_withEval")
                    .Replace("$base$", evalInstance.scoreBase.ToString("0.#"))
                    .Replace("$mult$", evalInstance.scoreMult.ToString("0.##"))
                    .Replace("$total$", evalInstance.totalScore.ToString("0")));
                score_current.SetExternalTooltip(evalInstance.ScoreDebug);
            }
            else
            {
                score_current.SetText(LocalizeDictionary.QueryThenParse("ui_videoEdit_score_noEval")
                    .Replace("$total$", evalInstance.totalScore.ToString("0")));
                score_current.SetExternalTooltip(evalInstance.ScoreDebug);
            }
        }

        if (duration_req != null)
        {
            string durText = EvalUpdate_Empty;
            if (eval != null && eval.durationRequirement != null)
            {
                var dur = eval.durationRequirement;
                if (dur.minMinutes > 0 && dur.maxMinutes > 0) durText = $"{dur.minMinutes}-{dur.maxMinutes}";
                else if (dur.minMinutes > 0) durText = $">= {dur.minMinutes}";
                else if (dur.maxMinutes > 0) durText = $"<= {dur.maxMinutes}";
            }
            duration_req.SetText(LocalizeDictionary.QueryThenParse("ui_videoEdit_durationReq").Replace("$req$", durText));
        }
        if (duration_current != null)
        {
            string totalText;
            if (comp == null) totalText = EvalUpdate_Empty;
            else
            {
                var total = comp.TotalPlayTime;
                var effective = EffectiveDuration();
                totalText = total == effective ? total.ToString("0") : $"{total.ToString("0")} -> {effective.ToString("0")}";
            }
            duration_current.SetText(LocalizeDictionary.QueryThenParse("ui_videoEdit_durationCurrent").Replace("$total$", totalText));
        }
    }

    void UpdateMessageCollectScores()
    {
        //if (evalInstance == null) return;

        foreach (var kvp in mcolDict)
        {
            float sum = 0f;
            if (evalInstance != null && kvp.Key.apRecords != null)
            {
                foreach (var ap in kvp.Key.apRecords)
                {
                    if (evalInstance.Actions.TryGetValue(ap, out var holder)) sum += holder.PotentialScore;
                }
            }

            float penalty = 0f;
            evalInstance?.BlockDurationPenalty.TryGetValue(kvp.Key, out penalty);
            string penaltyText = penalty == 0f ? "" : penalty.ToString("+0;-0;0");

            kvp.Value.score.SetText($"score: {sum:0}{penaltyText}");
        }
    }

    int EffectiveDuration()
    {
        if (comp == null) return 0;
        int duration = 0;
        foreach(var i in this.apTracker)
        {
            if (!i.Activate) continue;
            duration += i.mcol.Duration;
        }
        return duration;
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

    public enum ActorStatus
    {
        Main,
        Support,
        None
    }

    List<scr_actionHolder> apTracker = new List<scr_actionHolder>();
    // save and wipe change in current copy comp
    void SaveRecording()
    {
        if (string.IsNullOrEmpty(comp.parentRecordingID)) comp.parentRecordingID = $"{DateTime.Now.Ticks}";

        comp.value = evalInstance != null && evalInstance.Evaluator != null
            ? (int?)Math.Round(evalInstance.Evaluator.scoreToValueRatio * evalInstance.totalScore)
            : null;
        if (evalInstance != null && evalInstance.Evaluator != null) comp.evaluatorID = evalInstance.Evaluator.id;

        // save actor name override
        foreach (var actorrec in actors)
        {
            actorrec.Value.innerActorRecord.firstNameOverwrite = actorrec.Value.overwriteName.self_inputfield.text;
        }

        foreach(var kvp in mcolDict) { 
        }

        foreach(var key in comp.collect.Keys.ToList())
        {
            if (comp.collect.TryGetValue(key, out var msg) && mcolDict.TryGetValue(msg, out var button) && button.Activate)
            {
                // keep it
            }
            else
            {
                comp.collect.Remove(key);
            }

        }
        comp.InvalidateCache();
        /*
        // cleanup AP
        var existingAPs = msg.Value.apRecords;

        for (int i = existingAPs.Count - 1; i >= 0; i--)
        {
            if (existingAPs[i].Disable)
            {
                existingAPs.RemoveAt(i);
                continue;
            }
        }
        foreach (var registeredBox in this.boxTracker)
        {
            if (registeredBox.Activate) continue;
            if (registeredBox.source != null) registeredBox.source.PurgeEntry(registeredBox.rec);
            if (registeredBox.ap != null && registeredBox.ap.mcol != null) registeredBox.ap.mcol.PurgeEntry(registeredBox.rec);
        }
        */
    }

    public bool recalculate = false;

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
            tooltip = "";
            if (parent.evalInstance != null && parent.evalInstance.disqualified)
            {
                tooltip += $"final item disqualified by evaluator, cannot save";
                return false;
            }
            return true;

        }
        public void OnClickButton()
        {
            parent.SaveRecording();

            // original item load new recording and discard
            parent.originalItem.Comp_Records.LoadRecords(parent.comp);
            parent.originalItem.InvalidateTagsCache();
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
            parent.recalculate = true;
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
            //rec.titles.gameObject.SetActive(!rec.titles.gameObject.activeInHierarchy);
            parent.recalculate = true;
        }
    }

    public class button_selectEvaluator : ButtonValidator, I_ButtonClickable
    {
        new canvas_videoEdit parent;
        scr_SelectableText text;
        RecordingEvaluator evaluator;
        public button_selectEvaluator(canvas_videoEdit parent, scr_SelectableText text, RecordingEvaluator evaluator) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.evaluator = evaluator;
        }
        public override bool IsButtonValid()
        {
            if (parent.evalInstance == null) return false;
            this.text.Toggle(true, parent.evalInstance.Evaluator == evaluator);
            return true;
        }
        public void OnClickButton()
        {
            parent.evalInstance.SetEvaluator(parent.evalInstance.Evaluator == evaluator ? null : evaluator);
            parent.recalculate = true;
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
            foreach (var box in parent.apTracker) if (!box.Activate) return true;
            return false;
        }

        public void OnClickButton()
        {
            // reactivate all
            foreach (var box in parent.apTracker) box.Activate = true;
            parent.recalculate = true;

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

            if (parent.evalInstance != null && parent.evalInstance.disqualified)
            {
                tooltip += $"final item disqualified by evaluator, cannot create";
                return false;
            }
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
                // store into createitem
                createItem.Comp_Records.LoadRecords(parent.comp);
                createItem.InvalidateTagsCache();
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
        List<scr_actionHolder> targetList = null;
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
                    allInactive = allInactive && !i.Activate;
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
            //activated = !activated;
            foreach (var i in targetList)
            {
                i.Activate = activated;
            }
            parent.recalculate = true;
        }
    }


    public class button_filter_msg : ButtonValidator, I_ButtonClickable
    {
        new canvas_videoEdit parent;
        filter_actor sourceFilter;
        scr_SelectableText text;
        FilterMode mode;
        bool activated = false;
        List<scr_actionHolder> list_include = null;
        List<scr_actionHolder> list_only = null;
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
            parent.recalculate = true;
        }
    }

    public class button_setActorStatus : ButtonValidator, I_ButtonClickable
    {
        new canvas_videoEdit parent;
        scr_SelectableText text;
        filter_actor sourceFilter;
        ActorStatus status;

        public button_setActorStatus(canvas_videoEdit parent, scr_SelectableText text, filter_actor sourceFilter, ActorStatus status) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.sourceFilter = sourceFilter;
            this.status = status;
        }

        ActorStatus CurrentStatus()
        {
            var actor = sourceFilter.innerActorRecord;
            if (parent.evalInstance.actors_main.actors.Contains(actor)) return ActorStatus.Main;
            if (parent.evalInstance.actors_rivals.actors.Contains(actor)) return ActorStatus.Support;
            return ActorStatus.None;
        }

        public override bool IsButtonValid()
        {
            if (parent.evalInstance == null || sourceFilter.innerActorRecord == null) return false;
            text.Toggle(true, CurrentStatus() == status);
            return true;
        }

        public void OnClickButton()
        {
            var actor = sourceFilter.innerActorRecord;

            // persisted onto the ActorRecord itself (shared instance with comp.ActorSettings)
            // so the choice survives save/reload instead of being recomputed by the heuristic.
            actor.roleOverride = status == ActorStatus.Main ? ActorRole.Main
                : status == ActorStatus.Support ? ActorRole.Support
                : ActorRole.None;

            var main = new List<ActorRecord>(parent.evalInstance.actors_main.actors);
            var rivals = new List<ActorRecord>(parent.evalInstance.actors_rivals.actors);
            main.Remove(actor);
            rivals.Remove(actor);
            if (status == ActorStatus.Main) main.Add(actor);
            else if (status == ActorStatus.Support) rivals.Add(actor);

            parent.evalInstance.SetActors(main, rivals);
            parent.recalculate = true;
        }
    }

    /// <summary>
    /// Bulk delete/restore for messages that touch a non-actor (see nonActorMessages_Only/Include,
    /// BuildNonActorMessages). Same toggle-all-vs-none behavior as button_filter_ap; mode picks which
    /// of the two lists this instance drives - Only for "exclusively non-actor", Include for
    /// "touches a non-actor at all".
    /// </summary>
    public class button_filter_nonActor : ButtonValidator, I_ButtonClickable
    {
        new canvas_videoEdit parent;
        scr_SelectableText text;
        FilterMode mode;
        bool activated = false;
        List<scr_actionHolder> targetList = null;
        public button_filter_nonActor(canvas_videoEdit parent, scr_SelectableText text, FilterMode mode) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.mode = mode;

            targetList = mode == FilterMode.Only ? parent.nonActorAP_Only : parent.nonActorAP_Include;
        }

        public override bool IsButtonValid()
        {
            if (targetList != null && targetList.Count > 0)
            {
                text.useDisabledColorWhenUntoggled = false;

                bool allInactive = true;
                foreach (var i in targetList) allInactive = allInactive && !i.Activate;

                tooltip = $"MSG {targetList.Count}";
                activated = allInactive;
                text.Toggle(true, allInactive);
                return true;
            }
            else
            {
                text.useDisabledColorWhenUntoggled = true;

                tooltip = "MSG 0";
                text.Toggle(true, false);
                return false;
            }
        }
        public void OnClickButton()
        {
            foreach (var i in targetList) i.Activate = activated;
            parent.recalculate = true;
        }
    }
}

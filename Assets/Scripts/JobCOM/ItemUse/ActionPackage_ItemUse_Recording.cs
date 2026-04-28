using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;

public class ActionPackage_ItemUse_Recording : ActionPackage_ItemUse
{
    [JsonIgnore]
    public override List<string> ComTags
    {
        get
        {
            return base.ComTags;
        }
    }

    ItemComponent_Records _comp = null;

    [JsonIgnore]
    public ItemComponent_Records Comp
    {
        get
        {
            if (this.ItemInstance == null) return null;
            if (_comp == null)
            {
                _comp = ItemInstance.GetComp("ItemComponent_Records") as ItemComponent_Records;
            }
            return _comp;
        }
    }

    /// <summary>
    /// Return COM name. For AP description, go for DescriptionText()
    /// </summary>
    [JsonIgnore]
    public override string DisplayName
    {
        get
        {
            if (nameOverwrite != "") return nameOverwrite;
            if (targetCOM == null) return $" - ({(ItemInstance == null?"EMPTY": ItemInstance.DisplayName)})";
            var sourcekey = $"{(COMVariantID >= 0 ? targetCOM.variants[COMVariantID].displayName : targetCOM.displayName)}_EXTEND";
            return LocalizeDictionary.QueryThenParse(sourcekey).Replace("$name$", ItemInstance.DisplayName);
        }
    }

    public override bool Tick(ref List<int> actorList, int tickDuration = 1)
    {
        return base.Tick(ref actorList, tickDuration);
        //return true;
    }

    [JsonProperty] protected int elapsedTime = 0;

    protected override void PackageTick(MessageCollect m = null)
    {
        // for each minute, load interrupt
        base.PackageTick(m);

        if (Comp == null) return;
        if (ItemInstance == null) return;
        if (scr_System_CampaignManager.current.FindItemInstanceByID(itemRefID) != ItemInstance) return;
        if (Comp.Records == null) return;

    }

    public override List<ActionPackageOptions> LaunchOptions()
    {
        //Debug.LogError("LaunchOptions!");
        if (Comp == null || Comp.Records == null)
        {
            Debug.LogError($"error comp null?{Comp == null} record null? {(Comp == null || Comp.Records == null)}");
            return null;
        }
        if (Ticked)    // already launched, forbid further changes
        {
            //Debug.LogError($"error AP ticked, skipping launch options");
            return null;
        }
        List<ActionPackageOptions> options = new List<ActionPackageOptions>();
        List<string> debug = new List<string>();
        /*
        foreach(var actor_kvp in Comp.records.MessageCountByActor)
        {
            var c = scr_System_CampaignManager.current.FindInstanceByID(actor_kvp.Key);
            if (c == null) continue;
            options.Add(new ActionPackageOptions($"Set viewer {c.FirstName} ({actor_kvp.Value})", 
                new Action(() =>
                {
                    SetRecordingViewer(c);
                    LaunchOptionsChecked = true;
                }
                )));
            debug.Add($"adding option [Set viewer {c.FirstName} ({actor_kvp.Value})]");
        }*/
        //Debug.LogError($"loading options: {debug.Count}\n{String.Join("\n", debug)}");
        if (options.Count < 1) return null;
        return options;
    }

    public ActionPackage_ItemUse_Recording()
    {

    }
    public override void LoadItem(Item_Instance instance)
    {
        base.LoadItem(instance);
        _comp = null;
        // load iteminstance comp
        if (Comp != null && Comp.Records != null)
        {
            this.duration = 1;
            beginRecording = false;
            endRecording = false;

            // preprocess movie instance
            Comp.Records.Initialize();
        }
        else
        {
            this.duration = 1;
            beginRecording = true;
            endRecording = true;
        }
    }
    public ActionPackage_ItemUse_Recording(Job job, COM targetCOM, Item_Instance instance, List<int> doer, List<int> receiver, int masterRef) : base(job, targetCOM, instance, doer, receiver, masterRef)
    {
        

    }
    [JsonIgnore]
    public override bool PackageRepeat
    {
        get
        {
            if (this.Comp == null || this.Comp.Records == null) return false;
            return !beginRecording || !endRecording || NextRecord != null ;
            // return !this.extraCOMTags.Contains("norepeat") && toggleRepeat;
        }
        set
        {
            toggleRepeat = value;
            this.tooltip = new List<string>();
        }
    }

    protected override bool PreEvaluate()
    {
        bool base_evaluate = base.PreEvaluate();
        if (!base_evaluate) return base_evaluate;

        if (Comp == null)
        {
            tooltip.Add("Item has no Record component, AP invalid");
            isValid = false;
            return isValid;
        }
        else if (Comp.Records == null)
        {
            tooltip.Add("Item Record has null records, AP invalid");
            isValid = false;
            return isValid;
        }
        else if (Comp.Records.TotalPlayTime < 1)
        {
            tooltip.Add("Item Record total playtime < 1, AP invalid");
            isValid = false;
            return isValid;
        }

        return isValid;
    }

    protected override bool Evaluate()
    {
        return base.Evaluate();
    }

    public override void GetSerializedAPData(LLMUtils.SerializedAP ap)
    {
        base.GetSerializedAPData(ap);
    }

    protected override void PackageBegin(MessageCollect m = null)
    {
        base.PackageBegin(m);
    }


    bool RefreshInternalState()
    {
        _nextrecord = null;
        return NextRecord != null;
    }

    [JsonIgnore] protected bool endRecording = false;
    [JsonIgnore] protected bool beginRecording = false;
    [JsonIgnore] protected bool ended = false;

    MessageCollect _nextrecord = null;
   // [JsonProperty] protected bool endReplay = false;
    [JsonIgnore] protected MessageCollect NextRecord
    {
        get
        {
            if (!beginRecording) return null;
            if (endRecording) return null;
            if (_nextrecord == null)
            {
                if (Comp != null && Comp.Records != null)
                {
                    _nextrecord = Comp.Records.GetKojoFrom(ref elapsedTime, null, out duration);
                }
                else _nextrecord = null;
            }
            return _nextrecord;
        }
    }

    protected ActorRecord CameraMan { get
        {
            if (Comp == null || Comp.Records == null) return null;
            return Comp.Records.cameraman;
        } }

    protected override void Execution(MessageCollect m = null)
    {
        if (m == null) m = job.m;
        //base.Execution(m);
        if (!beginRecording)
        {
            Character_Trainable eventSelf = null;
            if (this.isPlayerRelatedPackage) eventSelf = scr_System_CampaignManager.current.Player;
            else eventSelf = doer.Count > 0 ? doer[0] : null;

            if (eventSelf != null)
            {
                var eventstart = new EventInstance(eventSelf, "Recording_OnBeginPlay","");

                eventstart.AppendStrings.Add("itemName", new List<string>() { ItemInstance == null ? "NULL" : ItemInstance.DisplayName });
                eventstart.AppendStrings.Add("duration", new List<string>() { Comp == null ? "NULL" : $"{Comp.Records.TotalPlayTime}" });
                eventstart.AppendStrings.Add("actorCount", new List<string>() { Comp == null ? "NULL" : $"{Comp.Records.ActorCount}" });
                eventstart.AppendStrings.Add("actorNames", new List<string>() { Comp == null ? "NULL" : $"{String.Join(" ", Comp.Records.ActorInfo)}" });
                eventstart.AppendStrings.Add("recordType", new List<string>() { Comp == null ? "NULL" : $"undefined" });
                eventstart.AppendStrings.Add("cameraMan", new List<string>() { CameraMan == null ? "NULL" : CameraMan.Name });

                var startCallback = new List<Action>();
                var endCallback = new List<Action>();
                var failCallback = new List<Action>();
                var nameChangeCallback = new List<Action>();

                startCallback.Add(() =>
                {
                    beginRecording = true;
                    var message = new DescriptionCollector(LocalizeDictionary.QueryThenParse("ui_ActionPackage_ItemUse_Recording_BEGIN"));
                    scr_UpdateHandler.current.AppendMessageAfter(message, null, true);
                    RefreshInternalState();
                });
                eventstart.FunctionCalls.Add("startCallback", startCallback);

                endCallback.Add(() =>
                {
                    beginRecording = true;
                    endRecording = true;
                    var message = new DescriptionCollector(LocalizeDictionary.QueryThenParse("ui_ActionPackage_ItemUse_Recording_END"));
                    scr_UpdateHandler.current.AppendMessageAfter(message, null, true);
                    RefreshInternalState();
                });
                eventstart.FunctionCalls.Add("endCallback", endCallback);

                failCallback.Add(() =>
                {
                    beginRecording = true;
                    endRecording = true;
                    RefreshInternalState();
                });
                eventstart.FunctionCalls.Add("failCallback", failCallback);

                nameChangeCallback.Add(() =>
                {
                    ItemInstance.nameOverwrite = eventstart.CurrentInput;
                    eventstart.AppendStrings["itemName"] = new List<string>() { ItemInstance.nameOverwrite };
                   // RefreshInternalState();
                });
                eventstart.FunctionCalls.Add("nameChangeCallback", nameChangeCallback);

                scr_UpdateHandler.current.AddEventCallback(() => scr_UpdateHandler.current.EventHandler.StartEvent(eventstart, false));
            }
            else
            {
                beginRecording = true;
                var message = new DescriptionCollector(LocalizeDictionary.QueryThenParse("ui_ActionPackage_ItemUse_Recording_BEGIN"));
                if (m != null) m.AddMessage_After(message);
            }


        }
        else if (NextRecord != null)
        {
            // respond to each entry in records? skip it for now, just replay record

            // directly log into updatehandler, will not be recorded
            scr_UpdateHandler.current.NotifyJobDescriptions(NextRecord, false);

            //this.job.m.Merge(NextRecord, false);
            if (NextRecord.apRecords != null)
            {
                // Debug.Log($"Calling interrupt on {aprecords.Count} records with {this.Actors.Count} actors");

                foreach(var c in this.Actors)
                {
                    var kol = new KojoCollector(c, "Interrupt");
                    kol.isrecording = true;
                    foreach (var aprecord in NextRecord.apRecords)
                    {
                        var kol2 = c.Relationships.GetKojoMessage_APRecord(kol, aprecord);
                        if (kol2 != null)
                        {
                            //Debug.Log("interrupt success on records");
                            kol2.LoadRelevantActors(this.actorRefs);
                            kol2.ReplaceString("$self$", c.FirstName);
                            this.job.m.AddKojo(kol2);
                            break;
                        }
                    }

                }
                
            }
            // call interrupt for each entry
            /*
            if (MapUtility.CheckInterrupt(xx, i, selfTags) && xx.RefID != 0)
            {
                interrupted = true;
                ignoreList.AddRange(i.actorRefs);
            }
            */

        }


        if (RefreshInternalState())
        {
            // dont do anything
            //Debug.Log("RECORDING CONTINUES");
        }
        else if (!beginRecording)
        {
            beginRecording = true;
            // waiting, but only wait for 1 extra minute to prevent potental infinite looping
            this.duration = 1;
        }
        else if (!endRecording)
        {
            Debug.Log("ENDRECORDING");
            endRecording = true;
            //duration = 1;
            var message = new DescriptionCollector(LocalizeDictionary.QueryThenParse("ui_ActionPackage_ItemUse_Recording_END"));
            if (m != null) m.AddMessage_After(message);
            // Debug.LogError("endrecording");
            this.duration = 0;
        }

    }

    public override ActionPackage Copy()
    {
        ActionPackage_ItemUse_Recording copy = new ActionPackage_ItemUse_Recording(job, targetCOM, ItemInstance, DoerRefs, ReceiverRefs, masterRef);
        copy.SetVariantID(this.validVariant);
        copy.LoggedBegin = this.LoggedBegin;
        copy.duration = this.duration;
        return copy;
    }
}


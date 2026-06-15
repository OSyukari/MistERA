using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

public class ActionPackage_Talk : ActionPackage
{

    public ActionPackage_Talk() : base()
    {

    }
    public ActionPackage_Talk(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef) : base(job, targetCOM, doer, receiver, masterRef)
    {

    }

    Knowledge_Instance selectedTopicInstance = null;
    string selectedTopic = string.Empty;


    [JsonIgnore]
    public override string DisplayName
    {
        get
        {
            if (nameOverwrite != "") return nameOverwrite;
            if (targetCOM != null && selectedTopic != string.Empty && selectedTopic != "")
            {
                string baseid = COMVariantID >= 0 && COMVariantID < targetCOM.variants.Count ? targetCOM.variants[COMVariantID].displayName : targetCOM.displayName;
                nameOverwrite = LocalizeDictionary.QueryThenParse($"{baseid}_topic", LocalizeDictionary.QueryThenParse(baseid))
                    .Replace("$topic$", LocalizeDictionary.QueryThenParse(selectedTopic));
                return nameOverwrite;
            }
            else return base.DisplayName;
            // return $"{(nameOverwrite != "" ? nameOverwrite : targetCOM != null ? (COMVariantID >= 0 ? targetCOM.DisplayName(COMVariantID) : targetCOM.DisplayName()) : " - ")} {ItemInstance.DisplayName}";
        }
    }

    [JsonIgnore]
    public override bool AllowJoining
    {
        get
        {
            return false;
        }
    }
    public override int canJoinAP(Character_Trainable c, out List<int> doers, out List<int> receivers, out List<string> tooltips)
    {
        var tempPackage = this.Copy();

        base.canJoinAP(c, out doers, out receivers, out var ttps);

        tempPackage.ResetRequest(doers, receivers, this.masterRef);
        if (tempPackage.Validate())
        {
            tooltips = tempPackage.tooltip;
            return tempPackage.COMVariantID;
        }
        else
        {
            tooltips = tempPackage.tooltip;
            return -1;
        }
    }

    public override int canJoinAP(List<Character_Trainable> cs, out List<int> doers, out List<int> receivers, out List<string> tooltips)
    {
        var tempPackage = this.Copy();

        base.canJoinAP(cs, out doers, out receivers, out tooltips);

        tempPackage.ResetRequest(doers, receivers, this.masterRef);
        if (tempPackage.Validate()) return tempPackage.COMVariantID;
        else
        {
            tooltips = tempPackage.tooltip;
            return -1;
        }
    }

    public override ActionPackage Copy()
    {
        ActionPackage_Talk copy = new ActionPackage_Talk(job, targetCOM, DoerRefs, ReceiverRefs, masterRef); copy.SetVariantID(this.validVariant);
        copy.LoggedBegin = this.LoggedBegin;
        copy.duration = this.duration;
        copy.selectedTopic = this.selectedTopic;
        copy.CollectCopy(this);
        return copy;
    }

    protected override void PackageBegin(MessageCollect m = null)
    {

        // select topic
        Dictionary<Knowledge_Instance, int> weights = new Dictionary<Knowledge_Instance, int>();
        bool debug = scr_System_CampaignManager.current.DebugMode;
        foreach (var i in Actors)
        {
            if (i == null || i.Skills == null) continue;
            var topic = i.Skills.RandomTopicInstance(debug);
            if (topic == null) continue;
            int interest = 1;
            foreach (var j in Actors)
            {
                if (i == j) continue;
                interest += 1;
            }
            weights.Add(topic, interest);
        }
        selectedTopicInstance = weights.Count > 0 ? Utility.WeightedRandInDict(weights) : null;
        selectedTopic = selectedTopicInstance != null ? selectedTopicInstance.baseKnowledge.baseID : "";

        if (selectedTopic == "" && targetCOM.fallbackCOMID != "")
        {
            this.targetCOMID = targetCOM.fallbackCOMID;
            this.targetCOMCache = null;
            //this.targetCOM = scr_System_Serializer.current.MasterList.COMs.GetByID(targetCOM.fallbackCOMID);
        }
        else
        {
            keyReplaceDictionary.Add("$topic$", selectedTopic == "" ? "null" : LocalizeDictionary.QueryThenParse(selectedTopicInstance.baseKnowledge.baseID));
        }

        base.PackageBegin(m);
    }


    protected override void Execution(MessageCollect m = null)
    {
        base.Execution(m);

        if (executeSuccessful && selectedTopic != "" && selectedTopicInstance != null)
        {
            var expname = scr_System_Serializer.current.MasterList.Knowledges.GetByID(selectedTopic);
            if (expname != null)
            {
                foreach (var i in Actors)
                {
                    if (i.Skills == selectedTopicInstance.Owner) continue;
                    // base learning is 0.1;
                    i.Skills.AddKnowledgeScore(selectedTopic, 0.1);
                    if (m != null) m.exp.AddStats(i.RefID, expname.baseID, 0.1);
                    else
                    {
                        //Debug.Log($"failed to log {i.FirstName} {expname.baseID} {0.1}");
                    }
                }
            }
        }

    }

}

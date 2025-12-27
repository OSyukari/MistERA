using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;


[System.Serializable]
public class COM_Descriptions
{
    /// <summary>
    /// SerializeRefence beware of null values
    /// </summary>
    public List<Description_Entry> Entries = null;

    public string GetText(ref EvaluationPackage evp)
    {
        if (Entries == null) return "$DEFAULT$";

        List<string> list = new List<string>();

        //Debug.LogError("GetText FOR COM " + evp.Package.targetCOM.displayName);
        bool keepLooking = true;
        foreach (var entry in Entries)
        {
            if (entry == null)
            {
                Debug.LogError("comdescription entry null");
                continue;
            }
            keepLooking = entry.GetValidText(ref list, ref evp);
            if (!keepLooking) break;
        }

        return String.Join("\n", list);
    }
    public string GetText(ref ActionPackage ap)
    {
        if (Entries == null) return "$DEFAULT$";

        List<string> list = new List<string>();

        //Debug.LogError("GetText FOR COM " + evp.Package.targetCOM.displayName);
        bool keepLooking = true;
        foreach (var entry in Entries)
        {
            if (entry == null)
            {
                Debug.LogError("comdescription entry null");
                continue;
            }
            keepLooking = entry.GetValidText(ref list, ref ap);
            if (!keepLooking) break;
        }

        return String.Join("\n", list);
    }

    public class Description_Entry
    {
        public bool keepLooking = false;
        public List<COMDesc_Conditions> conditions = new List<COMDesc_Conditions>();
        // if textOptions exists, then 
        public List<string> texts = new List<string>();


        public List<Description_Entry> Entries = new List<Description_Entry>();

        public class COMDesc_Conditions
        {
            public Validator_RandChance validatorChance = null;
            public Validator_Job validateJob = null;
            public Validator_Chara validateChara = null;
            public Validator_Package validatePackage = null;

            public bool Validate(ref EvaluationPackage evp)
            {
                bool returnVal = true;
                if (validatorChance != null) returnVal = returnVal && validatorChance.Validate(ref evp);
                if (validateJob != null) returnVal = returnVal && validateJob.Validate(ref evp);
                if (validateChara != null) returnVal = returnVal && validateChara.Validate(ref evp);
                if (validatePackage != null) returnVal = returnVal && validatePackage.Validate(ref evp);
                return returnVal;
            }

            public class Validator_RandChance
            {

                public int percentChance = 100;
                public bool Validate(ref EvaluationPackage evp)
                {
                    if (percentChance == 100) return true;
                    return Utility.Dice(1, 100) <= percentChance;
                }
            }

            public class Validator_Chara
            {
                public string target = "";
                public string evpTag = "";
                public string statID = "";
                public LogicalOperand operand = LogicalOperand.none;
                public string value = "";

                public bool Validate(ref EvaluationPackage evp)
                {
                    if (target == "" && statID == "" && operand == LogicalOperand.none && value == "") return true;

                    Character_Trainable targ = (target == "receiver" ? evp.Receiver : (target == "doer" ? evp.Doer : null));
                    if (targ == null)
                    {
                        //Debug.LogError("CANNOT FIND TARGET");
                        return false;
                    }
                    return targ.CompareStatValue(statID, operand, value);
                }
            }

            public class Validator_Job
            {
                public string existsPreviousCOM = "";
                public bool Validate(ref EvaluationPackage evp)
                {
                    bool returnVal = true;
                    if (existsPreviousCOM != "") returnVal = returnVal && evp.job.HasExistingCOMwithID(existsPreviousCOM, new List<int>() { evp.DoerRef }, new List<int>() { evp.ReceiverRef }, true, true);
                    return returnVal;
                }
            }

            public class Validator_Package
            {
                public bool existsMaster = false;
                public bool existsMaster_NonActor = false;
                public bool existsMaster_Doer = false;
                public bool existsMaster_Receiver = false;

                public bool Validate(ref EvaluationPackage evp)
                {
                    bool returnVal = true;
                    if (existsMaster || existsMaster_NonActor || existsMaster_Doer || existsMaster_Receiver) returnVal = returnVal && evp.Package.Master != null;
                    if (existsMaster_NonActor) returnVal = returnVal && !evp.Package.doer.Contains(evp.Package.Master) && !evp.Package.receiver.Contains(evp.Package.Master);
                    if (existsMaster_Receiver) returnVal = returnVal && evp.Package.receiver.Contains(evp.Package.Master);
                    if (existsMaster_Doer) returnVal = returnVal && evp.Package.doer.Contains(evp.Package.Master);
                    return returnVal;
                }
            }

        }

        Dictionary<int, string> _texts = new Dictionary<int, string>();

        public void AppendToText(string suffix)
        {
            for (int i = texts.Count - 1; i >= 0; i--) texts[i] += suffix;
            foreach (var entry in this.Entries) entry.AppendToText(suffix);
        }
        public bool GetValidText(ref List<string> list, ref ActionPackage ap)
        {
            //Debug.LogError("GetValidText options [" + String.Join("\n", texts) + "]");
            bool hasvalid = false;
            foreach (var ep in ap.ListEP)
            {
                if (ValidateConditions(ep)) hasvalid = true;
            }

            if (!hasvalid) return true;
            else
            {
                // first add text to list
                if (texts.Count > 0)
                {// meaning we do add text here
                    var randIndex = Utility.GetRandIndexFromListCount(texts);

                    if (_texts.TryGetValue(randIndex, out string value)) list.Add(value);
                    else
                    {
                        var s2 = LocalizeDictionary.QueryThenParse(texts[randIndex]);
                        if (_texts.ContainsKey(randIndex)) _texts.Add(randIndex, s2);
                        list.Add(s2);
                    }
                }

                if (Entries != null && Entries.Count > 0)
                {
                    bool _keepLooking = true;
                    foreach (var entry in Entries)
                    {
                        _keepLooking = (entry as Description_Entry).GetValidText(ref list, ref ap);
                        if (!_keepLooking) break;
                    }
                }
                // then if this.stoplooking == true return this to block searching on the same level
                return keepLooking;
            }
        }
        public bool GetValidText(ref List<string> list, ref EvaluationPackage evp)
        {
            //Debug.LogError("GetValidText options [" + String.Join("\n", texts) + "]");
            if (!ValidateConditions(evp)) return true;
            else
            {
                // first add text to list
                if (texts.Count > 0)
                {// meaning we do add text here
                    var randIndex = Utility.GetRandIndexFromListCount(texts);

                    if (_texts.TryGetValue(randIndex, out string value)) list.Add(value);
                    else
                    {
                        var s2 = LocalizeDictionary.QueryThenParse(texts[randIndex]);
                        if (_texts.ContainsKey(randIndex)) _texts.Add(randIndex, s2);
                        list.Add(s2);
                    }
                }

                if (Entries != null && Entries.Count > 0)
                {
                    bool _keepLooking = true;
                    foreach (var entry in Entries)
                    {
                        _keepLooking = (entry as Description_Entry).GetValidText(ref list, ref evp);
                        if (!_keepLooking) break;
                    }
                }
                // then if this.stoplooking == true return this to block searching on the same level
                return keepLooking;
            }
        }

        private bool ValidateConditions(EvaluationPackage evp)
        {
            if (conditions == null || conditions.Count < 1) return true;
            foreach (var cond in conditions) if (!cond.Validate(ref evp)) return false;
            return true;
        }

        public void OnBeforeSerialize()
        {

        }

        public void OnAfterDeserialize()
        {
            //if (this.texts != null && texts.Count > 0) Debug.Log(String.Join("\n", texts));
        }
    }




}
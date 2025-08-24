using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Xml;
using System.Runtime.Serialization;
using System.Linq;
using Newtonsoft.Json;

[System.Serializable]
public class Character_Body
{

    List<CombatAction> _alwaysValidActions = null;
    [JsonIgnore]
    public List<CombatAction> AlwaysValidActions
    { get
        {
            if (_alwaysValidActions == null)
            {
                _alwaysValidActions = new List<CombatAction>();
                foreach (var entry in scr_System_Serializer.current.MasterList.CombatActions.AllActions)
                {
                    if (entry.itemRequirement != null && entry.itemRequirement.isActive) continue;
                    if (_alwaysValidActions.Contains(entry)) continue;
                    _alwaysValidActions.Add(entry);
                }
                //Debug.Log($"Initializing alwaysvalidactions, dict count {scr_System_Serializer.current.MasterList.CombatActions.AllActions.Count} self count {_alwaysValidActions.Count}");
            }
            return _alwaysValidActions;
        } }

    [JsonIgnore]
    public Dictionary<Item_Instance, List<CombatAction>> CombatActions
    { get
        {
            var list = new Dictionary<Item_Instance, List<CombatAction>>();
            foreach(var i in this.Body)
            {
                foreach(var kvp in i.CombatActions)
                {
                    if (list.ContainsKey(kvp.Key)) continue;
                    list.Add(kvp.Key, kvp.Value);
                }
            }
            return list;
        } }

    public void UpdateTimeMinute(TimeSpan t)
    {
        foreach (BodyInternal_Instance i in Internals) i.UpdateTimeMinute(t);
    }
    public void UpdateTimeHour(TimeSpan t)
    {
        /*
        foreach(var inte in this.Internals)
        {
            if (inte.canContain && inte.Contains.Count > 0 && inte.canOverflowOut && inte.overflowOutTag != "ext")
            {
                var target = GetRandomInternalWithTag(inte.overflowOutTag);

                if (target != null)
                {
                    inte.TransferContentTo(target);
                }
            }
        }*/
    }
    public void UpdateTimeDay(TimeSpan t)
    {

    }
    //-------------------------
    protected int ownerRefID = -1;
    private Character_Trainable owner = null;
    [JsonIgnore] public Character_Trainable Owner
    {
        get
        {
            if (owner == null && ownerRefID > -1)
            {
                owner = scr_System_CampaignManager.current.FindInstanceByID(ownerRefID);
            }
            return owner;
        }
    }
    //----------------------
    [SerializeField][JsonProperty] private List<BodyPart_Instance> body = null;
    [JsonIgnore] public List<BodyPart_Instance> Body {
        get {
            if (body == null || body.Count < 1) AddMissing();
            return body;
        } }
    //--------------------------
    [JsonIgnore] private List<BodyInternal_Instance> internals = null;
    [JsonIgnore] public List<BodyInternal_Instance> Internals
    {
        get
        {
            if (internals == null || internals.Count < 1)
            {
                internals = new List<BodyInternal_Instance>();
                foreach (BodyPart_Instance b in Body) internals.AddRange(b.internals);
            }
            return internals;
        }
    }
    //----------------------------




    public Character_Body()
    {

    }
    public Character_Body(Character_Trainable c)
    {
        this.ReEstablishParent(c);
    }

    public void ReEstablishParent(Character_Trainable c)
    {
        this.owner = c;
        this.ownerRefID = c.RefID;

        if (this.body != null) foreach (var i in this.body) i.ReEstablishParent(c);
    }

    public void ClearLastInteractedRefs()
    {
        Climax = false;
        Cum = false;
        foreach (var organ in Internals) organ.ClearLastInteractedRefs();
    }


    public void AddMissing()
    {
        // Debug.Log("Body_AddMissing : baseID [" + Owner.Race.bodyPartRoot + "] refID [" + Owner.RefID + "]");
        if (Owner == null)
        {
            Debug.Log("CHARACTER BODY ADDMISSING OWNER NULL");
            return;
        }
        if (body == null || body.Count < 1 || body.Find(x => x.Base.ID == Owner.Race.bodyPartRoot) == null)
        {

            body = new List<BodyPart_Instance>();
            var bRoot = new BodyPart_Instance();
            bRoot.Initialize(Owner.Race.bodyPartRoot, Owner);

            body.Add(bRoot);
        }
        //Debug.Log("Body_AddMissing : rootNode DisplayName [" + Body.Find(x => x.baseID == Owner.Race.bodyPartRoot).DisplayName + "]");


        List<string> current = new List<string>();
        foreach (BodyPart_Instance i in Body) current.Add(i.Base.ID);

        List<string> newbody = scr_System_Serializer.current.GetByNameOrID_BodyPart_Base(Owner.Race.bodyPartRoot).GetAllChildsID();
        foreach (string s in current) newbody.Remove(s);

        foreach (string s in newbody)
        {
            if (body.Find(x => x.Base.ID == s) != null) continue;
           // Debug.LogError($"{Owner.FirstName} body addmissing on {s}");
            BodyPart_Instance b = new BodyPart_Instance();
            b.Initialize(s, Owner);
            /*
            if (this.Size_V.ID == "trait_Size_V_none" && b.hasTag("vagina")) { continue; }
            else if (this.Size_A.ID == "trait_Size_A_none" && b.hasTag("anus")) { continue; }
            else if (this.Size_P.ID == "trait_Size_P_none" && b.hasTag("penis")) { continue; }
            else if (this.Size_B.ID == "trait_Size_B_none" && b.hasTag("breast")) { continue; }
            else Body.Add(b);
            */
            body.Add(b);
        }

        body.Sort((x, y) => x.sortOrder.CompareTo(y.sortOrder));


        string sb = "Character " + Owner.FirstName + " Body_AddMissing\n";
        foreach (BodyPart_Instance i in Body)
        {
            sb += i.DisplayName + " || " + i.Tooltip + " [";
            foreach (BodyInternal_Instance j in i.internals)
            {
                sb += " " + j.DisplayName + " ";
            }
            sb += "]\n";
        }
        //Debug.Log(sb);

        foreach (var i in body) i.ReEstablishParent(Owner);
    }

    public bool EquipItem(int itemRefID, int count = 1, bool forceEquip = false)
    {
        Item_Instance item = scr_System_CampaignManager.current.FindItemInstanceByID(itemRefID);
        if (item == null) return false;

        var comp = item.GetComp_Equippable();
        if (comp == null) return false;
        
        //Debug.Log($"Equipping {item.DisplayName} on {Owner.FirstName} with slots [{String.Join("|", comp.equipSlot)}]");
        bool equipped = false;
        foreach (BodyPart_Instance part in Body)
        {
            if (part.EquipItem(item, forceEquip))
            {
                equipped = true;
                break;
            }
        }

        if (equipped)
        {
            var slots = new List<BodyPartEquipSlot>(comp.coverSlot);
            if (slots.Count > 0)
            {
                foreach (var part in Body)
                {
                    if (part.EquipItem(item, ref slots, forceEquip) && slots.Count < 1) break;
                }
            }
            if (comp != null && comp.statModifiers.Count > 0) Owner.Stats.RefreshAllStats(true);
            return true;
        }
        else
        {
            return false;
        }
        
    }

    [SerializeField][JsonProperty] protected bool Climax = false;
    [SerializeField][JsonProperty] protected bool Cum = false;

    public bool isClimaxing(bool checkCum)
    {
        if (checkCum) return Cum;
        else return Climax;
    }

    [JsonIgnore] public List<int> EquippedItemRefs
    {
        get
        {
            //Debug.LogError("EQUIPPEDREFS INSIDE BODY");
            List<int> items = new List<int>();
            foreach(BodyPart_Instance i in this.Body)
            {
                items.AddRange(i.EquippedRefIDs);
            }
            //Debug.LogError("EQUIPPEDREFS after BODY");
            foreach (BodyInternal_Instance i in this.Internals)
            {
                items.AddRange(i.EquippedRefIDs);
            }
            //Debug.LogError("EQUIPPEDREFS after internals");
            return items;
        }
    }

    /// <summary>
    /// if bodyTag is not specified, default first ingestmethod bodytag
    /// </summary>
    /// <param name="i"></param>
    /// <param name="bodyTag"></param>
    public bool ConsumeIngestible(Item_Instance i, string bodyTag = "")
    {
        ItemComponent_Ingestible ingest = i.GetComp_Ingestible();
        if (ingest == null) {
            Debug.LogError("ConsumeIngestible instance [" + i.DisplayName + "] has null ingest comp");
            return false;
        }

        if (bodyTag == "") bodyTag = ingest.ingestMethod.First().bodyTags;

        if (bodyTag != "")
        {
            var internals = Internals.FindAll(x => x.canContain && x.hasTag(bodyTag));
            if (internals.Count < 1)
            {
                Debug.LogError("ConsumeIngestible instance [" + i.DisplayName + "] ingestmethod [" + bodyTag + "] has null internal on " + Owner.FirstName);
                return false;
            }
            else
            {
                var randInternal = Utility.GetRandomElement(internals);
                if (randInternal == null) return false;
                randInternal.Ingest(i);
                if (i.Tags.Contains("food_meal")) Owner.NotifyFoodConsume(i);
                return true;
            }
        }
        else
        {
            return false;
        }
        
    }

    public bool HasBodyTag(List<string> list)
    {
        foreach (string s in list)
        {
            if (Body.Exists(x => x.hasTag(s))) continue;
            else return false;
        }
        return true;
    }

    public bool HasBodyTag(string s)
    {
        if (Body.Exists(x => x.hasTag(s))) return true;
        else return false;
    }

    public int GetRevealingScoreByTag(string tag, BodyEquipLayer layer = BodyEquipLayer.Skin)
    {
        var i = -1;
        foreach (BodyPart_Instance b in Body)
        {
            if (b.hasTag(tag)) i = Math.Max(i, b.GetRevealingScore(layer));
        }
        return i;
    }

    public int GetMaxRevealingScoreByTags(List<string> tags, BodyEquipLayer layer = BodyEquipLayer.Skin)
    {
        int i = 0;
        foreach (string s in tags)  i = Math.Max(i, GetRevealingScoreByTag(s, layer));
        return i;
    }

    public bool HasEquipByFilter(BodyEquipLayer layer, int revealingScoreFilter = -1)
    {
        foreach (BodyPart_Instance b in Body)
        {
            if (b.HasEquipByFilter(layer, revealingScoreFilter)) return true;
        }
        return false;
    }

    public bool UnequipItem(int itemRefID)
    {
        bool returnVal = false;
        foreach (BodyPart_Instance i in Body)
        {
            returnVal = i.UnequipItem(itemRefID) || returnVal;
            //if (returnVal) break;
        }

        return returnVal;

    }

    public BodyInternal_Instance GetRandomInternalWithTag(string tag)
    {
        List<BodyInternal_Instance> l = Internals.FindAll(x => x.hasTag(tag));
        if (l.Count == 0) return null;
        return Utility.GetRandomElement(l);
    }

    public BodyInternal_Instance GetRandomInternalWithBaseID(string baseID)
    {
        List<BodyInternal_Instance> l = Internals.FindAll(x => x.baseID == baseID);
        if (l.Count == 0) return null;
        return Utility.GetRandomElement(l);
    }


    protected void ConcatenateClimax(ref string originalString, string sensitivityString)
    {
        int count = 1;
        string key = sensitivityString == "" ? "" : sensitivityString.Substring(sensitivityString.Length - 1);

        if (originalString != null && originalString.Length > 0) 
        {
            var disassemble = originalString.Split("||");
            count += int.Parse(disassemble[2]);
            key += disassemble[1];
        }
        originalString = "experience_sex_strong_climax" + "||" + key + "||" + count;
    }

    public void CheckClimax()
    {
        ExperienceLog exp = new ExperienceLog();
        string climaxKeywords = "";
        string cumKeywords = "";
        if (Owner.Stats.SexStimulation.Severity >= Owner.Stats.CumThreshold) 
        {
            // forbid climax if timestopped
            // BUT!! ALLOW CLIMAX DURING RESUME
            if (scr_System_Time.current.TimeStopStrict && !Owner.CanActInTimeStop) return;
            if (Owner.Climaxing) return;
            if (Owner.CurrentJob != null && Owner.CurrentJob is Job_Sex_Group)
            {
                List<int> relevantActorRefs = Owner.CurrentJob.GetLastInteractedActorRefs(Owner.RefID);
            }


            List<string> climaxTags = new List<string>();
            UtilityEX.GetActorTag(ref climaxTags, Owner);

            int climaxDebuff = 0;
            foreach (var part in this.Internals)
            {

                if (part.Sensitivity != "" && (int)Owner.Stats.GetStatusSeverityByStringMatch(part.Sensitivity) >= Owner.Stats.CumThreshold)
                {
                    //char key = part.Sensitivity.Last();

                    ConcatenateClimax(ref climaxKeywords, part.Sensitivity);// climaxKeywords.Add("Strong Climax "+ part.Sensitivity);
                    Item_Instance cum = null;
                    // if penis then cum, if not then just climax

                    List<string> selfTag = new List<string>(part.Base.tags) { };
                    selfTag.AddRange(climaxTags);
                    selfTag.Add(part.Sensitivity);
                    selfTag.Add("climax");

                    this.Climax = true;
                    // ADDLOG CLIMAX
                    var cumAmount = 20;
                    if (part.canFuck) cum = part.Cum(cumAmount, exp);

                    if (cum != null)
                    {

                        this.Cum = true;

                        // find valid container for cum
                        List<BodyInternal_Instance> possiblecontainers = part.LastInteactedRefs.FindAll(x=>x.canContain);
                        List<BodyInternal_Instance> possibleEmptyContainers = possiblecontainers.FindAll(x => !x.containsOverCapacity);

                        BodyInternal_Instance container = (possibleEmptyContainers.Count > 0 ? Utility.GetRandomElement(possibleEmptyContainers)
                                        : (possiblecontainers.Count > 0 ? Utility.GetRandomElement(possiblecontainers) : null));

                        if (container != null)
                        {   // if valid bodyinternal container exist : Fucker != null && Fucker.canContain
                            container.Ingest(cum, exp);

                            string key = "ui_entry_memory_description_cum_";
                            // ADDLOG CUM FOR RECEIVER
                            var desc = LocalizeDictionary.QueryThenParse("ui_entry_memory_description_creampie");
                            desc = desc.Replace("$target$", part.Owner.FirstName)
                                        .Replace("$cum_verb$", LocalizeDictionary.QueryThenParse(key + container.Base.ID))
                                        .Replace("$amount$", cum.GetComp_Ingestible().amount.ToString("N0"));


                            List<string> containerTags = new List<string>(container.Base.tags) {};
                            containerTags.Add("cum");
                            UtilityEX.GetActorTag(ref containerTags, container.Owner);
                            if (container.Sensitivity != "") containerTags.Add(container.Sensitivity);

                            UtilityEX.CheckExperienceGainNoStimulate(container.Owner, cum.GetComp_Ingestible().amount, false, containerTags, new List<string>());

                            // ADDLOG CUM FOR SHOOTER
                            var desc2 = LocalizeDictionary.QueryThenParse("ui_entry_memory_description_cumOnto");
                            desc2 = desc2.Replace("$target$", container.Owner.FirstName)
                                        .Replace("$cum_verb$", LocalizeDictionary.QueryThenParse(key + container.Base.ID))
                                        .Replace("$amount$", cum.GetComp_Ingestible().amount.ToString("N0"));

                            
                            var memInst2 = new MemInstance(new List<int>() { part.Owner.RefID }, selfTag, "", -1, -1, false, Memory_Response.None, Memory_Attitude.None, desc);
                            container.Owner.Memory.AddEntry(memInst2, containerTags, -1, true);


                            
                            var memInst3 = new MemInstance(new List<int>() { container.Owner.RefID }, containerTags, "", -1, -1, true, Memory_Response.None, Memory_Attitude.Like, desc2);
                            part.Owner.Memory.AddEntry(memInst3, selfTag, -1, true);

                            cumKeywords = LocalizeDictionary.QueryThenParse("experience_sex_cumtainer").Replace("$amount$", cumAmount.ToString()).Replace("$partname$",container.DisplayNameFull);
                        }
                        else
                        {   // cum on ground
                            scr_System_CampaignManager.current.Map.FindRoomByChara(ownerRefID).AddItem(cum);

                            // ADDLOG CUM FOR SHOOTER
                            var desc = LocalizeDictionary.QueryThenParse("ui_entry_memory_description_cum");
                            desc = desc .Replace("$amount$", cum.GetComp_Ingestible().amount.ToString("N0"));

                            
                            var memInst4 = new MemInstance(new List<int>() { part.Owner.RefID }, new List<string>() { "" }, "", -1, -1, true, Memory_Response.None, Memory_Attitude.Like, desc);
                            part.Owner.Memory.AddEntry(memInst4, selfTag, -1, true);

                            cumKeywords = LocalizeDictionary.QueryThenParse("experience_sex_cum").Replace("$amount$", cumAmount.ToString());
                        }
                        climaxDebuff -= 200;
                        //climaxKeywords.Add("Cum(" + (container != null? container.DisplayName + ":"+ cum.GetComp_Ingestible().amount+"ml" + ":"+container.Owner.FirstName : cum.GetComp_Ingestible().amount+"ml") + ")");

                        var desc1 = LocalizeDictionary.QueryThenParse("ui_entry_memory_description_climax_keyworded").Replace("$part$", part.DisplayName);

                        
                        var memInst5 = new MemInstance(new List<int>() { part.Owner.RefID }, new List<string>(), "", -1, -1, true, Memory_Response.None, Memory_Attitude.Like, desc1);
                        part.Owner.Memory.AddEntry(memInst5, selfTag, -1, true);

                        UtilityEX.CheckExperienceGainNoStimulate(part.Owner, 1, false, selfTag, new List<string>());

                    }
                    else
                    {
                        climaxDebuff -= 50;

                        var desc1 = LocalizeDictionary.QueryThenParse("ui_entry_memory_description_climax_keyworded").Replace("$part$", part.DisplayName);
                        
                        var memInst6 = new MemInstance(new List<int>() { part.Owner.RefID }, new List<string>(), "", -1, -1, true, Memory_Response.None, Memory_Attitude.Like, desc1);
                        part.Owner.Memory.AddEntry(memInst6, selfTag, -1, true);

                        UtilityEX.CheckExperienceGainNoStimulate(part.Owner, 1, false, selfTag, new List<string>());
                    }
                }
            }


            if (climaxDebuff != 0 && climaxKeywords.Length > 0) 
            {
                if (scr_System_CentralControl.current.LogPrefs.DLog_Status) Debug.Log($"Adding climax status on {Owner.FirstName} debuffstrength {climaxDebuff}");

                Owner.Stats.AddOrModStatus("chara_status_sexual_climax_after", climaxDebuff);

                var disassemble = climaxKeywords.Split("||");
                climaxKeywords = LocalizeDictionary.QueryThenParse(disassemble[0])
                    .Replace("$sensitivity$", disassemble[1])
                    .Replace("$count$", LocalizeDictionary.QueryThenParse(disassemble[0] + "_" + disassemble[2]));

                string s = Owner.FirstName + ":" + climaxKeywords + (cumKeywords.Length > 0 ? "/" + cumKeywords : "");
                scr_UpdateHandler.current.NotifyClimax(Owner.RefID, s, exp);
            }

        }
    }

}
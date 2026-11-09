using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.UI.GridLayoutGroup;

public static class MapUtility
{
    // ---- visibility inside a room: room dividers (stalls) - Docs/Furniture/Plan_FurnitureOccupancy.md §5 ----
    // Same room assumed (callers already iterate one room). Same scope (open area / same divider) = see each other;
    // otherwise only when neither side is inside a divider that blocks visibility (a jail cell does not, a stall does).
    // Nobody inside a blocking divider game-wide (FurnitureOccupancyUtility.AnyoneInBlockingDivider, a cached count):
    // answered at once. Otherwise O(1): two ScopeRef reads, a job lookup only for a divider.

    /// <summary> a scope (divider RefID, -1 = open area) whose inside cannot be seen from outside (and vice versa) </summary>
    public static bool IsBlockingScope(int scope)
    {
        if (scope < 0) return false;
        var f = scr_System_CampaignManager.current.FindJobInstanceByID(scope) as Job_Furniture;
        return f != null && f.BlocksVisibility;
    }

    public static bool CanSeeScope(int scopeA, int scopeB)
    {
        if (scopeA == scopeB) return true;
        return !IsBlockingScope(scopeA) && !IsBlockingScope(scopeB);
    }

    /// <summary> a and b (same room) see each other </summary>
    public static bool CanSee(Character_Trainable a, Character_Trainable b)
    {
        if (a == null || b == null) return false;
        if (ReferenceEquals(a, b)) return true;
        if (!FurnitureOccupancyUtility.AnyoneInBlockingDivider) return true;   // nobody in a stall: nobody hidden
        return CanSeeScope(a.ScopeRef, b.ScopeRef);
    }

    /// <summary> observer (same room) sees what ap does - the package takes place where its first doer is </summary>
    public static bool CanSee(Character_Trainable observer, ActionPackage ap)
    {
        if (observer == null || ap == null) return false;
        if (!FurnitureOccupancyUtility.AnyoneInBlockingDivider) return true;   // nobody in a stall: nobody hidden
        return CanSeeScope(observer.ScopeRef, PackageScope(ap));
    }

    /// <summary>
    /// where ap takes place: its first doer's scope (a stall's commands run inside it, its Enter outside). All its actors
    /// share one scope - a package across a divider's door (actors inside and outside) is an error and is logged.
    /// </summary>
    public static int PackageScope(ActionPackage ap)
    {
        var doers = ap.DoerRefs;
        if (doers == null || doers.Count < 1) return -1;
        var campaign = scr_System_CampaignManager.current;
        var c = campaign.FindInstanceByID(doers[0]);
        int scope = c == null ? -1 : c.ScopeRef;

        var actors = ap.actorRefs;
        for (int i = 0; actors != null && i < actors.Count; i++)
        {
            var a = campaign.FindInstanceByID(actors[i]);
            if (a == null || a.ScopeRef == scope) continue;
            Debug.LogWarning($"[Visibility] mixed-scope package {ap.DisplayName} (job {ap.job?.RefID}): doer {c?.FirstName} in scope {scope}, actor {a.FirstName} in scope {a.ScopeRef}");
            break;
        }
        return scope;
    }

    /// <summary> no actor of refs is visible to c (refs empty = not actor bound, visible) - message filtering </summary>
    public static bool NoneVisibleTo(Character_Trainable c, List<int> refs)
    {
        if (c == null || refs == null || refs.Count < 1) return false;
        if (!FurnitureOccupancyUtility.AnyoneInBlockingDivider) return false;   // nobody in a stall: nobody hidden
        int scope = c.ScopeRef;
        var campaign = scr_System_CampaignManager.current;
        for (int i = 0; i < refs.Count; i++)
        {
            if (refs[i] == c.RefID) return false;
            var a = campaign.FindInstanceByID(refs[i]);
            if (a == null || CanSeeScope(scope, a.ScopeRef)) return false;
        }
        return true;
    }

    /// <summary>
    /// RefreshMoodlets for everyone in chara: each from the characters they see. No divider blocks anyone (the common
    /// case) = the whole list, as before; otherwise a per-character visible subset in one reused buffer (RefreshMoodlets
    /// only iterates it).
    /// </summary>
    public static void RefreshMoodletsVisible(List<Character_Trainable> chara)
    {
        bool anyBlocked = false;
        if (FurnitureOccupancyUtility.AnyoneInBlockingDivider)   // nobody in a stall anywhere: skip even the per-room check
            for (int i = 0; i < chara.Count && !anyBlocked; i++)
                if (chara[i] != null && chara[i].IsInBlockingDivider) anyBlocked = true;

        if (!anyBlocked)
        {
            for (int i = 0; i < chara.Count; i++) chara[i]?.Relationships.RefreshMoodlets(chara);
            return;
        }
        var visible = _moodletBuffer;
        for (int i = 0; i < chara.Count; i++)
        {
            var self = chara[i];
            if (self == null) continue;
            visible.Clear();
            for (int j = 0; j < chara.Count; j++) if (chara[j] != null && CanSee(self, chara[j])) visible.Add(chara[j]);
            self.Relationships.RefreshMoodlets(visible);
        }
        visible.Clear();
    }
    /// <summary>RefreshMoodlets for self alone, from the characters of roomChara it sees (see above).</summary>
    public static void RefreshMoodletsVisible(Character_Trainable self, List<Character_Trainable> roomChara)
    {
        if (self == null) return;
        bool anyBlocked = false;
        if (FurnitureOccupancyUtility.AnyoneInBlockingDivider)
            for (int i = 0; i < roomChara.Count && !anyBlocked; i++)
                if (roomChara[i] != null && roomChara[i].IsInBlockingDivider) anyBlocked = true;

        if (!anyBlocked)
        {
            self.Relationships.RefreshMoodlets(roomChara);
            return;
        }
        var visible = _moodletBuffer;
        visible.Clear();
        for (int j = 0; j < roomChara.Count; j++) if (roomChara[j] != null && CanSee(self, roomChara[j])) visible.Add(roomChara[j]);
        self.Relationships.RefreshMoodlets(visible);
        visible.Clear();
    }
    static readonly List<Character_Trainable> _moodletBuffer = new List<Character_Trainable>(16);


    public static bool CheckReverseInterrupt(List<EvaluationPackage> eps, Character_Trainable self, Character_Trainable target)
    {
        if (self == null || target == null) return false;
        if (self.RefID == 0) return false;
        var rel = self.Relationships.FindRelationshipWith(target);
        if (rel == null) return false;
        bool debug = true || scr_System_CentralControl.current.LogPrefs.DLog_Interrupt;

        foreach (var ap in eps)
        {
            if (ap.Package.isTemporaryAP) continue;
            if (ap.job.Actors.Contains(self) && ap.job.Actors.Contains(target)) continue;

            foreach(var ep in eps)
            {
                var kol2 = new KojoCollector(self, ep.targetCOM.tooltipID, "_Interrupt");
                kol2.LoadEP(ep, target);
                var kol4 = self.Relationships.GetKOJOMessage_Suffix(kol2, null);
                if (kol4 != null)
                {
                    kol4.ReplaceString("$self$", self.FirstName);
                    kol4.ReplaceString("$target$", target.FirstName);
                    kol4.ReplaceString("$epDescription$", ep.Description_Ongoing);
                    kol4.LoadRelevantActors(ep.ActorRefs);
                    kol4.autoAnimate = true;
                    scr_UpdateHandler.current.AppendKojoMessage(kol4, self.CurrentRoom);
                    if (debug) Debug.Log($"Interrupt [{self.FirstName}] visible? {kol4.VisibleTo(scr_System_CampaignManager.current.Player, self.CurrentRoom)} : {kol4.collect.message}");
                    return true;
                }
                else
                {
                    var kol3 = kol2.Copy("Interrupt", "");
                    //kol3.LoadRel(rel);
                    kol3 = self.Relationships.GetKOJOMessage_Suffix(kol3, null);
                    if (kol3 != null)
                    {
                        kol3.ReplaceString("$self$", self.FirstName);
                        kol3.ReplaceString("$target$", target.FirstName);
                        kol3.ReplaceString("$epDescription$", ep.Description_Ongoing);
                        kol3.LoadRelevantActors(ep.ActorRefs);
                        kol3.autoAnimate = true;
                        scr_UpdateHandler.current.AppendKojoMessage(kol3, self.CurrentRoom);
                        if (debug) Debug.Log($"Interrupt [{self.FirstName}] visible? {kol3.VisibleTo(scr_System_CampaignManager.current.Player, self.CurrentRoom)} : {kol3.collect.message}");
                        return true;
                    }
                }
            }
        }
        return false;
        /*
         
         
    public MessageCollect_KojoEntry GetKOJOMessage_Interrupt(bool isDoer, EvaluationPackage ep, Character_Relationship relation)
    {
        string comID = ep.targetCOM.tooltipID;
        if (comID.Contains("_noSex")) comID = comID.Substring(0, comID.Length - 6);
        comID = $"{comID}_Interrupt";

        //Debug.Log($"GetKOJOMessage_Interrupt {comID}");

        if (!entries.ContainsKey(comID))
        {
            //if (this.Fallback != null) return Fallback.GetKOJOMessage_Interrupt(isDoer, ep, relation);
            //else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) Debug.Log("Personality [" + this.DisplayName + "] unimplemented COM response for [" + comID + "] and for target [" + (relation == null ? "null" : relation.Target.FirstName) + "]");
            return null;
        }
        comID = entries[comID].CheckRedirect(comID);
        return entries[comID].GetResponse(relation, isDoer ? ep.DoerSelfTag : ep.ReceiverSelfTag, isDoer ? ep.ReceiverTargetTag : ep.DoerTargetTag, ep);
    }

         */
    }





    /// <summary>
    /// Conditions are pre-filtered in Map pre CheckInterrupt calls <br/>
    /// player will trigger interrupt but will skip kojo message logging (cuz messagelog is being taken care of from the other direction)
    /// </summary>
    /// <param name="ap"></param>
    /// <param name="selfTags"></param>
    public static bool CheckInterrupt(Character_Trainable Owner, ActionPackage ap, List<string> selfTags)
    {
        if (ap != null && ap.isTemporaryAP) return false;
        bool debug = scr_System_CentralControl.current.LogPrefs.DLog_Interrupt;
        // if any EP satisfy interrupt condition, every actor in ap are checked for relationship mod
        var triggerEventID = "Interrupt";

        var kol = new KojoCollector(Owner, triggerEventID);
        kol.LoadSelfTags(Owner, selfTags);
        kol = Owner.Relationships.GetKojoMessage_AP(kol, ap);

        // Debug.Log("checkInterrupt");

        if (kol != null)
        {
            kol.rightAlign = true;
            if (ap != null) kol.LoadRelevantActors(ap.actorRefs);
            kol.ReplaceString("$self$", Owner.FirstName);
            if (debug) Debug.Log($"Interrupt [{Owner.FirstName}] visible? {kol.VisibleTo(scr_System_CampaignManager.current.Player, Owner.CurrentRoom)} : {kol.collect.message}");
            scr_UpdateHandler.current.AppendKojoMessage(kol, Owner.CurrentRoom);
            return true;
        }
        else
        {
            if (debug) Debug.Log($"Interrupt [{Owner.FirstName}] visible? null");
            return false;
        }
        /*
var msg = Personality.GetKOJOMessage(triggerEventID, Owner, selfTags, ap.ListEP);
if (msg == null)
{
// for each ep check interrupt
}
if (scr_System_CampaignManager.current.Player != Owner && msg != null && msg.message != null && msg.message.Length > 0)
{
msg.message = $"<align=\"right\">{msg.message.Replace("$self$", Owner.FirstName)}</align>";//;
bool visible = scr_System_CampaignManager.current.isCharaVisibleToPlayer(Owner.RefID);
bool recording = Owner.CurrentRoom != null && Owner.CurrentRoom.HasRecording;

msg.AddRelevantActor(Owner);
msg.AddRelevantActors(ap.Actors);

scr_UpdateHandler.current.AppendKojoMessage(msg, visible, recording ? Owner.CurrentRoom : null);
return true;
}*/
    }
    public static List<COM> GetFurnitureCOMs(FurnitureBase.Furniture_COMGiver furniture)
    {
        List<COM> returnValues = new List<COM>();

        foreach (string i in furniture.comID)
        {
            var temp = scr_System_Serializer.current.GetByNameOrID_COM(i);
            if (temp != null) returnValues.Add(temp);
            else Debug.LogError($"FURNITURE COMGIVER CANNOT FIND COMMAND {i}");
        }

        if (furniture.comTags.Count > 0) returnValues.AddRange(scr_System_Serializer.current.index_COM.GetByTags(furniture.comTags));

        return returnValues;
    }

    public static List<ItemEntry> GetContent(MapPlan.SalesInventoryInit inv)
    {
        var list = new List<ItemEntry>();
        if (inv.matchByID != "") list.Add(new ItemEntry(inv.matchByID, inv.nameOverwrite, inv.itemCount, inv.countOverride));
        if (inv.matchByTags.Count > 0)
        {
            foreach (var recipe in Masterlist_Items.Instance.CraftingRecipe.Values)
            {
                var outputItem = recipe.OutputItem;
                if (outputItem == null) Debug.LogError($"sales inventory get content {recipe.outputItemBaseID} is null");
                else if (Utility.ListContainsStrict(outputItem.Tags, inv.matchByTags))
                {
                    if (inv.exceptTags.Count > 0 && Utility.ListContainsLoose(outputItem.Tags, inv.exceptTags)) continue;
                    else if (outputItem.Tags.Contains("do_not_sell")) continue;
                    list.Add(new ItemEntry(outputItem.id, "", recipe.outputAmount * inv.itemCount, inv.countOverride));
                }
            }

            foreach (var item in scr_System_Serializer.current.index_Item_Base.List)
            {
                if (item.Tags.Contains("do_not_use")) continue;
                if (item.GetCompTemplateByID("ItemComponent_Craftable") != null) continue;
                if (Utility.ListContainsStrict(item.Tags, inv.matchByTags))
                {
                    if (inv.exceptTags.Count > 0 && Utility.ListContainsLoose(item.Tags, inv.exceptTags)) continue;
                    else if (item.Tags.Contains("do_not_sell")) continue;
                    list.Add(new ItemEntry(item.id, "", inv.itemCount, inv.countOverride));
                }
            }
        }
        return list;
    }
}
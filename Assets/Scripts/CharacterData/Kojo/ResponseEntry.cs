using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class ResponseEntry
{
    /// <summary>
    /// ID could either be comID or eventID
    /// </summary>
    public string ID = "";
    public List<string> tags = new List<string>();
    public List<Variant> variants = new List<Variant>();
    public bool interruptSelfJob = false;
    public bool interruptTargetJob = false;
    public bool debugLogging = false;
    public string RedirectID = "";
    public bool requirePlayer = true;


    public string CheckRedirect(string original)
    {
        if (this.RedirectID != "") return this.RedirectID;
        else return original;
    }

    public void ValidateIntegrity()
    {
        foreach (var v in this.variants)
        {
            v.ValidateIntegrity();
        }
    }

    public bool Validate(Character_Trainable owner, Character_Trainable target = null)
    {
        if (interruptSelfJob && owner.CurrentJob != null && (owner.CurrentJob is Job_Sex_Group)) return false;
        if (interruptTargetJob && target != null && target.CurrentJob != null && (target.CurrentJob is Job_Sex_Group)) return false;
        return true;
    }
    public MessageCollect_KojoEntry GetResponse(KojoCollector kol)
    {
        if (!Validate(kol.Owner, kol.Target))
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.LogError($"Character_Personality GetKOJOMessage evID[{kol.eventID}{kol.suffix}] [{(kol.Owner.FirstName)}{(kol.Target == null ? "" : $" -> {kol.Target.FirstName}")}] self and target validation failed");
            return null;
        }else if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("Validating kojoResponse [" + ID + "] req [" + kol.Owner.FirstName + "->" + kol.Target.FirstName + "], self[" + String.Join("|", kol.selfTags) + "] target[" + String.Join("|", kol.targetTags) + "]");

        MessageCollect_KojoEntry responses = null;
        bool playerInvolved = kol.isPlayerInvolved;
        if (!requirePlayer) playerInvolved = true;

        foreach (var i in variants)
        {
            if (i.GetRandomResponse(out var result, kol) && result != null)
            {
                if (responses == null) responses = result;
                else responses.nexts.Add(result);
                if (!i.keepLooking) break;
            }
        }
        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents && responses != null) Debug.Log($"kojoResponse [{responses.message}]");
        return responses;
    }

    public MessageCollect_KojoEntry GetResponse(Character_Relationship rel, List<string> selfTags, List<string> targetTags, EvaluationPackage ep = null)
    {
        if (!Validate(rel.Owner, rel.Target)) return null;

        if (debugLogging && scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("Validating kojoResponse [" + ID + "] req [" + rel.Owner.FirstName + "->" + rel.Target.FirstName + "], self[" + String.Join("|", selfTags) + "] target[" + String.Join("|", targetTags) + "]");



        MessageCollect_KojoEntry responses = null;
        bool playerInvolved = ep != null && ep.Package != null && ep.Package.actorRefs.Contains(scr_System_CampaignManager.current.Player.RefID);
        if (!requirePlayer) playerInvolved = true;

        foreach (var i in variants)
        {
            if (i.GetRandomResponse(out var result, rel, ep, selfTags, targetTags, playerInvolved) && result != null)
            {
                if (responses == null) responses = result;
                else responses.nexts.Add(result);
                if (!i.keepLooking) break;
            }
        }
        //if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents && responses != null) Debug.Log($"kojoResponse [{responses.message}]");
        return responses;
    }

    public MessageCollect_KojoEntry GetResponse(Character_Trainable owner, List<string> selfTags, List<EvaluationPackage> allEPs)
    {
        if (!Validate(owner)) return null;

        Character_Relationship relDoer, relReceiver, relSelf;
        List<int> allChara = null;
        MessageCollect_KojoEntry responses = null, responses2 = null;
        bool isValid = false;
        var newlist = new List<EvaluationPackage>(allEPs);
        foreach (var i in variants)
        {   // allEP might all have different doer receivers. Validate all in variant priority order, if any one is valid, then everyone in EP will get same treatment (cuz here we assume they all acting together

            isValid = false;
            foreach (var ep in newlist)
            {
                relSelf = owner == ep.Doer ? owner.Relationships.FindRelationshipWith(ep.Doer) : null;
                relDoer = owner == ep.Doer ? null : owner.Relationships.FindRelationshipWith(ep.Doer);
                relReceiver = owner == ep.Receiver ? null : owner.Relationships.FindRelationshipWith(ep.Receiver);
                bool playerInvolved = ep != null && ep.Package != null && ep.Package.actorRefs.Contains(scr_System_CampaignManager.current.Player.RefID);

                if (!Validate(owner, ep.Doer)) continue;
                //if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log(owner.FirstName + " kojo getresponse past validate before doer/receiver validate, relOwner [" + (relDoer == null ? "null " + (ep.Doer == null ? "" : ep.Doer.FirstName) : relDoer.Owner.FirstName + "->" + relDoer.Target.FirstName) + "] relreceiver[" + (relReceiver == null ? "null " + (ep.Receiver == null ? "" : ep.Receiver.FirstName) : relReceiver.Owner.FirstName + "->" + relReceiver.Target.FirstName) + "]");
                if (relDoer != null && i.GetRandomResponse(out var v1, relDoer, ep, selfTags, ep.DoerTargetTag, playerInvolved, true))
                {
                    isValid = true;
                    if (v1 == null) break;
                    if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("finding epdescription " + v1 + ", replace with " + ep.Description_Ongoing);
                    // v1.message = v1.message.Replace("$epDescription$", ep.Package.targetCOM.DisplayName(ep.Package.COMVariantID));
                    v1.message = v1.message.Replace("$epDescription$", ep.Description_Ongoing);
                    if (responses == null) responses = v1;
                    else
                    {
                        responses.nexts.Add(v1);
                        responses2 = v1;
                    }
                    break;
                }
                else if (relReceiver != null && i.GetRandomResponse(out var v2, relReceiver, ep, selfTags, ep.ReceiverTargetTag, playerInvolved, true))
                {
                    isValid = true;
                    if (v2 == null) break;
                    if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("finding epdescription " + v2 + ", replace with " + ep.Description_Ongoing);
                   //v2.message = v2.message.Replace("$epDescription$", ep.Package.targetCOM.DisplayName(ep.Package.COMVariantID));
                    v2.message = v2.message.Replace("$epDescription$", ep.Package.targetCOM.DisplayName(ep.Package.COMVariantID));
                    if (responses == null) responses = v2;
                    else
                    {
                        responses.nexts.Add(v2);
                        responses2 = v2;
                    }
                    break;
                }
                else if (relSelf != null && relDoer == null && relReceiver == null && ep.Receiver == null && i.GetRandomResponse(out var v3, relSelf, ep, selfTags, ep.DoerTargetTag, playerInvolved, true))
                {   // relDoer == null && relReceiver == null
                    // self referencing package, both null, ep.doer is relOwner and ep.receiver == null

                    // i.isValid(relSelf, selfTags, ep.DoerTargetTag, ep) &&

                    isValid = true;
                    //var v = i.GetRandomResponse(relSelf, ep, selfTags, ep.ReceiverTargetTag, playerInvolved, true);
                    if (v3 == null) break;
                    if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("finding epdescription " + v3 + ", replace with " + ep.Description_Ongoing);
                    //v3.message = v3.message.Replace("$epDescription$", ep.Package.targetCOM.DisplayName(ep.Package.COMVariantID));
                    v3.message = v3.message.Replace("$epDescription$", ep.Description_Ongoing);
                    if (responses == null) responses = v3;
                    else
                    {
                        responses.nexts.Add(v3);
                        responses2 = v3;
                    }
                    break;

                }
            }

            if (isValid)
            {
                // do execution
                //Debug.LogError("KOJO CHECKINTERRUPT TRUE IS VALID: " + String.Join("|", responses));

                allChara = new List<int>();
                foreach (var ep in newlist)
                {
                    if (ep.Doer != null && !allChara.Contains(ep.Doer.RefID))
                    {
                        relDoer = owner.Relationships.FindRelationshipWith(ep.Doer);
                        foreach (var ii in i.results) ii.Execute(responses2 != null ? responses2 : responses, relDoer, selfTags, ep.DoerTargetTag);
                        allChara.Add(ep.Doer.RefID);
                    }
                    if (ep.Receiver != null && !allChara.Contains(ep.Receiver.RefID))
                    {
                        relReceiver = owner.Relationships.FindRelationshipWith(ep.Receiver);
                        foreach (var ii in i.results) ii.Execute(responses2 != null ? responses2 : responses, relReceiver, selfTags, ep.ReceiverTargetTag);
                        allChara.Add(ep.Receiver.RefID);
                    }
                }

                if (!i.keepLooking) break;// String.Join("\n", responses);
            }
        }
        //if (!isValid) Debug.LogError($"cannot find response for {owner.FirstName} on ID {ID}, listEP_count: {allEPs.Count}");


        //var str = String.Join("\n", responses);
        return responses;
    }

    public void RemoveVariantsByTag(string tag)
    {
        this.variants.RemoveAll(x => x.tags.Contains(tag));
    }
    public MessageCollect_KojoEntry GetResponse(Character_Relationship rel, List<EvaluationPackage> selfEPs, List<EvaluationPackage> targetEPs)
    {
        if (!Validate(rel.Owner, rel.Target)) return null;

        List<string> ss1 = new List<string>();
        List<string> ss2 = new List<string>();
        foreach (var i in selfEPs) ss1.Add(i.ToString());
        foreach (var i in targetEPs) ss2.Add(i.ToString());
        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("Validating kojo req Multiple EPs [" + rel.Owner.FirstName + "->" + rel.Target.FirstName + "], self[" + String.Join("|", ss1) + "] target[" + String.Join("|", ss2) + "]");



        var sTags = new List<string>();
        var tTags = new List<string>();

        MessageCollect_KojoEntry ss = null;


        foreach (var ep in selfEPs)
        {
            sTags = (rel.Owner.RefID == ep.DoerRef ? ep.DoerTargetTag : (rel.Owner.RefID == ep.ReceiverRef ? ep.ReceiverTargetTag : new List<string>()));
            tTags = (rel.Target.RefID == ep.DoerRef ? ep.DoerTargetTag : (rel.Target.RefID == ep.ReceiverRef ? ep.ReceiverTargetTag : new List<string>()));

            ss = GetResponse(rel, sTags, tTags, ep);
            if (ss != null) return ss;
        }

        foreach (var ep in targetEPs)
        {
            sTags = (rel.Owner.RefID == ep.DoerRef ? ep.DoerTargetTag : (rel.Owner.RefID == ep.ReceiverRef ? ep.ReceiverTargetTag : new List<string>()));
            tTags = (rel.Target.RefID == ep.DoerRef ? ep.DoerTargetTag : (rel.Target.RefID == ep.ReceiverRef ? ep.ReceiverTargetTag : new List<string>()));

            ss = GetResponse(rel, sTags, tTags, ep);
            if (ss != null) return ss;
        }

        UtilityEX.GetInteractionTagsFrom(rel.Owner, rel.Target, null, -1, ref sTags, ref tTags, ref tTags);
        return GetResponse(rel, sTags, tTags, null);
    }

    public MessageCollect_KojoEntry GetResponse(Character_Relationship rel, List<string> selfTags, List<string> targetTags)
    {
        if (!Validate(rel.Owner, rel.Target)) return null;
        return GetResponse(rel, selfTags, targetTags, null);
    }

}
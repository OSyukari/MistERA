using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;


public class KojoCollector : I_ResultStorage, I_Records
{
    public VisibilityLevel Visibility = VisibilityLevel.Roomwide;
    public List<int> relevantActorRefs = new List<int>();
    public bool autoAnimate = false;
    public void LoadRelevantActors(List<int> list)
    {
        if (list == null) return;
        relevantActorRefs.AddRange(list);
        relevantActorRefs = Utility.Distinct(relevantActorRefs);
    }
    public bool DirectlyRelated(Character_Trainable c)
    {
        return c == null || Owner == c || Target == c || doerRef == c.RefID || receiverRef == c.RefID;
    }
    [JsonIgnore]
    public bool requestAccepted
    {
        get
        {
            return this.isRequestAccepted;
        }
    }
    [JsonIgnore]
    public bool HasPermission
    {
        get
        {
            return this.hasPermission;
        }
    }
    [JsonIgnore]
    public bool isForced
    {
        get
        {
            return this.IsForced;
        }
    }

    public string tooltip = "";
    public bool VisibleTo(Character_Trainable c, Room_Instance room = null)
    {
        if (c == null) return true;
        if (collect == null) return false;
        if (Visibility == VisibilityLevel.Global) return true;
        if (room != null && !room.RoomChara.Contains(c)) return false;
        return DirectlyRelated(c) || relevantActorRefs.Contains(c.RefID);
    }

    // validate target baseID

    Character_Trainable _owner = null;
    [JsonIgnore]
    public Character_Trainable Owner
    {
        get
        {
            if (_owner == null && selfRef != -1)
            {
                _owner = scr_System_CampaignManager.current.FindInstanceByID(selfRef);
            }
            return _owner;

        }
    }

    Character_Trainable _target = null;
    [JsonIgnore]
    public Character_Trainable Target
    {
        get
        {
            if (_target == null && targetRef != -1)
            {
                _target = scr_System_CampaignManager.current.FindInstanceByID(targetRef);
            }
            return _target;

        }
        set
        {
            _target = value;
            targetRef = value == null ? -1 : value.RefID;
        }
    }



    [JsonProperty] protected string _eventID = "";
    [JsonIgnore] 
    public string eventID
    {
        get
        {
            return _eventID;
        }
        set
        {
            if (value == null || value.Length < 1) _eventID = "";
            else if (value.Length > 0 && value.Contains("_noSex")) _eventID = value.Substring(0, value.Length - 6);
            else _eventID = value;
        }
    }
    public string suffix = "";

    public Memory_Attitude GetActorAttitude(int actorRef)
    {
        if (doerRef == actorRef) return doerAttitude;
        else if (receiverRef == actorRef) return receiverAttitude;
        else return Memory_Attitude.None;
    }
    public int comVariantID = -1;
    [JsonIgnore] public int VariantID { get { return comVariantID; } }


    Character_Relationship _relation = null;
    [JsonIgnore]
    public Character_Relationship Relation
    {
        get
        {
            if (_relation == null && Owner != null && Target != null)
            {
                _relation = Owner.Relationships.FindRelationshipWith(Target);
            }
            return _relation;
        }
    }

    [JsonIgnore]
    public Memory_Response Response
    {
        get
        {
            return response;
        }
    }

    public KojoCollector Copy()
    {
        var newinstance = new KojoCollector(this.Owner, this.eventID, this.suffix);
        newinstance.comVariantID = comVariantID;
        newinstance.selfTags = new List<string>(selfTags);
        newinstance.targetTags = new List<string>(targetTags);
        newinstance.rightAlign = rightAlign;
        newinstance.isDoer = isDoer;
        newinstance.selfRef = selfRef;
        newinstance.targetRef = targetRef;
        newinstance.doerRef = doerRef;
        newinstance.doerAttitude = doerAttitude;
        newinstance.receiverAttitude = receiverAttitude;
        newinstance.receiverRef = receiverRef;
        newinstance.masterRef = masterRef;
        newinstance.response = response;
        newinstance.commandID = commandID;
        newinstance.comVariantID = comVariantID;
        newinstance.isRequestAccepted = isRequestAccepted;
        newinstance.hasPackageData = hasPackageData;
        newinstance.hasPermission = hasPermission;
        newinstance.isStrongP = isStrongP;
        newinstance.isPlayerInvolved = isPlayerInvolved;
        newinstance.timestamp = timestamp;

        return newinstance;
    }

    public void LoadSelfTags(Character_Trainable c, List<string> extraTags)
    {
        selfRef = c.RefID;
        _owner = c;
        if (extraTags != null) this.selfTags.AddRange(extraTags);
        UtilityEX.GetActorTag(ref this.selfTags, c);
    }

    [JsonIgnore] public List<string> SelfTags { get { return selfTags; } }
    [JsonProperty] protected List<string> selfTags = new List<string>();
    public List<string> targetTags = new List<string>();

    public bool requireAnimate = true;

    public bool rightAlign = false;
    public bool isDoer = true;
    public bool isPlayerInvolved = false;

    public MessageCollect_KojoEntry collect = null;


    // first, will need to get self and target character instance
    public int selfRef = -1;
    public int targetRef = -1;
    public int doerRef = -1;
    public Memory_Attitude doerAttitude = Memory_Attitude.None;
    public int receiverRef = -1;
    public Memory_Attitude receiverAttitude = Memory_Attitude.None;
    public int masterRef = -1;
    public Memory_Response response = Memory_Response.Accept;

    public string commandID = "";

    COM _targetCOM = null;
    [JsonIgnore]
    public COM targetCOM { get
        {
            if (_targetCOM == null && commandID != "")
            {
                _targetCOM = scr_System_Serializer.current.MasterList.COMs.GetByID(commandID);
            }
            return _targetCOM;
        } }


    // if relationship exist -> it will always exist
    // 

    // validate ep

    /*
    - success?
    - attitude
    - permission
    */
    public bool isRequestAccepted = true;
    public bool hasPermission = true;
    public bool IsForced = false;
    public bool hasPackageData = false;
    public bool isStrongP = false;

    public KojoCollector() { }
    public KojoCollector(Character_Trainable c, string eventID, string suffix = "", VisibilityLevel visibility = VisibilityLevel.Roomwide)
    {
        this.eventID = eventID;
        selfRef = c.RefID;
        this.suffix = suffix;
        this.Visibility = visibility;
        this.timestamp = scr_System_Time.current.getCurrentTime();
    }

    public void LoadRel(Character_Relationship rel)
    {
        if (rel == null)
        {
            Debug.LogError("error loadrel null");
            return;
        }else if (this.Owner != null && this.Owner != rel.Owner)
        {
            Debug.LogError("ERROR LOADREL WRONG REL OWNER");
            return;
        }
        this.Target = rel.Target;
        this.isPlayerInvolved = this.isPlayerInvolved 
            || (Owner.CurrentJob != null && Owner.CurrentJob.actorRefID.Contains(0)) 
            || (Target.CurrentJob != null && Target.CurrentJob.actorRefID.Contains(0));

        UtilityEX.GetActorTag(ref this.selfTags, Owner);

        UtilityEX.GetActorTag(ref this.targetTags, Target);
    }

    /// <summary>
    /// Load EP data.
    /// </summary>
    /// <param name="loadReceiver">if True, load receiver's relationship</param>
    public void LoadEP(EvaluationPackage ep, Character_Trainable target)
    {
        if (ep == null) return;
        hasPackageData = true;
        hasPermission = ep.hasPermission;
        isStrongP = ep.isStrongP;

        if (ep.Doer == Owner)
        {
            if (ep.Receiver == null) this.selfTags = ep.DoerTargetTag;
            else this.selfTags = ep.DoerSelfTag;

            UtilityEX.GetActorTag(ref this.selfTags, ep.Doer);
        }
        else if (ep.Receiver == Owner)
        {
            this.selfTags = ep.ReceiverSelfTag;

            UtilityEX.GetActorTag(ref this.selfTags, ep.Receiver);
        }

        if (ep.Receiver != null)
        {
            receiverRef = ep.Receiver.RefID;
            if (target == ep.Receiver)
            {
                Target = ep.Receiver;
                targetTags = ep.ReceiverTargetTag;
                UtilityEX.GetActorTag(ref this.targetTags, ep.Receiver);
            }
            receiverAttitude = ep.ReceiverAttitude;
        }
        if (ep.Doer != null)
        {
            doerRef = ep.Doer.RefID;
            if (target == ep.Doer)
            {
                Target = ep.Doer;
                targetTags = ep.DoerTargetTag;
                UtilityEX.GetActorTag(ref this.targetTags, ep.Doer);
            }
            doerAttitude = ep.DoerAttitude;
        }

        if (ep.Master != null) masterRef = ep.Master.RefID;
        else if (ep.Doer != null) masterRef = ep.Doer.RefID;

        if (ep.targetCOM != null)
        {
            this.commandID = ep.targetCOM.ID;
            this.comVariantID = ep.VariantID;
        }

        if (Target == null && target != null)
        {
            Target = target;
        }

        IsForced = ep.isForced;

        isPlayerInvolved = isPlayerInvolved || ep.Package.actorRefs.Contains(scr_System_CampaignManager.current.Player.RefID);
        isRequestAccepted = ep.requestAccepted;
        response = ep.Response;
    }

    public bool RightAlign(Character_Trainable c)
    {
        return rightAlign || !DirectlyRelated(c);
    }

    public DateTime timestamp = DateTime.MinValue;
    [JsonIgnore] public DateTime Timestamp { get { return timestamp; } }

    public void ReplaceString(string oldstring, string newstring)
    {
        if (collect != null)
        {
            collect.ReplaceString(oldstring, newstring);
        }
    }
}


using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Xml;
using System.Runtime.Serialization;
using System.Linq;
using Newtonsoft.Json;

[System.Serializable]
public abstract class StatusBase : ISerializationCallbackReceiver
{

    public string statusID = "";
    public string displayName = "";
    public bool noDisplay = false;
    public bool constant = false;

   [JsonIgnore] public bool isValid
    {
        get
        {
            if (this.statusID != "") return true;
            return false;
        }
    }

    public List<Variant> variants = new List<Variant>();

    [System.Serializable]
    public class Variant
    {
        public string displayName = "";
        public float threshold = -1;
        public List<string> tags = new List<string>();
        public List<Stat_Modifier> stat_modifiers = new List<Stat_Modifier>();
    }

    public virtual void OnBeforeSerialize()
    {

    }

    public virtual void OnAfterDeserialize()
    {

    }
}


[System.Serializable]
public abstract class StatusInstance : ISerializationCallbackReceiver
{
    [SerializeField][JsonProperty] protected string baseID;
    [JsonIgnore] public string ID { get { return baseID; } }

    public int duration = -1;

    [JsonIgnore] public virtual float Severity
    {
        get
        {
            //else if (this.BaseRef.variationMode.variationType == Status_Base.Status_Variation_Type.condition)
            //{
            //    severity = this.BaseRef.variationMode.Validate(Owner);
            //}

            return 0f;
        }
    }

    [SerializeField][JsonProperty] protected float severity;
    public int pauseXMinAfterMod = 0;

    protected int ownerRef = -1;
    protected Character_Trainable owner = null;
    [JsonIgnore] public Character_Trainable Owner
    {
        get
        {
            if (owner == null && ownerRef > -1) owner = scr_System_CampaignManager.current.FindInstanceByID(ownerRef);
            return owner;
        }
    }

    public StatusInstance()
    {

    }
    public StatusInstance(StatusBase baseStatus, int refID, float initialSeverity = 0f, int duration = -1)
    {
        this.ownerRef = refID;
        this.baseID = baseStatus.statusID;
        this.duration = duration;
        if (initialSeverity < 0.001f && initialSeverity > -0.001f) this.severity = Math.Clamp(0f, 0, 0);
        else this.severity = initialSeverity;
    }

    public void ReEstablishParent(Character_Trainable c)
    {
        this.owner = c;
        this.ownerRef = c.RefID;
    }

    public void OnBeforeSerialize()
    {

    }

    public void OnAfterDeserialize()
    {

    }
}


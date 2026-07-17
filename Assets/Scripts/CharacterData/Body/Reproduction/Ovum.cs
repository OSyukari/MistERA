using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;
using UnityEngine.UIElements;


public class Ovum
{
    public float ovumPower = 0;
    public float fertility = 0;

    // ── Filled by the womb on creation ────────────────────────────────────
    public int lifespan, totalLifespan;              // viability window in in-game minutes

    public float fertilizationChance;  // per 1ml of cum, checked once per hour

    public Ovum() { }

    public Ovum(BodyInternal_Womb womb, Character_Trainable owner, ReproductionTemplate template)
    {
        // TODO more codes on ovum

        /*
        Fertilization procedure:
        1. check owner allow fert from all, or if fertilization floor.
        2. check target, if target can fertilize all, or if fertilization floor
        3. if both not same race, fert / 2
        4. if not sharing a same tag, fert / 2
        5. floor value

        Determining gender and race? random from parent race. check if they restrict gender though
         */
        this.womb = womb;

        lifespan = template.ovumLifespanMinutes;
        totalLifespan = template.ovumLifespanMinutes;
        fertilizationChance = template.fertilizationChance;
        Owner = owner;
    }

    [JsonIgnore]
    public Character_Trainable Owner
    {
        get
        {
            if (_owner == null && ownerRef != -1)
            {
                _owner = scr_System_CampaignManager.current.FindInstanceByID(ownerRef);
            }
            return _owner;
        }
        set
        {
            _owner = value;
            ownerRef = value == null ? -1 : value.RefID;
        }
    }
    Character_Trainable _owner = null;
    [JsonProperty]
    protected int ownerRef = -1;

    public Item_Instance_Cum father = null;

    public void Fertilize(Item_Instance_Cum fertilizer)
    {
        // if fertilized, store father ref
        this.father = fertilizer;

        var masterlist = scr_System_Serializer.current.MasterList.humanoid_Races;
        List<string> validtemplates = new List<string>();

        var fatherf = masterlist.CollectValidFoetus(father.raceID, father.baseID);
        var fatherops = new List<string>();
        var motherf = masterlist.CollectValidFoetus(Owner.Race.ID, Owner.BaseID);
        var motherops = new List<string>();

        if (fatherf != null)
        {
            fatherops.AddRange(fatherf.offspring_templates);
            if (father.templateID != "" && !fatherops.Contains(father.templateID))
            {
                var template = scr_System_Serializer.current.MasterList.Character_Bases.GetGeneratorByID(father.templateID);
                if (template != null && template.allowDuplicateID) fatherops.Add(father.templateID);
            }
        }

        if (motherf != null)
        {
            motherops.AddRange(motherf.offspring_templates);
            if (Owner.baseTemplateID != "" && !motherops.Contains(Owner.baseTemplateID))
            {
                var template = scr_System_Serializer.current.MasterList.Character_Bases.GetGeneratorByID(Owner.baseTemplateID);
                if (template != null && template.allowDuplicateID) motherops.Add(Owner.baseTemplateID);
            }
        }

        var total = fatherops.Count + motherops.Count;
        if (total < 1)
        {
            Debug.LogError($"error fertilization failed, father {fatherf != null} {fatherops.Count} mother {motherf != null} {motherops.Count}");
            return;
        }
        foetus = new FoetusTemplates();
        var diceroll = Utility.Dice(1, fatherops.Count + motherops.Count);

        // random select one
        if (total <= fatherops.Count)
        {
            foetus.MergeWith(fatherf);
            foetus.offspring_templates = fatherops;
        }
        else
        {
            foetus.MergeWith(motherf);
            foetus.offspring_templates = motherops;
        }

        State = OvumState.Fertilized;
        this.lifespan = 0;
    }

    [JsonIgnore]
    public string Tooltip
    {
        get
        {
            return $"state {this.State}\nlifespan {lifespan}\nsize {(foetusItem == null ? "0" : $"{foetusItem.GetComp_Ingestible().amount}")}\nmaxsize {(foetus == null ? "0" : $"{foetus.size_end}")}";
        }
    }

    public string GetImage(bool hasMultiplet)
    {
        if (State == OvumState.Fertilized)
        {
            var ratio = lifespan / foetus.duration_fertilized;
            var index = Math.Clamp(ratio * ReproductionUtility.fertilizedStages.Length, 0, ReproductionUtility.fertilizedStages.Length - 1);
            return ReproductionUtility.fertilizedStages[index];
        }
        if (State == OvumState.Implanted)
        {
            return ReproductionUtility.egg_implanted;
        }
        if (State > OvumState.Implanted)
        {
            float ratio = (float)lifespan / (float)(foetus.duration_first + foetus.duration_second + foetus.duration_third);

            if (hasMultiplet && foetus.images_multiplet?.Count > 0)
            {
                var index = (int)Math.Clamp(ratio * foetus.images_multiplet.Count, 0, foetus.images_multiplet.Count - 1);
                return foetus.images_multiplet[index];
            }
            else
            {
                var index = (int)Math.Clamp(ratio * foetus.images.Count, 0, foetus.images.Count - 1);
                return foetus.images[index];
            }
        }
        return "";
    }

    [JsonProperty] public FoetusTemplates foetus = null;

    public void HourTick(BodyInternal_Womb wb)
    {
        if (State == OvumState.Default)
        {
            lifespan -= 60;
            if (lifespan <= 0) State = OvumState.Aborted;
            return;
        }
        // advance ovum stage
        if (State == OvumState.Fertilized)
        {
            if (foetus == null) State = OvumState.Aborted;
            else
            {
                foetus.Advance(this);
            }
        }
        else if (State == OvumState.Final_RequireHelp)
        {
            // stuck, require help
        }
        else if (State == OvumState.Final)
        {
            // foetus no longer advance
            lifespan += 60; // was reset to 0 when state change
        }
        else if (State > OvumState.Fertilized)
        {
            if (foetus == null) State = OvumState.Aborted;
            else if (foetusItem == null || !wb.source.HasContent(foetusItem))
            {
                State = OvumState.Aborted;
            }
            else
            {
                foetus.Advance(this);
            }

        }

    }

    string _ovumname = null;
    [JsonIgnore]
    public string OvumName
    { get
        {
            if (_ovumname == null)
            {
                _ovumname = LocalizeDictionary.QueryThenParse("ovum_finalName")
                    .Replace("$mother$", Owner.FirstName)
                    .Replace("$father$", father.FatherName);
            }
            return _ovumname;
        } }


    [JsonProperty] protected int foetusItemRef = -1;
    Item_Instance _foetusItem = null;
    [JsonIgnore]
    public Item_Instance foetusItem
    {
        get
        {
            if (_foetusItem == null && foetusItemRef != -1)
            {
                _foetusItem = scr_System_CampaignManager.current.FindItemInstanceByID(foetusItemRef);
            }
            return _foetusItem;
        }
        set
        {
            _foetusItem = value;
            foetusItemRef = value == null ? -1 : value.RefID;
        }
    }
    public bool isOlderThan(Ovum ov)
    {
        if (ov == null) return true;
        if (this.State != ov.State) return this.State > ov.State;
        return this.lifespan > ov.lifespan;
    }

    public OvumState State = OvumState.Default;
    [JsonIgnore]
    public int FertilizedStage
    {
        get
        {
            if (State != OvumState.Fertilized) return -1;
            return 0;
        }
    }
    [JsonIgnore]
    public int ReleaseStage
    {
        get
        {
            if (State != OvumState.Default) return -1;
            var diff = totalLifespan - lifespan;
            if (diff > 240) return -1;
            else if (diff >= 120) return 1;
            return 0;
        }
    }

    [JsonIgnore] public BodyInternal_Womb womb; // injected
}

public enum OvumState
{
    Default,
    Aborted,
    Fertilized,
    // created item in womb
    Implanted,
    // 1st to 3rd trimester are here to distinguish phase and apply different debuff
    First_trimester,
    Second_trimester,
    Third_trimester,
    // giving birth
    Final,
    Final_RequireHelp
}
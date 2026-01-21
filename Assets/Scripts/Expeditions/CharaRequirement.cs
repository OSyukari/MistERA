using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

[System.Serializable]
public class CharaReq
{

    public List<string> BodyTags = new List<string>();
    public int minRevealingScore = -1;

    public int cost_EN = 0;
    public int cost_ST = 0;

    public bool allowPlayer = true;
    public bool allowNPC = true;

    public bool requireConscious = true;
    public bool requireUnconscious = false;
    // require conscious to react, like work.
    // action that do not require conscious are action that are done unilaterally
    public bool requireUnrestrained = false;
    public bool requireAction = true;
    public bool requireNoTeammate = false;
    public bool requireFollowing = false;
    public bool requireNotFollowing = false;
    public bool requireTimestopped = false;
    public bool addPartyMembers = false;
    public bool requireUndressed = false;
    public bool requireMovement = false;
    public bool requireCombat = false;
    public bool requireFullHP = false;
    public bool requireMissingHP = false;
    //public bool requireAroused = false;

    public bool requireMale = false;
    public bool requireFemale = false;

    public List<string> requireInflatedBodyTags = new List<string>();
    public List<string> requireExtremeInflatedBodyTags = new List<string>();

    public List<string> requireAbsentJobwithCOMTag = new List<string>();
    public List<string> requireExistingJobwithCOMTag = new List<string>();
    public void Read(CharaReq req)
    {
        this.BodyTags.AddRange(req.BodyTags);
        this.requireAbsentJobwithCOMTag.AddRange(req.requireAbsentJobwithCOMTag);
        this.requireExistingJobwithCOMTag.AddRange(req.requireExistingJobwithCOMTag);
        this.BodyTags = this.BodyTags.Distinct().ToList();
        requireConscious = requireConscious && req.requireConscious;
        requireUnrestrained = requireUnrestrained || req.requireUnrestrained;
        requireMovement = requireMovement || req.requireMovement;
        requireAction = requireAction && req.requireAction;
        requireMale = this.requireMale || req.requireMale;
        requireFemale = this.requireFemale || req.requireFemale;

        requireUnconscious = requireUnconscious || req.requireUnconscious;
        requireFollowing = requireFollowing || req.requireFollowing;
        requireNotFollowing = requireNotFollowing || req.requireNotFollowing;
        requireTimestopped = requireTimestopped || req.requireTimestopped;

        //requireAroused = this.requireAroused || req.requireAroused;
        if (this.minRevealingScore == -1 && req.minRevealingScore != -1) this.minRevealingScore = req.minRevealingScore;
        if (this.cost_EN == 0 && req.cost_EN != 0) this.cost_EN = req.cost_EN;
        if (this.cost_ST == 0 && req.cost_ST != 0) this.cost_ST = req.cost_ST;
        this.addPartyMembers = this.addPartyMembers || req.addPartyMembers;
        this.requireNoTeammate = this.requireNoTeammate || req.requireNoTeammate;

        this.requireUndressed = this.requireUndressed || req.requireUndressed;
        this.requireCombat = this.requireCombat || req.requireCombat;
        this.requireFullHP = this.requireFullHP || req.requireFullHP;
        this.requireMissingHP = this.requireMissingHP || req.requireMissingHP;

    }
}
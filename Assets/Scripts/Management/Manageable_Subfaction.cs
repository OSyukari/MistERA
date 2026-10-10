using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


public class Manageable_Subfaction : Manageable
{
    [JsonIgnore] public override I_IsJobGiver getLocaleFaction { get {
            if (this.Parent != null) return this.Parent;
            else return this; 
        } }
    public Manageable_Subfaction()
    {

    }

    [JsonIgnore]
    public override Room_Instance MainExit
    {
        get
        {
            if (this.Parent != null) return this.Parent.MainExit;
            else return null;
        }
    }
    /// <summary>
    /// Its own mainExit room from its MapPlan (e.g. a mall shop's sportswear_room), not the parent's MainExit: the rally
    /// job only counts a character as arrived inside a room this faction owns, which the parent's room never is.
    /// </summary>
    [JsonIgnore]
    public override Room_Instance RallyRoom
    {
        get
        {
            return base.MainExit ?? MainExit;
        }
    }
    [JsonIgnore]
    public override List<Manageable> ConnectedFactions
    {
        get
        {
            if (this.Parent != null) return this.Parent.ConnectedFactions;
            else return new List<Manageable>();
        }
    }
    [JsonIgnore]
    public override int MainExitCost
    {
        get
        {
            if (this.Parent != null) return this.Parent.MainExitCost;
            else return base.MainExitCost;
        }
    }

    [JsonIgnore] public Manageable Parent
    {
        get
        {
            if (_parent == null && parentID != "")
            {
                _parent = scr_System_CampaignManager.current.FindFactionByID(parentID);
            }
            return _parent;
        }
    }

    Manageable _parent = null;
    [JsonProperty] protected string parentID = "";

    public Manageable_Subfaction(string id, Manageable parent) : base(id)
    {
        _parent = parent;
        parentID = parent == null ? "" : parent.ID;
    }

    

}


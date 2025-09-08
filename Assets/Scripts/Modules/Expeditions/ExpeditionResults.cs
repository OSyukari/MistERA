using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;


public class ExpResults
{
    public int baseWeight = 0;
    public ExpEvents.TeamReq teamRequirement = new ExpEvents.TeamReq();
    public List<ExpEvents.WeightModifier> weightMods = new List<ExpEvents.WeightModifier>();

    public string resultText = "";

    public Result_Character results_character = null;

}
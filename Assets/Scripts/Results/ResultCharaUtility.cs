using System.Collections.Generic;

public static class ResultCharaUtility
{

    public static void Apply(Result_Character r, EvaluationPackage m, Character_Trainable c, bool isDoer, bool isReceiver, ExperienceLog log)
    {
        if (r.entry_conditions != null && !ValidateCondition(r.entry_conditions, isDoer, isReceiver, m.GetActorAttitude(c.RefID))) return;
        //Debug.Log("COM_Results Result_Character from " + m.Package.targetCOM.displayName + " Apply on " + c.FirstName +" passed condition validation !");
        if (r.entry_results != null) ApplyResult(r.entry_results, m.job.FactionOwner, c, log);
    }

    public static void Apply(Result_Character r, I_IsJobGiver faction, Character_Trainable c, List<string> tooltips = null)
    {
        if (r.entry_conditions != null && !ValidateCondition(r.entry_conditions, true, false, Memory_Attitude.None)) return;
        //Debug.Log("COM_Results Result_Character from " + m.Package.targetCOM.displayName + " Apply on " + c.FirstName +" passed condition validation !");
        if (r.entry_results != null) ApplyResult(r.entry_results, faction, c, null, tooltips);
    }

    public static bool ValidateCondition(Result_Character.Entry_Condition r, bool isDoer, bool isReceiver, Memory_Attitude att)
    {
        //Debug.LogError("Validating EntryCondition isDoer["+ isDoer + "] isReceiver[" + isReceiver + "] attitude[" + att + "]");
        if ((r.applyToDoer && isDoer) || (r.applyToReceiver && isReceiver))
        {

        }
        else return false;
        if (r.attitudeLTE != (int)Memory_Attitude.None && (int)att > r.attitudeLTE) return false;
        if (r.attitudeGTE != (int)Memory_Attitude.None && (int)att < r.attitudeGTE) return false;
        return true;
    }

    public static void ApplyResult(Result_Character.Entry_Result r, I_IsJobGiver jobOwner, Character_Trainable c, ExperienceLog log = null, List<string> tooltips = null)
    {
        int i;
        //Debug.Log("COM_Results Result_Character from " + m.Package.targetCOM.displayName + " Apply on " + c.FirstName + " applying EntryResult "+type);
        switch (r.type)
        {
            case CharaResultType.statMod_ST:
                if (!int.TryParse(r.value, out i) || c.Stats.Stamina == null) break;
                c.Stats.Stamina.ModValue(i);
                if (log != null) log.AddStats(c.RefID, "stats_derived_extended_stamina", i);
                break;
            case CharaResultType.statMod_EN:
                if (!int.TryParse(r.value, out i) || c.Stats.Energy == null) break;
                c.Stats.Energy.ModValue(i);
                if (log != null) log.AddStats(c.RefID, "stats_derived_extended_energy", i);
                break;
            case CharaResultType.statMod_HP:
                if (!int.TryParse(r.value, out i) || c.Stats.HP == null) break;
                c.Stats.HP.ModValue(i);
                if (log != null) log.AddStats(c.RefID, "stats_derived_extended_hp", i);
                break;
            case CharaResultType.statMod_MP:
                if (!int.TryParse(r.value, out i) || c.Stats.MP == null) break;
                c.Stats.MP.ModValue(i);
                if (log != null) log.AddStats(c.RefID, "stats_derived_extended_mp", i);
                break;
            case CharaResultType.redress:
                c.Redress();
                break;
            default: break;
        }
        if (r.useItemFromTargetInventory != "")
        {
            Item_Instance instance = null;
            if (jobOwner != null) instance = jobOwner.Inventory.RemoveItem(r.useItemFromTargetInventory);
            if (instance != null && instance.GetComp_Ingestible() != null) c.Body.ConsumeIngestible(instance);
            // Debug.Log("Applying COM Result, useItemFromTargetInventory[" + useItemFromTargetInventory + "], factionOwner?[" + (m.job.FactionOwner != null) + "] instance?[" + (instance != null) + "]");
        }

        if (tooltips != null) tooltips.Add($"{c.CallName} {r.Print}");

    }
}
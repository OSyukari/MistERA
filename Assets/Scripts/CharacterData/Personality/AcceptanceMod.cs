using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// PersonalityAcceptanceMod will be checked in the context of ActionPackage's command request check.
/// it will have access to: ActionPackage, Self, all of ActionPackage's participants as Target (list iterate)
/// Most of the requirement objects can validate themselves,
///
/// THE EXCEPTION IS RequireKojoVariable. KojoVariable require very specific targeting setting,
/// with these possible relationship configuration:
/// - owner self, target self (owner's self rel)
/// - owner self, target target (owner's relationship with target)
/// - owner target, target self (target's relationship with owner)
/// - owner target, target target (target's self rel)
/// </summary>
public class PersonalityAcceptanceMod
{
    // self requirement
    public CharaReq SelfReq;

    // action package requirement — quick/temporary, replace with Requirement_ActionPackage later
    public class RequireActionPackage_Quick
    {
        public string targetComID = "";
        public bool isDoer = false;

        public bool Validate(Character_Trainable self, EvaluationPackage ep)
        {
            if (targetComID != "" && (ep.targetCOM == null || ep.targetCOM.ID != targetComID)) return false;
            if (ep.isDoer(self) != isDoer) return false;
            return true;
        }
    }
    public RequireActionPackage_Quick Requirement_ActionPackage = null;


    // kojovariable requirement
    public enum KojoRelScope { Self_Self, Self_Target, Target_Self, Target_Target }
    public class ScopedKojoReq
    {
        public KojoRelScope scope = KojoRelScope.Self_Target;
        public RequireKojoVariable require = new RequireKojoVariable();
    }
    public List<ScopedKojoReq> Requirement_KojoVariables = new List<ScopedKojoReq>();

    // faction requirement
    public Requirement_Faction RequireFaction;

    // target requirement
    public CharaReq TargetReq;


    // PersonalityAcceptanceMod will have a validation call with ActionPackage and Character_Trainable as parameters,
    // and with these it should have everything needed to call validations on each component
    public bool Validate(Character_Trainable self, Character_Trainable target, EvaluationPackage ep, ref List<string> tooltip, out bool hardlock)
    {
        hardlock = false;

        if (Requirement_ActionPackage != null && !Requirement_ActionPackage.Validate(self, ep)) return false;
        if (SelfReq != null && !CharaReqUtility.Validate(SelfReq, ref tooltip, self, out hardlock)) return false;
        if (TargetReq != null && !CharaReqUtility.Validate(TargetReq, ref tooltip, target, out hardlock)) return false;

        if (RequireFaction != null && RequireFaction.isValid && ep.Package.job != null && !RequireFaction.Validate(ep.Package.job.FactionOwner, out var factionTooltip))
        {
            if (tooltip != null && factionTooltip != "") tooltip.Add(factionTooltip);
            return false;
        }

        if (Requirement_KojoVariables != null)
        {
            foreach (var scoped in Requirement_KojoVariables)
            {
                if (scoped == null || scoped.require == null || !scoped.require.isValid) continue;

                Character_Trainable owner = (scoped.scope == KojoRelScope.Self_Self || scoped.scope == KojoRelScope.Self_Target) ? self : target;
                Character_Trainable relTarget = (scoped.scope == KojoRelScope.Target_Self || scoped.scope == KojoRelScope.Self_Self) ? self : target;

                if (owner == null || relTarget == null) return false;

                var rel = owner.Relationships.FindRelationshipWith(relTarget);
                if (rel == null || !scoped.require.Validate(rel)) return false;
            }
        }

        return true;
    }

    // ------------------------

    public Result_ActionPackage Result_ActionPackage = null;
    public Result_EvaluationPackage Result_EvaluationPackage = null;

    public void Apply(EvaluationPackage ep, Character_Trainable self, Character_Trainable target)
    {
        if (Result_ActionPackage != null) Result_ActionPackage.Apply(ep);
        if (Result_EvaluationPackage != null) Result_EvaluationPackage.Apply(ep, self, target);
    }

    // ------------------------

    // nested mods, checked only if this node validates — lets shared conditions live once on the parent
    public List<PersonalityAcceptanceMod> Children = new List<PersonalityAcceptanceMod>();
}

using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Doer/target-specific effects applied onto a single EvaluationPackage's acceptance check.
/// See Result_ActionPackage for the AP-wide (uniform) counterpart.
/// </summary>
public class Result_EvaluationPackage
{
    // add/inject doer/target tag
    public string injectSelfTag = "";
    public string injectTargetTag = "";

    // modify/override acceptance check
    public Memory_Response overrideResponse = Memory_Response.None;
    public string overrideResponseExplanation = "";
    public Memory_Attitude overrideAttitude = Memory_Attitude.None;

    // add acceptance check modifier with custom value and explanation string
    public string modifierExplanation = "";
    public int modifierValue = 0;

    // redirect this side's KOJO dialogue lookup to a different event ID (see EvaluationPackage.SetForcedKojoEventID)
    public string overrideKojoEventID = "";

    public void Apply(EvaluationPackage ep, Character_Trainable self, Character_Trainable target)
    {
        bool isDoer = ep.isDoer(self);
        if (injectSelfTag != "" || injectTargetTag != "")
            ep.AddExtraActorTags(isDoer ? injectSelfTag : injectTargetTag, isDoer ? injectTargetTag : injectSelfTag);

        if (overrideResponse != Memory_Response.None) ep.SetForcedResponse(isDoer, overrideResponse, overrideResponseExplanation);
        if (overrideAttitude != Memory_Attitude.None) ep.SetForcedAttitude(isDoer, overrideAttitude);
        if (modifierExplanation != "" && modifierValue != 0) ep.AddAttitudeModifier(isDoer, modifierExplanation, modifierValue);
        if (overrideKojoEventID != "") ep.SetForcedKojoEventID(isDoer, overrideKojoEventID);
    }
}

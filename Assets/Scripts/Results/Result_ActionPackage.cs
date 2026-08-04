using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Allowed to change parameters inside ActionPackage.
/// AP-wide only (uniform across doer/receiver) — see Result_EvaluationPackage for doer/target-specific effects.
/// </summary>
public class Result_ActionPackage
{
    // add/inject a whole-package COM tag (uniform across doer/receiver)
    public string injectCOMTag = "";

    public void Apply(EvaluationPackage ep)
    {
        if (injectCOMTag == "") return;
        if (!ep.Package.ExtraCOMTags.Contains(injectCOMTag)) ep.Package.AddExtraCOMTag(injectCOMTag);
        ep.AddExtraCOMTags(injectCOMTag);
    }
}

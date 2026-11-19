using System.Collections.Generic;

/// <summary>
/// Who takes part in a command as kojo sees it (Docs/TextAuthoring/Plan_KojoTemplates.md §7.12): COM.kojoParticipants,
/// or - Auto - derived from the requirements of the command and its variants (COM_Requirements.Requirement.Validate):
/// receiverCount &gt; 0 requires a receiver, 0 forbids one, -1 allows any number (none too, unless minReceiverCount);
/// treatReceiverAsDoer / treatDoerAsReceiver make every participant equivalent (actorCount = doers + receivers, so
/// minDoerCount &gt; 1 needs two or more); selfOnly never attaches a receiver. A command that can run several ways
/// (variants) gets the union. Engine-accurate on purpose: a receiverCount -1 command that isn't treat-as-doer can run
/// alone, so Auto says SoloOrPairDistinct - override where it never does.
/// </summary>
public static class TA_KojoParticipants
{
    public static KojoParticipants Resolve(COM com)
    {
        if (com == null) return KojoParticipants.SoloOrPairDistinct;
        if (com.kojoParticipants != KojoParticipants.Auto) return com.kojoParticipants;
        if (com.selfOnly) return KojoParticipants.SoloOnly;

        bool solo = false, distinct = false, mutualPair = false, mutualSolo = false;
        Add(com.requirements?.requirement, ref solo, ref distinct, ref mutualPair, ref mutualSolo);
        for (int i = 0; com.variants != null && i < com.variants.Count; i++)
            Add(com.variants[i]?.requirements?.requirement, ref solo, ref distinct, ref mutualPair, ref mutualSolo);

        if (distinct) return solo ? KojoParticipants.SoloOrPairDistinct : KojoParticipants.PairDistinct;
        if (mutualSolo) return KojoParticipants.SoloOrGroupMutual;
        if (mutualPair) return KojoParticipants.PairMutual;
        return KojoParticipants.SoloOnly;
    }

    /// <summary>What one requirement set allows: alone (solo), a distinct receiver, equivalent participants (pair-only or solo-capable).</summary>
    static void Add(COM_Requirements.Requirement r, ref bool solo, ref bool distinct, ref bool mutualPair, ref bool mutualSolo)
    {
        if (r == null) return;
        if (r.TreatReceiverAsDoer || r.TreatDoerAsReceiver)
        {
            // every participant is equivalent; alone only when one actor is enough
            bool alone = r.minDoerCount <= 1 && r.minReceiverCount <= 0;
            bool group = r.doerCount != 1 && r.receiverCount != 0;
            if (alone && group) mutualSolo = true;
            else if (alone) solo = true;
            else mutualPair = true;
            return;
        }
        if (r.receiverCount == 0) { solo = true; return; }
        distinct = true;
        if (r.receiverCount < 0 && r.minReceiverCount <= 0) solo = true;
    }

    /// <summary>
    /// Which scenario / case sets a command's docs need (TA_CommandDocBuilder, TA_CommandCases): pair = acted on a receiver
    /// (doer / receiver EPs - distinct roles, or equivalent partners that aren't folded into the doers), solo = acted
    /// alone (self EP - can run alone, or treat-as-doer: every participant acts on their own). Both for SoloOrPairDistinct.
    /// </summary>
    public static void DocShapes(COM com, out bool pair, out bool solo)
    {
        var k = Resolve(com);
        var req = com?.requirements?.requirement;
        bool allDoers = req != null && (req.TreatReceiverAsDoer || req.TreatDoerAsReceiver);
        pair = !allDoers && k != KojoParticipants.SoloOnly && (Distinct(k) || Mutual(k));
        solo = allDoers || Solo(k);
        if (!pair && !solo) solo = true;
    }

    /// <summary>Doer and receiver play different roles (a role split can be required).</summary>
    public static bool Distinct(KojoParticipants k) { return k == KojoParticipants.PairDistinct || k == KojoParticipants.SoloOrPairDistinct; }

    /// <summary>The doer can act with nobody else (listener = itself: branches need requireSelfOnly).</summary>
    public static bool Solo(KojoParticipants k) { return k == KojoParticipants.SoloOnly || k == KojoParticipants.SoloOrGroupMutual || k == KojoParticipants.SoloOrPairDistinct; }

    /// <summary>Other participants exist and are equivalent.</summary>
    public static bool Mutual(KojoParticipants k) { return k == KojoParticipants.PairMutual || k == KojoParticipants.SoloOrGroupMutual; }
}

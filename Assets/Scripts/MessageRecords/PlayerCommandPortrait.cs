using System.Collections.Generic;

/// <summary>
/// The player has no kojo, so the player's image goes on the generic system lines of commands the player acts in
/// (doer or receiver of any EP): AC check, begin text, result check, end text, generic refusal/abort. NPC kojo keeps
/// the NPC image. The player is a normal portrait ref of the line (recordings stay faithful) with tags snapshot at
/// log time - existing action tags only, the same source NPC kojo uses (UtilityEX.GetEPPortraitActionTags).
/// Docs/ImagePortraits/Plan_PlayerCommandPortraits.md
/// </summary>
public static class PlayerCommandPortrait
{
    public static bool Enabled
    {
        get { return scr_System_CentralControl.current.DisplaySetting.displayPlayerPortraitInLogs.value; }
    }

    /// <summary> setting on, player is doer or receiver of any EP of ap, not an LLM agent run </summary>
    public static bool Applies(ActionPackage ap)
    {
        if (ap == null || ap.IsAgentRun || !Enabled) return false;
        var list = ap.ListEP;
        for (int i = 0; i < list.Count; i++)
        {
            var ep = list[i];
            if (ep != null && (ep.DoerRef == 0 || ep.ReceiverRef == 0)) return true;
        }
        return false;
    }

    /// <summary> player as portrait ref + self/target tags of this ap's player EPs, snapshot now </summary>
    public static void Load(DescriptionCollector desc, ActionPackage ap)
    {
        var player = scr_System_CampaignManager.current.Player;
        if (desc == null || ap == null || player == null) return;

        desc.LoadActors(player.RefID, true, true);

        var self = desc.displayTagsOverride_Self;
        var target = desc.displayTagsOverride_Target;
        UtilityEX.GetActorTag(ref self, player);

        var list = ap.ListEP;
        for (int i = 0; i < list.Count; i++)
        {
            var ep = list[i];
            if (ep == null) continue;
            Character_Trainable partner;
            if (ep.DoerRef == player.RefID) partner = ep.Receiver;
            else if (ep.ReceiverRef == player.RefID) partner = ep.Doer;
            else continue;

            self.AddRange(UtilityEX.GetEPPortraitActionTags(ep, player));
            if (partner != null && partner != player)
            {
                UtilityEX.GetActorTag(ref target, partner);
                target.AddRange(UtilityEX.GetEPPortraitActionTags(ep, partner));
            }
        }
        Utility.DistinctInPlace(self);
        Utility.DistinctInPlace(target);
    }

    /// <summary> new player line carrying text s (player-only: empty message_excludeRelated) </summary>
    public static DescriptionCollector Create(string s, ActionPackage ap)
    {
        var desc = new DescriptionCollector(s);
        desc.LoadActors(ap.actorRefs);
        Load(desc, ap);
        return desc;
    }
}

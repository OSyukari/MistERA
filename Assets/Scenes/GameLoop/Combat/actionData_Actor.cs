using UnityEngine;

public class actionData_Actor : MonoBehaviour
{
    public scr_HoverableText hp, mp, pos, mov;

    public void Refresh(Character_Trainable c, CombatInstance Handler)
    {
        var stats = c == null ? null : !Handler.ActorStats.ContainsKey(c.RefID) ? null : Handler.ActorStats[c.RefID];

        if (stats == null || stats.HP == null) hp.SetText(" - ");
        else stats.HP.Draw(hp);

        if (stats == null || stats.MP == null) mp.SetText(" - ");
        else stats.MP.Draw(mp);

        if (stats == null) pos.SetText(" - ");
        else CombatUtility.DrawPosture(stats, pos);

        //if (stats == null) mov.SetText(" - ");
        //else CombatUtility.DrawEvasion(stats, mov, true);// mov.SetText( $"Evasion: {Handler.ActorStats[c.RefID].Evasion}");
    }
}

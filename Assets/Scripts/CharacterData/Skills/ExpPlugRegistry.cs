using System.Collections.Generic;

/// <summary>
/// Runtime-only per-world index of open (unbound) experience plugs on plug-active characters.
/// Built lazily on first query after campaign start/load (one walk over campaign characters), then kept up to date
/// by SetHomeFaction, Unregister, Bind, and BindLate hooks.
/// </summary>
public static class ExpPlugRegistry
{
    static readonly Dictionary<string, List<(int charaRef, int plugIndex)>> byWorld = new Dictionary<string, List<(int charaRef, int plugIndex)>>();
    static bool built = false;

    public static bool IsPlugActive(Character_Trainable c)
    {
        return c != null
            && c.RefID > 0
            && string.IsNullOrEmpty(c.fallbackPoolID)
            && !c.isTemporaryActor
            && c.FactionManager != null
            && c.FactionManager.Faction_Home != null;
    }

    public static void Reset()
    {
        built = false;
        byWorld.Clear();
    }

    static void EnsureBuilt()
    {
        if (built) return;
        built = true;
        byWorld.Clear();
        var campaign = scr_System_CampaignManager.current;
        if (campaign == null) return;
        foreach (var c in campaign.AllCharacters)
        {
            if (IsPlugActive(c)) AddCharacterEntries(c);
        }
    }

    static void AddCharacterEntries(Character_Trainable c)
    {
        if (c == null || c.ExpPlugs == null || c.ExpPlugs.Count == 0) return;
        string worldID = Manageable.ResolveWorldID(c.FactionManager.Faction_Home);
        if (string.IsNullOrEmpty(worldID)) return;

        List<(int charaRef, int plugIndex)> list = null;
        for (int i = 0; i < c.ExpPlugs.Count; i++)
        {
            var plug = c.ExpPlugs[i];
            if (plug == null || plug.boundRef >= 0) continue;
            if (list == null && !byWorld.TryGetValue(worldID, out list))
            {
                list = new List<(int charaRef, int plugIndex)>();
                byWorld[worldID] = list;
            }
            list.Add((c.RefID, i));
        }
    }

    public static void RegisterCharacter(Character_Trainable c)
    {
        if (!built || c == null) return;
        UnregisterCharacter(c.RefID);
        if (IsPlugActive(c)) AddCharacterEntries(c);
    }

    public static void UnregisterCharacter(int charaRef)
    {
        if (!built || charaRef < 0) return;
        foreach (var list in byWorld.Values)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i].charaRef == charaRef) list.RemoveAt(i);
        }
    }

    public static void RemovePlug(int charaRef, int plugIndex)
    {
        if (!built || charaRef < 0) return;
        foreach (var list in byWorld.Values)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i].charaRef == charaRef && list[i].plugIndex == plugIndex) list.RemoveAt(i);
        }
    }

    public static void OnHomeChanged(Character_Trainable c)
    {
        if (!built || c == null || CharacterGeneration.IsDeferred) return;
        RegisterCharacter(c);
    }

    public static List<(int charaRef, int plugIndex)> GetWorldPlugs(string worldID)
    {
        if (string.IsNullOrEmpty(worldID)) return null;
        EnsureBuilt();
        return byWorld.TryGetValue(worldID, out var list) ? list : null;
    }
}

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public enum PermissionGrantSource { Request, Event, Preset }

[JsonObject(MemberSerialization.OptIn)]
public sealed class PermissionGrant
{
    [JsonProperty] public int granterRef { get; private set; } = -1;
    [JsonProperty] public int holderRef { get; private set; } = -1;
    [JsonProperty] public string permissionID { get; private set; }
    [JsonProperty] public PermissionGrantType type { get; private set; } = (PermissionGrantType)(-1);
    [JsonProperty] public PermissionGrantSource source { get; private set; } = (PermissionGrantSource)(-1);
    [JsonProperty] public DateTime grantedAt { get; private set; }

    [JsonConstructor]
    private PermissionGrant() { }

    public PermissionGrant(int granterRef, int holderRef, string permissionID,
        PermissionGrantType type, PermissionGrantSource source, DateTime grantedAt)
    {
        this.granterRef = granterRef;
        this.holderRef = holderRef;
        this.permissionID = permissionID;
        this.type = type;
        this.source = source;
        this.grantedAt = grantedAt;
    }

    // Normal grants still require the caller to check their live relationship prerequisites.
    // Temporary grants live until removed: the granter's day update (unless the pair shares a running sex scene)
    // or the pair's sex scene ending (PermissionService.ClearTemporaryOnDayUpdate / ClearTemporaryForPair).
    public bool IsWithinLifetime(DateTime gameTime)
    {
        if (gameTime < grantedAt) return false;
        return type == PermissionGrantType.Normal || type == PermissionGrantType.Temporary;
    }
}

/// <summary>Explicit directed grants only; relationship, pride and skill sources are not stored here.</summary>
[JsonObject(MemberSerialization.OptIn)]
public sealed class PermissionGrantDatabase
{
    private readonly object sync = new object();
    private List<PermissionGrant> grants = new List<PermissionGrant>();

    // Serialize a detached list, never a collection that parallel jobs could be modifying.
    [JsonProperty("grants", ObjectCreationHandling = ObjectCreationHandling.Replace)]
    private List<PermissionGrant> SerializedGrants
    {
        get { lock (sync) return new List<PermissionGrant>(grants); }
        set
        {
            lock (sync)
            {
                grants = new List<PermissionGrant>();
                if (value == null) return;
                foreach (var grant in value)
                {
                    if (!IsValidRecord(grant)) continue;
                    Store(grant);
                }
            }
        }
    }

    public PermissionGrantDatabase CreateSnapshot()
    {
        return new PermissionGrantDatabase { SerializedGrants = this.SerializedGrants };
    }

    public IReadOnlyList<PermissionGrant> GetSnapshot()
    {
        lock (sync) return grants.ToArray();
    }

    public IReadOnlyList<PermissionGrant> GetForPair(int granterRef, int holderRef, string permissionID)
    {
        lock (sync)
            return grants.FindAll(grant => grant.granterRef == granterRef
                && grant.holderRef == holderRef && grant.permissionID == permissionID);
    }

    // Recheck identity under the same lock as removal so an in-flight change cannot follow RefID recycling.
    internal bool TryStore(PermissionDefinition permission, int granterRef, int holderRef,
        PermissionGrantType type, PermissionGrantSource source, DateTime gameTime,
        Func<bool> isLivePair, out bool changed)
    {
        changed = false;
        if (permission == null) return false;
        var grant = new PermissionGrant(granterRef, holderRef, permission.ID, type, source, gameTime);
        if (!IsValidRecord(grant)) return false;
        lock (sync)
        {
            if (isLivePair == null || !isLivePair()) return false;
            changed = Store(grant);
            return true;
        }
    }

    internal bool Revoke(int granterRef, int holderRef, string permissionID, Func<bool> isLivePair, out bool changed)
    {
        changed = false;
        lock (sync)
        {
            if (isLivePair == null || !isLivePair()) return false;
            changed = grants.RemoveAll(grant => grant.granterRef == granterRef
                && grant.holderRef == holderRef && grant.permissionID == permissionID) > 0;
            return true;
        }
    }

    public void RemoveCharacter(int characterRef)
    {
        lock (sync)
            grants.RemoveAll(grant => grant.granterRef == characterRef || grant.holderRef == characterRef);
    }

    /// <summary>
    /// Removes the temporary grants given by granterRef, except those whose holder keepHolder protects. Holders of removed
    /// grants are appended to removedHolders (no duplicates) so the caller can invalidate the pairs.
    /// </summary>
    public void RemoveTemporaryByGranter(int granterRef, Func<int, bool> keepHolder, List<int> removedHolders)
    {
        lock (sync)
        {
            for (int i = grants.Count - 1; i >= 0; i--)
            {
                var grant = grants[i];
                if (grant.granterRef != granterRef || grant.type != PermissionGrantType.Temporary) continue;
                if (keepHolder != null && keepHolder(grant.holderRef)) continue;
                grants.RemoveAt(i);
                if (removedHolders != null && !removedHolders.Contains(grant.holderRef)) removedHolders.Add(grant.holderRef);
            }
        }
    }

    /// <summary>Removes the temporary grants between two characters, both directions. True if any was removed.</summary>
    public bool RemoveTemporaryForPair(int refA, int refB)
    {
        bool removed = false;
        lock (sync)
        {
            for (int i = grants.Count - 1; i >= 0; i--)
            {
                var grant = grants[i];
                if (grant.type != PermissionGrantType.Temporary) continue;
                if (!((grant.granterRef == refA && grant.holderRef == refB) || (grant.granterRef == refB && grant.holderRef == refA))) continue;
                grants.RemoveAt(i);
                removed = true;
            }
        }
        return removed;
    }

    /// <summary>
    /// Load cleanup for saves from before temporary grants were removed explicitly: drops temporary grants issued before
    /// today's date unless keepPair protects the (granter, holder) pair. Returns the removed count.
    /// </summary>
    public int RemoveStaleTemporary(DateTime gameTime, Func<int, int, bool> keepPair)
    {
        int removed = 0;
        lock (sync)
        {
            for (int i = grants.Count - 1; i >= 0; i--)
            {
                var grant = grants[i];
                if (grant.type != PermissionGrantType.Temporary || grant.grantedAt.Date >= gameTime.Date) continue;
                if (keepPair != null && keepPair(grant.granterRef, grant.holderRef)) continue;
                grants.RemoveAt(i);
                removed++;
            }
        }
        return removed;
    }

    public int RemoveMissingCharacters(ISet<int> existingCharacterRefs)
    {
        if (existingCharacterRefs == null) throw new ArgumentNullException(nameof(existingCharacterRefs));
        lock (sync)
            return grants.RemoveAll(grant => !existingCharacterRefs.Contains(grant.granterRef)
                || !existingCharacterRefs.Contains(grant.holderRef));
    }

    private static bool IsValidRecord(PermissionGrant grant)
    {
        // Missing JSON fields must not fabricate player-owned normal grants.
        return grant != null && grant.granterRef >= 0 && grant.holderRef >= 0
            && grant.granterRef != grant.holderRef && !string.IsNullOrWhiteSpace(grant.permissionID)
            && grant.grantedAt != default(DateTime)
            && Enum.IsDefined(typeof(PermissionGrantType), grant.type)
            && Enum.IsDefined(typeof(PermissionGrantSource), grant.source);
    }

    // Called only while holding sync. Keep the first normal grant and the latest temporary game day.
    private bool Store(PermissionGrant grant)
    {
        int index = grants.FindIndex(existing => existing.granterRef == grant.granterRef
            && existing.holderRef == grant.holderRef && existing.permissionID == grant.permissionID
            && existing.type == grant.type);
        if (index < 0)
        {
            grants.Add(grant);
            return true;
        }
        if (grant.type == PermissionGrantType.Temporary && grant.grantedAt.Date > grants[index].grantedAt.Date)
        {
            grants[index] = grant;
            return true;
        }
        return false;
    }
}

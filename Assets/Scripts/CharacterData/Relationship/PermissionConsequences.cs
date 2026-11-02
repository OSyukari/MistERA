using System.Collections.Generic;

/// <summary>
/// Relationship costs of acts that happened without permission (PermissionDefinition.consequences). Called where an
/// experience gain has a known partner - for now the cum delivery (Character_Body). Never blocks or undoes anything:
/// the body facts happen with or without permission.
/// </summary>
public static class PermissionConsequences
{
    /// <summary>
    /// granter gained gainedExperienceIDs from holder (experience tags = ownerTags). For every registered permission the
    /// granter has not given the holder, each consequence whose experience was gained and whose owner-tag conditions
    /// match changes its scores by amount x multiplier (amount = countTotalAmount for CountTotal experiences, else 1),
    /// granter → holder, logged into exp. True if any consequence applied.
    /// </summary>
    public static bool Apply(Character_Trainable granter, Character_Trainable holder, IEnumerable<string> gainedExperienceIDs,
        float countTotalAmount, ICollection<string> ownerTags, ExperienceLog exp)
    {
        if (granter == null || holder == null || granter == holder || gainedExperienceIDs == null) return false;
        var index = scr_System_Serializer.current?.MasterList?.PermissionDefinitions;
        if (index?.list == null) return false;
        var gained = new HashSet<string>(gainedExperienceIDs);
        if (gained.Count == 0) return false;

        Character_Relationship rel = null;
        bool applied = false;
        foreach (var definition in index.list)
        {
            if (definition?.consequences == null || definition.consequences.Count == 0) continue;
            if (!ReferenceEquals(index.GetByID(definition.ID), definition)) continue;

            PermissionQueryResult query = null;
            foreach (var consequence in definition.consequences)
            {
                if (consequence == null || !gained.Contains(consequence.experienceID) || !MatchesTags(consequence, ownerTags)) continue;
                if (query == null) query = PermissionService.Query(granter, holder, definition.ID);
                if (query.Error.Length == 0 && query.HasPermission) break;

                if (rel == null) rel = granter.Relationships.FindRelationshipWith(holder);
                if (rel == null) return applied;
                float amount = ExperienceAmount(consequence.experienceID, countTotalAmount);
                foreach (var change in consequence.scores)
                {
                    if (change == null || change.score == RelationshipScoreType.None || change.multiplier == 0f) continue;
                    float delta = amount * change.multiplier;
                    rel.ModRelationValue(change.score, delta, false);
                    exp?.AddRelations(granter.RefID, holder.RefID, change.score, (int)delta);
                }
                applied = true;
            }
        }
        return applied;
    }

    static bool MatchesTags(PermissionConsequence consequence, ICollection<string> ownerTags)
    {
        if (consequence.RequiredOwnerTags != null)
            foreach (var tag in consequence.RequiredOwnerTags) if (ownerTags == null || !ownerTags.Contains(tag)) return false;
        if (consequence.ExcludeOwnerTags != null && ownerTags != null)
            foreach (var tag in consequence.ExcludeOwnerTags) if (ownerTags.Contains(tag)) return false;
        return true;
    }

    /// <summary>The amount SkillManager.AddExperience credits: the gain amount for CountTotal experiences, else 1.</summary>
    static float ExperienceAmount(string experienceID, float countTotalAmount)
    {
        var experience = scr_System_Serializer.current.index_Experiences.List.Find(x => x.ExperienceID == experienceID);
        return experience != null && experience.CountTotal ? countTotalAmount : 1f;
    }
}

using System.Collections.Generic;

/// <summary>
/// A cached stat value (Stats_Base, Stats_Derived_Instance, StatusEx_Instance) that can be invalidated on its own instead of
/// with the whole character (Docs/Stats/Plan_StatInvalidation.md).
/// </summary>
public interface I_StatNode
{
    /// <summary>
    /// Marks the cache stale (recomputed lazily on the next read) and propagates to every node that read it. Stops at a node
    /// already dirty: its readers were dirtied when it went dirty, and none can have recomputed from it since (that read
    /// would have recomputed it and cleared the flag) - which also ends propagation on the graph's cycles. Never recomputes,
    /// so it is safe to call from anywhere, including mid-computation.
    /// </summary>
    void MarkStatDirty();
    StatDependents Dependents { get; }
}

/// <summary>
/// The nodes that read a value: filled while they compute (I_StatsManager.RegisterStatRead / RegisterStatusValueRead), never
/// pruned - an edge a reader no longer uses only costs an extra (lazy) invalidation, and the list is bounded by the node count.
/// </summary>
public sealed class StatDependents
{
    readonly List<I_StatNode> nodes = new List<I_StatNode>(2);

    public void Add(I_StatNode node)
    {
        if (!nodes.Contains(node)) nodes.Add(node);
    }

    public void MarkAllDirty()
    {
        for (int i = 0; i < nodes.Count; i++) nodes[i].MarkStatDirty();
    }
}

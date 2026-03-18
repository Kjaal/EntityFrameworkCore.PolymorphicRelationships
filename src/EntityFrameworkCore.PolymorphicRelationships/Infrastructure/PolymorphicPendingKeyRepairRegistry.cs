using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

internal static class PolymorphicPendingKeyRepairRegistry
{
    private static readonly ConditionalWeakTable<DbContext, PendingState> States = new();

    public static void TrackMorphReference(DbContext dbContext, object dependent, string relationshipName, object principal)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(dependent);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipName);

        var state = States.GetValue(dbContext, static _ => new PendingState());
        state.MorphReferenceRepairs.Add(new PendingMorphReferenceRepair(dependent, relationshipName, principal));
    }

    public static void TrackMorphToMany(DbContext dbContext, object pivot, object principal, object related, string relationshipName)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(pivot);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(related);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipName);

        var state = States.GetValue(dbContext, static _ => new PendingState());
        state.MorphToManyRepairs.Add(new PendingMorphToManyRepair(pivot, principal, related, relationshipName));
    }

    public static bool TryBeginRepair(DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var state = States.GetValue(dbContext, static _ => new PendingState());
        if (state.IsRepairing || (state.MorphReferenceRepairs.Count == 0 && state.MorphToManyRepairs.Count == 0))
        {
            return false;
        }

        state.IsRepairing = true;
        return true;
    }

    public static PendingRepairBatch Drain(DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var state = States.GetValue(dbContext, static _ => new PendingState());
        var references = state.MorphReferenceRepairs.ToArray();
        var relations = state.MorphToManyRepairs.ToArray();
        state.MorphReferenceRepairs.Clear();
        state.MorphToManyRepairs.Clear();
        return new PendingRepairBatch(references, relations);
    }

    public static void EndRepair(DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var state = States.GetValue(dbContext, static _ => new PendingState());
        state.IsRepairing = false;
    }

    public static void Reset(DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var state = States.GetValue(dbContext, static _ => new PendingState());
        state.IsRepairing = false;
        state.MorphReferenceRepairs.Clear();
        state.MorphToManyRepairs.Clear();
    }

    internal sealed record PendingRepairBatch(
        PendingMorphReferenceRepair[] MorphReferenceRepairs,
        PendingMorphToManyRepair[] MorphToManyRepairs);

    internal sealed record PendingMorphReferenceRepair(object Dependent, string RelationshipName, object Principal);

    internal sealed record PendingMorphToManyRepair(object Pivot, object Principal, object Related, string RelationshipName);

    private sealed class PendingState
    {
        public bool IsRepairing { get; set; }

        public HashSet<PendingMorphReferenceRepair> MorphReferenceRepairs { get; } = new(PendingMorphReferenceRepairComparer.Instance);

        public HashSet<PendingMorphToManyRepair> MorphToManyRepairs { get; } = new(PendingMorphToManyRepairComparer.Instance);
    }

    private sealed class PendingMorphReferenceRepairComparer : IEqualityComparer<PendingMorphReferenceRepair>
    {
        public static PendingMorphReferenceRepairComparer Instance { get; } = new();

        public bool Equals(PendingMorphReferenceRepair? x, PendingMorphReferenceRepair? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return ReferenceEquals(x.Dependent, y.Dependent)
                && ReferenceEquals(x.Principal, y.Principal)
                && string.Equals(x.RelationshipName, y.RelationshipName, StringComparison.Ordinal);
        }

        public int GetHashCode(PendingMorphReferenceRepair obj)
        {
            return HashCode.Combine(
                RuntimeHelpers.GetHashCode(obj.Dependent),
                RuntimeHelpers.GetHashCode(obj.Principal),
                StringComparer.Ordinal.GetHashCode(obj.RelationshipName));
        }
    }

    private sealed class PendingMorphToManyRepairComparer : IEqualityComparer<PendingMorphToManyRepair>
    {
        public static PendingMorphToManyRepairComparer Instance { get; } = new();

        public bool Equals(PendingMorphToManyRepair? x, PendingMorphToManyRepair? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return ReferenceEquals(x.Pivot, y.Pivot)
                && ReferenceEquals(x.Principal, y.Principal)
                && ReferenceEquals(x.Related, y.Related)
                && string.Equals(x.RelationshipName, y.RelationshipName, StringComparison.Ordinal);
        }

        public int GetHashCode(PendingMorphToManyRepair obj)
        {
            return HashCode.Combine(
                RuntimeHelpers.GetHashCode(obj.Pivot),
                RuntimeHelpers.GetHashCode(obj.Principal),
                RuntimeHelpers.GetHashCode(obj.Related),
                StringComparer.Ordinal.GetHashCode(obj.RelationshipName));
        }
    }
}

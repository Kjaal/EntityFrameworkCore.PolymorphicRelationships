using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

internal static class PolymorphicLoadedNavigationRegistry
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DbContext, SnapshotState> States = new();

    public static void RecordReference(DbContext dbContext, object entity, string propertyName, object? value)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        SetSnapshot(dbContext, entity, propertyName, isCollection: false, value is null ? [] : [value]);
    }

    public static void RecordCollection(DbContext dbContext, object entity, string propertyName, IEnumerable<object> values)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        SetSnapshot(dbContext, entity, propertyName, isCollection: true, values.Where(static value => value is not null).ToList()!);
    }

    public static IReadOnlyList<LoadedNavigationSnapshot> GetSnapshots(DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        return States.GetValue(dbContext, static _ => new SnapshotState()).Snapshots.ToArray();
    }

    public static void CommitCurrentValues(DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var state = States.GetValue(dbContext, static _ => new SnapshotState());
        for (var index = state.Snapshots.Count - 1; index >= 0; index--)
        {
            var snapshot = state.Snapshots[index];
            if (!TryGetTrackedValues(dbContext, snapshot.Entity, snapshot.PropertyName, snapshot.IsCollection, out var values))
            {
                state.Snapshots.RemoveAt(index);
                continue;
            }

            snapshot.Values.Clear();
            snapshot.Values.AddRange(values);
        }
    }

    private static void SetSnapshot(DbContext dbContext, object entity, string propertyName, bool isCollection, IReadOnlyList<object> values)
    {
        var state = States.GetValue(dbContext, static _ => new SnapshotState());
        var existing = state.Snapshots.FirstOrDefault(snapshot => ReferenceEquals(snapshot.Entity, entity)
            && string.Equals(snapshot.PropertyName, propertyName, StringComparison.Ordinal));

        if (!TryValidateTrackedValues(dbContext, entity, values))
        {
            if (existing is not null)
            {
                state.Snapshots.Remove(existing);
            }

            return;
        }

        if (existing is null)
        {
            existing = new LoadedNavigationSnapshot(entity, propertyName, isCollection, []);
            state.Snapshots.Add(existing);
        }
        else
        {
            existing.IsCollection = isCollection;
        }

        existing.Values.Clear();
        existing.Values.AddRange(values);
    }

    private static bool TryGetTrackedValues(DbContext dbContext, object entity, string propertyName, bool isCollection, out List<object> values)
    {
        values = [];
        if (!IsTracked(dbContext, entity))
        {
            return false;
        }

        var currentValue = PolymorphicMemberAccessorCache.GetValue(dbContext, entity, propertyName);
        if (isCollection)
        {
            if (currentValue is not System.Collections.IEnumerable enumerable || currentValue is string)
            {
                return true;
            }

            foreach (var item in enumerable)
            {
                if (item is null)
                {
                    continue;
                }

                if (!IsTracked(dbContext, item))
                {
                    return false;
                }

                values.Add(item);
            }

            return true;
        }

        if (currentValue is null)
        {
            return true;
        }

        if (!IsTracked(dbContext, currentValue))
        {
            return false;
        }

        values.Add(currentValue);
        return true;
    }

    private static bool TryValidateTrackedValues(DbContext dbContext, object entity, IReadOnlyList<object> values)
    {
        if (!IsTracked(dbContext, entity))
        {
            return false;
        }

        return values.All(value => IsTracked(dbContext, value));
    }

    private static bool IsTracked(DbContext dbContext, object entity)
    {
        var state = dbContext.Entry(entity).State;
        return state != EntityState.Detached && state != EntityState.Deleted;
    }

    internal sealed class LoadedNavigationSnapshot(object entity, string propertyName, bool isCollection, List<object> values)
    {
        public object Entity { get; } = entity;

        public string PropertyName { get; } = propertyName;

        public bool IsCollection { get; set; } = isCollection;

        public List<object> Values { get; } = values;
    }

    private sealed class SnapshotState
    {
        public List<LoadedNavigationSnapshot> Snapshots { get; } = [];
    }
}

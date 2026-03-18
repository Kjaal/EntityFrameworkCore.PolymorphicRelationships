using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

internal static class PolymorphicPendingKeyRepairCommandExecutor
{
    public static void Execute(DbContext dbContext, PolymorphicPendingKeyRepairRegistry.PendingRepairBatch batch)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        foreach (var repair in batch.MorphReferenceRepairs)
        {
            ExecuteMorphReferenceRepair(dbContext, repair);
        }

        foreach (var repair in batch.MorphToManyRepairs)
        {
            ExecuteMorphToManyRepair(dbContext, repair);
        }
    }

    public static async Task ExecuteAsync(DbContext dbContext, PolymorphicPendingKeyRepairRegistry.PendingRepairBatch batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        foreach (var repair in batch.MorphReferenceRepairs)
        {
            await ExecuteMorphReferenceRepairAsync(dbContext, repair, cancellationToken);
        }

        foreach (var repair in batch.MorphToManyRepairs)
        {
            await ExecuteMorphToManyRepairAsync(dbContext, repair, cancellationToken);
        }
    }

    private static void ExecuteMorphReferenceRepair(DbContext dbContext, PolymorphicPendingKeyRepairRegistry.PendingMorphReferenceRepair repair)
    {
        var dependentEntry = dbContext.Entry(repair.Dependent);
        if (dependentEntry.State is EntityState.Detached or EntityState.Deleted)
        {
            return;
        }

        var reference = PolymorphicModelMetadata.GetRequiredReference(dbContext.Model, repair.Dependent.GetType(), repair.RelationshipName);
        ExecuteUpdate(
            dbContext,
            dependentEntry,
            [
                (reference.TypePropertyName, dependentEntry.Property(reference.TypePropertyName).CurrentValue),
                (reference.IdPropertyName, dependentEntry.Property(reference.IdPropertyName).CurrentValue),
            ]);
    }

    private static Task ExecuteMorphReferenceRepairAsync(DbContext dbContext, PolymorphicPendingKeyRepairRegistry.PendingMorphReferenceRepair repair, CancellationToken cancellationToken)
    {
        var dependentEntry = dbContext.Entry(repair.Dependent);
        if (dependentEntry.State is EntityState.Detached or EntityState.Deleted)
        {
            return Task.CompletedTask;
        }

        var reference = PolymorphicModelMetadata.GetRequiredReference(dbContext.Model, repair.Dependent.GetType(), repair.RelationshipName);
        return ExecuteUpdateAsync(
            dbContext,
            dependentEntry,
            [
                (reference.TypePropertyName, dependentEntry.Property(reference.TypePropertyName).CurrentValue),
                (reference.IdPropertyName, dependentEntry.Property(reference.IdPropertyName).CurrentValue),
            ],
            cancellationToken);
    }

    private static void ExecuteMorphToManyRepair(DbContext dbContext, PolymorphicPendingKeyRepairRegistry.PendingMorphToManyRepair repair)
    {
        var pivotEntry = dbContext.Entry(repair.Pivot);
        if (pivotEntry.State is EntityState.Detached or EntityState.Deleted)
        {
            return;
        }

        var relation = PolymorphicModelMetadata.GetRequiredManyToMany(dbContext.Model, repair.Principal.GetType(), repair.Related.GetType(), repair.RelationshipName);
        ExecuteUpdate(
            dbContext,
            pivotEntry,
            [
                (relation.PivotTypePropertyName, pivotEntry.Property(relation.PivotTypePropertyName).CurrentValue),
                (relation.PivotIdPropertyName, pivotEntry.Property(relation.PivotIdPropertyName).CurrentValue),
                (relation.PivotRelatedIdPropertyName, pivotEntry.Property(relation.PivotRelatedIdPropertyName).CurrentValue),
            ]);
    }

    private static Task ExecuteMorphToManyRepairAsync(DbContext dbContext, PolymorphicPendingKeyRepairRegistry.PendingMorphToManyRepair repair, CancellationToken cancellationToken)
    {
        var pivotEntry = dbContext.Entry(repair.Pivot);
        if (pivotEntry.State is EntityState.Detached or EntityState.Deleted)
        {
            return Task.CompletedTask;
        }

        var relation = PolymorphicModelMetadata.GetRequiredManyToMany(dbContext.Model, repair.Principal.GetType(), repair.Related.GetType(), repair.RelationshipName);
        return ExecuteUpdateAsync(
            dbContext,
            pivotEntry,
            [
                (relation.PivotTypePropertyName, pivotEntry.Property(relation.PivotTypePropertyName).CurrentValue),
                (relation.PivotIdPropertyName, pivotEntry.Property(relation.PivotIdPropertyName).CurrentValue),
                (relation.PivotRelatedIdPropertyName, pivotEntry.Property(relation.PivotRelatedIdPropertyName).CurrentValue),
            ],
            cancellationToken);
    }

    private static void ExecuteUpdate(DbContext dbContext, Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, IReadOnlyList<(string PropertyName, object? Value)> assignments)
    {
        var (sql, parameters) = BuildUpdateCommand(dbContext, entry, assignments);
        if (sql is null)
        {
            return;
        }

        dbContext.Database.ExecuteSqlRaw(sql, NormalizeParameters(parameters));
    }

    private static Task ExecuteUpdateAsync(
        DbContext dbContext,
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
        IReadOnlyList<(string PropertyName, object? Value)> assignments,
        CancellationToken cancellationToken)
    {
        var (sql, parameters) = BuildUpdateCommand(dbContext, entry, assignments);
        if (sql is null)
        {
            return Task.CompletedTask;
        }

        return dbContext.Database.ExecuteSqlRawAsync(sql, NormalizeParameters(parameters), cancellationToken);
    }

    private static (string? Sql, object?[] Parameters) BuildUpdateCommand(
        DbContext dbContext,
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
        IReadOnlyList<(string PropertyName, object? Value)> assignments)
    {
        var entityType = entry.Metadata;
        var tableName = entityType.GetTableName();
        if (tableName is null)
        {
            throw new NotSupportedException($"Temporary key repair requires a table-mapped entity for '{entityType.DisplayName()}'.");
        }

        var primaryKey = entityType.FindPrimaryKey();
        if (primaryKey is null || primaryKey.Properties.Count != 1)
        {
            throw new NotSupportedException($"Temporary key repair currently requires a single-column primary key for '{entityType.DisplayName()}'.");
        }

        var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
        var primaryKeyProperty = primaryKey.Properties[0];
        var primaryKeyValue = entry.Property(primaryKeyProperty.Name).CurrentValue ?? entry.Property(primaryKeyProperty.Name).OriginalValue;
        if (primaryKeyValue is null)
        {
            return (null, []);
        }

        var sqlGenerationHelper = dbContext.GetService<ISqlGenerationHelper>();
        var delimitedTable = sqlGenerationHelper.DelimitIdentifier(tableName, entityType.GetSchema());
        var setClauses = new List<string>(assignments.Count);
        var parameters = new object?[assignments.Count + 1];

        for (var index = 0; index < assignments.Count; index++)
        {
            var property = entityType.FindProperty(assignments[index].PropertyName)
                ?? throw new InvalidOperationException($"Property '{assignments[index].PropertyName}' was not found on '{entityType.DisplayName()}'.");
            var columnName = property.GetColumnName(storeObject)
                ?? throw new InvalidOperationException($"Property '{property.Name}' is not mapped to '{entityType.DisplayName()}'.");
            setClauses.Add($"{sqlGenerationHelper.DelimitIdentifier(columnName)} = {{{index}}}");
            parameters[index] = assignments[index].Value;
        }

        var primaryKeyColumnName = primaryKeyProperty.GetColumnName(storeObject)
            ?? throw new InvalidOperationException($"Primary key '{primaryKeyProperty.Name}' is not mapped to '{entityType.DisplayName()}'.");
        parameters[^1] = primaryKeyValue;

        var sql = $"UPDATE {delimitedTable} SET {string.Join(", ", setClauses)} WHERE {sqlGenerationHelper.DelimitIdentifier(primaryKeyColumnName)} = {{{assignments.Count}}}";
        return (sql, parameters);
    }

    private static object[] NormalizeParameters(object?[] parameters)
    {
        return parameters
            .Select(parameter => parameter ?? DBNull.Value)
            .ToArray();
    }
}

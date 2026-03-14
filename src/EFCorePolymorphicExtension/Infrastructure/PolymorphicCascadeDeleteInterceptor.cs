using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EFCorePolymorphicExtension.Infrastructure;

public sealed class PolymorphicCascadeDeleteInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            CascadeDeletes(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            await CascadeDeletesAsync(eventData.Context, cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void CascadeDeletes(DbContext dbContext)
    {
        var processedKeys = new HashSet<string>(StringComparer.Ordinal);
        var createdNewDeletes = true;

        while (createdNewDeletes)
        {
            createdNewDeletes = false;
            var deletedEntries = dbContext.ChangeTracker.Entries()
                .Where(entry => entry.State == EntityState.Deleted)
                .ToList();

            foreach (var deletedEntry in deletedEntries)
            {
                foreach (var relationship in GetCascadeTargets(dbContext, deletedEntry))
                {
                    var relationshipKey = BuildRelationshipKey(deletedEntry.Entity, relationship.Reference.RelationshipName, relationship.Association.InverseRelationshipName, "morph");
                    if (!processedKeys.Add(relationshipKey))
                    {
                        continue;
                    }

                    var dependents = PolymorphicQueryExecutor.ListByTwoProperties(
                        dbContext,
                        relationship.Reference.DependentType,
                        relationship.Reference.TypePropertyName,
                        typeof(string),
                        relationship.Association.Alias,
                        relationship.Reference.IdPropertyName,
                        relationship.Reference.IdPropertyType,
                        relationship.OwnerId);

                    foreach (var dependent in dependents)
                    {
                        var dependentEntry = dbContext.Entry(dependent);
                        if (dependentEntry.State != EntityState.Deleted)
                        {
                            dependentEntry.State = EntityState.Deleted;
                            createdNewDeletes = true;
                        }
                    }
                }

                foreach (var pivotCleanup in GetManyToManyCascadeTargets(dbContext, deletedEntry))
                {
                    var relationshipKey = BuildRelationshipKey(deletedEntry.Entity, pivotCleanup.Relation.RelationshipName, pivotCleanup.Relation.InverseRelationshipName, pivotCleanup.Mode);
                    if (!processedKeys.Add(relationshipKey))
                    {
                        continue;
                    }

                    var pivots = pivotCleanup.Mode == "principal"
                        ? PolymorphicQueryExecutor.ListByTwoProperties(
                            dbContext,
                            pivotCleanup.Relation.PivotType,
                            pivotCleanup.Relation.PivotTypePropertyName,
                            typeof(string),
                            pivotCleanup.Relation.PrincipalAlias,
                            pivotCleanup.Relation.PivotIdPropertyName,
                            pivotCleanup.Relation.PivotIdPropertyType,
                            pivotCleanup.KeyValue)
                        : PolymorphicQueryExecutor.ListByProperty(
                            dbContext,
                            pivotCleanup.Relation.PivotType,
                            pivotCleanup.Relation.PivotRelatedIdPropertyName,
                            pivotCleanup.Relation.PivotRelatedIdPropertyType,
                            pivotCleanup.KeyValue);

                    foreach (var pivot in pivots)
                    {
                        var pivotEntry = dbContext.Entry(pivot);
                        if (pivotEntry.State != EntityState.Deleted)
                        {
                            pivotEntry.State = EntityState.Deleted;
                            createdNewDeletes = true;
                        }
                    }
                }
            }
        }
    }

    private static async Task CascadeDeletesAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var processedKeys = new HashSet<string>(StringComparer.Ordinal);
        var createdNewDeletes = true;

        while (createdNewDeletes)
        {
            createdNewDeletes = false;
            var deletedEntries = dbContext.ChangeTracker.Entries()
                .Where(entry => entry.State == EntityState.Deleted)
                .ToList();

            foreach (var deletedEntry in deletedEntries)
            {
                foreach (var relationship in GetCascadeTargets(dbContext, deletedEntry))
                {
                    var relationshipKey = BuildRelationshipKey(deletedEntry.Entity, relationship.Reference.RelationshipName, relationship.Association.InverseRelationshipName, "morph");
                    if (!processedKeys.Add(relationshipKey))
                    {
                        continue;
                    }

                    var dependents = await PolymorphicQueryExecutor.ListByTwoPropertiesAsync(
                        dbContext,
                        relationship.Reference.DependentType,
                        relationship.Reference.TypePropertyName,
                        typeof(string),
                        relationship.Association.Alias,
                        relationship.Reference.IdPropertyName,
                        relationship.Reference.IdPropertyType,
                        relationship.OwnerId,
                        cancellationToken);

                    foreach (var dependent in dependents)
                    {
                        var dependentEntry = dbContext.Entry(dependent);
                        if (dependentEntry.State != EntityState.Deleted)
                        {
                            dependentEntry.State = EntityState.Deleted;
                            createdNewDeletes = true;
                        }
                    }
                }

                foreach (var pivotCleanup in GetManyToManyCascadeTargets(dbContext, deletedEntry))
                {
                    var relationshipKey = BuildRelationshipKey(deletedEntry.Entity, pivotCleanup.Relation.RelationshipName, pivotCleanup.Relation.InverseRelationshipName, pivotCleanup.Mode);
                    if (!processedKeys.Add(relationshipKey))
                    {
                        continue;
                    }

                    var pivots = pivotCleanup.Mode == "principal"
                        ? await PolymorphicQueryExecutor.ListByTwoPropertiesAsync(
                            dbContext,
                            pivotCleanup.Relation.PivotType,
                            pivotCleanup.Relation.PivotTypePropertyName,
                            typeof(string),
                            pivotCleanup.Relation.PrincipalAlias,
                            pivotCleanup.Relation.PivotIdPropertyName,
                            pivotCleanup.Relation.PivotIdPropertyType,
                            pivotCleanup.KeyValue,
                            cancellationToken)
                        : await PolymorphicQueryExecutor.ListByPropertyAsync(
                            dbContext,
                            pivotCleanup.Relation.PivotType,
                            pivotCleanup.Relation.PivotRelatedIdPropertyName,
                            pivotCleanup.Relation.PivotRelatedIdPropertyType,
                            pivotCleanup.KeyValue,
                            cancellationToken);

                    foreach (var pivot in pivots)
                    {
                        var pivotEntry = dbContext.Entry(pivot);
                        if (pivotEntry.State != EntityState.Deleted)
                        {
                            pivotEntry.State = EntityState.Deleted;
                            createdNewDeletes = true;
                        }
                    }
                }
            }
        }
    }

    private static IEnumerable<(PolymorphicModelMetadata.MorphReference Reference, PolymorphicModelMetadata.MorphAssociation Association, object OwnerId)> GetCascadeTargets(DbContext dbContext, Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry deletedEntry)
    {
        foreach (var reference in PolymorphicModelMetadata.GetReferences(dbContext.Model))
        {
            foreach (var association in reference.Associations.Where(association => association.DeleteBehavior == PolymorphicDeleteBehavior.Cascade))
            {
                if (!association.PrincipalType.IsAssignableFrom(deletedEntry.Entity.GetType()))
                {
                    continue;
                }

                var ownerId = deletedEntry.Property(association.PrincipalKeyPropertyName).CurrentValue
                    ?? deletedEntry.Property(association.PrincipalKeyPropertyName).OriginalValue;

                if (ownerId is not null)
                {
                    yield return (reference, association, ownerId);
                }
            }
        }
    }

    private static IEnumerable<(PolymorphicModelMetadata.MorphManyToManyRelation Relation, object KeyValue, string Mode)> GetManyToManyCascadeTargets(DbContext dbContext, Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry deletedEntry)
    {
        foreach (var relation in PolymorphicModelMetadata.GetManyToManyRelations(dbContext.Model).Where(relation => relation.DeleteBehavior == PolymorphicDeleteBehavior.Cascade))
        {
            if (relation.PrincipalType.IsAssignableFrom(deletedEntry.Entity.GetType()))
            {
                var keyValue = deletedEntry.Property(relation.PrincipalKeyPropertyName).CurrentValue
                    ?? deletedEntry.Property(relation.PrincipalKeyPropertyName).OriginalValue;

                if (keyValue is not null)
                {
                    yield return (relation, keyValue, "principal");
                }
            }

            if (relation.RelatedType.IsAssignableFrom(deletedEntry.Entity.GetType()))
            {
                var keyValue = deletedEntry.Property(relation.RelatedKeyPropertyName).CurrentValue
                    ?? deletedEntry.Property(relation.RelatedKeyPropertyName).OriginalValue;

                if (keyValue is not null)
                {
                    yield return (relation, keyValue, "related");
                }
            }
        }
    }

    private static string BuildRelationshipKey(object entity, string relationshipName, string inverseRelationshipName, string kind)
    {
        return $"{RuntimeHelpers.GetHashCode(entity)}:{kind}:{relationshipName}:{inverseRelationshipName}";
    }
}


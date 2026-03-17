using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Reflection;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

public sealed class PolymorphicNavigationSyncInterceptor : SaveChangesInterceptor
{
    private static readonly MethodInfo SetMorphReferenceMethod = typeof(DbContextMorphExtensions)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method.Name == nameof(DbContextMorphExtensions.SetMorphReference));

    private static readonly MethodInfo AttachMorphToManyMethod = typeof(DbContextMorphExtensions)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method.Name == nameof(DbContextMorphExtensions.AttachMorphToMany));

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            SyncNavigations(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            SyncNavigations(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void SyncNavigations(DbContext dbContext)
    {
        SyncMorphToAssignments(dbContext);
        SyncInverseMorphNavigations(dbContext);
        SyncMorphToManyNavigations(dbContext);
    }

    private static void SyncMorphToAssignments(DbContext dbContext)
    {
        foreach (var reference in PolymorphicModelMetadata.GetReferences(dbContext.Model))
        {
            var entries = dbContext.ChangeTracker.Entries()
                .Where(entry => reference.DependentType.IsAssignableFrom(entry.Entity.GetType())
                    && entry.State != EntityState.Deleted)
                .ToList();

            foreach (var entry in entries)
            {
                var owner = PolymorphicMemberAccessorCache.GetValue(dbContext, entry.Entity, reference.RelationshipName);
                if (owner is null)
                {
                    if (entry.State != EntityState.Added)
                    {
                        entry.Property(reference.TypePropertyName).CurrentValue = null;
                        entry.Property(reference.IdPropertyName).CurrentValue = null;
                    }

                    continue;
                }

                EnsureEntityTracked(dbContext, owner, preferAttach: true);
                EnsureEntityTracked(dbContext, entry.Entity, preferAttach: false);
                SetMorphReference(dbContext, entry.Entity, reference.RelationshipName, owner);
            }
        }
    }

    private static void SyncInverseMorphNavigations(DbContext dbContext)
    {
        foreach (var reference in PolymorphicModelMetadata.GetReferences(dbContext.Model))
        {
            foreach (var association in reference.Associations)
            {
                var principals = dbContext.ChangeTracker.Entries()
                    .Where(entry => association.PrincipalType.IsAssignableFrom(entry.Entity.GetType())
                        && entry.State != EntityState.Deleted)
                    .ToList();

                foreach (var principalEntry in principals)
                {
                    if (association.Multiplicity == MorphMultiplicity.One)
                    {
                        var dependent = PolymorphicMemberAccessorCache.GetValue(dbContext, principalEntry.Entity, association.InverseRelationshipName);
                        if (dependent is null)
                        {
                            continue;
                        }

                        EnsureEntityTracked(dbContext, dependent, preferAttach: false);
                        SetMorphReference(dbContext, dependent, reference.RelationshipName, principalEntry.Entity);
                    }
                    else
                    {
                        var dependents = PolymorphicMemberAccessorCache.GetValue(dbContext, principalEntry.Entity, association.InverseRelationshipName) as System.Collections.IEnumerable;
                        if (dependents is null)
                        {
                            continue;
                        }

                        foreach (var dependent in dependents)
                        {
                            if (dependent is null)
                            {
                                continue;
                            }

                            EnsureEntityTracked(dbContext, dependent, preferAttach: false);
                            SetMorphReference(dbContext, dependent, reference.RelationshipName, principalEntry.Entity);
                        }
                    }
                }
            }
        }
    }

    private static void EnsureEntityTracked(DbContext dbContext, object entity, bool preferAttach)
    {
        var entry = dbContext.Entry(entity);
        if (entry.State != EntityState.Detached)
        {
            return;
        }

        if (preferAttach && HasNonDefaultPrimaryKey(dbContext, entity))
        {
            dbContext.Attach(entity);
        }
        else
        {
            dbContext.Add(entity);
        }
    }

    private static bool HasNonDefaultPrimaryKey(DbContext dbContext, object entity)
    {
        var entityType = dbContext.Model.FindEntityType(entity.GetType());
        var primaryKey = entityType?.FindPrimaryKey();
        if (primaryKey is null || primaryKey.Properties.Count != 1)
        {
            return false;
        }

        var property = primaryKey.Properties[0];
        var value = PolymorphicMemberAccessorCache.GetValue(dbContext, entity, property.Name);
        if (value is null)
        {
            return false;
        }

        var defaultValue = property.ClrType.IsValueType ? Activator.CreateInstance(property.ClrType) : null;
        return !Equals(value, defaultValue);
    }

    private static void SyncMorphToManyNavigations(DbContext dbContext)
    {
        foreach (var relation in PolymorphicModelMetadata.GetManyToManyRelations(dbContext.Model))
        {
            SyncMorphToManyPrincipalSide(dbContext, relation);
            SyncMorphToManyRelatedSide(dbContext, relation);
        }
    }

    private static void SyncMorphToManyPrincipalSide(DbContext dbContext, PolymorphicModelMetadata.MorphManyToManyRelation relation)
    {
        var principals = dbContext.ChangeTracker.Entries()
            .Where(entry => relation.PrincipalType.IsAssignableFrom(entry.Entity.GetType()) && entry.State != EntityState.Deleted)
            .Select(entry => entry.Entity)
            .ToList();

        if (principals.Count == 0)
        {
            return;
        }

        var existingPairs = LoadExistingPairs(dbContext, relation, principals);
        foreach (var principal in principals)
        {
            var principalId = PolymorphicMemberAccessorCache.GetValue(dbContext, principal, relation.PrincipalKeyPropertyName);
            if (principalId is null)
            {
                continue;
            }

            var principalKey = NormalizeLookupKey(principalId, relation.PrincipalKeyType);
            var collection = PolymorphicMemberAccessorCache.GetValue(dbContext, principal, relation.RelationshipName) as System.Collections.IEnumerable;
            if (collection is null)
            {
                continue;
            }

            foreach (var related in collection)
            {
                if (related is null)
                {
                    continue;
                }

                EnsureEntityTracked(dbContext, related, preferAttach: true);
                var relatedId = PolymorphicMemberAccessorCache.GetValue(dbContext, related, relation.RelatedKeyPropertyName);
                if (relatedId is null)
                {
                    continue;
                }

                var relatedKey = NormalizeLookupKey(relatedId, relation.RelatedKeyType);
                var pair = (principalKey, relatedKey);
                if (!existingPairs.Add(pair))
                {
                    continue;
                }

                AttachMorphToMany(dbContext, principal, relation.RelationshipName, related, relation.PivotType);
            }
        }
    }

    private static void SyncMorphToManyRelatedSide(DbContext dbContext, PolymorphicModelMetadata.MorphManyToManyRelation relation)
    {
        var relatedEntities = dbContext.ChangeTracker.Entries()
            .Where(entry => relation.RelatedType.IsAssignableFrom(entry.Entity.GetType()) && entry.State != EntityState.Deleted)
            .Select(entry => entry.Entity)
            .ToList();

        if (relatedEntities.Count == 0)
        {
            return;
        }

        var principals = dbContext.ChangeTracker.Entries()
            .Where(entry => relation.PrincipalType.IsAssignableFrom(entry.Entity.GetType()) && entry.State != EntityState.Deleted)
            .Select(entry => entry.Entity)
            .ToList();

        var existingPairs = LoadExistingPairs(dbContext, relation, principals);
        foreach (var related in relatedEntities)
        {
            var relatedId = PolymorphicMemberAccessorCache.GetValue(dbContext, related, relation.RelatedKeyPropertyName);
            if (relatedId is null)
            {
                continue;
            }

            var relatedKey = NormalizeLookupKey(relatedId, relation.RelatedKeyType);
            var collection = PolymorphicMemberAccessorCache.GetValue(dbContext, related, relation.InverseRelationshipName) as System.Collections.IEnumerable;
            if (collection is null)
            {
                continue;
            }

            foreach (var principal in collection)
            {
                if (principal is null)
                {
                    continue;
                }

                EnsureEntityTracked(dbContext, principal, preferAttach: true);
                var principalId = PolymorphicMemberAccessorCache.GetValue(dbContext, principal, relation.PrincipalKeyPropertyName);
                if (principalId is null)
                {
                    continue;
                }

                var principalKey = NormalizeLookupKey(principalId, relation.PrincipalKeyType);
                var pair = (principalKey, relatedKey);
                if (!existingPairs.Add(pair))
                {
                    continue;
                }

                AttachMorphToMany(dbContext, principal, relation.RelationshipName, related, relation.PivotType);
            }
        }
    }

    private static HashSet<(object PrincipalKey, object RelatedKey)> LoadExistingPairs(
        DbContext dbContext,
        PolymorphicModelMetadata.MorphManyToManyRelation relation,
        IEnumerable<object> principals)
    {
        var pairs = new HashSet<(object PrincipalKey, object RelatedKey)>();
        var principalIds = principals
            .Select(principal => PolymorphicMemberAccessorCache.GetValue(dbContext, principal, relation.PrincipalKeyPropertyName))
            .Where(value => value is not null)
            .Select(value => NormalizeLookupKey(value!, relation.PrincipalKeyType))
            .Distinct()
            .ToArray();

        if (principalIds.Length > 0)
        {
            foreach (var pivot in PolymorphicQueryExecutor.ListByPropertyValues(
                         dbContext,
                         relation.PivotType,
                         relation.PivotIdPropertyName,
                         relation.PivotIdPropertyType,
                         principalIds))
            {
                var typeAlias = PolymorphicMemberAccessorCache.GetValue(dbContext, pivot, relation.PivotTypePropertyName) as string;
                if (!string.Equals(typeAlias, relation.PrincipalAlias, StringComparison.Ordinal))
                {
                    continue;
                }

                var principalId = PolymorphicMemberAccessorCache.GetValue(dbContext, pivot, relation.PivotIdPropertyName);
                var relatedId = PolymorphicMemberAccessorCache.GetValue(dbContext, pivot, relation.PivotRelatedIdPropertyName);
                if (principalId is null || relatedId is null)
                {
                    continue;
                }

                pairs.Add((
                    NormalizeLookupKey(principalId, relation.PrincipalKeyType),
                    NormalizeLookupKey(relatedId, relation.RelatedKeyType)));
            }
        }

        foreach (var pivot in dbContext.ChangeTracker.Entries()
                     .Where(entry => relation.PivotType.IsAssignableFrom(entry.Entity.GetType()) && entry.State != EntityState.Deleted)
                     .Select(entry => entry.Entity))
        {
            var typeAlias = PolymorphicMemberAccessorCache.GetValue(dbContext, pivot, relation.PivotTypePropertyName) as string;
            if (!string.Equals(typeAlias, relation.PrincipalAlias, StringComparison.Ordinal))
            {
                continue;
            }

            var principalId = PolymorphicMemberAccessorCache.GetValue(dbContext, pivot, relation.PivotIdPropertyName);
            var relatedId = PolymorphicMemberAccessorCache.GetValue(dbContext, pivot, relation.PivotRelatedIdPropertyName);
            if (principalId is null || relatedId is null)
            {
                continue;
            }

            pairs.Add((
                NormalizeLookupKey(principalId, relation.PrincipalKeyType),
                NormalizeLookupKey(relatedId, relation.RelatedKeyType)));
        }

        return pairs;
    }

    private static object NormalizeLookupKey(object value, Type propertyType)
    {
        return PolymorphicValueConverter.ConvertForAssignment(value, propertyType)
            ?? throw new InvalidOperationException($"Lookup key for '{propertyType.Name}' was null.");
    }

    private static void SetMorphReference(DbContext dbContext, object dependent, string relationshipName, object principal)
    {
        SetMorphReferenceMethod
            .MakeGenericMethod(dependent.GetType(), principal.GetType())
            .Invoke(null, new object?[] { dbContext, dependent, relationshipName, principal });
    }

    private static void AttachMorphToMany(DbContext dbContext, object principal, string relationshipName, object related, Type pivotType)
    {
        AttachMorphToManyMethod
            .MakeGenericMethod(principal.GetType(), related.GetType(), pivotType)
            .Invoke(null, new object?[] { dbContext, principal, relationshipName, related, null });
    }
}

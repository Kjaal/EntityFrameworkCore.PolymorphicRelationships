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

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is not null)
        {
            result += RepairTemporaryKeyAssignments(eventData.Context);
        }

        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            result += await RepairTemporaryKeyAssignmentsAsync(eventData.Context, cancellationToken);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is not null)
        {
            PolymorphicPendingKeyRepairRegistry.Reset(eventData.Context);
        }

        base.SaveChangesFailed(eventData);
    }

    public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            PolymorphicPendingKeyRepairRegistry.Reset(eventData.Context);
        }

        await base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private static void SyncNavigations(DbContext dbContext)
    {
        SyncMorphToAssignments(dbContext);
        SyncLoadedNavigationRemovals(dbContext);
        SyncInverseMorphNavigations(dbContext);
        SyncMorphToManyNavigations(dbContext);
        PolymorphicLoadedNavigationRegistry.CommitCurrentValues(dbContext);
    }

    private static void SyncLoadedNavigationRemovals(DbContext dbContext)
    {
        foreach (var snapshot in PolymorphicLoadedNavigationRegistry.GetSnapshots(dbContext))
        {
            if (dbContext.Entry(snapshot.Entity).State is EntityState.Detached or EntityState.Deleted)
            {
                continue;
            }

            var relationship = PolymorphicRelationshipResolver.Resolve(dbContext.Model, snapshot.Entity.GetType(), snapshot.PropertyName);
            switch (relationship.Kind)
            {
                case PolymorphicRelationshipResolver.RelationshipType.MorphMany:
                    SyncMorphManyRemovals(dbContext, snapshot);
                    break;
                case PolymorphicRelationshipResolver.RelationshipType.MorphOne:
                    SyncMorphOneRemovals(dbContext, snapshot);
                    break;
                case PolymorphicRelationshipResolver.RelationshipType.MorphToMany:
                    SyncMorphToManyRemovals(dbContext, snapshot);
                    break;
                case PolymorphicRelationshipResolver.RelationshipType.MorphedByMany:
                    SyncMorphedByManyRemovals(dbContext, snapshot);
                    break;
            }
        }
    }

    private static void SyncMorphManyRemovals(DbContext dbContext, PolymorphicLoadedNavigationRegistry.LoadedNavigationSnapshot snapshot)
    {
        var currentValues = GetCurrentValues(dbContext, snapshot.Entity, snapshot.PropertyName, isCollection: true);
        var currentIdentities = currentValues.Select(value => BuildEntityIdentity(dbContext, value)).ToHashSet(StringComparer.Ordinal);
        foreach (var removedDependent in snapshot.Values.Where(value => !currentIdentities.Contains(BuildEntityIdentity(dbContext, value))))
        {
            ClearInverseMorphReference(dbContext, snapshot.Entity, removedDependent, snapshot.PropertyName, MorphMultiplicity.Many);
        }
    }

    private static void SyncMorphOneRemovals(DbContext dbContext, PolymorphicLoadedNavigationRegistry.LoadedNavigationSnapshot snapshot)
    {
        var currentValues = GetCurrentValues(dbContext, snapshot.Entity, snapshot.PropertyName, isCollection: false);
        var currentIdentities = currentValues.Select(value => BuildEntityIdentity(dbContext, value)).ToHashSet(StringComparer.Ordinal);
        foreach (var removedDependent in snapshot.Values.Where(value => !currentIdentities.Contains(BuildEntityIdentity(dbContext, value))))
        {
            ClearInverseMorphReference(dbContext, snapshot.Entity, removedDependent, snapshot.PropertyName, MorphMultiplicity.One);
        }
    }

    private static void SyncMorphToManyRemovals(DbContext dbContext, PolymorphicLoadedNavigationRegistry.LoadedNavigationSnapshot snapshot)
    {
        var currentValues = GetCurrentValues(dbContext, snapshot.Entity, snapshot.PropertyName, isCollection: true);
        var currentIdentities = currentValues.Select(value => BuildEntityIdentity(dbContext, value)).ToHashSet(StringComparer.Ordinal);
        foreach (var removedRelated in snapshot.Values.Where(value => !currentIdentities.Contains(BuildEntityIdentity(dbContext, value))))
        {
            RemoveMorphToManyPair(dbContext, snapshot.Entity, removedRelated, snapshot.PropertyName, inverseSide: false);
        }
    }

    private static void SyncMorphedByManyRemovals(DbContext dbContext, PolymorphicLoadedNavigationRegistry.LoadedNavigationSnapshot snapshot)
    {
        var currentValues = GetCurrentValues(dbContext, snapshot.Entity, snapshot.PropertyName, isCollection: true);
        var currentIdentities = currentValues.Select(value => BuildEntityIdentity(dbContext, value)).ToHashSet(StringComparer.Ordinal);
        foreach (var removedPrincipal in snapshot.Values.Where(value => !currentIdentities.Contains(BuildEntityIdentity(dbContext, value))))
        {
            RemoveMorphToManyPair(dbContext, snapshot.Entity, removedPrincipal, snapshot.PropertyName, inverseSide: true);
        }
    }

    private static void ClearInverseMorphReference(
        DbContext dbContext,
        object principal,
        object dependent,
        string inverseRelationshipName,
        MorphMultiplicity multiplicity)
    {
        var dependentEntry = dbContext.Entry(dependent);
        if (dependentEntry.State is EntityState.Detached or EntityState.Deleted)
        {
            return;
        }

        var (reference, association) = PolymorphicModelMetadata.GetRequiredInverse(
            dbContext.Model,
            principal.GetType(),
            dependent.GetType(),
            inverseRelationshipName,
            multiplicity);

        var principalEntry = dbContext.Entry(principal);
        var ownerId = principalEntry.Property(association.PrincipalKeyPropertyName).CurrentValue
            ?? principalEntry.Property(association.PrincipalKeyPropertyName).OriginalValue;
        var currentAlias = dependentEntry.Property(reference.TypePropertyName).CurrentValue as string;
        var currentOwnerId = dependentEntry.Property(reference.IdPropertyName).CurrentValue
            ?? dependentEntry.Property(reference.IdPropertyName).OriginalValue;

        if (ownerId is null || currentOwnerId is null)
        {
            return;
        }

        if (!string.Equals(currentAlias, association.Alias, StringComparison.Ordinal)
            || !Equals(NormalizeLookupKey(currentOwnerId, reference.IdPropertyType), NormalizeLookupKey(ownerId, reference.IdPropertyType)))
        {
            return;
        }

        dependentEntry.Property(reference.TypePropertyName).CurrentValue = null;
        dependentEntry.Property(reference.IdPropertyName).CurrentValue = null;
        PolymorphicMemberAccessorCache.SetValue(dependent, reference.RelationshipName, null);
    }

    private static void RemoveMorphToManyPair(DbContext dbContext, object anchorEntity, object otherEntity, string propertyName, bool inverseSide)
    {
        if (inverseSide)
        {
            var relation = PolymorphicModelMetadata.GetRequiredMorphedByMany(dbContext.Model, anchorEntity.GetType(), otherEntity.GetType(), propertyName);
            RemovePivotRows(dbContext, relation, otherEntity, anchorEntity);
            PolymorphicMemberAccessorCache.RemoveCollectionValue(otherEntity, relation.RelationshipName, anchorEntity);
            return;
        }

        var directRelation = PolymorphicModelMetadata.GetRequiredManyToMany(dbContext.Model, anchorEntity.GetType(), otherEntity.GetType(), propertyName);
        RemovePivotRows(dbContext, directRelation, anchorEntity, otherEntity);
        PolymorphicMemberAccessorCache.RemoveCollectionValue(otherEntity, directRelation.InverseRelationshipName, anchorEntity);
    }

    private static void RemovePivotRows(
        DbContext dbContext,
        PolymorphicModelMetadata.MorphManyToManyRelation relation,
        object principal,
        object related)
    {
        var principalEntry = dbContext.Entry(principal);
        var relatedEntry = dbContext.Entry(related);
        if (principalEntry.State is EntityState.Detached or EntityState.Deleted
            || relatedEntry.State is EntityState.Detached or EntityState.Deleted)
        {
            return;
        }

        var principalId = principalEntry.Property(relation.PrincipalKeyPropertyName).CurrentValue
            ?? principalEntry.Property(relation.PrincipalKeyPropertyName).OriginalValue;
        var relatedId = relatedEntry.Property(relation.RelatedKeyPropertyName).CurrentValue
            ?? relatedEntry.Property(relation.RelatedKeyPropertyName).OriginalValue;
        if (principalId is null || relatedId is null)
        {
            return;
        }

        var pivots = PolymorphicQueryExecutor.ListByTwoProperties(
            dbContext,
            relation.PivotType,
            relation.PivotTypePropertyName,
            typeof(string),
            relation.PrincipalAlias,
            relation.PivotRelatedIdPropertyName,
            relation.PivotRelatedIdPropertyType,
            relatedId);

        foreach (var pivot in pivots)
        {
            var pivotEntry = dbContext.Entry(pivot);
            if (pivotEntry.State == EntityState.Deleted)
            {
                continue;
            }

            var pivotPrincipalId = pivotEntry.Property(relation.PivotIdPropertyName).CurrentValue
                ?? pivotEntry.Property(relation.PivotIdPropertyName).OriginalValue;
            if (pivotPrincipalId is null)
            {
                continue;
            }

            if (Equals(NormalizeLookupKey(pivotPrincipalId, relation.PrincipalKeyType), NormalizeLookupKey(principalId, relation.PrincipalKeyType)))
            {
                pivotEntry.State = EntityState.Deleted;
            }
        }
    }

    private static IReadOnlyList<object> GetCurrentValues(DbContext dbContext, object entity, string propertyName, bool isCollection)
    {
        var currentValue = PolymorphicMemberAccessorCache.GetValue(dbContext, entity, propertyName);
        if (isCollection)
        {
            if (currentValue is not System.Collections.IEnumerable enumerable || currentValue is string)
            {
                return [];
            }

            return enumerable.Cast<object?>().Where(value => value is not null).Cast<object>().ToList();
        }

        return currentValue is null ? [] : [currentValue];
    }

    private static string BuildEntityIdentity(DbContext dbContext, object entity)
    {
        var entry = dbContext.Entry(entity);
        var primaryKey = entry.Metadata.FindPrimaryKey();
        if (primaryKey is null || primaryKey.Properties.Count != 1)
        {
            return $"ref:{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(entity)}";
        }

        var keyProperty = primaryKey.Properties[0];
        var keyValue = entry.Property(keyProperty.Name).CurrentValue ?? entry.Property(keyProperty.Name).OriginalValue;
        return keyValue is null
            ? $"ref:{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(entity)}"
            : Convert.ToString(PolymorphicValueConverter.ConvertForAssignment(keyValue, keyProperty.ClrType), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static int RepairTemporaryKeyAssignments(DbContext dbContext)
    {
        if (!PolymorphicPendingKeyRepairRegistry.TryBeginRepair(dbContext))
        {
            return 0;
        }

        try
        {
            var pendingRepairs = PolymorphicPendingKeyRepairRegistry.Drain(dbContext);
            ApplyPendingRepairs(dbContext, pendingRepairs);
            return dbContext.ChangeTracker.HasChanges() ? dbContext.SaveChanges() : 0;
        }
        finally
        {
            PolymorphicPendingKeyRepairRegistry.EndRepair(dbContext);
        }
    }

    private static async Task<int> RepairTemporaryKeyAssignmentsAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        if (!PolymorphicPendingKeyRepairRegistry.TryBeginRepair(dbContext))
        {
            return 0;
        }

        try
        {
            var pendingRepairs = PolymorphicPendingKeyRepairRegistry.Drain(dbContext);
            ApplyPendingRepairs(dbContext, pendingRepairs);
            return dbContext.ChangeTracker.HasChanges()
                ? await dbContext.SaveChangesAsync(cancellationToken)
                : 0;
        }
        finally
        {
            PolymorphicPendingKeyRepairRegistry.EndRepair(dbContext);
        }
    }

    private static void ApplyPendingRepairs(DbContext dbContext, PolymorphicPendingKeyRepairRegistry.PendingRepairBatch pendingRepairs)
    {
        foreach (var repair in pendingRepairs.MorphReferenceRepairs)
        {
            ApplyMorphReferenceRepair(dbContext, repair);
        }

        foreach (var repair in pendingRepairs.MorphToManyRepairs)
        {
            ApplyMorphToManyRepair(dbContext, repair);
        }
    }

    private static void ApplyMorphReferenceRepair(DbContext dbContext, PolymorphicPendingKeyRepairRegistry.PendingMorphReferenceRepair repair)
    {
        var dependentEntry = dbContext.Entry(repair.Dependent);
        var principalEntry = dbContext.Entry(repair.Principal);
        if (dependentEntry.State == EntityState.Detached || dependentEntry.State == EntityState.Deleted
            || principalEntry.State == EntityState.Detached || principalEntry.State == EntityState.Deleted)
        {
            return;
        }

        var reference = PolymorphicModelMetadata.GetRequiredReference(dbContext.Model, repair.Dependent.GetType(), repair.RelationshipName);
        var association = reference.Associations.FirstOrDefault(candidate => candidate.PrincipalType.IsAssignableFrom(repair.Principal.GetType()))
            ?? throw new InvalidOperationException($"Relationship '{repair.RelationshipName}' on '{repair.Dependent.GetType().Name}' does not allow principals of type '{repair.Principal.GetType().Name}'.");

        var principalKeyEntry = principalEntry.Property(association.PrincipalKeyPropertyName);
        var keyValue = principalKeyEntry.CurrentValue ?? principalKeyEntry.OriginalValue;
        if (keyValue is null || principalKeyEntry.IsTemporary)
        {
            return;
        }

        dependentEntry.Property(reference.TypePropertyName).CurrentValue = association.Alias;
        dependentEntry.Property(reference.IdPropertyName).CurrentValue = PolymorphicValueConverter.ConvertForAssignment(keyValue, reference.IdPropertyType);
    }

    private static void ApplyMorphToManyRepair(DbContext dbContext, PolymorphicPendingKeyRepairRegistry.PendingMorphToManyRepair repair)
    {
        var pivotEntry = dbContext.Entry(repair.Pivot);
        var principalEntry = dbContext.Entry(repair.Principal);
        var relatedEntry = dbContext.Entry(repair.Related);
        if (pivotEntry.State == EntityState.Detached || pivotEntry.State == EntityState.Deleted
            || principalEntry.State == EntityState.Detached || principalEntry.State == EntityState.Deleted
            || relatedEntry.State == EntityState.Detached || relatedEntry.State == EntityState.Deleted)
        {
            return;
        }

        var relation = PolymorphicModelMetadata.GetRequiredManyToMany(dbContext.Model, repair.Principal.GetType(), repair.Related.GetType(), repair.RelationshipName);
        var principalKeyEntry = principalEntry.Property(relation.PrincipalKeyPropertyName);
        var relatedKeyEntry = relatedEntry.Property(relation.RelatedKeyPropertyName);
        var principalId = principalKeyEntry.CurrentValue ?? principalKeyEntry.OriginalValue;
        var relatedId = relatedKeyEntry.CurrentValue ?? relatedKeyEntry.OriginalValue;
        if (principalId is null || relatedId is null || principalKeyEntry.IsTemporary || relatedKeyEntry.IsTemporary)
        {
            return;
        }

        pivotEntry.Property(relation.PivotTypePropertyName).CurrentValue = relation.PrincipalAlias;
        pivotEntry.Property(relation.PivotIdPropertyName).CurrentValue = PolymorphicValueConverter.ConvertForAssignment(principalId, relation.PivotIdPropertyType);
        pivotEntry.Property(relation.PivotRelatedIdPropertyName).CurrentValue = PolymorphicValueConverter.ConvertForAssignment(relatedId, relation.PivotRelatedIdPropertyType);
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

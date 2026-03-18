using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

public sealed class PolymorphicIntegrityValidationInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            Validate(eventData.Context);
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
            await ValidateAsync(eventData.Context, cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Validate(DbContext dbContext)
    {
        ValidateMorphReferences(dbContext, queryAsync: false, CancellationToken.None).GetAwaiter().GetResult();
        ValidateManyToManyRelations(dbContext, queryAsync: false, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static async Task ValidateAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        await ValidateMorphReferences(dbContext, queryAsync: true, cancellationToken);
        await ValidateManyToManyRelations(dbContext, queryAsync: true, cancellationToken);
    }

    private static async Task ValidateMorphReferences(DbContext dbContext, bool queryAsync, CancellationToken cancellationToken)
    {
        foreach (var reference in PolymorphicModelMetadata.GetReferences(dbContext.Model))
        {
            var entries = dbContext.ChangeTracker.Entries()
                .Where(entry => reference.DependentType.IsAssignableFrom(entry.Entity.GetType())
                    && (entry.State == EntityState.Added || entry.State == EntityState.Modified))
                .ToList();

            foreach (var entry in entries)
            {
                var typeAlias = entry.Property(reference.TypePropertyName).CurrentValue as string;
                var ownerId = entry.Property(reference.IdPropertyName).CurrentValue;

                if (string.IsNullOrWhiteSpace(typeAlias) && ownerId is null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(typeAlias) || ownerId is null)
                {
                    throw new InvalidOperationException($"Morph relationship '{reference.DependentType.Name}.{reference.RelationshipName}' requires both '{reference.TypePropertyName}' and '{reference.IdPropertyName}' to be set together.");
                }
            }

            foreach (var aliasGroup in entries
                         .Where(entry => !string.IsNullOrWhiteSpace(entry.Property(reference.TypePropertyName).CurrentValue as string)
                             && entry.Property(reference.IdPropertyName).CurrentValue is not null)
                         .GroupBy(entry => (string)entry.Property(reference.TypePropertyName).CurrentValue!, StringComparer.Ordinal))
            {
                var association = reference.Associations.FirstOrDefault(candidate => string.Equals(candidate.Alias, aliasGroup.Key, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException($"Morph relationship '{reference.DependentType.Name}.{reference.RelationshipName}' uses unregistered type alias '{aliasGroup.Key}'.");

                var keyPropertyType = dbContext.Model.FindEntityType(association.PrincipalType)?.FindProperty(association.PrincipalKeyPropertyName)?.ClrType
                    ?? throw new InvalidOperationException($"Property '{association.PrincipalKeyPropertyName}' was not found on '{association.PrincipalType.Name}'.");

                var ownerIds = aliasGroup
                    .Select(entry => PolymorphicValueConverter.ConvertForAssignment(entry.Property(reference.IdPropertyName).CurrentValue, keyPropertyType)!)
                    .Distinct()
                    .ToArray();

                await EnsureOwnersExist(
                    dbContext,
                    association.PrincipalType,
                    association.PrincipalKeyPropertyName,
                    keyPropertyType,
                    ownerIds,
                    $"Morph relationship '{reference.DependentType.Name}.{reference.RelationshipName}'",
                    queryAsync,
                    cancellationToken);

                if (association.Multiplicity == MorphMultiplicity.One)
                {
                    await EnsureMorphOneUnique(
                        dbContext,
                        reference,
                        association,
                        aliasGroup.ToArray(),
                        keyPropertyType,
                        queryAsync,
                        cancellationToken);
                }
            }
        }
    }

    private static async Task ValidateManyToManyRelations(DbContext dbContext, bool queryAsync, CancellationToken cancellationToken)
    {
        var processedPivotTypes = new HashSet<Type>();

        foreach (var relation in PolymorphicModelMetadata.GetManyToManyRelations(dbContext.Model))
        {
            if (!processedPivotTypes.Add(relation.PivotType))
            {
                continue;
            }

            var pivotRelations = PolymorphicModelMetadata.GetManyToManyRelations(dbContext.Model)
                .Where(candidate => candidate.PivotType == relation.PivotType)
                .ToArray();

            var entries = dbContext.ChangeTracker.Entries()
                .Where(entry => relation.PivotType.IsAssignableFrom(entry.Entity.GetType())
                    && (entry.State == EntityState.Added || entry.State == EntityState.Modified))
                .ToList();

            var entriesByRelation = new Dictionary<PolymorphicModelMetadata.MorphManyToManyRelation, List<EntityEntry>>();

            foreach (var entry in entries)
            {
                var typeAlias = entry.Property(relation.PivotTypePropertyName).CurrentValue as string;
                var principalId = entry.Property(relation.PivotIdPropertyName).CurrentValue;
                var relatedId = entry.Property(relation.PivotRelatedIdPropertyName).CurrentValue;

                if (string.IsNullOrWhiteSpace(typeAlias) && principalId is null && relatedId is null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(typeAlias) || principalId is null || relatedId is null)
                {
                    throw new InvalidOperationException($"Morph pivot relationship '{relation.PivotType.Name}' requires '{relation.PivotTypePropertyName}', '{relation.PivotIdPropertyName}', and '{relation.PivotRelatedIdPropertyName}' to be set together.");
                }

                var matchingRelation = pivotRelations.FirstOrDefault(candidate => string.Equals(candidate.PrincipalAlias, typeAlias, StringComparison.Ordinal));
                if (matchingRelation is null)
                {
                    throw new InvalidOperationException($"Morph pivot relationship '{relation.PivotType.Name}' uses unregistered type alias '{typeAlias}'.");
                }

                if (!entriesByRelation.TryGetValue(matchingRelation, out var relationEntries))
                {
                    relationEntries = new List<EntityEntry>();
                    entriesByRelation.Add(matchingRelation, relationEntries);
                }

                relationEntries.Add(entry);
            }

            foreach (var relationGroup in entriesByRelation)
            {
                var groupedRelation = relationGroup.Key;
                var relationEntries = relationGroup.Value;

                var principalIds = relationEntries
                    .Select(entry => PolymorphicValueConverter.ConvertForAssignment(entry.Property(groupedRelation.PivotIdPropertyName).CurrentValue, groupedRelation.PrincipalKeyType)!)
                    .Distinct()
                    .ToArray();

                if (principalIds.Length > 0)
                {
                    await EnsureOwnersExist(
                        dbContext,
                        groupedRelation.PrincipalType,
                        groupedRelation.PrincipalKeyPropertyName,
                        groupedRelation.PrincipalKeyType,
                        principalIds,
                        $"Morph pivot relationship '{groupedRelation.PivotType.Name}.{groupedRelation.RelationshipName}'",
                        queryAsync,
                        cancellationToken);
                }

                var relatedIds = relationEntries
                    .Select(entry => PolymorphicValueConverter.ConvertForAssignment(entry.Property(groupedRelation.PivotRelatedIdPropertyName).CurrentValue, groupedRelation.RelatedKeyType)!)
                    .Distinct()
                    .ToArray();

                if (relatedIds.Length > 0)
                {
                    await EnsureOwnersExist(
                        dbContext,
                        groupedRelation.RelatedType,
                        groupedRelation.RelatedKeyPropertyName,
                        groupedRelation.RelatedKeyType,
                        relatedIds,
                        $"Morph pivot relationship '{groupedRelation.PivotType.Name}.{groupedRelation.RelationshipName}'",
                        queryAsync,
                        cancellationToken);
                }

                await EnsurePivotUnique(dbContext, groupedRelation, relationEntries, queryAsync, cancellationToken);
            }
        }
    }

    private static async Task EnsureMorphOneUnique(
        DbContext dbContext,
        PolymorphicModelMetadata.MorphReference reference,
        PolymorphicModelMetadata.MorphAssociation association,
        IReadOnlyList<EntityEntry> entries,
        Type keyPropertyType,
        bool queryAsync,
        CancellationToken cancellationToken)
    {
        foreach (var ownerGroup in entries.GroupBy(
                     entry => CreateLookupKey(entry.Property(reference.IdPropertyName).CurrentValue!, keyPropertyType),
                     StringComparer.Ordinal))
        {
            if (ownerGroup.Count() > 1)
            {
                throw new InvalidOperationException($"Morph relationship '{reference.DependentType.Name}.{reference.RelationshipName}' allows only one dependent for owner '{association.Alias}:{ownerGroup.Key}'.");
            }

            var trackedEntry = ownerGroup.Single();
            var ownerId = PolymorphicValueConverter.ConvertForAssignment(trackedEntry.Property(reference.IdPropertyName).CurrentValue, keyPropertyType)!;
            var trackedPrimaryKey = GetEntryPrimaryKey(trackedEntry);
            var matches = queryAsync
                ? await PolymorphicQueryExecutor.ListByTwoPropertiesAsync(
                    dbContext,
                    reference.DependentType,
                    reference.TypePropertyName,
                    typeof(string),
                    association.Alias,
                    reference.IdPropertyName,
                    keyPropertyType,
                    ownerId,
                    cancellationToken)
                : PolymorphicQueryExecutor.ListByTwoProperties(
                    dbContext,
                    reference.DependentType,
                    reference.TypePropertyName,
                    typeof(string),
                    association.Alias,
                    reference.IdPropertyName,
                    keyPropertyType,
                    ownerId);

            var hasDuplicate = matches.Any(match => !string.Equals(GetEntityPrimaryKey(dbContext, match), trackedPrimaryKey, StringComparison.Ordinal));
            if (hasDuplicate)
            {
                throw new InvalidOperationException($"Morph relationship '{reference.DependentType.Name}.{reference.RelationshipName}' allows only one dependent for owner '{association.Alias}:{ownerGroup.Key}'.");
            }
        }
    }

    private static async Task EnsurePivotUnique(
        DbContext dbContext,
        PolymorphicModelMetadata.MorphManyToManyRelation relation,
        IReadOnlyList<EntityEntry> entries,
        bool queryAsync,
        CancellationToken cancellationToken)
    {
        foreach (var pairGroup in entries.GroupBy(
                     entry => (
                         Principal: CreateLookupKey(entry.Property(relation.PivotIdPropertyName).CurrentValue!, relation.PrincipalKeyType),
                         Related: CreateLookupKey(entry.Property(relation.PivotRelatedIdPropertyName).CurrentValue!, relation.RelatedKeyType))))
        {
            if (pairGroup.Count() > 1)
            {
                throw new InvalidOperationException($"Morph pivot relationship '{relation.PivotType.Name}.{relation.RelationshipName}' allows only one pivot row for pair '{relation.PrincipalAlias}:{pairGroup.Key.Principal}:{pairGroup.Key.Related}'.");
            }

            var trackedEntry = pairGroup.Single();
            var principalId = PolymorphicValueConverter.ConvertForAssignment(trackedEntry.Property(relation.PivotIdPropertyName).CurrentValue, relation.PrincipalKeyType)!;
            var relatedId = CreateLookupKey(trackedEntry.Property(relation.PivotRelatedIdPropertyName).CurrentValue!, relation.RelatedKeyType);
            var trackedPrimaryKey = GetEntryPrimaryKey(trackedEntry);
            var matches = queryAsync
                ? await PolymorphicQueryExecutor.ListByTwoPropertiesAsync(
                    dbContext,
                    relation.PivotType,
                    relation.PivotTypePropertyName,
                    typeof(string),
                    relation.PrincipalAlias,
                    relation.PivotIdPropertyName,
                    relation.PrincipalKeyType,
                    principalId,
                    cancellationToken)
                : PolymorphicQueryExecutor.ListByTwoProperties(
                    dbContext,
                    relation.PivotType,
                    relation.PivotTypePropertyName,
                    typeof(string),
                    relation.PrincipalAlias,
                    relation.PivotIdPropertyName,
                    relation.PrincipalKeyType,
                    principalId);

            var hasDuplicate = matches.Any(match =>
                string.Equals(CreateLookupKey(GetPropertyValue(dbContext, match, relation.PivotRelatedIdPropertyName)!, relation.RelatedKeyType), relatedId, StringComparison.Ordinal)
                && !string.Equals(GetEntityPrimaryKey(dbContext, match), trackedPrimaryKey, StringComparison.Ordinal));

            if (hasDuplicate)
            {
                throw new InvalidOperationException($"Morph pivot relationship '{relation.PivotType.Name}.{relation.RelationshipName}' allows only one pivot row for pair '{relation.PrincipalAlias}:{pairGroup.Key.Principal}:{pairGroup.Key.Related}'.");
            }
        }
    }

    private static async Task EnsureOwnersExist(
        DbContext dbContext,
        Type principalType,
        string keyPropertyName,
        Type keyPropertyType,
        IEnumerable<object> ownerIds,
        string relationshipDescription,
        bool queryAsync,
        CancellationToken cancellationToken)
    {
        var requestedKeys = ownerIds
            .Select(ownerId => CreateLookupKey(ownerId, keyPropertyType))
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        if (requestedKeys.Count == 0)
        {
            return;
        }

        var deletedKeys = dbContext.ChangeTracker.Entries()
            .Where(entry => principalType.IsAssignableFrom(entry.Entity.GetType()) && entry.State == EntityState.Deleted)
            .Select(entry => GetEntryKey(entry, keyPropertyName, keyPropertyType))
            .Where(key => key is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        if (requestedKeys.Overlaps(deletedKeys))
        {
            var missingKey = requestedKeys.First(key => deletedKeys.Contains(key));
            throw new InvalidOperationException($"{relationshipDescription} points to an owner that is being deleted in the current save operation (key '{missingKey}').");
        }

        var trackedKeys = dbContext.ChangeTracker.Entries()
            .Where(entry => principalType.IsAssignableFrom(entry.Entity.GetType()) && entry.State != EntityState.Deleted)
            .Select(entry => GetEntryKey(entry, keyPropertyName, keyPropertyType))
            .Where(key => key is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        var missingKeys = requestedKeys
            .Where(requestedKey => !trackedKeys.Contains(requestedKey))
            .ToArray();

        if (missingKeys.Length == 0)
        {
            return;
        }

        var missingValues = ownerIds
            .Where(ownerId => missingKeys.Contains(CreateLookupKey(ownerId, keyPropertyType), StringComparer.Ordinal))
            .Distinct()
            .ToArray();

        var foundOwners = queryAsync
            ? await PolymorphicQueryExecutor.ListByPropertyValuesAsync(dbContext, principalType, keyPropertyName, keyPropertyType, missingValues, cancellationToken)
            : PolymorphicQueryExecutor.ListByPropertyValues(dbContext, principalType, keyPropertyName, keyPropertyType, missingValues);

        var databaseKeys = foundOwners
            .Select(owner => GetPropertyValue(dbContext, owner, keyPropertyName))
            .Where(value => value is not null)
            .Select(value => CreateLookupKey(value!, keyPropertyType))
            .ToHashSet(StringComparer.Ordinal);

        var unresolved = missingKeys.FirstOrDefault(key => !databaseKeys.Contains(key));
        if (unresolved is not null)
        {
            throw new InvalidOperationException($"{relationshipDescription} points to a missing owner key '{unresolved}'.");
        }
    }

    private static object? GetPropertyValue(DbContext dbContext, object entity, string propertyName)
    {
        var entry = dbContext.Entry(entity);
        return entry.Property(propertyName).CurrentValue ?? entry.Property(propertyName).OriginalValue;
    }

    private static string? GetEntryKey(EntityEntry entry, string propertyName, Type propertyType)
    {
        var value = entry.Property(propertyName).CurrentValue ?? entry.Property(propertyName).OriginalValue;
        return value is null ? null : CreateLookupKey(value, propertyType);
    }

    private static string? GetEntryPrimaryKey(EntityEntry entry)
    {
        var primaryKey = entry.Metadata.FindPrimaryKey();
        if (primaryKey is null || primaryKey.Properties.Count != 1)
        {
            return null;
        }

        var keyProperty = primaryKey.Properties[0];
        var value = entry.Property(keyProperty.Name).CurrentValue ?? entry.Property(keyProperty.Name).OriginalValue;
        return value is null ? null : CreateLookupKey(value, keyProperty.ClrType);
    }

    private static string? GetEntityPrimaryKey(DbContext dbContext, object entity)
    {
        return GetEntryPrimaryKey(dbContext.Entry(entity));
    }

    private static string CreateLookupKey(object value, Type propertyType)
    {
        return Convert.ToString(PolymorphicValueConverter.ConvertForAssignment(value, propertyType), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }
}


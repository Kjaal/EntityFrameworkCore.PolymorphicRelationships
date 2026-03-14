using System.Collections;
using System.Linq.Expressions;
using EFCorePolymorphicExtension.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EFCorePolymorphicExtension;

public static class DbContextMorphExtensions
{
    public static void SetMorphReference<TDependent, TPrincipal>(
        this DbContext dbContext,
        TDependent dependent,
        string relationshipName,
        TPrincipal? principal)
        where TDependent : class
        where TPrincipal : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(dependent);

        var reference = PolymorphicModelMetadata.GetRequiredReference(dbContext.Model, typeof(TDependent), relationshipName);
        var dependentEntry = dbContext.Entry(dependent);

        if (principal is null)
        {
            dependentEntry.Property(reference.TypePropertyName).CurrentValue = null;
            dependentEntry.Property(reference.IdPropertyName).CurrentValue = null;
            AssignProperty(dependent, relationshipName, null);
            return;
        }

        var association = reference.Associations.FirstOrDefault(candidate => candidate.PrincipalType.IsAssignableFrom(principal.GetType()))
            ?? throw new InvalidOperationException($"Relationship '{relationshipName}' on '{typeof(TDependent).Name}' does not allow principals of type '{principal.GetType().Name}'.");

        var principalEntry = dbContext.Entry(principal);
        var keyValue = principalEntry.Property(association.PrincipalKeyPropertyName).CurrentValue
            ?? principalEntry.Property(association.PrincipalKeyPropertyName).OriginalValue;

        if (keyValue is null)
        {
            throw new InvalidOperationException($"The owner key '{association.PrincipalKeyPropertyName}' on '{principal.GetType().Name}' is null. This starter implementation expects morph owners to have an assigned key before the reference is set.");
        }

        dependentEntry.Property(reference.TypePropertyName).CurrentValue = association.Alias;
        dependentEntry.Property(reference.IdPropertyName).CurrentValue = PolymorphicValueConverter.ConvertForAssignment(keyValue, reference.IdPropertyType);
        AssignProperty(dependent, relationshipName, principal);
    }

    public static async Task<object?> LoadMorphAsync<TDependent>(
        this DbContext dbContext,
        TDependent dependent,
        string relationshipName,
        CancellationToken cancellationToken = default)
        where TDependent : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(dependent);

        var reference = PolymorphicModelMetadata.GetRequiredReference(dbContext.Model, typeof(TDependent), relationshipName);
        var dependentEntry = dbContext.Entry(dependent);
        var typeAlias = dependentEntry.Property<string?>(reference.TypePropertyName).CurrentValue;
        var ownerId = dependentEntry.Property(reference.IdPropertyName).CurrentValue;

        if (string.IsNullOrWhiteSpace(typeAlias) || ownerId is null)
        {
            AssignProperty(dependent, relationshipName, null);
            return null;
        }

        var association = reference.Associations.FirstOrDefault(candidate => string.Equals(candidate.Alias, typeAlias, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Type alias '{typeAlias}' is not registered for relationship '{relationshipName}'.");

        var keyPropertyType = GetPropertyType(dbContext, association.PrincipalType, association.PrincipalKeyPropertyName);
        var entity = await PolymorphicQueryExecutor.SingleOrDefaultByPropertyAsync(
            dbContext,
            association.PrincipalType,
            association.PrincipalKeyPropertyName,
            keyPropertyType,
            ownerId,
            cancellationToken);

        AssignProperty(dependent, relationshipName, entity);
        return entity;
    }

    public static async Task<TPrincipal?> LoadMorphAsync<TDependent, TPrincipal>(
        this DbContext dbContext,
        TDependent dependent,
        string relationshipName,
        CancellationToken cancellationToken = default)
        where TDependent : class
        where TPrincipal : class
    {
        return (TPrincipal?)await dbContext.LoadMorphAsync(dependent, relationshipName, cancellationToken);
    }

    public static async Task<IReadOnlyDictionary<TDependent, object?>> LoadMorphsAsync<TDependent>(
        this DbContext dbContext,
        IEnumerable<TDependent> dependents,
        string relationshipName,
        CancellationToken cancellationToken = default)
        where TDependent : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(dependents);

        var dependentList = dependents.Where(dependent => dependent is not null).Distinct().ToList();
        var results = new Dictionary<TDependent, object?>();

        if (dependentList.Count == 0)
        {
            return results;
        }

        var reference = PolymorphicModelMetadata.GetRequiredReference(dbContext.Model, typeof(TDependent), relationshipName);
        var groupedDependents = dependentList
            .Select(dependent => new
            {
                Dependent = dependent,
                Entry = dbContext.Entry(dependent),
            })
            .Select(item => new
            {
                item.Dependent,
                TypeAlias = item.Entry.Property<string?>(reference.TypePropertyName).CurrentValue,
                OwnerId = item.Entry.Property(reference.IdPropertyName).CurrentValue,
            })
            .ToList();

        foreach (var item in groupedDependents.Where(item => string.IsNullOrWhiteSpace(item.TypeAlias) || item.OwnerId is null))
        {
            AssignProperty(item.Dependent!, relationshipName, null);
            results[item.Dependent!] = null;
        }

        foreach (var aliasGroup in groupedDependents
                     .Where(item => !string.IsNullOrWhiteSpace(item.TypeAlias) && item.OwnerId is not null)
                     .GroupBy(item => item.TypeAlias!, StringComparer.Ordinal))
        {
            var association = reference.Associations.FirstOrDefault(candidate => string.Equals(candidate.Alias, aliasGroup.Key, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Type alias '{aliasGroup.Key}' is not registered for relationship '{relationshipName}'.");

            var keyPropertyType = GetPropertyType(dbContext, association.PrincipalType, association.PrincipalKeyPropertyName);
            var ownerIds = aliasGroup
                .Select(item => PolymorphicValueConverter.ConvertForAssignment(item.OwnerId, keyPropertyType)!)
                .Distinct()
                .ToArray();

            var owners = await PolymorphicQueryExecutor.ListByPropertyValuesAsync(
                dbContext,
                association.PrincipalType,
                association.PrincipalKeyPropertyName,
                keyPropertyType,
                ownerIds,
                cancellationToken);

            var ownersById = owners.ToDictionary(
                owner => CreateLookupKey(GetPropertyValueViaEntry(dbContext, owner, association.PrincipalKeyPropertyName)),
                owner => owner,
                StringComparer.Ordinal);

            foreach (var item in aliasGroup)
            {
                var owner = ownersById.GetValueOrDefault(CreateLookupKey(PolymorphicValueConverter.ConvertForAssignment(item.OwnerId, keyPropertyType)));
                AssignProperty(item.Dependent!, relationshipName, owner);
                results[item.Dependent!] = owner;
            }
        }

        return results;
    }

    public static async Task<TDependent?> LoadMorphOneAsync<TPrincipal, TDependent>(
        this DbContext dbContext,
        TPrincipal principal,
        string inverseRelationshipName,
        CancellationToken cancellationToken = default)
        where TPrincipal : class
        where TDependent : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(principal);

        var (reference, association) = PolymorphicModelMetadata.GetRequiredInverse(
            dbContext.Model,
            principal.GetType(),
            typeof(TDependent),
            inverseRelationshipName,
            MorphMultiplicity.One);

        var principalEntry = dbContext.Entry(principal);
        var ownerId = principalEntry.Property(association.PrincipalKeyPropertyName).CurrentValue
            ?? principalEntry.Property(association.PrincipalKeyPropertyName).OriginalValue;

        if (ownerId is null)
        {
            return null;
        }

        var dependent = await PolymorphicQueryExecutor.SingleOrDefaultByTwoPropertiesAsync(
            dbContext,
            typeof(TDependent),
            reference.TypePropertyName,
            typeof(string),
            association.Alias,
            reference.IdPropertyName,
            reference.IdPropertyType,
            ownerId,
            cancellationToken);

        AssignProperty(principal, inverseRelationshipName, dependent);
        return (TDependent?)dependent;
    }

    public static async Task<IReadOnlyList<TDependent>> LoadMorphManyAsync<TPrincipal, TDependent>(
        this DbContext dbContext,
        TPrincipal principal,
        string inverseRelationshipName,
        CancellationToken cancellationToken = default)
        where TPrincipal : class
        where TDependent : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(principal);

        var (reference, association) = PolymorphicModelMetadata.GetRequiredInverse(
            dbContext.Model,
            principal.GetType(),
            typeof(TDependent),
            inverseRelationshipName,
            MorphMultiplicity.Many);

        var principalEntry = dbContext.Entry(principal);
        var ownerId = principalEntry.Property(association.PrincipalKeyPropertyName).CurrentValue
            ?? principalEntry.Property(association.PrincipalKeyPropertyName).OriginalValue;

        if (ownerId is null)
        {
            return Array.Empty<TDependent>();
        }

        var dependents = await PolymorphicQueryExecutor.ListByTwoPropertiesAsync(
            dbContext,
            typeof(TDependent),
            reference.TypePropertyName,
            typeof(string),
            association.Alias,
            reference.IdPropertyName,
            reference.IdPropertyType,
            ownerId,
            cancellationToken);

        var typedDependents = dependents.Cast<TDependent>().ToList();
        AssignProperty(principal, inverseRelationshipName, typedDependents);
        return typedDependents;
    }

    public static async Task<IReadOnlyDictionary<TPrincipal, IReadOnlyList<TDependent>>> LoadMorphManyAsync<TPrincipal, TDependent>(
        this DbContext dbContext,
        IEnumerable<TPrincipal> principals,
        string inverseRelationshipName,
        CancellationToken cancellationToken = default)
        where TPrincipal : class
        where TDependent : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(principals);

        var principalList = principals.Where(principal => principal is not null).Distinct().ToList();
        var results = new Dictionary<TPrincipal, IReadOnlyList<TDependent>>();

        if (principalList.Count == 0)
        {
            return results;
        }

        var (reference, association) = PolymorphicModelMetadata.GetRequiredInverse(
            dbContext.Model,
            typeof(TPrincipal),
            typeof(TDependent),
            inverseRelationshipName,
            MorphMultiplicity.Many);

        var principalIds = principalList
            .Select(principal => new
            {
                Principal = principal,
                OwnerId = GetEntityKeyValueOrNull(dbContext, principal!, association.PrincipalKeyPropertyName),
            })
            .ToList();

        var ownerIds = principalIds
            .Where(item => item.OwnerId is not null)
            .Select(item => PolymorphicValueConverter.ConvertForAssignment(item.OwnerId, reference.IdPropertyType)!)
            .Distinct()
            .ToArray();

        var dependents = await PolymorphicQueryExecutor.ListByPropertyValuesAsync(
            dbContext,
            typeof(TDependent),
            reference.IdPropertyName,
            reference.IdPropertyType,
            ownerIds,
            cancellationToken);

        var groupedDependents = dependents
            .Where(dependent => string.Equals(GetPropertyValueViaEntry(dbContext, dependent, reference.TypePropertyName) as string, association.Alias, StringComparison.Ordinal))
            .GroupBy(dependent => CreateLookupKey(GetPropertyValueViaEntry(dbContext, dependent, reference.IdPropertyName)), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<TDependent>)group.Cast<TDependent>().ToList(),
                StringComparer.Ordinal);

        foreach (var item in principalIds)
        {
            var value = item.OwnerId is null
                ? Array.Empty<TDependent>()
                : groupedDependents.GetValueOrDefault(CreateLookupKey(PolymorphicValueConverter.ConvertForAssignment(item.OwnerId, reference.IdPropertyType))) ?? Array.Empty<TDependent>();

            AssignProperty(item.Principal!, inverseRelationshipName, value);
            results[item.Principal!] = value;
        }

        return results;
    }

    public static Task<TDependent?> LoadMorphLatestOfManyAsync<TPrincipal, TDependent, TOrder>(
        this DbContext dbContext,
        TPrincipal principal,
        string inverseRelationshipName,
        Expression<Func<TDependent, TOrder>> orderBy,
        string? assignToPropertyName = null,
        CancellationToken cancellationToken = default)
        where TPrincipal : class
        where TDependent : class
    {
        return dbContext.LoadMorphOneOfManyAsync(principal, inverseRelationshipName, orderBy, MorphOneOfManyAggregate.Max, assignToPropertyName, cancellationToken);
    }

    public static Task<TDependent?> LoadMorphOldestOfManyAsync<TPrincipal, TDependent, TOrder>(
        this DbContext dbContext,
        TPrincipal principal,
        string inverseRelationshipName,
        Expression<Func<TDependent, TOrder>> orderBy,
        string? assignToPropertyName = null,
        CancellationToken cancellationToken = default)
        where TPrincipal : class
        where TDependent : class
    {
        return dbContext.LoadMorphOneOfManyAsync(principal, inverseRelationshipName, orderBy, MorphOneOfManyAggregate.Min, assignToPropertyName, cancellationToken);
    }

    public static async Task<TDependent?> LoadMorphOneOfManyAsync<TPrincipal, TDependent, TOrder>(
        this DbContext dbContext,
        TPrincipal principal,
        string inverseRelationshipName,
        Expression<Func<TDependent, TOrder>> orderBy,
        MorphOneOfManyAggregate aggregate,
        string? assignToPropertyName = null,
        CancellationToken cancellationToken = default)
        where TPrincipal : class
        where TDependent : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(orderBy);

        var (reference, association) = PolymorphicModelMetadata.GetRequiredInverse(
            dbContext.Model,
            principal.GetType(),
            typeof(TDependent),
            inverseRelationshipName,
            MorphMultiplicity.Many);

        var ownerId = GetEntityKeyValueOrNull(dbContext, principal, association.PrincipalKeyPropertyName);
        if (ownerId is null)
        {
            AssignProperty(principal, assignToPropertyName ?? inverseRelationshipName, null);
            return null;
        }

        var orderPropertyName = ExpressionHelpers.GetPropertyName(orderBy);
        var orderPropertyType = GetPropertyType(dbContext, typeof(TDependent), orderPropertyName);
        var selected = (TDependent?)await PolymorphicQueryExecutor.FirstOrDefaultByTwoPropertiesOrderedAsync(
            dbContext,
            typeof(TDependent),
            reference.TypePropertyName,
            typeof(string),
            association.Alias,
            reference.IdPropertyName,
            reference.IdPropertyType,
            ownerId,
            orderPropertyName,
            orderPropertyType,
            aggregate == MorphOneOfManyAggregate.Max,
            cancellationToken);

        AssignProperty(principal!, assignToPropertyName ?? inverseRelationshipName, selected);
        return selected;
    }

    public static Task<IReadOnlyDictionary<TPrincipal, TDependent?>> LoadMorphLatestOfManyAsync<TPrincipal, TDependent, TOrder>(
        this DbContext dbContext,
        IEnumerable<TPrincipal> principals,
        string inverseRelationshipName,
        Expression<Func<TDependent, TOrder>> orderBy,
        string? assignToPropertyName = null,
        CancellationToken cancellationToken = default)
        where TPrincipal : class
        where TDependent : class
    {
        return dbContext.LoadMorphOneOfManyAsync(principals, inverseRelationshipName, orderBy, MorphOneOfManyAggregate.Max, assignToPropertyName, cancellationToken);
    }

    public static Task<IReadOnlyDictionary<TPrincipal, TDependent?>> LoadMorphOldestOfManyAsync<TPrincipal, TDependent, TOrder>(
        this DbContext dbContext,
        IEnumerable<TPrincipal> principals,
        string inverseRelationshipName,
        Expression<Func<TDependent, TOrder>> orderBy,
        string? assignToPropertyName = null,
        CancellationToken cancellationToken = default)
        where TPrincipal : class
        where TDependent : class
    {
        return dbContext.LoadMorphOneOfManyAsync(principals, inverseRelationshipName, orderBy, MorphOneOfManyAggregate.Min, assignToPropertyName, cancellationToken);
    }

    public static async Task<IReadOnlyDictionary<TPrincipal, TDependent?>> LoadMorphOneOfManyAsync<TPrincipal, TDependent, TOrder>(
        this DbContext dbContext,
        IEnumerable<TPrincipal> principals,
        string inverseRelationshipName,
        Expression<Func<TDependent, TOrder>> orderBy,
        MorphOneOfManyAggregate aggregate,
        string? assignToPropertyName = null,
        CancellationToken cancellationToken = default)
        where TPrincipal : class
        where TDependent : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(principals);
        ArgumentNullException.ThrowIfNull(orderBy);

        var principalList = principals.Where(principal => principal is not null).Distinct().ToList();
        var results = new Dictionary<TPrincipal, TDependent?>();

        if (principalList.Count == 0)
        {
            return results;
        }

        foreach (var principal in principalList)
        {
            var selected = await dbContext.LoadMorphOneOfManyAsync<TPrincipal, TDependent, TOrder>(
                principal,
                inverseRelationshipName,
                orderBy,
                aggregate,
                assignToPropertyName,
                cancellationToken);

            AssignProperty(principal!, assignToPropertyName ?? inverseRelationshipName, selected);
            results[principal!] = selected;
        }

        return results;
    }

    public static TPivot AttachMorphToMany<TPrincipal, TRelated, TPivot>(
        this DbContext dbContext,
        TPrincipal principal,
        string relationshipName,
        TRelated related,
        Func<TPivot>? pivotFactory = null)
        where TPrincipal : class
        where TRelated : class
        where TPivot : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(related);

        var relation = PolymorphicModelMetadata.GetRequiredManyToMany(dbContext.Model, principal.GetType(), related.GetType(), relationshipName);
        var principalId = GetEntityKeyValue(dbContext, principal, relation.PrincipalKeyPropertyName);
        var relatedId = GetEntityKeyValue(dbContext, related, relation.RelatedKeyPropertyName);

        var pivot = pivotFactory is null
            ? (TPivot?)Activator.CreateInstance(typeof(TPivot))
            : pivotFactory();

        if (pivot is null)
        {
            throw new InvalidOperationException($"A pivot instance for '{typeof(TPivot).Name}' could not be created. Provide a pivotFactory or add a public parameterless constructor.");
        }

        var pivotEntry = dbContext.Add(pivot);
        pivotEntry.Property(relation.PivotTypePropertyName).CurrentValue = relation.PrincipalAlias;
        pivotEntry.Property(relation.PivotIdPropertyName).CurrentValue = PolymorphicValueConverter.ConvertForAssignment(principalId, relation.PivotIdPropertyType);
        pivotEntry.Property(relation.PivotRelatedIdPropertyName).CurrentValue = PolymorphicValueConverter.ConvertForAssignment(relatedId, relation.PivotRelatedIdPropertyType);

        AddCollectionValue(principal, relationshipName, related);
        AddCollectionValue(related, relation.InverseRelationshipName, principal);
        return pivot;
    }

    public static async Task<int> DetachMorphToManyAsync<TPrincipal, TRelated>(
        this DbContext dbContext,
        TPrincipal principal,
        string relationshipName,
        TRelated related,
        CancellationToken cancellationToken = default)
        where TPrincipal : class
        where TRelated : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(related);

        var relation = PolymorphicModelMetadata.GetRequiredManyToMany(dbContext.Model, principal.GetType(), related.GetType(), relationshipName);
        var principalId = GetEntityKeyValue(dbContext, principal, relation.PrincipalKeyPropertyName);
        var relatedId = GetEntityKeyValue(dbContext, related, relation.RelatedKeyPropertyName);

        var pivotEntries = await PolymorphicQueryExecutor.ListByPropertyAsync(
            dbContext,
            relation.PivotType,
            relation.PivotRelatedIdPropertyName,
            relation.PivotRelatedIdPropertyType,
            relatedId,
            cancellationToken);

        var matches = pivotEntries.Where(pivot =>
                string.Equals(GetPropertyValueViaEntry(dbContext, pivot, relation.PivotTypePropertyName) as string, relation.PrincipalAlias, StringComparison.Ordinal)
                && Equals(
                    PolymorphicValueConverter.ConvertForAssignment(GetPropertyValueViaEntry(dbContext, pivot, relation.PivotIdPropertyName), relation.PrincipalKeyType),
                    PolymorphicValueConverter.ConvertForAssignment(principalId, relation.PrincipalKeyType)))
            .ToList();

        foreach (var pivot in matches)
        {
            dbContext.Remove(pivot);
        }

        return matches.Count;
    }

    public static async Task<IReadOnlyList<TRelated>> LoadMorphToManyAsync<TPrincipal, TRelated>(
        this DbContext dbContext,
        TPrincipal principal,
        string relationshipName,
        CancellationToken cancellationToken = default)
        where TPrincipal : class
        where TRelated : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(principal);

        var relation = PolymorphicModelMetadata.GetRequiredManyToMany(dbContext.Model, principal.GetType(), typeof(TRelated), relationshipName);
        var principalId = GetEntityKeyValueOrNull(dbContext, principal, relation.PrincipalKeyPropertyName);

        if (principalId is null)
        {
            var empty = Array.Empty<TRelated>();
            AssignProperty(principal, relationshipName, empty);
            return empty;
        }

        var pivotRows = await PolymorphicQueryExecutor.ListByTwoPropertiesAsync(
            dbContext,
            relation.PivotType,
            relation.PivotTypePropertyName,
            typeof(string),
            relation.PrincipalAlias,
            relation.PivotIdPropertyName,
            relation.PivotIdPropertyType,
            principalId,
            cancellationToken);

        var relatedIds = pivotRows
            .Select(pivot => GetPropertyValueViaEntry(dbContext, pivot, relation.PivotRelatedIdPropertyName))
            .Where(value => value is not null)
            .Cast<object>()
            .Distinct()
            .ToArray();

        if (relatedIds.Length == 0)
        {
            var empty = Array.Empty<TRelated>();
            AssignProperty(principal, relationshipName, empty);
            return empty;
        }

        var relatedEntities = await PolymorphicQueryExecutor.ListByPropertyValuesAsync(
            dbContext,
            relation.RelatedType,
            relation.RelatedKeyPropertyName,
            relation.RelatedKeyType,
            relatedIds,
            cancellationToken);

        var typedRelatedEntities = relatedEntities.Cast<TRelated>().ToList();
        AssignProperty(principal, relationshipName, typedRelatedEntities);
        return typedRelatedEntities;
    }

    public static async Task<IReadOnlyDictionary<TPrincipal, IReadOnlyList<TRelated>>> LoadMorphToManyAsync<TPrincipal, TRelated>(
        this DbContext dbContext,
        IEnumerable<TPrincipal> principals,
        string relationshipName,
        CancellationToken cancellationToken = default)
        where TPrincipal : class
        where TRelated : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(principals);

        var principalList = principals.Where(principal => principal is not null).Distinct().ToList();
        var results = new Dictionary<TPrincipal, IReadOnlyList<TRelated>>();

        if (principalList.Count == 0)
        {
            return results;
        }

        var relation = PolymorphicModelMetadata.GetRequiredManyToMany(dbContext.Model, typeof(TPrincipal), typeof(TRelated), relationshipName);
        var principalIds = principalList
            .Select(principal => new
            {
                Principal = principal,
                OwnerId = GetEntityKeyValueOrNull(dbContext, principal!, relation.PrincipalKeyPropertyName),
            })
            .ToList();

        var ownerIds = principalIds
            .Where(item => item.OwnerId is not null)
            .Select(item => PolymorphicValueConverter.ConvertForAssignment(item.OwnerId, relation.PivotIdPropertyType)!)
            .Distinct()
            .ToArray();

        var pivotRows = await PolymorphicQueryExecutor.ListByPropertyValuesAsync(
            dbContext,
            relation.PivotType,
            relation.PivotIdPropertyName,
            relation.PivotIdPropertyType,
            ownerIds,
            cancellationToken);

        var filteredPivots = pivotRows
            .Where(pivot => string.Equals(GetPropertyValueViaEntry(dbContext, pivot, relation.PivotTypePropertyName) as string, relation.PrincipalAlias, StringComparison.Ordinal))
            .ToList();

        var relatedIds = filteredPivots
            .Select(pivot => GetPropertyValueViaEntry(dbContext, pivot, relation.PivotRelatedIdPropertyName))
            .Where(value => value is not null)
            .Cast<object>()
            .Select(value => PolymorphicValueConverter.ConvertForAssignment(value, relation.RelatedKeyType)!)
            .Distinct()
            .ToArray();

        var relatedEntities = await PolymorphicQueryExecutor.ListByPropertyValuesAsync(
            dbContext,
            relation.RelatedType,
            relation.RelatedKeyPropertyName,
            relation.RelatedKeyType,
            relatedIds,
            cancellationToken);

        var relatedLookup = relatedEntities.ToDictionary(
            related => CreateLookupKey(GetPropertyValueViaEntry(dbContext, related, relation.RelatedKeyPropertyName)),
            related => (TRelated)related,
            StringComparer.Ordinal);

        var groupedPivots = filteredPivots
            .GroupBy(pivot => CreateLookupKey(GetPropertyValueViaEntry(dbContext, pivot, relation.PivotIdPropertyName)), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (var item in principalIds)
        {
            var ownerKey = item.OwnerId is null
                ? null
                : CreateLookupKey(PolymorphicValueConverter.ConvertForAssignment(item.OwnerId, relation.PivotIdPropertyType));

            IReadOnlyList<TRelated> relatedValues = ownerKey is null || !groupedPivots.TryGetValue(ownerKey, out var ownerPivots)
                ? Array.Empty<TRelated>()
                : ownerPivots
                    .Select(pivot => relatedLookup.GetValueOrDefault(CreateLookupKey(GetPropertyValueViaEntry(dbContext, pivot, relation.PivotRelatedIdPropertyName))))
                    .Where(related => related is not null)
                    .Distinct()
                    .Cast<TRelated>()
                    .ToList();

            AssignProperty(item.Principal!, relationshipName, relatedValues);
            results[item.Principal!] = relatedValues;
        }

        return results;
    }

    public static async Task<IReadOnlyList<TPrincipal>> LoadMorphedByManyAsync<TRelated, TPrincipal>(
        this DbContext dbContext,
        TRelated related,
        string inverseRelationshipName,
        CancellationToken cancellationToken = default)
        where TRelated : class
        where TPrincipal : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(related);

        var relation = PolymorphicModelMetadata.GetRequiredMorphedByMany(dbContext.Model, related.GetType(), typeof(TPrincipal), inverseRelationshipName);
        var relatedId = GetEntityKeyValueOrNull(dbContext, related, relation.RelatedKeyPropertyName);

        if (relatedId is null)
        {
            var empty = Array.Empty<TPrincipal>();
            AssignProperty(related, inverseRelationshipName, empty);
            return empty;
        }

        var pivotRows = await PolymorphicQueryExecutor.ListByTwoPropertiesAsync(
            dbContext,
            relation.PivotType,
            relation.PivotTypePropertyName,
            typeof(string),
            relation.PrincipalAlias,
            relation.PivotRelatedIdPropertyName,
            relation.PivotRelatedIdPropertyType,
            relatedId,
            cancellationToken);

        var principalIds = pivotRows
            .Select(pivot => GetPropertyValueViaEntry(dbContext, pivot, relation.PivotIdPropertyName))
            .Where(value => value is not null)
            .Cast<object>()
            .Distinct()
            .ToArray();

        if (principalIds.Length == 0)
        {
            var empty = Array.Empty<TPrincipal>();
            AssignProperty(related, inverseRelationshipName, empty);
            return empty;
        }

        var principals = await PolymorphicQueryExecutor.ListByPropertyValuesAsync(
            dbContext,
            relation.PrincipalType,
            relation.PrincipalKeyPropertyName,
            relation.PrincipalKeyType,
            principalIds,
            cancellationToken);

        var typedPrincipals = principals.Cast<TPrincipal>().ToList();
        AssignProperty(related, inverseRelationshipName, typedPrincipals);
        return typedPrincipals;
    }

    public static async Task<IReadOnlyDictionary<TRelated, IReadOnlyList<TPrincipal>>> LoadMorphedByManyAsync<TRelated, TPrincipal>(
        this DbContext dbContext,
        IEnumerable<TRelated> relatedEntities,
        string inverseRelationshipName,
        CancellationToken cancellationToken = default)
        where TRelated : class
        where TPrincipal : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(relatedEntities);

        var relatedList = relatedEntities.Where(related => related is not null).Distinct().ToList();
        var results = new Dictionary<TRelated, IReadOnlyList<TPrincipal>>();

        if (relatedList.Count == 0)
        {
            return results;
        }

        var relation = PolymorphicModelMetadata.GetRequiredMorphedByMany(dbContext.Model, typeof(TRelated), typeof(TPrincipal), inverseRelationshipName);
        var relatedIds = relatedList
            .Select(related => new
            {
                Related = related,
                RelatedId = GetEntityKeyValueOrNull(dbContext, related!, relation.RelatedKeyPropertyName),
            })
            .ToList();

        var keyValues = relatedIds
            .Where(item => item.RelatedId is not null)
            .Select(item => PolymorphicValueConverter.ConvertForAssignment(item.RelatedId, relation.PivotRelatedIdPropertyType)!)
            .Distinct()
            .ToArray();

        var pivotRows = await PolymorphicQueryExecutor.ListByPropertyValuesAsync(
            dbContext,
            relation.PivotType,
            relation.PivotRelatedIdPropertyName,
            relation.PivotRelatedIdPropertyType,
            keyValues,
            cancellationToken);

        var filteredPivots = pivotRows
            .Where(pivot => string.Equals(GetPropertyValueViaEntry(dbContext, pivot, relation.PivotTypePropertyName) as string, relation.PrincipalAlias, StringComparison.Ordinal))
            .ToList();

        var principalIds = filteredPivots
            .Select(pivot => GetPropertyValueViaEntry(dbContext, pivot, relation.PivotIdPropertyName))
            .Where(value => value is not null)
            .Cast<object>()
            .Select(value => PolymorphicValueConverter.ConvertForAssignment(value, relation.PrincipalKeyType)!)
            .Distinct()
            .ToArray();

        var principals = await PolymorphicQueryExecutor.ListByPropertyValuesAsync(
            dbContext,
            relation.PrincipalType,
            relation.PrincipalKeyPropertyName,
            relation.PrincipalKeyType,
            principalIds,
            cancellationToken);

        var principalLookup = principals.ToDictionary(
            principal => CreateLookupKey(GetPropertyValueViaEntry(dbContext, principal, relation.PrincipalKeyPropertyName)),
            principal => (TPrincipal)principal,
            StringComparer.Ordinal);

        var groupedPivots = filteredPivots
            .GroupBy(pivot => CreateLookupKey(GetPropertyValueViaEntry(dbContext, pivot, relation.PivotRelatedIdPropertyName)), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (var item in relatedIds)
        {
            var relatedKey = item.RelatedId is null
                ? null
                : CreateLookupKey(PolymorphicValueConverter.ConvertForAssignment(item.RelatedId, relation.PivotRelatedIdPropertyType));

            IReadOnlyList<TPrincipal> owners = relatedKey is null || !groupedPivots.TryGetValue(relatedKey, out var entityPivots)
                ? Array.Empty<TPrincipal>()
                : entityPivots
                    .Select(pivot => principalLookup.GetValueOrDefault(CreateLookupKey(GetPropertyValueViaEntry(dbContext, pivot, relation.PivotIdPropertyName))))
                    .Where(principal => principal is not null)
                    .Distinct()
                    .Cast<TPrincipal>()
                    .ToList();

            AssignProperty(item.Related!, inverseRelationshipName, owners);
            results[item.Related!] = owners;
        }

        return results;
    }

    private static Type GetPropertyType(DbContext dbContext, Type entityType, string propertyName)
    {
        var property = dbContext.Model.FindEntityType(entityType)?.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"Property '{propertyName}' was not found on entity '{entityType.Name}'.");

        return property.ClrType;
    }

    private static object GetEntityKeyValue(DbContext dbContext, object entity, string propertyName)
    {
        return GetEntityKeyValueOrNull(dbContext, entity, propertyName)
            ?? throw new InvalidOperationException($"The key property '{propertyName}' on '{entity.GetType().Name}' is null.");
    }

    private static object? GetEntityKeyValueOrNull(DbContext dbContext, object entity, string propertyName)
    {
        var entry = dbContext.Entry(entity);
        return entry.Property(propertyName).CurrentValue ?? entry.Property(propertyName).OriginalValue;
    }

    private static object? GetPropertyValueViaEntry(DbContext dbContext, object entity, string propertyName)
    {
        var entry = dbContext.Entry(entity);
        return entry.Property(propertyName).CurrentValue ?? entry.Property(propertyName).OriginalValue;
    }

    private static string CreateLookupKey(object? value)
    {
        return value?.ToString() ?? string.Empty;
    }

    private static void AssignProperty(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
        if (property is null || !property.CanWrite)
        {
            return;
        }

        if (value is null)
        {
            if (!property.PropertyType.IsValueType || Nullable.GetUnderlyingType(property.PropertyType) is not null)
            {
                property.SetValue(target, null);
            }

            return;
        }

        if (property.PropertyType.IsInstanceOfType(value))
        {
            property.SetValue(target, value);
        }
    }

    private static void AddCollectionValue(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
        if (property is null || !property.CanRead || !property.CanWrite)
        {
            return;
        }

        var collection = property.GetValue(target);
        if (collection is null)
        {
            var elementType = property.PropertyType.GenericTypeArguments.FirstOrDefault();
            if (elementType is null)
            {
                return;
            }

            var listType = typeof(List<>).MakeGenericType(elementType);
            if (!property.PropertyType.IsAssignableFrom(listType))
            {
                return;
            }

            collection = Activator.CreateInstance(listType);
            property.SetValue(target, collection);
        }

        if (collection is IList list && !list.Contains(value))
        {
            list.Add(value);
        }
    }

}


using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using EntityFrameworkCore.PolymorphicRelationships.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.PolymorphicRelationships;

public static class DbContextMorphExtensions
{
    private static readonly ConditionalWeakTable<IReadOnlyModel, Dictionary<(Type EntityType, string PropertyName), Type>> PropertyTypeCache = new();

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
        return await dbContext.LoadMorphAsync<TDependent, TPrincipal>(dependent, relationshipName, queryTransform: null, cancellationToken);
    }

    public static async Task<TPrincipal?> LoadMorphAsync<TDependent, TPrincipal>(
        this DbContext dbContext,
        TDependent dependent,
        string relationshipName,
        Func<IQueryable<TPrincipal>, IQueryable<TPrincipal>>? queryTransform,
        CancellationToken cancellationToken = default)
        where TDependent : class
        where TPrincipal : class
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

        var association = reference.Associations.FirstOrDefault(candidate => candidate.PrincipalType.IsAssignableFrom(typeof(TPrincipal))
            && string.Equals(candidate.Alias, typeAlias, StringComparison.Ordinal));

        if (association is null)
        {
            AssignProperty(dependent, relationshipName, null);
            return null;
        }

        var keyPropertyType = GetPropertyType(dbContext, association.PrincipalType, association.PrincipalKeyPropertyName);
        var query = ApplyQueryTransform(dbContext.Set<TPrincipal>().AsQueryable(), queryTransform);
        query = PolymorphicQueryableLoader.WherePropertyEquals(query, association.PrincipalKeyPropertyName, keyPropertyType, ownerId);

        var entity = await query.SingleOrDefaultAsync(cancellationToken);
        AssignProperty(dependent, relationshipName, entity);
        return entity;
    }

    public static async Task<IReadOnlyDictionary<TDependent, object?>> LoadMorphsAsync<TDependent>(
        this DbContext dbContext,
        IEnumerable<TDependent> dependents,
        string relationshipName,
        CancellationToken cancellationToken = default)
        where TDependent : class
    {
        return await dbContext.LoadMorphsAsync(dependents, relationshipName, configure: null, cancellationToken);
    }

    public static async Task<IReadOnlyDictionary<TDependent, object?>> LoadMorphsAsync<TDependent>(
        this DbContext dbContext,
        IEnumerable<TDependent> dependents,
        string relationshipName,
        Action<MorphBatchLoadPlan<TDependent>>? configure,
        CancellationToken cancellationToken = default)
        where TDependent : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(dependents);

        MorphBatchLoadPlan<TDependent>? batchLoadPlan = null;
        if (configure is not null)
        {
            batchLoadPlan = new MorphBatchLoadPlan<TDependent>();
            configure(batchLoadPlan);
        }

        var dependentList = CollectDistinctEntities(dependents);
        var results = new Dictionary<TDependent, object?>(dependentList.Count);

        if (dependentList.Count == 0)
        {
            return results;
        }

        var reference = PolymorphicModelMetadata.GetRequiredReference(dbContext.Model, typeof(TDependent), relationshipName);
        var groupedDependents = new Dictionary<string, List<MorphDependentState<TDependent>>>(StringComparer.Ordinal);

        foreach (var dependent in dependentList)
        {
            var typeAlias = GetPropertyValueViaEntry(dbContext, dependent, reference.TypePropertyName) as string;
            var ownerId = GetPropertyValueViaEntry(dbContext, dependent, reference.IdPropertyName);

            if (string.IsNullOrWhiteSpace(typeAlias) || ownerId is null)
            {
                AssignProperty(dependent, relationshipName, null);
                results[dependent] = null;
                continue;
            }

            if (!groupedDependents.TryGetValue(typeAlias, out var items))
            {
                items = new List<MorphDependentState<TDependent>>();
                groupedDependents.Add(typeAlias, items);
            }

            items.Add(new MorphDependentState<TDependent>(dependent, ownerId));
        }

        foreach (var aliasGroup in groupedDependents)
        {
            var association = reference.Associations.FirstOrDefault(candidate => string.Equals(candidate.Alias, aliasGroup.Key, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Type alias '{aliasGroup.Key}' is not registered for relationship '{relationshipName}'.");

            var keyPropertyType = GetPropertyType(dbContext, association.PrincipalType, association.PrincipalKeyPropertyName);
            var ownerIds = new HashSet<object>(EqualityComparer<object>.Default);
            foreach (var item in aliasGroup.Value)
            {
                ownerIds.Add(NormalizeLookupKey(item.OwnerId, keyPropertyType));
            }

            var owners = await LoadMorphOwnersAsync(
                dbContext,
                association,
                ownerIds,
                batchLoadPlan,
                cancellationToken);

            var ownersById = owners.ToDictionary(
                owner => NormalizeLookupKey(GetPropertyValueViaEntry(dbContext, owner, association.PrincipalKeyPropertyName), keyPropertyType),
                owner => owner,
                EqualityComparer<object>.Default);

            foreach (var item in aliasGroup.Value)
            {
                var owner = ownersById.GetValueOrDefault(NormalizeLookupKey(item.OwnerId, keyPropertyType));
                AssignProperty(item.Dependent, relationshipName, owner);
                results[item.Dependent] = owner;
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
        return await dbContext.LoadMorphManyAsync<TPrincipal, TDependent>(principal, inverseRelationshipName, queryTransform: null, cancellationToken);
    }

    public static async Task<IReadOnlyList<TDependent>> LoadMorphManyAsync<TPrincipal, TDependent>(
        this DbContext dbContext,
        TPrincipal principal,
        string inverseRelationshipName,
        Func<IQueryable<TDependent>, IQueryable<TDependent>>? queryTransform,
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

        var query = ApplyQueryTransform(dbContext.Set<TDependent>().AsQueryable(), queryTransform);
        query = PolymorphicQueryableLoader.WherePropertyEquals(query, reference.TypePropertyName, typeof(string), association.Alias);
        query = PolymorphicQueryableLoader.WherePropertyEquals(query, reference.IdPropertyName, reference.IdPropertyType, ownerId);

        var typedDependents = await query.ToListAsync(cancellationToken);
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
        return await dbContext.LoadMorphManyAsync<TPrincipal, TDependent>(principals, inverseRelationshipName, queryTransform: null, cancellationToken);
    }

    public static async Task<IReadOnlyDictionary<TPrincipal, IReadOnlyList<TDependent>>> LoadMorphManyAsync<TPrincipal, TDependent>(
        this DbContext dbContext,
        IEnumerable<TPrincipal> principals,
        string inverseRelationshipName,
        Func<IQueryable<TDependent>, IQueryable<TDependent>>? queryTransform,
        CancellationToken cancellationToken = default)
        where TPrincipal : class
        where TDependent : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(principals);

        var principalList = CollectDistinctEntities(principals);
        var results = new Dictionary<TPrincipal, IReadOnlyList<TDependent>>(principalList.Count);

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

        var principalIds = new List<EntityKeyState<TPrincipal>>(principalList.Count);
        var ownerIds = new HashSet<object>(EqualityComparer<object>.Default);

        foreach (var principal in principalList)
        {
            var ownerId = GetEntityKeyValueOrNull(dbContext, principal, association.PrincipalKeyPropertyName);
            principalIds.Add(new EntityKeyState<TPrincipal>(principal, ownerId));

            if (ownerId is not null)
            {
                ownerIds.Add(NormalizeLookupKey(ownerId, reference.IdPropertyType));
            }
        }

        var query = ApplyQueryTransform(dbContext.Set<TDependent>().AsQueryable(), queryTransform);
        query = PolymorphicQueryableLoader.WherePropertyEquals(query, reference.TypePropertyName, typeof(string), association.Alias);
        query = PolymorphicQueryableLoader.WherePropertyIn(query, reference.IdPropertyName, reference.IdPropertyType, ownerIds);

        var dependents = await query.ToListAsync(cancellationToken);

        var groupedDependents = new Dictionary<object, List<TDependent>>(EqualityComparer<object>.Default);
        foreach (var dependent in dependents)
        {
            var key = NormalizeLookupKey(GetPropertyValueViaEntry(dbContext, dependent, reference.IdPropertyName), reference.IdPropertyType);
            if (!groupedDependents.TryGetValue(key, out var items))
            {
                items = new List<TDependent>();
                groupedDependents.Add(key, items);
            }

            items.Add(dependent);
        }

        foreach (var item in principalIds)
        {
            IReadOnlyList<TDependent> value = item.OwnerId is null
                ? Array.Empty<TDependent>()
                : groupedDependents.TryGetValue(NormalizeLookupKey(item.OwnerId, reference.IdPropertyType), out var items)
                    ? items
                    : Array.Empty<TDependent>();

            AssignProperty(item.Entity, inverseRelationshipName, value);
            results[item.Entity] = value;
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
        return await dbContext.LoadMorphToManyAsync<TPrincipal, TRelated>(principal, relationshipName, queryTransform: null, cancellationToken);
    }

    public static async Task<IReadOnlyList<TRelated>> LoadMorphToManyAsync<TPrincipal, TRelated>(
        this DbContext dbContext,
        TPrincipal principal,
        string relationshipName,
        Func<IQueryable<TRelated>, IQueryable<TRelated>>? queryTransform,
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

        var query = ApplyQueryTransform(dbContext.Set<TRelated>().AsQueryable(), queryTransform);
        query = PolymorphicQueryableLoader.WherePropertyIn(query, relation.RelatedKeyPropertyName, relation.RelatedKeyType, relatedIds);

        var typedRelatedEntities = await query.ToListAsync(cancellationToken);
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
        return await dbContext.LoadMorphToManyAsync<TPrincipal, TRelated>(principals, relationshipName, queryTransform: null, cancellationToken);
    }

    public static async Task<IReadOnlyDictionary<TPrincipal, IReadOnlyList<TRelated>>> LoadMorphToManyAsync<TPrincipal, TRelated>(
        this DbContext dbContext,
        IEnumerable<TPrincipal> principals,
        string relationshipName,
        Func<IQueryable<TRelated>, IQueryable<TRelated>>? queryTransform,
        CancellationToken cancellationToken = default)
        where TPrincipal : class
        where TRelated : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(principals);

        var principalList = CollectDistinctEntities(principals);
        var results = new Dictionary<TPrincipal, IReadOnlyList<TRelated>>(principalList.Count);

        if (principalList.Count == 0)
        {
            return results;
        }

        var relation = PolymorphicModelMetadata.GetRequiredManyToMany(dbContext.Model, typeof(TPrincipal), typeof(TRelated), relationshipName);
        var principalIds = new List<EntityKeyState<TPrincipal>>(principalList.Count);
        var ownerIds = new HashSet<object>(EqualityComparer<object>.Default);
        foreach (var principal in principalList)
        {
            var ownerId = GetEntityKeyValueOrNull(dbContext, principal, relation.PrincipalKeyPropertyName);
            principalIds.Add(new EntityKeyState<TPrincipal>(principal, ownerId));

            if (ownerId is not null)
            {
                ownerIds.Add(NormalizeLookupKey(ownerId, relation.PivotIdPropertyType));
            }
        }

        var pivotRows = await PolymorphicQueryExecutor.ListByPropertyValuesAsync(
            dbContext,
            relation.PivotType,
            relation.PivotIdPropertyName,
            relation.PivotIdPropertyType,
            ownerIds,
            cancellationToken);

        var filteredPivots = new List<object>(pivotRows.Count);
        var relatedIds = new HashSet<object>(EqualityComparer<object>.Default);
        foreach (var pivot in pivotRows)
        {
            if (!string.Equals(GetPropertyValueViaEntry(dbContext, pivot, relation.PivotTypePropertyName) as string, relation.PrincipalAlias, StringComparison.Ordinal))
            {
                continue;
            }

            filteredPivots.Add(pivot);
            var relatedId = GetPropertyValueViaEntry(dbContext, pivot, relation.PivotRelatedIdPropertyName);
            if (relatedId is not null)
            {
                relatedIds.Add(NormalizeLookupKey(relatedId, relation.RelatedKeyType));
            }
        }

        var query = ApplyQueryTransform(dbContext.Set<TRelated>().AsQueryable(), queryTransform);
        query = PolymorphicQueryableLoader.WherePropertyIn(query, relation.RelatedKeyPropertyName, relation.RelatedKeyType, relatedIds);
        var relatedEntities = await query.ToListAsync(cancellationToken);

        var relatedLookup = relatedEntities.ToDictionary(
            related => NormalizeLookupKey(GetPropertyValueViaEntry(dbContext, related!, relation.RelatedKeyPropertyName), relation.RelatedKeyType),
            related => related,
            EqualityComparer<object>.Default);

        var groupedPivots = new Dictionary<object, List<object>>(EqualityComparer<object>.Default);
        foreach (var pivot in filteredPivots)
        {
            var key = NormalizeLookupKey(GetPropertyValueViaEntry(dbContext, pivot, relation.PivotIdPropertyName), relation.PivotIdPropertyType);
            if (!groupedPivots.TryGetValue(key, out var items))
            {
                items = new List<object>();
                groupedPivots.Add(key, items);
            }

            items.Add(pivot);
        }

        foreach (var item in principalIds)
        {
            var ownerKey = item.OwnerId is null
                ? null
                : NormalizeLookupKey(item.OwnerId, relation.PivotIdPropertyType);

            IReadOnlyList<TRelated> relatedValues;
            if (ownerKey is null || !groupedPivots.TryGetValue(ownerKey, out var ownerPivots))
            {
                relatedValues = Array.Empty<TRelated>();
            }
            else
            {
                var unique = new HashSet<TRelated>();
                var list = new List<TRelated>();
                foreach (var pivot in ownerPivots)
                {
                    var related = relatedLookup.GetValueOrDefault(NormalizeLookupKey(GetPropertyValueViaEntry(dbContext, pivot, relation.PivotRelatedIdPropertyName), relation.RelatedKeyType));
                    if (related is not null && unique.Add(related))
                    {
                        list.Add(related);
                    }
                }

                relatedValues = list;
            }

            AssignProperty(item.Entity, relationshipName, relatedValues);
            results[item.Entity] = relatedValues;
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
        return await dbContext.LoadMorphedByManyAsync<TRelated, TPrincipal>(related, inverseRelationshipName, queryTransform: null, cancellationToken);
    }

    public static async Task<IReadOnlyList<TPrincipal>> LoadMorphedByManyAsync<TRelated, TPrincipal>(
        this DbContext dbContext,
        TRelated related,
        string inverseRelationshipName,
        Func<IQueryable<TPrincipal>, IQueryable<TPrincipal>>? queryTransform,
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

        var query = ApplyQueryTransform(dbContext.Set<TPrincipal>().AsQueryable(), queryTransform);
        query = PolymorphicQueryableLoader.WherePropertyIn(query, relation.PrincipalKeyPropertyName, relation.PrincipalKeyType, principalIds);
        var typedPrincipals = await query.ToListAsync(cancellationToken);
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
        return await dbContext.LoadMorphedByManyAsync<TRelated, TPrincipal>(relatedEntities, inverseRelationshipName, queryTransform: null, cancellationToken);
    }

    public static async Task<IReadOnlyDictionary<TRelated, IReadOnlyList<TPrincipal>>> LoadMorphedByManyAsync<TRelated, TPrincipal>(
        this DbContext dbContext,
        IEnumerable<TRelated> relatedEntities,
        string inverseRelationshipName,
        Func<IQueryable<TPrincipal>, IQueryable<TPrincipal>>? queryTransform,
        CancellationToken cancellationToken = default)
        where TRelated : class
        where TPrincipal : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(relatedEntities);

        var relatedList = CollectDistinctEntities(relatedEntities);
        var results = new Dictionary<TRelated, IReadOnlyList<TPrincipal>>(relatedList.Count);

        if (relatedList.Count == 0)
        {
            return results;
        }

        var relation = PolymorphicModelMetadata.GetRequiredMorphedByMany(dbContext.Model, typeof(TRelated), typeof(TPrincipal), inverseRelationshipName);
        var relatedIds = new List<EntityKeyState<TRelated>>(relatedList.Count);
        var keyValues = new HashSet<object>(EqualityComparer<object>.Default);
        foreach (var related in relatedList)
        {
            var relatedId = GetEntityKeyValueOrNull(dbContext, related, relation.RelatedKeyPropertyName);
            relatedIds.Add(new EntityKeyState<TRelated>(related, relatedId));

            if (relatedId is not null)
            {
                keyValues.Add(NormalizeLookupKey(relatedId, relation.PivotRelatedIdPropertyType));
            }
        }

        var pivotRows = await PolymorphicQueryExecutor.ListByPropertyValuesAsync(
            dbContext,
            relation.PivotType,
            relation.PivotRelatedIdPropertyName,
            relation.PivotRelatedIdPropertyType,
            keyValues,
            cancellationToken);

        var filteredPivots = new List<object>(pivotRows.Count);
        var principalIds = new HashSet<object>(EqualityComparer<object>.Default);
        foreach (var pivot in pivotRows)
        {
            if (!string.Equals(GetPropertyValueViaEntry(dbContext, pivot, relation.PivotTypePropertyName) as string, relation.PrincipalAlias, StringComparison.Ordinal))
            {
                continue;
            }

            filteredPivots.Add(pivot);
            var principalId = GetPropertyValueViaEntry(dbContext, pivot, relation.PivotIdPropertyName);
            if (principalId is not null)
            {
                principalIds.Add(NormalizeLookupKey(principalId, relation.PrincipalKeyType));
            }
        }

        var query = ApplyQueryTransform(dbContext.Set<TPrincipal>().AsQueryable(), queryTransform);
        query = PolymorphicQueryableLoader.WherePropertyIn(query, relation.PrincipalKeyPropertyName, relation.PrincipalKeyType, principalIds);
        var principals = await query.ToListAsync(cancellationToken);

        var principalLookup = principals.ToDictionary(
            principal => NormalizeLookupKey(GetPropertyValueViaEntry(dbContext, principal!, relation.PrincipalKeyPropertyName), relation.PrincipalKeyType),
            principal => principal,
            EqualityComparer<object>.Default);

        var groupedPivots = new Dictionary<object, List<object>>(EqualityComparer<object>.Default);
        foreach (var pivot in filteredPivots)
        {
            var key = NormalizeLookupKey(GetPropertyValueViaEntry(dbContext, pivot, relation.PivotRelatedIdPropertyName), relation.PivotRelatedIdPropertyType);
            if (!groupedPivots.TryGetValue(key, out var items))
            {
                items = new List<object>();
                groupedPivots.Add(key, items);
            }

            items.Add(pivot);
        }

        foreach (var item in relatedIds)
        {
            var relatedKey = item.OwnerId is null
                ? null
                : NormalizeLookupKey(item.OwnerId, relation.PivotRelatedIdPropertyType);

            IReadOnlyList<TPrincipal> owners;
            if (relatedKey is null || !groupedPivots.TryGetValue(relatedKey, out var entityPivots))
            {
                owners = Array.Empty<TPrincipal>();
            }
            else
            {
                var unique = new HashSet<TPrincipal>();
                var list = new List<TPrincipal>();
                foreach (var pivot in entityPivots)
                {
                    var principal = principalLookup.GetValueOrDefault(NormalizeLookupKey(GetPropertyValueViaEntry(dbContext, pivot, relation.PivotIdPropertyName), relation.PrincipalKeyType));
                    if (principal is not null && unique.Add(principal))
                    {
                        list.Add(principal);
                    }
                }

                owners = list;
            }

            AssignProperty(item.Entity, inverseRelationshipName, owners);
            results[item.Entity] = owners;
        }

        return results;
    }

    private static Type GetPropertyType(DbContext dbContext, Type entityType, string propertyName)
    {
        var cache = PropertyTypeCache.GetValue(dbContext.Model, static _ => new Dictionary<(Type EntityType, string PropertyName), Type>());
        var key = (entityType, propertyName);

        if (cache.TryGetValue(key, out var propertyType))
        {
            return propertyType;
        }

        var property = dbContext.Model.FindEntityType(entityType)?.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"Property '{propertyName}' was not found on entity '{entityType.Name}'.");

        propertyType = property.ClrType;
        cache[key] = propertyType;
        return propertyType;
    }

    private static object GetEntityKeyValue(DbContext dbContext, object entity, string propertyName)
    {
        return GetEntityKeyValueOrNull(dbContext, entity, propertyName)
            ?? throw new InvalidOperationException($"The key property '{propertyName}' on '{entity.GetType().Name}' is null.");
    }

    private static async Task<IReadOnlyList<object>> LoadMorphOwnersAsync<TDependent>(
        DbContext dbContext,
        PolymorphicModelMetadata.MorphAssociation association,
        IEnumerable<object> ownerIds,
        MorphBatchLoadPlan<TDependent>? batchLoadPlan,
        CancellationToken cancellationToken)
        where TDependent : class
    {
        var keyPropertyType = GetPropertyType(dbContext, association.PrincipalType, association.PrincipalKeyPropertyName);
        var registration = batchLoadPlan?.FindRegistration(association.PrincipalType);

        if (registration is not null)
        {
            return await registration.LoadAsync(
                dbContext,
                association.PrincipalKeyPropertyName,
                keyPropertyType,
                ownerIds,
                cancellationToken);
        }

        return await PolymorphicQueryExecutor.ListByPropertyValuesAsync(
            dbContext,
            association.PrincipalType,
            association.PrincipalKeyPropertyName,
            keyPropertyType,
            ownerIds,
            cancellationToken);
    }

    private static List<TEntity> CollectDistinctEntities<TEntity>(IEnumerable<TEntity> entities)
        where TEntity : class
    {
        var results = new List<TEntity>();
        var seen = new HashSet<TEntity>();

        foreach (var entity in entities)
        {
            if (entity is not null && seen.Add(entity))
            {
                results.Add(entity);
            }
        }

        return results;
    }

    private static IQueryable<TEntity> ApplyQueryTransform<TEntity>(
        IQueryable<TEntity> query,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? queryTransform)
        where TEntity : class
    {
        return queryTransform is null ? query : queryTransform(query);
    }

    private static object? GetEntityKeyValueOrNull(DbContext dbContext, object entity, string propertyName)
    {
        return PolymorphicMemberAccessorCache.GetValue(dbContext, entity, propertyName);
    }

    private static object? GetPropertyValueViaEntry(DbContext dbContext, object entity, string propertyName)
    {
        return PolymorphicMemberAccessorCache.GetValue(dbContext, entity, propertyName);
    }

    private static object NormalizeLookupKey(object? value, Type keyType)
    {
        return PolymorphicValueConverter.ConvertForAssignment(value, keyType)
            ?? throw new InvalidOperationException($"A lookup key value for '{keyType.Name}' was null.");
    }

    private static void AssignProperty(object target, string propertyName, object? value)
    {
        PolymorphicMemberAccessorCache.SetValue(target, propertyName, value);
    }

    private static void AddCollectionValue(object target, string propertyName, object value)
    {
        PolymorphicMemberAccessorCache.AddCollectionValue(target, propertyName, value);
    }

    private sealed record MorphDependentState<TDependent>(TDependent Dependent, object OwnerId)
        where TDependent : class;

    private sealed record EntityKeyState<TEntity>(TEntity Entity, object? OwnerId)
        where TEntity : class;

}



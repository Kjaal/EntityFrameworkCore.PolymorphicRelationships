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
        var principalKeyEntry = principalEntry.Property(association.PrincipalKeyPropertyName);
        var keyValue = principalKeyEntry.CurrentValue
            ?? principalKeyEntry.OriginalValue;

        if (keyValue is null)
        {
            if (principalEntry.State == EntityState.Added
                && principalKeyEntry.Metadata.ValueGenerated != Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never)
            {
                dependentEntry.Property(reference.TypePropertyName).CurrentValue = null;
                dependentEntry.Property(reference.IdPropertyName).CurrentValue = null;
                AssignProperty(dependent, relationshipName, principal);
                PolymorphicPendingKeyRepairRegistry.TrackMorphReference(dbContext, dependent, relationshipName, principal);
                return;
            }

            throw new InvalidOperationException($"The owner key '{association.PrincipalKeyPropertyName}' on '{principal.GetType().Name}' is null. Morph owners must either have an assigned key or use a store-generated key configuration that resolves during SaveChanges.");
        }

        dependentEntry.Property(reference.TypePropertyName).CurrentValue = association.Alias;
        dependentEntry.Property(reference.IdPropertyName).CurrentValue = PolymorphicValueConverter.ConvertForAssignment(keyValue, reference.IdPropertyType);
        AssignProperty(dependent, relationshipName, principal);

        if (principalKeyEntry.IsTemporary)
        {
            PolymorphicPendingKeyRepairRegistry.TrackMorphReference(dbContext, dependent, relationshipName, principal);
        }
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

    public static Task<TPrincipal?> LoadMorphUntrackedAsync<TDependent, TPrincipal>(
        this DbContext dbContext,
        TDependent dependent,
        string relationshipName,
        CancellationToken cancellationToken = default)
        where TDependent : class
        where TPrincipal : class
    {
        return dbContext.LoadMorphAsync<TDependent, TPrincipal>(dependent, relationshipName, query => query.AsNoTracking(), cancellationToken);
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
        return await LoadMorphsCoreAsync(dbContext, dependents, relationshipName, configure, asNoTracking: false, cancellationToken);
    }

    public static async Task<IReadOnlyDictionary<TDependent, object?>> LoadMorphsUntrackedAsync<TDependent>(
        this DbContext dbContext,
        IEnumerable<TDependent> dependents,
        string relationshipName,
        CancellationToken cancellationToken = default)
        where TDependent : class
    {
        return await LoadMorphsCoreAsync(dbContext, dependents, relationshipName, configure: null, asNoTracking: true, cancellationToken);
    }

    public static async Task<IReadOnlyDictionary<TDependent, object?>> LoadMorphsUntrackedAsync<TDependent>(
        this DbContext dbContext,
        IEnumerable<TDependent> dependents,
        string relationshipName,
        Action<MorphBatchLoadPlan<TDependent>>? configure,
        CancellationToken cancellationToken = default)
        where TDependent : class
    {
        return await LoadMorphsCoreAsync(dbContext, dependents, relationshipName, configure, asNoTracking: true, cancellationToken);
    }

    private static async Task<IReadOnlyDictionary<TDependent, object?>> LoadMorphsCoreAsync<TDependent>(
        DbContext dbContext,
        IEnumerable<TDependent> dependents,
        string relationshipName,
        Action<MorphBatchLoadPlan<TDependent>>? configure,
        bool asNoTracking,
        CancellationToken cancellationToken)
        where TDependent : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(dependents);

        MorphBatchLoadPlan<TDependent>? batchLoadPlan = null;
        if (configure is not null)
        {
            batchLoadPlan = new MorphBatchLoadPlan<TDependent>(asNoTracking);
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
                asNoTracking,
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
        return await dbContext.LoadMorphOneAsync<TPrincipal, TDependent>(principal, inverseRelationshipName, queryTransform: null, cancellationToken);
    }

    public static async Task<TDependent?> LoadMorphOneAsync<TPrincipal, TDependent>(
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

        var results = await LoadMorphOneBatchAsync(dbContext, new[] { principal }, inverseRelationshipName, queryTransform, cancellationToken);
        return results.GetValueOrDefault(principal);
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

    public static Task<IReadOnlyList<TDependent>> LoadMorphManyUntrackedAsync<TPrincipal, TDependent>(
        this DbContext dbContext,
        TPrincipal principal,
        string inverseRelationshipName,
        CancellationToken cancellationToken = default)
        where TPrincipal : class
        where TDependent : class
    {
        return dbContext.LoadMorphManyAsync<TPrincipal, TDependent>(principal, inverseRelationshipName, query => query.AsNoTracking(), cancellationToken);
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
        RecordLoadedCollectionSnapshot(dbContext, principal, inverseRelationshipName, typedDependents);
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

    public static Task<IReadOnlyDictionary<TPrincipal, IReadOnlyList<TDependent>>> LoadMorphManyUntrackedAsync<TPrincipal, TDependent>(
        this DbContext dbContext,
        IEnumerable<TPrincipal> principals,
        string inverseRelationshipName,
        CancellationToken cancellationToken = default)
        where TPrincipal : class
        where TDependent : class
    {
        return dbContext.LoadMorphManyAsync<TPrincipal, TDependent>(principals, inverseRelationshipName, query => query.AsNoTracking(), cancellationToken);
    }

    public static async Task<IReadOnlyDictionary<object, IReadOnlyList<TDependent>>> LoadMorphManyAcrossAsync<TDependent>(
        this DbContext dbContext,
        IEnumerable<object> principals,
        string inverseRelationshipName,
        CancellationToken cancellationToken = default)
        where TDependent : class
    {
        return await dbContext.LoadMorphManyAcrossAsync<TDependent>(principals, inverseRelationshipName, queryTransform: null, cancellationToken);
    }

    public static async Task<IReadOnlyDictionary<object, IReadOnlyList<TDependent>>> LoadMorphManyAcrossAsync<TDependent>(
        this DbContext dbContext,
        IEnumerable<object> principals,
        string inverseRelationshipName,
        Func<IQueryable<TDependent>, IQueryable<TDependent>>? queryTransform,
        CancellationToken cancellationToken = default)
        where TDependent : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(principals);

        var principalList = CollectDistinctObjects(principals);
        var results = new Dictionary<object, IReadOnlyList<TDependent>>(principalList.Count);

        if (principalList.Count == 0)
        {
            return results;
        }

        foreach (var principalGroup in principalList.GroupBy(principal => principal.GetType()))
        {
            var (reference, association) = PolymorphicModelMetadata.GetRequiredInverse(
                dbContext.Model,
                principalGroup.Key,
                typeof(TDependent),
                inverseRelationshipName,
                MorphMultiplicity.Many);

            var principalIds = new List<EntityKeyState<object>>();
            var ownerIds = new HashSet<object>(EqualityComparer<object>.Default);

            foreach (var principal in principalGroup)
            {
                var ownerId = GetEntityKeyValueOrNull(dbContext, principal, association.PrincipalKeyPropertyName);
                principalIds.Add(new EntityKeyState<object>(principal, ownerId));

                if (ownerId is not null)
                {
                    ownerIds.Add(NormalizeLookupKey(ownerId, reference.IdPropertyType));
                }
            }

            var query = ApplyQueryTransform(dbContext.Set<TDependent>().AsQueryable(), queryTransform);
            query = PolymorphicQueryableLoader.WherePropertyEquals(query, reference.TypePropertyName, typeof(string), association.Alias);
            var dependents = (await PolymorphicQueryableLoader.ListByPropertyValuesAsync(query, reference.IdPropertyName, reference.IdPropertyType, ownerIds, cancellationToken))
                .Cast<TDependent>()
                .ToList();
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
                RecordLoadedCollectionSnapshot(dbContext, item.Entity, inverseRelationshipName, value.Cast<object>());
                results[item.Entity] = value;
            }
        }

        return results;
    }

    public static Task<IReadOnlyDictionary<object, IReadOnlyList<TDependent>>> LoadMorphManyAcrossUntrackedAsync<TDependent>(
        this DbContext dbContext,
        IEnumerable<object> principals,
        string inverseRelationshipName,
        CancellationToken cancellationToken = default)
        where TDependent : class
    {
        return dbContext.LoadMorphManyAcrossAsync<TDependent>(principals, inverseRelationshipName, query => query.AsNoTracking(), cancellationToken);
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

        foreach (var principalGroup in principalList.GroupBy(principal => principal.GetType()))
        {
            var (reference, association) = PolymorphicModelMetadata.GetRequiredInverse(
                dbContext.Model,
                principalGroup.Key,
                typeof(TDependent),
                inverseRelationshipName,
                MorphMultiplicity.Many);

            var principalIds = new List<EntityKeyState<TPrincipal>>();
            var ownerIds = new HashSet<object>(EqualityComparer<object>.Default);

            foreach (var principal in principalGroup)
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
            var dependents = (await PolymorphicQueryableLoader.ListByPropertyValuesAsync(query, reference.IdPropertyName, reference.IdPropertyType, ownerIds, cancellationToken))
                .Cast<TDependent>()
                .ToList();

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
                RecordLoadedCollectionSnapshot(dbContext, item.Entity, inverseRelationshipName, value.Cast<object>());
                results[item.Entity] = value;
            }
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
        return dbContext.LoadMorphOneOfManyAsync(principal, inverseRelationshipName, orderBy, MorphOneOfManyAggregate.Max, assignToPropertyName, queryTransform: null, cancellationToken);
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
        return dbContext.LoadMorphOneOfManyAsync(principal, inverseRelationshipName, orderBy, MorphOneOfManyAggregate.Min, assignToPropertyName, queryTransform: null, cancellationToken);
    }

    public static Task<TDependent?> LoadMorphLatestOfManyUntrackedAsync<TPrincipal, TDependent, TOrder>(
        this DbContext dbContext,
        TPrincipal principal,
        string inverseRelationshipName,
        Expression<Func<TDependent, TOrder>> orderBy,
        string? assignToPropertyName = null,
        CancellationToken cancellationToken = default)
        where TPrincipal : class
        where TDependent : class
    {
        return dbContext.LoadMorphOneOfManyAsync(principal, inverseRelationshipName, orderBy, MorphOneOfManyAggregate.Max, assignToPropertyName, query => query.AsNoTracking(), cancellationToken);
    }

    public static async Task<TDependent?> LoadMorphOneOfManyAsync<TPrincipal, TDependent, TOrder>(
        this DbContext dbContext,
        TPrincipal principal,
        string inverseRelationshipName,
        Expression<Func<TDependent, TOrder>> orderBy,
        MorphOneOfManyAggregate aggregate,
        string? assignToPropertyName = null,
        Func<IQueryable<TDependent>, IQueryable<TDependent>>? queryTransform = null,
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
        var query = ApplyQueryTransform(dbContext.Set<TDependent>().AsQueryable(), queryTransform);
        query = PolymorphicQueryableLoader.WherePropertyEquals(query, reference.TypePropertyName, typeof(string), association.Alias);
        query = PolymorphicQueryableLoader.WherePropertyEquals(query, reference.IdPropertyName, reference.IdPropertyType, ownerId);
        query = PolymorphicQueryableLoader.OrderByProperty(query, orderPropertyName, orderPropertyType, aggregate == MorphOneOfManyAggregate.Max);

        var selected = await query.FirstOrDefaultAsync(cancellationToken);

        AssignProperty(principal!, assignToPropertyName ?? inverseRelationshipName, selected);
        if (string.Equals(assignToPropertyName ?? inverseRelationshipName, inverseRelationshipName, StringComparison.Ordinal))
        {
            RecordLoadedReferenceSnapshot(dbContext, principal, inverseRelationshipName, selected);
        }
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
        return dbContext.LoadMorphOneOfManyAsync(principals, inverseRelationshipName, orderBy, MorphOneOfManyAggregate.Max, assignToPropertyName, queryTransform: null, cancellationToken);
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
        return dbContext.LoadMorphOneOfManyAsync(principals, inverseRelationshipName, orderBy, MorphOneOfManyAggregate.Min, assignToPropertyName, queryTransform: null, cancellationToken);
    }

    public static async Task<IReadOnlyDictionary<TPrincipal, TDependent?>> LoadMorphOneOfManyAsync<TPrincipal, TDependent, TOrder>(
        this DbContext dbContext,
        IEnumerable<TPrincipal> principals,
        string inverseRelationshipName,
        Expression<Func<TDependent, TOrder>> orderBy,
        MorphOneOfManyAggregate aggregate,
        string? assignToPropertyName = null,
        Func<IQueryable<TDependent>, IQueryable<TDependent>>? queryTransform = null,
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

        var orderPropertyName = ExpressionHelpers.GetPropertyName(orderBy);
        var orderPropertyType = GetPropertyType(dbContext, typeof(TDependent), orderPropertyName);

        foreach (var principalGroup in principalList.GroupBy(principal => principal.GetType()))
        {
            var (reference, association) = PolymorphicModelMetadata.GetRequiredInverse(
                dbContext.Model,
                principalGroup.Key,
                typeof(TDependent),
                inverseRelationshipName,
                MorphMultiplicity.Many);

            var principalIds = new List<EntityKeyState<TPrincipal>>();
            var ownerIds = new HashSet<object>(EqualityComparer<object>.Default);

            foreach (var principal in principalGroup)
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
            query = PolymorphicQueryableLoader.OrderByProperty(query, orderPropertyName, orderPropertyType, aggregate == MorphOneOfManyAggregate.Max);

            var dependents = (await PolymorphicQueryableLoader.ListByPropertyValuesAsync(query, reference.IdPropertyName, reference.IdPropertyType, ownerIds, cancellationToken))
                .Cast<TDependent>()
                .ToList();
            var selectedByOwner = new Dictionary<object, TDependent>(EqualityComparer<object>.Default);
            foreach (var dependent in dependents)
            {
                var ownerKey = NormalizeLookupKey(GetPropertyValueViaEntry(dbContext, dependent, reference.IdPropertyName), reference.IdPropertyType);
                selectedByOwner.TryAdd(ownerKey, dependent);
            }

            foreach (var item in principalIds)
            {
                TDependent? selected = null;
                if (item.OwnerId is not null)
                {
                    selectedByOwner.TryGetValue(NormalizeLookupKey(item.OwnerId, reference.IdPropertyType), out selected);
                }

                AssignProperty(item.Entity, assignToPropertyName ?? inverseRelationshipName, selected);
                if (string.Equals(assignToPropertyName ?? inverseRelationshipName, inverseRelationshipName, StringComparison.Ordinal))
                {
                    RecordLoadedReferenceSnapshot(dbContext, item.Entity, inverseRelationshipName, selected);
                }
                results[item.Entity] = selected;
            }
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
        var principalEntry = dbContext.Entry(principal);
        var relatedEntry = dbContext.Entry(related);
        var principalKeyEntry = principalEntry.Property(relation.PrincipalKeyPropertyName);
        var relatedKeyEntry = relatedEntry.Property(relation.RelatedKeyPropertyName);
        var principalId = principalKeyEntry.CurrentValue
            ?? principalKeyEntry.OriginalValue
            ?? throw new InvalidOperationException($"The key property '{relation.PrincipalKeyPropertyName}' on '{principal.GetType().Name}' is null.");
        var relatedId = relatedKeyEntry.CurrentValue
            ?? relatedKeyEntry.OriginalValue
            ?? throw new InvalidOperationException($"The key property '{relation.RelatedKeyPropertyName}' on '{related.GetType().Name}' is null.");

        if (TryGetTrackedMorphToManyPivot(dbContext, relation, principalId, relatedId, out var existingPivot))
        {
            AddCollectionValue(principal, relationshipName, related);
            AddCollectionValue(related, relation.InverseRelationshipName, principal);

            if (principalKeyEntry.IsTemporary || relatedKeyEntry.IsTemporary)
            {
                PolymorphicPendingKeyRepairRegistry.TrackMorphToMany(dbContext, existingPivot, principal, related, relationshipName);
            }

            return (TPivot)existingPivot;
        }

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

        if (principalKeyEntry.IsTemporary || relatedKeyEntry.IsTemporary)
        {
            PolymorphicPendingKeyRepairRegistry.TrackMorphToMany(dbContext, pivot, principal, related, relationshipName);
        }

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

        var pivotEntries = await PolymorphicQueryExecutor.ListByTwoPropertiesAsync(
            dbContext,
            relation.PivotType,
            relation.PivotTypePropertyName,
            typeof(string),
            relation.PrincipalAlias,
            relation.PivotRelatedIdPropertyName,
            relation.PivotRelatedIdPropertyType,
            relatedId,
            cancellationToken);

        var matches = pivotEntries.Where(pivot =>
                Equals(
                    PolymorphicValueConverter.ConvertForAssignment(GetPropertyValueViaEntry(dbContext, pivot, relation.PivotIdPropertyName), relation.PrincipalKeyType),
                    PolymorphicValueConverter.ConvertForAssignment(principalId, relation.PrincipalKeyType)))
            .ToList();

        foreach (var pivot in matches)
        {
            dbContext.Remove(pivot);
        }

        if (matches.Count > 0)
        {
            RemoveCollectionValue(principal, relationshipName, related);
            RemoveCollectionValue(related, relation.InverseRelationshipName, principal);
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

    public static Task<IReadOnlyList<TRelated>> LoadMorphToManyUntrackedAsync<TPrincipal, TRelated>(
        this DbContext dbContext,
        TPrincipal principal,
        string relationshipName,
        CancellationToken cancellationToken = default)
        where TPrincipal : class
        where TRelated : class
    {
        return dbContext.LoadMorphToManyAsync<TPrincipal, TRelated>(principal, relationshipName, query => query.AsNoTracking(), cancellationToken);
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
            RecordLoadedCollectionSnapshot(dbContext, principal, relationshipName, empty.Cast<object>());
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
            RecordLoadedCollectionSnapshot(dbContext, principal, relationshipName, empty.Cast<object>());
            return empty;
        }

        var query = ApplyQueryTransform(dbContext.Set<TRelated>().AsQueryable(), queryTransform);
        var typedRelatedEntities = (await PolymorphicQueryableLoader.ListByPropertyValuesAsync(query, relation.RelatedKeyPropertyName, relation.RelatedKeyType, relatedIds, cancellationToken))
            .Cast<TRelated>()
            .ToList();
        AssignProperty(principal, relationshipName, typedRelatedEntities);
        RecordLoadedCollectionSnapshot(dbContext, principal, relationshipName, typedRelatedEntities);
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

    public static Task<IReadOnlyDictionary<TPrincipal, IReadOnlyList<TRelated>>> LoadMorphToManyUntrackedAsync<TPrincipal, TRelated>(
        this DbContext dbContext,
        IEnumerable<TPrincipal> principals,
        string relationshipName,
        CancellationToken cancellationToken = default)
        where TPrincipal : class
        where TRelated : class
    {
        return dbContext.LoadMorphToManyAsync<TPrincipal, TRelated>(principals, relationshipName, query => query.AsNoTracking(), cancellationToken);
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

        foreach (var principalGroup in principalList.GroupBy(principal => principal.GetType()))
        {
            var relation = PolymorphicModelMetadata.GetRequiredManyToMany(dbContext.Model, principalGroup.Key, typeof(TRelated), relationshipName);
            var principalIds = new List<EntityKeyState<TPrincipal>>();
            var ownerIds = new HashSet<object>(EqualityComparer<object>.Default);
            foreach (var principal in principalGroup)
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
            var relatedEntities = (await PolymorphicQueryableLoader.ListByPropertyValuesAsync(query, relation.RelatedKeyPropertyName, relation.RelatedKeyType, relatedIds, cancellationToken))
                .Cast<TRelated>()
                .ToList();

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
                RecordLoadedCollectionSnapshot(dbContext, item.Entity, relationshipName, relatedValues.Cast<object>());
                results[item.Entity] = relatedValues;
            }
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

    public static Task<IReadOnlyList<TPrincipal>> LoadMorphedByManyUntrackedAsync<TRelated, TPrincipal>(
        this DbContext dbContext,
        TRelated related,
        string inverseRelationshipName,
        CancellationToken cancellationToken = default)
        where TRelated : class
        where TPrincipal : class
    {
        return dbContext.LoadMorphedByManyAsync<TRelated, TPrincipal>(related, inverseRelationshipName, query => query.AsNoTracking(), cancellationToken);
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
            RecordLoadedCollectionSnapshot(dbContext, related, inverseRelationshipName, empty.Cast<object>());
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
            RecordLoadedCollectionSnapshot(dbContext, related, inverseRelationshipName, empty.Cast<object>());
            return empty;
        }

        var query = ApplyQueryTransform(dbContext.Set<TPrincipal>().AsQueryable(), queryTransform);
        var typedPrincipals = (await PolymorphicQueryableLoader.ListByPropertyValuesAsync(query, relation.PrincipalKeyPropertyName, relation.PrincipalKeyType, principalIds, cancellationToken))
            .Cast<TPrincipal>()
            .ToList();
        AssignProperty(related, inverseRelationshipName, typedPrincipals);
        RecordLoadedCollectionSnapshot(dbContext, related, inverseRelationshipName, typedPrincipals);
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

    public static Task<IReadOnlyDictionary<TRelated, IReadOnlyList<TPrincipal>>> LoadMorphedByManyUntrackedAsync<TRelated, TPrincipal>(
        this DbContext dbContext,
        IEnumerable<TRelated> relatedEntities,
        string inverseRelationshipName,
        CancellationToken cancellationToken = default)
        where TRelated : class
        where TPrincipal : class
    {
        return dbContext.LoadMorphedByManyAsync<TRelated, TPrincipal>(relatedEntities, inverseRelationshipName, query => query.AsNoTracking(), cancellationToken);
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

        foreach (var relatedGroup in relatedList.GroupBy(related => related.GetType()))
        {
            var relation = PolymorphicModelMetadata.GetRequiredMorphedByMany(dbContext.Model, relatedGroup.Key, typeof(TPrincipal), inverseRelationshipName);
            var relatedIds = new List<EntityKeyState<TRelated>>();
            var keyValues = new HashSet<object>(EqualityComparer<object>.Default);
            foreach (var related in relatedGroup)
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
            var principals = (await PolymorphicQueryableLoader.ListByPropertyValuesAsync(query, relation.PrincipalKeyPropertyName, relation.PrincipalKeyType, principalIds, cancellationToken))
                .Cast<TPrincipal>()
                .ToList();

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
                RecordLoadedCollectionSnapshot(dbContext, item.Entity, inverseRelationshipName, owners.Cast<object>());
                results[item.Entity] = owners;
            }
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
        bool asNoTracking,
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

        if (asNoTracking)
        {
            return await PolymorphicQueryExecutor.ListByPropertyValuesAsync(
                dbContext,
                association.PrincipalType,
                association.PrincipalKeyPropertyName,
                keyPropertyType,
                ownerIds,
                asNoTracking: true,
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

    internal static async Task<IReadOnlyDictionary<TPrincipal, TDependent?>> LoadMorphOneBatchAsync<TPrincipal, TDependent>(
        DbContext dbContext,
        IReadOnlyList<TPrincipal> principals,
        string inverseRelationshipName,
        Func<IQueryable<TDependent>, IQueryable<TDependent>>? queryTransform,
        CancellationToken cancellationToken)
        where TPrincipal : class
        where TDependent : class
    {
        var results = new Dictionary<TPrincipal, TDependent?>(principals.Count);
        if (principals.Count == 0)
        {
            return results;
        }

        foreach (var principalGroup in principals.GroupBy(principal => principal!.GetType()))
        {
            var (reference, association) = PolymorphicModelMetadata.GetRequiredInverse(
                dbContext.Model,
                principalGroup.Key,
                typeof(TDependent),
                inverseRelationshipName,
                MorphMultiplicity.One);

            var principalIds = new List<EntityKeyState<TPrincipal>>();
            var ownerIds = new HashSet<object>(EqualityComparer<object>.Default);

            foreach (var principal in principalGroup)
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
            var dependents = (await PolymorphicQueryableLoader.ListByPropertyValuesAsync(query, reference.IdPropertyName, reference.IdPropertyType, ownerIds, cancellationToken))
                .Cast<TDependent>()
                .ToList();
            var byOwner = new Dictionary<object, TDependent>(EqualityComparer<object>.Default);
            foreach (var dependent in dependents)
            {
                var ownerKey = NormalizeLookupKey(GetPropertyValueViaEntry(dbContext, dependent, reference.IdPropertyName), reference.IdPropertyType);
                byOwner.TryAdd(ownerKey, dependent);
            }

            foreach (var item in principalIds)
            {
                TDependent? dependent = null;
                if (item.OwnerId is not null)
                {
                    byOwner.TryGetValue(NormalizeLookupKey(item.OwnerId, reference.IdPropertyType), out dependent);
                }

                AssignProperty(item.Entity, inverseRelationshipName, dependent);
                RecordLoadedReferenceSnapshot(dbContext, item.Entity, inverseRelationshipName, dependent);
                results[item.Entity] = dependent;
            }
        }

        return results;
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

    private static List<object> CollectDistinctObjects(IEnumerable<object> entities)
    {
        var results = new List<object>();
        var seen = new HashSet<object>();

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

    private static bool TryGetTrackedMorphToManyPivot(
        DbContext dbContext,
        PolymorphicModelMetadata.MorphManyToManyRelation relation,
        object principalId,
        object relatedId,
        out object pivot)
    {
        var normalizedPrincipalId = NormalizeLookupKey(principalId, relation.PivotIdPropertyType);
        var normalizedRelatedId = NormalizeLookupKey(relatedId, relation.PivotRelatedIdPropertyType);

        foreach (var entry in dbContext.ChangeTracker.Entries())
        {
            if (!relation.PivotType.IsAssignableFrom(entry.Entity.GetType()) || entry.State == EntityState.Deleted)
            {
                continue;
            }

            if (!string.Equals(entry.Property(relation.PivotTypePropertyName).CurrentValue as string, relation.PrincipalAlias, StringComparison.Ordinal))
            {
                continue;
            }

            var existingPrincipalId = entry.Property(relation.PivotIdPropertyName).CurrentValue ?? entry.Property(relation.PivotIdPropertyName).OriginalValue;
            var existingRelatedId = entry.Property(relation.PivotRelatedIdPropertyName).CurrentValue ?? entry.Property(relation.PivotRelatedIdPropertyName).OriginalValue;
            if (existingPrincipalId is null || existingRelatedId is null)
            {
                continue;
            }

            if (Equals(NormalizeLookupKey(existingPrincipalId, relation.PivotIdPropertyType), normalizedPrincipalId)
                && Equals(NormalizeLookupKey(existingRelatedId, relation.PivotRelatedIdPropertyType), normalizedRelatedId))
            {
                pivot = entry.Entity;
                return true;
            }
        }

        pivot = null!;
        return false;
    }

    private static void AssignProperty(object target, string propertyName, object? value)
    {
        PolymorphicMemberAccessorCache.SetValue(target, propertyName, value);
    }

    private static void AddCollectionValue(object target, string propertyName, object value)
    {
        PolymorphicMemberAccessorCache.AddCollectionValue(target, propertyName, value);
    }

    private static void RemoveCollectionValue(object target, string propertyName, object value)
    {
        PolymorphicMemberAccessorCache.RemoveCollectionValue(target, propertyName, value);
    }

    private static void RecordLoadedReferenceSnapshot(DbContext dbContext, object entity, string propertyName, object? value)
    {
        PolymorphicLoadedNavigationRegistry.RecordReference(dbContext, entity, propertyName, value);
    }

    private static void RecordLoadedCollectionSnapshot(DbContext dbContext, object entity, string propertyName, IEnumerable<object> values)
    {
        PolymorphicLoadedNavigationRegistry.RecordCollection(dbContext, entity, propertyName, values);
    }

    private sealed record MorphDependentState<TDependent>(TDependent Dependent, object OwnerId)
        where TDependent : class;

    private sealed record EntityKeyState<TEntity>(TEntity Entity, object? OwnerId)
        where TEntity : class;

}



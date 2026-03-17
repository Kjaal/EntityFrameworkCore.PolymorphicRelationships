using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

internal static class MorphIncludeLoader
{
    private static readonly ConditionalWeakTable<IReadOnlyModel, ConcurrentDictionary<(Type EntityType, string PropertyName), RelationshipKind>> RelationshipKinds = new();

    private static readonly MethodInfo ApplyMorphManyMethod = typeof(MorphIncludeLoader)
        .GetMethod(nameof(ApplyMorphManyAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ApplyMorphToManyMethod = typeof(MorphIncludeLoader)
        .GetMethod(nameof(ApplyMorphToManyAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ApplyMorphedByManyMethod = typeof(MorphIncludeLoader)
        .GetMethod(nameof(ApplyMorphedByManyAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ApplyMorphOwnerMethod = typeof(MorphIncludeLoader)
        .GetMethod(nameof(ApplyMorphOwnerAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ApplyMorphOneMethod = typeof(MorphIncludeLoader)
        .GetMethod(nameof(ApplyMorphOneAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static Task ApplyAsync<TEntity>(
        DbContext dbContext,
        IReadOnlyList<TEntity> entities,
        MorphIncludeQuery<TEntity>.MorphIncludeRequest request,
        bool asNoTracking,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var entityType = typeof(TEntity);
        var cache = RelationshipKinds.GetValue(dbContext.Model, static _ => new ConcurrentDictionary<(Type EntityType, string PropertyName), RelationshipKind>());
        var relationshipKind = cache.GetOrAdd((entityType, request.PropertyName), key => ResolveRelationshipKind(dbContext.Model, key.EntityType, key.PropertyName));

        return relationshipKind.Kind switch
        {
            RelationshipType.MorphMany => (Task)ApplyMorphManyMethod
                .MakeGenericMethod(entityType, relationshipKind.RelatedType!)
                .Invoke(null, new object?[] { dbContext, entities, request, asNoTracking, cancellationToken })!,
            RelationshipType.MorphToMany => (Task)ApplyMorphToManyMethod
                .MakeGenericMethod(entityType, relationshipKind.RelatedType!)
                .Invoke(null, new object?[] { dbContext, entities, request, asNoTracking, cancellationToken })!,
            RelationshipType.MorphedByMany => (Task)ApplyMorphedByManyMethod
                .MakeGenericMethod(entityType, relationshipKind.RelatedType!)
                .Invoke(null, new object?[] { dbContext, entities, request, asNoTracking, cancellationToken })!,
            RelationshipType.MorphOwner => (Task)ApplyMorphOwnerMethod
                .MakeGenericMethod(entityType)
                .Invoke(null, new object?[] { dbContext, entities, request, asNoTracking, cancellationToken })!,
            RelationshipType.MorphOne => (Task)ApplyMorphOneMethod
                .MakeGenericMethod(entityType, relationshipKind.RelatedType!)
                .Invoke(null, new object?[] { dbContext, entities, request, asNoTracking, cancellationToken })!,
            _ => throw new InvalidOperationException($"Property '{entityType.Name}.{request.PropertyName}' is not a registered polymorphic relationship."),
        };
    }

    private static async Task ApplyMorphManyAsync<TPrincipal, TDependent>(DbContext dbContext, IReadOnlyList<TPrincipal> principals, MorphIncludeQuery<TPrincipal>.MorphIncludeRequest request, bool asNoTracking, CancellationToken cancellationToken)
        where TPrincipal : class
        where TDependent : class
    {
        var queryTransform = request.Plan?.GetQueryTransform<TDependent>();
        if (asNoTracking)
        {
            if (request.Plan is null)
            {
                await dbContext.LoadMorphManyUntrackedAsync<TPrincipal, TDependent>(principals, request.PropertyName, cancellationToken);
                return;
            }

            queryTransform = ComposeNoTracking(queryTransform);
        }

        await dbContext.LoadMorphManyAsync<TPrincipal, TDependent>(principals, request.PropertyName, queryTransform, cancellationToken);
    }

    private static async Task ApplyMorphToManyAsync<TPrincipal, TRelated>(DbContext dbContext, IReadOnlyList<TPrincipal> principals, MorphIncludeQuery<TPrincipal>.MorphIncludeRequest request, bool asNoTracking, CancellationToken cancellationToken)
        where TPrincipal : class
        where TRelated : class
    {
        var queryTransform = request.Plan?.GetQueryTransform<TRelated>();
        if (asNoTracking)
        {
            if (request.Plan is null)
            {
                await dbContext.LoadMorphToManyUntrackedAsync<TPrincipal, TRelated>(principals, request.PropertyName, cancellationToken);
                return;
            }

            queryTransform = ComposeNoTracking(queryTransform);
        }

        await dbContext.LoadMorphToManyAsync<TPrincipal, TRelated>(principals, request.PropertyName, queryTransform, cancellationToken);
    }

    private static async Task ApplyMorphedByManyAsync<TRelated, TPrincipal>(DbContext dbContext, IReadOnlyList<TRelated> relatedEntities, MorphIncludeQuery<TRelated>.MorphIncludeRequest request, bool asNoTracking, CancellationToken cancellationToken)
        where TRelated : class
        where TPrincipal : class
    {
        var queryTransform = request.Plan?.GetQueryTransform<TPrincipal>();
        if (asNoTracking)
        {
            if (request.Plan is null)
            {
                await dbContext.LoadMorphedByManyUntrackedAsync<TRelated, TPrincipal>(relatedEntities, request.PropertyName, cancellationToken);
                return;
            }

            queryTransform = ComposeNoTracking(queryTransform);
        }

        await dbContext.LoadMorphedByManyAsync<TRelated, TPrincipal>(relatedEntities, request.PropertyName, queryTransform, cancellationToken);
    }

    private static async Task ApplyMorphOwnerAsync<TDependent>(DbContext dbContext, IReadOnlyList<TDependent> dependents, MorphIncludeQuery<TDependent>.MorphIncludeRequest request, bool asNoTracking, CancellationToken cancellationToken)
        where TDependent : class
    {
        if (asNoTracking)
        {
            if (request.Plan is null)
            {
                await dbContext.LoadMorphsUntrackedAsync(dependents, request.PropertyName, cancellationToken);
                return;
            }

            await dbContext.LoadMorphsUntrackedAsync(dependents, request.PropertyName, configure: plan => CopyPlanRegistrations(request.Plan, plan), cancellationToken);
            return;
        }

        if (request.Plan is null)
        {
            await dbContext.LoadMorphsAsync(dependents, request.PropertyName, cancellationToken);
            return;
        }

        await dbContext.LoadMorphsAsync(dependents, request.PropertyName, configure: plan => CopyPlanRegistrations(request.Plan, plan), cancellationToken);
    }

    private static async Task ApplyMorphOneAsync<TPrincipal, TDependent>(DbContext dbContext, IReadOnlyList<TPrincipal> principals, MorphIncludeQuery<TPrincipal>.MorphIncludeRequest request, bool asNoTracking, CancellationToken cancellationToken)
        where TPrincipal : class
        where TDependent : class
    {
        var queryTransform = request.Plan?.GetQueryTransform<TDependent>();
        if (asNoTracking)
        {
            queryTransform = ComposeNoTracking(queryTransform);
        }

        await DbContextMorphExtensions.LoadMorphOneBatchAsync<TPrincipal, TDependent>(
            dbContext,
            principals,
            request.PropertyName,
            queryTransform,
            cancellationToken);
    }

    private static Func<IQueryable<TEntity>, IQueryable<TEntity>> ComposeNoTracking<TEntity>(Func<IQueryable<TEntity>, IQueryable<TEntity>>? queryTransform)
        where TEntity : class
    {
        return queryTransform is null
            ? query => query.AsNoTracking()
            : query => queryTransform(query.AsNoTracking());
    }

    private static void CopyPlanRegistrations<TDependent>(MorphIncludePlan? source, MorphBatchLoadPlan<TDependent> target)
        where TDependent : class
    {
        if (source is null)
        {
            return;
        }

        foreach (var registration in source.GetRegistrations())
        {
            CopyRegistration(target, registration.Key, registration.Value);
        }
    }

    private static void CopyRegistration<TDependent>(MorphBatchLoadPlan<TDependent> target, Type relatedType, Delegate queryTransform)
        where TDependent : class
    {
        target.AddRegistration(relatedType, queryTransform);
    }

    private static bool IsCollectionProperty(PropertyInfo propertyInfo, out Type? elementType)
    {
        if (propertyInfo.PropertyType == typeof(string))
        {
            elementType = null;
            return false;
        }

        if (!typeof(IEnumerable).IsAssignableFrom(propertyInfo.PropertyType))
        {
            elementType = null;
            return false;
        }

        elementType = propertyInfo.PropertyType.GenericTypeArguments.FirstOrDefault();
        return elementType is not null;
    }

    private static RelationshipKind ResolveRelationshipKind(IReadOnlyModel model, Type entityType, string propertyName)
    {
        var property = entityType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
            ?? throw new InvalidOperationException($"Property '{propertyName}' was not found on '{entityType.Name}'.");

        if (IsCollectionProperty(property, out var elementType))
        {
            var relatedType = elementType!;
            var references = PolymorphicModelMetadata.GetReferences(model);

            if (references.Any(reference => reference.DependentType == relatedType
                && reference.Associations.Any(association => association.Multiplicity == MorphMultiplicity.Many
                    && association.PrincipalType.IsAssignableFrom(entityType)
                    && string.Equals(association.InverseRelationshipName, propertyName, StringComparison.Ordinal))))
            {
                return new RelationshipKind(RelationshipType.MorphMany, relatedType);
            }

            if (PolymorphicModelMetadata.GetManyToManyRelations(model).Any(relation => relation.PrincipalType.IsAssignableFrom(entityType)
                && relation.RelatedType == relatedType
                && string.Equals(relation.RelationshipName, propertyName, StringComparison.Ordinal)))
            {
                return new RelationshipKind(RelationshipType.MorphToMany, relatedType);
            }

            if (PolymorphicModelMetadata.GetManyToManyRelations(model).Any(relation => relation.RelatedType.IsAssignableFrom(entityType)
                && relation.PrincipalType == relatedType
                && string.Equals(relation.InverseRelationshipName, propertyName, StringComparison.Ordinal)))
            {
                return new RelationshipKind(RelationshipType.MorphedByMany, relatedType);
            }

            return new RelationshipKind(RelationshipType.Unknown, relatedType);
        }

        var propertyType = property.PropertyType;
        var modelReferences = PolymorphicModelMetadata.GetReferences(model);

        if (modelReferences.Any(reference => reference.DependentType == entityType
            && string.Equals(reference.RelationshipName, propertyName, StringComparison.Ordinal)))
        {
            return new RelationshipKind(RelationshipType.MorphOwner, propertyType);
        }

        if (modelReferences.Any(reference => reference.DependentType == propertyType
            && reference.Associations.Any(association => association.Multiplicity == MorphMultiplicity.One
                && association.PrincipalType.IsAssignableFrom(entityType)
                && string.Equals(association.InverseRelationshipName, propertyName, StringComparison.Ordinal))))
        {
            return new RelationshipKind(RelationshipType.MorphOne, propertyType);
        }

        return new RelationshipKind(RelationshipType.Unknown, propertyType);
    }

    private readonly record struct RelationshipKind(RelationshipType Kind, Type? RelatedType);

    private enum RelationshipType
    {
        Unknown = 0,
        MorphMany = 1,
        MorphToMany = 2,
        MorphedByMany = 3,
        MorphOwner = 4,
        MorphOne = 5,
    }
}

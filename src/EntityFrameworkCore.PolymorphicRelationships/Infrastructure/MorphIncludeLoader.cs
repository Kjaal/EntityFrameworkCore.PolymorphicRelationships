using System.Collections;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

internal static class MorphIncludeLoader
{
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
        var property = entityType.GetProperty(request.PropertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
            ?? throw new InvalidOperationException($"Property '{request.PropertyName}' was not found on '{entityType.Name}'.");

        if (IsCollectionProperty(property, out var elementType))
        {
            return ApplyCollectionIncludeAsync(dbContext, entities, request, elementType!, asNoTracking, cancellationToken);
        }

        return ApplyReferenceIncludeAsync(dbContext, entities, request, property.PropertyType, asNoTracking, cancellationToken);
    }

    private static Task ApplyCollectionIncludeAsync<TEntity>(DbContext dbContext, IReadOnlyList<TEntity> entities, MorphIncludeQuery<TEntity>.MorphIncludeRequest request, Type elementType, bool asNoTracking, CancellationToken cancellationToken)
        where TEntity : class
    {
        var entityType = typeof(TEntity);
        var references = PolymorphicModelMetadata.GetReferences(dbContext.Model);

        if (references.Any(reference => reference.DependentType == elementType
            && reference.Associations.Any(association => association.Multiplicity == MorphMultiplicity.Many
                && association.PrincipalType.IsAssignableFrom(entityType)
                && string.Equals(association.InverseRelationshipName, request.PropertyName, StringComparison.Ordinal))))
        {
            return (Task)ApplyMorphManyMethod
                .MakeGenericMethod(entityType, elementType)
                .Invoke(null, new object?[] { dbContext, entities, request, asNoTracking, cancellationToken })!;
        }

        if (PolymorphicModelMetadata.GetManyToManyRelations(dbContext.Model).Any(relation => relation.PrincipalType.IsAssignableFrom(entityType)
            && relation.RelatedType == elementType
            && string.Equals(relation.RelationshipName, request.PropertyName, StringComparison.Ordinal)))
        {
            return (Task)ApplyMorphToManyMethod
                .MakeGenericMethod(entityType, elementType)
                .Invoke(null, new object?[] { dbContext, entities, request, asNoTracking, cancellationToken })!;
        }

        if (PolymorphicModelMetadata.GetManyToManyRelations(dbContext.Model).Any(relation => relation.RelatedType.IsAssignableFrom(entityType)
            && relation.PrincipalType == elementType
            && string.Equals(relation.InverseRelationshipName, request.PropertyName, StringComparison.Ordinal)))
        {
            return (Task)ApplyMorphedByManyMethod
                .MakeGenericMethod(entityType, elementType)
                .Invoke(null, new object?[] { dbContext, entities, request, asNoTracking, cancellationToken })!;
        }

        throw new InvalidOperationException($"Property '{entityType.Name}.{request.PropertyName}' is not a registered polymorphic collection relationship.");
    }

    private static Task ApplyReferenceIncludeAsync<TEntity>(DbContext dbContext, IReadOnlyList<TEntity> entities, MorphIncludeQuery<TEntity>.MorphIncludeRequest request, Type propertyType, bool asNoTracking, CancellationToken cancellationToken)
        where TEntity : class
    {
        var entityType = typeof(TEntity);
        var references = PolymorphicModelMetadata.GetReferences(dbContext.Model);

        if (references.Any(reference => reference.DependentType == entityType
            && string.Equals(reference.RelationshipName, request.PropertyName, StringComparison.Ordinal)))
        {
            return (Task)ApplyMorphOwnerMethod
                .MakeGenericMethod(entityType)
                .Invoke(null, new object?[] { dbContext, entities, request, asNoTracking, cancellationToken })!;
        }

        if (references.Any(reference => reference.DependentType == propertyType
            && reference.Associations.Any(association => association.Multiplicity == MorphMultiplicity.One
                && association.PrincipalType.IsAssignableFrom(entityType)
                && string.Equals(association.InverseRelationshipName, request.PropertyName, StringComparison.Ordinal))))
        {
            return (Task)ApplyMorphOneMethod
                .MakeGenericMethod(entityType, propertyType)
                .Invoke(null, new object?[] { dbContext, entities, request, asNoTracking, cancellationToken })!;
        }

        throw new InvalidOperationException($"Property '{entityType.Name}.{request.PropertyName}' is not a registered polymorphic reference relationship.");
    }

    private static async Task ApplyMorphManyAsync<TPrincipal, TDependent>(DbContext dbContext, IReadOnlyList<TPrincipal> principals, MorphIncludeQuery<TPrincipal>.MorphIncludeRequest request, bool asNoTracking, CancellationToken cancellationToken)
        where TPrincipal : class
        where TDependent : class
    {
        var queryTransform = request.Plan?.GetQueryTransform<TDependent>();
        if (asNoTracking)
        {
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
            queryTransform = ComposeNoTracking(queryTransform);
        }

        await dbContext.LoadMorphedByManyAsync<TRelated, TPrincipal>(relatedEntities, request.PropertyName, queryTransform, cancellationToken);
    }

    private static async Task ApplyMorphOwnerAsync<TDependent>(DbContext dbContext, IReadOnlyList<TDependent> dependents, MorphIncludeQuery<TDependent>.MorphIncludeRequest request, bool asNoTracking, CancellationToken cancellationToken)
        where TDependent : class
    {
        if (asNoTracking)
        {
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
        foreach (var principal in principals)
        {
            if (asNoTracking)
            {
                await dbContext.LoadMorphOneAsync<TPrincipal, TDependent>(principal, request.PropertyName, cancellationToken);
                continue;
            }

            await dbContext.LoadMorphOneAsync<TPrincipal, TDependent>(principal, request.PropertyName, cancellationToken);
        }
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

        var field = typeof(MorphIncludePlan).GetField("_registrations", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var registrations = (Dictionary<Type, Delegate>)field.GetValue(source)!;
        foreach (var registration in registrations)
        {
            CopyRegistration(target, registration.Key, registration.Value);
        }
    }

    private static void CopyRegistration<TDependent>(MorphBatchLoadPlan<TDependent> target, Type relatedType, Delegate queryTransform)
        where TDependent : class
    {
        target.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method => method.Name == nameof(MorphBatchLoadPlan<TDependent>.For) && method.IsGenericMethodDefinition)
            .MakeGenericMethod(relatedType)
            .Invoke(target, new object?[] { queryTransform });
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
}

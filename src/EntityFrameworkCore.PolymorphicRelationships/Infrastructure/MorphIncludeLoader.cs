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
        var relationshipKind = PolymorphicRelationshipResolver.Resolve(dbContext.Model, entityType, request.PropertyName);

        return relationshipKind.Kind switch
        {
            PolymorphicRelationshipResolver.RelationshipType.MorphMany => (Task)ApplyMorphManyMethod
                .MakeGenericMethod(entityType, relationshipKind.RelatedType!)
                .Invoke(null, new object?[] { dbContext, entities, request, asNoTracking, cancellationToken })!,
            PolymorphicRelationshipResolver.RelationshipType.MorphToMany => (Task)ApplyMorphToManyMethod
                .MakeGenericMethod(entityType, relationshipKind.RelatedType!)
                .Invoke(null, new object?[] { dbContext, entities, request, asNoTracking, cancellationToken })!,
            PolymorphicRelationshipResolver.RelationshipType.MorphedByMany => (Task)ApplyMorphedByManyMethod
                .MakeGenericMethod(entityType, relationshipKind.RelatedType!)
                .Invoke(null, new object?[] { dbContext, entities, request, asNoTracking, cancellationToken })!,
            PolymorphicRelationshipResolver.RelationshipType.MorphOwner => (Task)ApplyMorphOwnerMethod
                .MakeGenericMethod(entityType)
                .Invoke(null, new object?[] { dbContext, entities, request, asNoTracking, cancellationToken })!,
            PolymorphicRelationshipResolver.RelationshipType.MorphOne => (Task)ApplyMorphOneMethod
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

}

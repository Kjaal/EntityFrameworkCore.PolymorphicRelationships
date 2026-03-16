using System.Collections;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

internal static class MorphIncludeLoader
{
    private static readonly MethodInfo LoadMorphManyMethod = typeof(DbContextMorphExtensions)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method.Name == nameof(DbContextMorphExtensions.LoadMorphManyAsync)
            && method.IsGenericMethodDefinition
            && method.GetParameters().Length == 4
            && method.GetParameters()[1].ParameterType.IsGenericType);

    private static readonly MethodInfo LoadMorphOneMethod = typeof(MorphIncludeLoader)
        .GetMethod(nameof(LoadMorphOneCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo LoadMorphToManyMethod = typeof(DbContextMorphExtensions)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method.Name == nameof(DbContextMorphExtensions.LoadMorphToManyAsync)
            && method.IsGenericMethodDefinition
            && method.GetParameters().Length == 4
            && method.GetParameters()[1].ParameterType.IsGenericType);

    private static readonly MethodInfo LoadMorphedByManyMethod = typeof(DbContextMorphExtensions)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method.Name == nameof(DbContextMorphExtensions.LoadMorphedByManyAsync)
            && method.IsGenericMethodDefinition
            && method.GetParameters().Length == 4
            && method.GetParameters()[1].ParameterType.IsGenericType);

    private static readonly MethodInfo LoadMorphsMethod = typeof(DbContextMorphExtensions)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method.Name == nameof(DbContextMorphExtensions.LoadMorphsAsync)
            && method.IsGenericMethodDefinition
            && method.GetParameters().Length == 4);

    public static Task ApplyAsync<TEntity>(DbContext dbContext, IReadOnlyList<TEntity> entities, string propertyName, CancellationToken cancellationToken)
        where TEntity : class
    {
        var entityType = typeof(TEntity);
        var property = entityType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
            ?? throw new InvalidOperationException($"Property '{propertyName}' was not found on '{entityType.Name}'.");

        if (IsCollectionProperty(property, out var elementType))
        {
            return ApplyCollectionIncludeAsync(dbContext, entities, propertyName, elementType!, cancellationToken);
        }

        return ApplyReferenceIncludeAsync(dbContext, entities, propertyName, property.PropertyType, cancellationToken);
    }

    private static Task ApplyCollectionIncludeAsync<TEntity>(DbContext dbContext, IReadOnlyList<TEntity> entities, string propertyName, Type elementType, CancellationToken cancellationToken)
        where TEntity : class
    {
        var entityType = typeof(TEntity);
        var references = PolymorphicModelMetadata.GetReferences(dbContext.Model);

        if (references.Any(reference => reference.DependentType == elementType
            && reference.Associations.Any(association => association.Multiplicity == MorphMultiplicity.Many
                && association.PrincipalType.IsAssignableFrom(entityType)
                && string.Equals(association.InverseRelationshipName, propertyName, StringComparison.Ordinal))))
        {
            return (Task)LoadMorphManyMethod
                .MakeGenericMethod(entityType, elementType)
                .Invoke(null, new object?[] { dbContext, entities, propertyName, cancellationToken })!;
        }

        if (PolymorphicModelMetadata.GetManyToManyRelations(dbContext.Model).Any(relation => relation.PrincipalType.IsAssignableFrom(entityType)
            && relation.RelatedType == elementType
            && string.Equals(relation.RelationshipName, propertyName, StringComparison.Ordinal)))
        {
            return (Task)LoadMorphToManyMethod
                .MakeGenericMethod(entityType, elementType)
                .Invoke(null, new object?[] { dbContext, entities, propertyName, cancellationToken })!;
        }

        if (PolymorphicModelMetadata.GetManyToManyRelations(dbContext.Model).Any(relation => relation.RelatedType.IsAssignableFrom(entityType)
            && relation.PrincipalType == elementType
            && string.Equals(relation.InverseRelationshipName, propertyName, StringComparison.Ordinal)))
        {
            return (Task)LoadMorphedByManyMethod
                .MakeGenericMethod(entityType, elementType)
                .Invoke(null, new object?[] { dbContext, entities, propertyName, cancellationToken })!;
        }

        throw new InvalidOperationException($"Property '{entityType.Name}.{propertyName}' is not a registered polymorphic collection relationship.");
    }

    private static Task ApplyReferenceIncludeAsync<TEntity>(DbContext dbContext, IReadOnlyList<TEntity> entities, string propertyName, Type propertyType, CancellationToken cancellationToken)
        where TEntity : class
    {
        var entityType = typeof(TEntity);
        var references = PolymorphicModelMetadata.GetReferences(dbContext.Model);

        if (references.Any(reference => reference.DependentType == entityType
            && string.Equals(reference.RelationshipName, propertyName, StringComparison.Ordinal)))
        {
            return (Task)LoadMorphsMethod
                .MakeGenericMethod(entityType)
                .Invoke(null, new object?[] { dbContext, entities, propertyName, cancellationToken })!;
        }

        if (references.Any(reference => reference.DependentType == propertyType
            && reference.Associations.Any(association => association.Multiplicity == MorphMultiplicity.One
                && association.PrincipalType.IsAssignableFrom(entityType)
                && string.Equals(association.InverseRelationshipName, propertyName, StringComparison.Ordinal))))
        {
            return (Task)LoadMorphOneMethod
                .MakeGenericMethod(entityType, propertyType)
                .Invoke(null, new object?[] { dbContext, entities, propertyName, cancellationToken })!;
        }

        throw new InvalidOperationException($"Property '{entityType.Name}.{propertyName}' is not a registered polymorphic reference relationship.");
    }

    private static async Task LoadMorphOneCoreAsync<TPrincipal, TDependent>(DbContext dbContext, IReadOnlyList<TPrincipal> principals, string propertyName, CancellationToken cancellationToken)
        where TPrincipal : class
        where TDependent : class
    {
        foreach (var principal in principals)
        {
            await dbContext.LoadMorphOneAsync<TPrincipal, TDependent>(principal, propertyName, cancellationToken);
        }
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

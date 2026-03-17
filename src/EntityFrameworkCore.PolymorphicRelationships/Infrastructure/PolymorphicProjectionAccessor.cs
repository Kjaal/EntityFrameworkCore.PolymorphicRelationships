using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

internal static class PolymorphicProjectionAccessor
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<string, object?>> LoadedValues = new();

    public static TProperty? GetNavigation<TSource, TProperty>(TSource source, Guid contextId, string propertyName)
        where TSource : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        var cache = LoadedValues.GetValue(source, static _ => new Dictionary<string, object?>(StringComparer.Ordinal));
        if (cache.TryGetValue(propertyName, out var cached))
        {
            return (TProperty?)cached;
        }

        using var dbContext = PolymorphicDbContextRegistry.CreateCompanionContext(contextId);
        var relationship = PolymorphicRelationshipResolver.Resolve(dbContext.Model, source.GetType(), propertyName);
        var value = relationship.Kind switch
        {
            PolymorphicRelationshipResolver.RelationshipType.MorphOwner => LoadMorphOwner(source, dbContext, propertyName),
            PolymorphicRelationshipResolver.RelationshipType.MorphMany => LoadMorphMany(source, dbContext, propertyName, relationship.RelatedType!),
            PolymorphicRelationshipResolver.RelationshipType.MorphOne => LoadMorphOne(source, dbContext, propertyName, relationship.RelatedType!),
            PolymorphicRelationshipResolver.RelationshipType.MorphToMany => LoadMorphToMany(source, dbContext, propertyName, relationship.RelatedType!),
            PolymorphicRelationshipResolver.RelationshipType.MorphedByMany => LoadMorphedByMany(source, dbContext, propertyName, relationship.RelatedType!),
            _ => throw new InvalidOperationException($"Property '{source.GetType().Name}.{propertyName}' is not a registered polymorphic relationship."),
        };

        cache[propertyName] = value;
        return (TProperty?)value;
    }

    private static object? LoadMorphOwner(object dependent, DbContext dbContext, string relationshipName)
    {
        var reference = PolymorphicModelMetadata.GetRequiredReference(dbContext.Model, dependent.GetType(), relationshipName);
        var typeAlias = PolymorphicMemberAccessorCache.GetValue(dbContext, dependent, reference.TypePropertyName) as string;
        var ownerId = PolymorphicMemberAccessorCache.GetValue(dbContext, dependent, reference.IdPropertyName);

        if (string.IsNullOrWhiteSpace(typeAlias) || ownerId is null)
        {
            return null;
        }

        var association = reference.Associations.FirstOrDefault(candidate => string.Equals(candidate.Alias, typeAlias, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Type alias '{typeAlias}' is not registered for relationship '{relationshipName}'.");

        var keyType = dbContext.Model.FindEntityType(association.PrincipalType)?.FindProperty(association.PrincipalKeyPropertyName)?.ClrType
            ?? throw new InvalidOperationException($"Property '{association.PrincipalKeyPropertyName}' was not found on '{association.PrincipalType.Name}'.");

        var owner = PolymorphicQueryExecutor.SingleOrDefaultByProperty(
            dbContext,
            association.PrincipalType,
            association.PrincipalKeyPropertyName,
            keyType,
            ownerId);

        PolymorphicMemberAccessorCache.SetValue(dependent, relationshipName, owner);
        return owner;
    }

    private static object LoadMorphMany(object principal, DbContext dbContext, string relationshipName, Type dependentType)
    {
        var (reference, association) = PolymorphicModelMetadata.GetRequiredInverse(
            dbContext.Model,
            principal.GetType(),
            dependentType,
            relationshipName,
            MorphMultiplicity.Many);

        var ownerId = PolymorphicMemberAccessorCache.GetValue(dbContext, principal, association.PrincipalKeyPropertyName);
        if (ownerId is null)
        {
            return Array.CreateInstance(dependentType, 0);
        }

        var dependents = PolymorphicQueryExecutor.ListByTwoProperties(
            dbContext,
            dependentType,
            reference.TypePropertyName,
            typeof(string),
            association.Alias,
            reference.IdPropertyName,
            reference.IdPropertyType,
            ownerId);

        var typedList = CreateTypedList(dependentType, dependents);
        PolymorphicMemberAccessorCache.SetValue(principal, relationshipName, typedList);
        return typedList;
    }

    private static object? LoadMorphOne(object principal, DbContext dbContext, string relationshipName, Type dependentType)
    {
        var (reference, association) = PolymorphicModelMetadata.GetRequiredInverse(
            dbContext.Model,
            principal.GetType(),
            dependentType,
            relationshipName,
            MorphMultiplicity.One);

        var ownerId = PolymorphicMemberAccessorCache.GetValue(dbContext, principal, association.PrincipalKeyPropertyName);
        if (ownerId is null)
        {
            return null;
        }

        var dependent = PolymorphicQueryExecutor.SingleOrDefaultByTwoProperties(
            dbContext,
            dependentType,
            reference.TypePropertyName,
            typeof(string),
            association.Alias,
            reference.IdPropertyName,
            reference.IdPropertyType,
            ownerId);

        PolymorphicMemberAccessorCache.SetValue(principal, relationshipName, dependent);
        return dependent;
    }

    private static object LoadMorphToMany(object principal, DbContext dbContext, string relationshipName, Type relatedType)
    {
        var relation = PolymorphicModelMetadata.GetRequiredManyToMany(dbContext.Model, principal.GetType(), relatedType, relationshipName);
        var ownerId = PolymorphicMemberAccessorCache.GetValue(dbContext, principal, relation.PrincipalKeyPropertyName);
        if (ownerId is null)
        {
            return Array.CreateInstance(relatedType, 0);
        }

        var pivots = PolymorphicQueryExecutor.ListByTwoProperties(
            dbContext,
            relation.PivotType,
            relation.PivotTypePropertyName,
            typeof(string),
            relation.PrincipalAlias,
            relation.PivotIdPropertyName,
            relation.PivotIdPropertyType,
            ownerId);

        var relatedIds = pivots
            .Select(pivot => PolymorphicMemberAccessorCache.GetValue(dbContext, pivot, relation.PivotRelatedIdPropertyName))
            .Where(value => value is not null)
            .Cast<object>()
            .ToArray();

        var related = PolymorphicQueryExecutor.ListByPropertyValues(
            dbContext,
            relatedType,
            relation.RelatedKeyPropertyName,
            relation.RelatedKeyType,
            relatedIds);

        var typedList = CreateTypedList(relatedType, related);
        PolymorphicMemberAccessorCache.SetValue(principal, relationshipName, typedList);
        return typedList;
    }

    private static object LoadMorphedByMany(object related, DbContext dbContext, string relationshipName, Type principalType)
    {
        var relation = PolymorphicModelMetadata.GetRequiredMorphedByMany(dbContext.Model, related.GetType(), principalType, relationshipName);
        var relatedId = PolymorphicMemberAccessorCache.GetValue(dbContext, related, relation.RelatedKeyPropertyName);
        if (relatedId is null)
        {
            return Array.CreateInstance(principalType, 0);
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

        var principalIds = pivots
            .Select(pivot => PolymorphicMemberAccessorCache.GetValue(dbContext, pivot, relation.PivotIdPropertyName))
            .Where(value => value is not null)
            .Cast<object>()
            .ToArray();

        var principals = PolymorphicQueryExecutor.ListByPropertyValues(
            dbContext,
            principalType,
            relation.PrincipalKeyPropertyName,
            relation.PrincipalKeyType,
            principalIds);

        var typedList = CreateTypedList(principalType, principals);
        PolymorphicMemberAccessorCache.SetValue(related, relationshipName, typedList);
        return typedList;
    }

    private static object CreateTypedList(Type elementType, IReadOnlyList<object> values)
    {
        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
        foreach (var value in values)
        {
            list.Add(value);
        }

        return list;
    }
}

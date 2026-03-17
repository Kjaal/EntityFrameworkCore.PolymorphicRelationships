using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

internal static class PolymorphicRelationshipResolver
{
    private static readonly ConditionalWeakTable<IReadOnlyModel, ConcurrentDictionary<(Type EntityType, string PropertyName), RelationshipKind>> Cache = new();

    public static RelationshipKind Resolve(IReadOnlyModel model, Type entityType, string propertyName)
    {
        var relationships = Cache.GetValue(model, static _ => new ConcurrentDictionary<(Type EntityType, string PropertyName), RelationshipKind>());
        return relationships.GetOrAdd((entityType, propertyName), key => ResolveCore(model, key.EntityType, key.PropertyName));
    }

    private static RelationshipKind ResolveCore(IReadOnlyModel model, Type entityType, string propertyName)
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

    internal readonly record struct RelationshipKind(RelationshipType Kind, Type? RelatedType);

    internal enum RelationshipType
    {
        Unknown = 0,
        MorphMany = 1,
        MorphToMany = 2,
        MorphedByMany = 3,
        MorphOwner = 4,
        MorphOne = 5,
    }
}

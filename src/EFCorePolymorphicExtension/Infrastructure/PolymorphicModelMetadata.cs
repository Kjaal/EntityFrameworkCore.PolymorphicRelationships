using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCorePolymorphicExtension.Infrastructure;

internal static class PolymorphicModelMetadata
{
    private const string TypeMappingsAnnotation = "EFCorePolymorphicExtension:TypeMappings";
    private const string ReferencesAnnotation = "EFCorePolymorphicExtension:References";
    private const string ManyToManyAnnotation = "EFCorePolymorphicExtension:ManyToMany";

    public static List<MorphTypeMapping> GetOrCreateTypeMappings(IMutableModel model)
    {
        if (model.FindAnnotation(TypeMappingsAnnotation)?.Value is List<MorphTypeMapping> mappings)
        {
            return mappings;
        }

        mappings = new List<MorphTypeMapping>();
        model.SetAnnotation(TypeMappingsAnnotation, mappings);
        return mappings;
    }

    public static List<MorphReference> GetOrCreateReferences(IMutableModel model)
    {
        if (model.FindAnnotation(ReferencesAnnotation)?.Value is List<MorphReference> references)
        {
            return references;
        }

        references = new List<MorphReference>();
        model.SetAnnotation(ReferencesAnnotation, references);
        return references;
    }

    public static IReadOnlyList<MorphReference> GetReferences(IReadOnlyModel model)
    {
        return model.FindAnnotation(ReferencesAnnotation)?.Value as IReadOnlyList<MorphReference>
            ?? Array.Empty<MorphReference>();
    }

    public static List<MorphManyToManyRelation> GetOrCreateManyToManyRelations(IMutableModel model)
    {
        if (model.FindAnnotation(ManyToManyAnnotation)?.Value is List<MorphManyToManyRelation> relations)
        {
            return relations;
        }

        relations = new List<MorphManyToManyRelation>();
        model.SetAnnotation(ManyToManyAnnotation, relations);
        return relations;
    }

    public static IReadOnlyList<MorphManyToManyRelation> GetManyToManyRelations(IReadOnlyModel model)
    {
        return model.FindAnnotation(ManyToManyAnnotation)?.Value as IReadOnlyList<MorphManyToManyRelation>
            ?? Array.Empty<MorphManyToManyRelation>();
    }

    public static string GetAlias(IReadOnlyModel model, Type clrType)
    {
        return FindTypeMapping(model, clrType)?.Alias
            ?? clrType.FullName
            ?? clrType.Name;
    }

    public static MorphTypeMapping? FindTypeMapping(IReadOnlyModel model, Type clrType)
    {
        var mappings = model.FindAnnotation(TypeMappingsAnnotation)?.Value as IEnumerable<MorphTypeMapping>;
        if (mappings is null)
        {
            return null;
        }

        return mappings.FirstOrDefault(mapping => mapping.ClrType == clrType)
            ?? mappings.FirstOrDefault(mapping => mapping.ClrType.IsAssignableFrom(clrType));
    }

    public static MorphReference GetRequiredReference(IReadOnlyModel model, Type dependentType, string relationshipName)
    {
        return GetReferences(model)
            .FirstOrDefault(reference => reference.DependentType == dependentType
                && string.Equals(reference.RelationshipName, relationshipName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No morphTo relationship named '{relationshipName}' is registered for '{dependentType.Name}'.");
    }

    public static (MorphReference Reference, MorphAssociation Association) GetRequiredInverse(
        IReadOnlyModel model,
        Type principalType,
        Type dependentType,
        string inverseRelationshipName,
        MorphMultiplicity multiplicity)
    {
        foreach (var reference in GetReferences(model).Where(reference => reference.DependentType == dependentType))
        {
            var association = reference.Associations.FirstOrDefault(candidate =>
                candidate.Multiplicity == multiplicity
                && string.Equals(candidate.InverseRelationshipName, inverseRelationshipName, StringComparison.Ordinal)
                && candidate.PrincipalType.IsAssignableFrom(principalType));

            if (association is not null)
            {
                return (reference, association);
            }
        }

        throw new InvalidOperationException($"No {(multiplicity == MorphMultiplicity.One ? "morphOne" : "morphMany")} relationship named '{inverseRelationshipName}' is registered between '{principalType.Name}' and '{dependentType.Name}'.");
    }

    public static MorphManyToManyRelation GetRequiredManyToMany(IReadOnlyModel model, Type principalType, Type relatedType, string relationshipName)
    {
        return GetManyToManyRelations(model)
            .FirstOrDefault(relation => relation.PrincipalType.IsAssignableFrom(principalType)
                && relation.RelatedType.IsAssignableFrom(relatedType)
                && string.Equals(relation.RelationshipName, relationshipName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No morphToMany relationship named '{relationshipName}' is registered between '{principalType.Name}' and '{relatedType.Name}'.");
    }

    public static MorphManyToManyRelation GetRequiredMorphedByMany(IReadOnlyModel model, Type relatedType, Type principalType, string inverseRelationshipName)
    {
        return GetManyToManyRelations(model)
            .FirstOrDefault(relation => relation.RelatedType.IsAssignableFrom(relatedType)
                && relation.PrincipalType.IsAssignableFrom(principalType)
                && string.Equals(relation.InverseRelationshipName, inverseRelationshipName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No morphedByMany relationship named '{inverseRelationshipName}' is registered between '{relatedType.Name}' and '{principalType.Name}'.");
    }

    internal sealed class MorphTypeMapping
    {
        public MorphTypeMapping(Type clrType, string alias)
        {
            ClrType = clrType;
            Alias = alias;
        }

        public Type ClrType { get; }

        public string Alias { get; set; }
    }

    internal sealed class MorphReference
    {
        public MorphReference(Type dependentType, string relationshipName, string typePropertyName, string idPropertyName, Type idPropertyType)
        {
            DependentType = dependentType;
            RelationshipName = relationshipName;
            TypePropertyName = typePropertyName;
            IdPropertyName = idPropertyName;
            IdPropertyType = idPropertyType;
        }

        public Type DependentType { get; }

        public string RelationshipName { get; }

        public string TypePropertyName { get; }

        public string IdPropertyName { get; }

        public Type IdPropertyType { get; }

        public List<MorphAssociation> Associations { get; } = new();
    }

    internal sealed class MorphAssociation
    {
        public MorphAssociation(
            Type principalType,
            string inverseRelationshipName,
            string principalKeyPropertyName,
            string alias,
            MorphMultiplicity multiplicity,
            PolymorphicDeleteBehavior deleteBehavior)
        {
            PrincipalType = principalType;
            InverseRelationshipName = inverseRelationshipName;
            PrincipalKeyPropertyName = principalKeyPropertyName;
            Alias = alias;
            Multiplicity = multiplicity;
            DeleteBehavior = deleteBehavior;
        }

        public Type PrincipalType { get; }

        public string InverseRelationshipName { get; }

        public string PrincipalKeyPropertyName { get; }

        public string Alias { get; }

        public MorphMultiplicity Multiplicity { get; }

        public PolymorphicDeleteBehavior DeleteBehavior { get; }
    }

    internal sealed class MorphManyToManyRelation
    {
        public MorphManyToManyRelation(
            Type principalType,
            Type relatedType,
            Type pivotType,
            string relationshipName,
            string inverseRelationshipName,
            string pivotTypePropertyName,
            string pivotIdPropertyName,
            Type pivotIdPropertyType,
            string pivotRelatedIdPropertyName,
            Type pivotRelatedIdPropertyType,
            string principalKeyPropertyName,
            Type principalKeyType,
            string relatedKeyPropertyName,
            Type relatedKeyType,
            string principalAlias,
            PolymorphicDeleteBehavior deleteBehavior)
        {
            PrincipalType = principalType;
            RelatedType = relatedType;
            PivotType = pivotType;
            RelationshipName = relationshipName;
            InverseRelationshipName = inverseRelationshipName;
            PivotTypePropertyName = pivotTypePropertyName;
            PivotIdPropertyName = pivotIdPropertyName;
            PivotIdPropertyType = pivotIdPropertyType;
            PivotRelatedIdPropertyName = pivotRelatedIdPropertyName;
            PivotRelatedIdPropertyType = pivotRelatedIdPropertyType;
            PrincipalKeyPropertyName = principalKeyPropertyName;
            PrincipalKeyType = principalKeyType;
            RelatedKeyPropertyName = relatedKeyPropertyName;
            RelatedKeyType = relatedKeyType;
            PrincipalAlias = principalAlias;
            DeleteBehavior = deleteBehavior;
        }

        public Type PrincipalType { get; }

        public Type RelatedType { get; }

        public Type PivotType { get; }

        public string RelationshipName { get; }

        public string InverseRelationshipName { get; }

        public string PivotTypePropertyName { get; }

        public string PivotIdPropertyName { get; }

        public Type PivotIdPropertyType { get; }

        public string PivotRelatedIdPropertyName { get; }

        public Type PivotRelatedIdPropertyType { get; }

        public string PrincipalKeyPropertyName { get; }

        public Type PrincipalKeyType { get; }

        public string RelatedKeyPropertyName { get; }

        public Type RelatedKeyType { get; }

        public string PrincipalAlias { get; }

        public PolymorphicDeleteBehavior DeleteBehavior { get; }
    }
}


using Microsoft.EntityFrameworkCore.Metadata;
using System.Text.Json;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

internal static class PolymorphicModelMetadata
{
    private const string TypeMappingsAnnotation = "EntityFrameworkCore.PolymorphicRelationships:TypeMappings";
    private const string ReferencesAnnotation = "EntityFrameworkCore.PolymorphicRelationships:References";
    private const string ManyToManyAnnotation = "EntityFrameworkCore.PolymorphicRelationships:ManyToMany";
    private static readonly JsonSerializerOptions JsonOptions = new();

    public static List<MorphTypeMapping> GetOrCreateTypeMappings(IMutableModel model)
    {
        return ParseTypeMappings(model.FindAnnotation(TypeMappingsAnnotation)?.Value as string);
    }

    public static List<MorphReference> GetOrCreateReferences(IMutableModel model)
    {
        return ParseReferences(model.FindAnnotation(ReferencesAnnotation)?.Value as string);
    }

    public static IReadOnlyList<MorphReference> GetReferences(IReadOnlyModel model)
    {
        return ParseReferences(model.FindAnnotation(ReferencesAnnotation)?.Value as string);
    }

    public static List<MorphManyToManyRelation> GetOrCreateManyToManyRelations(IMutableModel model)
    {
        return ParseManyToManyRelations(model.FindAnnotation(ManyToManyAnnotation)?.Value as string);
    }

    public static IReadOnlyList<MorphManyToManyRelation> GetManyToManyRelations(IReadOnlyModel model)
    {
        return ParseManyToManyRelations(model.FindAnnotation(ManyToManyAnnotation)?.Value as string);
    }

    public static string GetAlias(IReadOnlyModel model, Type clrType)
    {
        return FindTypeMapping(model, clrType)?.Alias
            ?? clrType.FullName
            ?? clrType.Name;
    }

    public static MorphTypeMapping? FindTypeMapping(IReadOnlyModel model, Type clrType)
    {
        var mappings = ParseTypeMappings(model.FindAnnotation(TypeMappingsAnnotation)?.Value as string);
        return mappings.FirstOrDefault(mapping => mapping.ClrType == clrType)
            ?? mappings.FirstOrDefault(mapping => mapping.ClrType.IsAssignableFrom(clrType));
    }

    public static void SyncTypeMappings(IMutableModel model, IEnumerable<MorphTypeMapping> mappings)
    {
        model.SetAnnotation(TypeMappingsAnnotation, JsonSerializer.Serialize(
            mappings.Select(mapping => new StoredMorphTypeMapping(mapping.ClrType.AssemblyQualifiedName!, mapping.Alias)).ToList(),
            JsonOptions));
    }

    public static void SyncReferences(IMutableModel model, IEnumerable<MorphReference> references)
    {
        model.SetAnnotation(ReferencesAnnotation, JsonSerializer.Serialize(
            references.Select(reference => new StoredMorphReference(
                reference.DependentType.AssemblyQualifiedName!,
                reference.RelationshipName,
                reference.TypePropertyName,
                reference.IdPropertyName,
                reference.IdPropertyType.AssemblyQualifiedName!,
                reference.Associations.Select(association => new StoredMorphAssociation(
                    association.PrincipalType.AssemblyQualifiedName!,
                    association.InverseRelationshipName,
                    association.PrincipalKeyPropertyName,
                    association.Alias,
                    association.Multiplicity,
                    association.DeleteBehavior)).ToList())).ToList(),
            JsonOptions));
    }

    public static void SyncManyToManyRelations(IMutableModel model, IEnumerable<MorphManyToManyRelation> relations)
    {
        model.SetAnnotation(ManyToManyAnnotation, JsonSerializer.Serialize(
            relations.Select(relation => new StoredMorphManyToManyRelation(
                relation.PrincipalType.AssemblyQualifiedName!,
                relation.RelatedType.AssemblyQualifiedName!,
                relation.PivotType.AssemblyQualifiedName!,
                relation.RelationshipName,
                relation.InverseRelationshipName,
                relation.PivotTypePropertyName,
                relation.PivotIdPropertyName,
                relation.PivotIdPropertyType.AssemblyQualifiedName!,
                relation.PivotRelatedIdPropertyName,
                relation.PivotRelatedIdPropertyType.AssemblyQualifiedName!,
                relation.PrincipalKeyPropertyName,
                relation.PrincipalKeyType.AssemblyQualifiedName!,
                relation.RelatedKeyPropertyName,
                relation.RelatedKeyType.AssemblyQualifiedName!,
                relation.PrincipalAlias,
                relation.DeleteBehavior)).ToList(),
            JsonOptions));
    }

    private static List<MorphTypeMapping> ParseTypeMappings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<MorphTypeMapping>();
        }

        var storedMappings = JsonSerializer.Deserialize<List<StoredMorphTypeMapping>>(json, JsonOptions) ?? new();
        return storedMappings.Select(mapping => new MorphTypeMapping(GetRequiredType(mapping.ClrType), mapping.Alias)).ToList();
    }

    private static List<MorphReference> ParseReferences(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<MorphReference>();
        }

        var references = new List<MorphReference>();
        var storedReferences = JsonSerializer.Deserialize<List<StoredMorphReference>>(json, JsonOptions) ?? new();
        foreach (var storedReference in storedReferences)
        {
            var reference = new MorphReference(
                GetRequiredType(storedReference.DependentType),
                storedReference.RelationshipName,
                storedReference.TypePropertyName,
                storedReference.IdPropertyName,
                GetRequiredType(storedReference.IdPropertyType));

            reference.Associations.AddRange(storedReference.Associations.Select(association => new MorphAssociation(
                GetRequiredType(association.PrincipalType),
                association.InverseRelationshipName,
                association.PrincipalKeyPropertyName,
                association.Alias,
                association.Multiplicity,
                association.DeleteBehavior)));

            references.Add(reference);
        }

        return references;
    }

    private static List<MorphManyToManyRelation> ParseManyToManyRelations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<MorphManyToManyRelation>();
        }

        var storedRelations = JsonSerializer.Deserialize<List<StoredMorphManyToManyRelation>>(json, JsonOptions) ?? new();
        return storedRelations.Select(relation => new MorphManyToManyRelation(
            GetRequiredType(relation.PrincipalType),
            GetRequiredType(relation.RelatedType),
            GetRequiredType(relation.PivotType),
            relation.RelationshipName,
            relation.InverseRelationshipName,
            relation.PivotTypePropertyName,
            relation.PivotIdPropertyName,
            GetRequiredType(relation.PivotIdPropertyType),
            relation.PivotRelatedIdPropertyName,
            GetRequiredType(relation.PivotRelatedIdPropertyType),
            relation.PrincipalKeyPropertyName,
            GetRequiredType(relation.PrincipalKeyType),
            relation.RelatedKeyPropertyName,
            GetRequiredType(relation.RelatedKeyType),
            relation.PrincipalAlias,
            relation.DeleteBehavior)).ToList();
    }

    private static Type GetRequiredType(string assemblyQualifiedTypeName)
    {
        return Type.GetType(assemblyQualifiedTypeName, throwOnError: true)!;
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

    private sealed record StoredMorphTypeMapping(string ClrType, string Alias);

    private sealed record StoredMorphAssociation(
        string PrincipalType,
        string InverseRelationshipName,
        string PrincipalKeyPropertyName,
        string Alias,
        MorphMultiplicity Multiplicity,
        PolymorphicDeleteBehavior DeleteBehavior);

    private sealed record StoredMorphReference(
        string DependentType,
        string RelationshipName,
        string TypePropertyName,
        string IdPropertyName,
        string IdPropertyType,
        List<StoredMorphAssociation> Associations);

    private sealed record StoredMorphManyToManyRelation(
        string PrincipalType,
        string RelatedType,
        string PivotType,
        string RelationshipName,
        string InverseRelationshipName,
        string PivotTypePropertyName,
        string PivotIdPropertyName,
        string PivotIdPropertyType,
        string PivotRelatedIdPropertyName,
        string PivotRelatedIdPropertyType,
        string PrincipalKeyPropertyName,
        string PrincipalKeyType,
        string RelatedKeyPropertyName,
        string RelatedKeyType,
        string PrincipalAlias,
        PolymorphicDeleteBehavior DeleteBehavior);
}



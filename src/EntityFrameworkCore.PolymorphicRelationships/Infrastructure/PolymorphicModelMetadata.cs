using Microsoft.EntityFrameworkCore.Metadata;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

internal static class PolymorphicModelMetadata
{
    private const string TypeMappingsAnnotation = "EntityFrameworkCore.PolymorphicRelationships:TypeMappings";
    private const string ReferencesAnnotation = "EntityFrameworkCore.PolymorphicRelationships:References";
    private const string ManyToManyAnnotation = "EntityFrameworkCore.PolymorphicRelationships:ManyToMany";
    private static readonly JsonSerializerOptions JsonOptions = new();
    private static readonly ConditionalWeakTable<IReadOnlyModel, CachedState> Cache = new();

    public static List<MorphTypeMapping> GetOrCreateTypeMappings(IMutableModel model)
    {
        return GetCachedState(model).TypeMappings;
    }

    public static List<MorphReference> GetOrCreateReferences(IMutableModel model)
    {
        return GetCachedState(model).References;
    }

    public static IReadOnlyList<MorphReference> GetReferences(IReadOnlyModel model)
    {
        return GetCachedState(model).References;
    }

    public static List<MorphManyToManyRelation> GetOrCreateManyToManyRelations(IMutableModel model)
    {
        return GetCachedState(model).ManyToManyRelations;
    }

    public static IReadOnlyList<MorphManyToManyRelation> GetManyToManyRelations(IReadOnlyModel model)
    {
        return GetCachedState(model).ManyToManyRelations;
    }

    public static string GetAlias(IReadOnlyModel model, Type clrType)
    {
        return FindTypeMapping(model, clrType)?.Alias
            ?? clrType.FullName
            ?? clrType.Name;
    }

    public static MorphTypeMapping? FindTypeMapping(IReadOnlyModel model, Type clrType)
    {
        var mappings = GetCachedState(model).TypeMappings;
        return mappings.FirstOrDefault(mapping => mapping.ClrType == clrType)
            ?? mappings.FirstOrDefault(mapping => mapping.ClrType.IsAssignableFrom(clrType));
    }

    public static void SyncTypeMappings(IMutableModel model, IEnumerable<MorphTypeMapping> mappings)
    {
        var list = mappings.ToList();
        var json = JsonSerializer.Serialize(
            list.Select(mapping => new StoredMorphTypeMapping(mapping.ClrType.AssemblyQualifiedName!, mapping.Alias)).ToList(),
            JsonOptions);

        model.SetAnnotation(TypeMappingsAnnotation, json);
        UpdateCachedState(model, typeMappingsJson: json, typeMappings: list);
    }

    public static void SyncReferences(IMutableModel model, IEnumerable<MorphReference> references)
    {
        var list = references.ToList();
        var json = JsonSerializer.Serialize(
            list.Select(reference => new StoredMorphReference(
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
            JsonOptions);

        model.SetAnnotation(ReferencesAnnotation, json);
        UpdateCachedState(model, referencesJson: json, references: list);
    }

    public static void SyncManyToManyRelations(IMutableModel model, IEnumerable<MorphManyToManyRelation> relations)
    {
        var list = relations.ToList();
        var json = JsonSerializer.Serialize(
            list.Select(relation => new StoredMorphManyToManyRelation(
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
            JsonOptions);

        model.SetAnnotation(ManyToManyAnnotation, json);
        UpdateCachedState(model, manyToManyJson: json, manyToManyRelations: list);
    }

    private static CachedState GetCachedState(IReadOnlyModel model)
    {
        var state = Cache.GetValue(model, _ => new CachedState());
        RefreshState(state, model);
        return state;
    }

    private static void UpdateCachedState(
        IReadOnlyModel model,
        string? typeMappingsJson = null,
        List<MorphTypeMapping>? typeMappings = null,
        string? referencesJson = null,
        List<MorphReference>? references = null,
        string? manyToManyJson = null,
        List<MorphManyToManyRelation>? manyToManyRelations = null)
    {
        var state = Cache.GetValue(model, _ => new CachedState());

        if (typeMappingsJson is not null && typeMappings is not null)
        {
            state.TypeMappingsJson = typeMappingsJson;
            state.TypeMappings = typeMappings;
        }

        if (referencesJson is not null && references is not null)
        {
            state.ReferencesJson = referencesJson;
            state.References = references;
            state.ReferencesByDependent = references.ToDictionary(
                reference => (reference.DependentType, reference.RelationshipName),
                reference => reference);
            state.InverseAssociations = references
                .SelectMany(reference => reference.Associations.Select(association => new ReferenceAssociation(reference, association)))
                .GroupBy(item => (item.Reference.DependentType, item.Association.InverseRelationshipName, item.Association.Multiplicity))
                .ToDictionary(group => group.Key, group => group.ToList());
        }

        if (manyToManyJson is not null && manyToManyRelations is not null)
        {
            state.ManyToManyJson = manyToManyJson;
            state.ManyToManyRelations = manyToManyRelations;
            state.ManyToManyByRelationship = manyToManyRelations
                .GroupBy(relation => relation.RelationshipName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
            state.ManyToManyByInverseRelationship = manyToManyRelations
                .GroupBy(relation => relation.InverseRelationshipName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        }
    }

    private static void RefreshState(CachedState state, IReadOnlyModel model)
    {
        var typeMappingsJson = model.FindAnnotation(TypeMappingsAnnotation)?.Value as string;
        if (!string.Equals(state.TypeMappingsJson, typeMappingsJson, StringComparison.Ordinal))
        {
            state.TypeMappingsJson = typeMappingsJson;
            state.TypeMappings = ParseTypeMappings(typeMappingsJson);
        }

        var referencesJson = model.FindAnnotation(ReferencesAnnotation)?.Value as string;
        if (!string.Equals(state.ReferencesJson, referencesJson, StringComparison.Ordinal))
        {
            state.ReferencesJson = referencesJson;
            state.References = ParseReferences(referencesJson);
            state.ReferencesByDependent = state.References.ToDictionary(
                reference => (reference.DependentType, reference.RelationshipName),
                reference => reference);
            state.InverseAssociations = state.References
                .SelectMany(reference => reference.Associations.Select(association => new ReferenceAssociation(reference, association)))
                .GroupBy(item => (item.Reference.DependentType, item.Association.InverseRelationshipName, item.Association.Multiplicity))
                .ToDictionary(group => group.Key, group => group.ToList());
        }

        var manyToManyJson = model.FindAnnotation(ManyToManyAnnotation)?.Value as string;
        if (!string.Equals(state.ManyToManyJson, manyToManyJson, StringComparison.Ordinal))
        {
            state.ManyToManyJson = manyToManyJson;
            state.ManyToManyRelations = ParseManyToManyRelations(manyToManyJson);
            state.ManyToManyByRelationship = state.ManyToManyRelations
                .GroupBy(relation => relation.RelationshipName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
            state.ManyToManyByInverseRelationship = state.ManyToManyRelations
                .GroupBy(relation => relation.InverseRelationshipName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        }
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
        var state = GetCachedState(model);
        return state.ReferencesByDependent.GetValueOrDefault((dependentType, relationshipName))
            ?? throw new InvalidOperationException($"No morphTo relationship named '{relationshipName}' is registered for '{dependentType.Name}'.");
    }

    public static (MorphReference Reference, MorphAssociation Association) GetRequiredInverse(
        IReadOnlyModel model,
        Type principalType,
        Type dependentType,
        string inverseRelationshipName,
        MorphMultiplicity multiplicity)
    {
        var state = GetCachedState(model);
        if (state.InverseAssociations.TryGetValue((dependentType, inverseRelationshipName, multiplicity), out var candidates))
        {
            foreach (var candidate in candidates)
            {
                if (candidate.Association.PrincipalType.IsAssignableFrom(principalType))
                {
                    return (candidate.Reference, candidate.Association);
                }
            }
        }

        throw new InvalidOperationException($"No {(multiplicity == MorphMultiplicity.One ? "morphOne" : "morphMany")} relationship named '{inverseRelationshipName}' is registered between '{principalType.Name}' and '{dependentType.Name}'.");
    }

    public static MorphManyToManyRelation GetRequiredManyToMany(IReadOnlyModel model, Type principalType, Type relatedType, string relationshipName)
    {
        var state = GetCachedState(model);
        if (state.ManyToManyByRelationship.TryGetValue(relationshipName, out var relations))
        {
            var match = relations.FirstOrDefault(relation => relation.PrincipalType.IsAssignableFrom(principalType)
                && relation.RelatedType.IsAssignableFrom(relatedType));

            if (match is not null)
            {
                return match;
            }
        }

        throw new InvalidOperationException($"No morphToMany relationship named '{relationshipName}' is registered between '{principalType.Name}' and '{relatedType.Name}'.");
    }

    public static MorphManyToManyRelation GetRequiredMorphedByMany(IReadOnlyModel model, Type relatedType, Type principalType, string inverseRelationshipName)
    {
        var state = GetCachedState(model);
        if (state.ManyToManyByInverseRelationship.TryGetValue(inverseRelationshipName, out var relations))
        {
            var match = relations.FirstOrDefault(relation => relation.RelatedType.IsAssignableFrom(relatedType)
                && relation.PrincipalType.IsAssignableFrom(principalType));

            if (match is not null)
            {
                return match;
            }
        }

        throw new InvalidOperationException($"No morphedByMany relationship named '{inverseRelationshipName}' is registered between '{relatedType.Name}' and '{principalType.Name}'.");
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

    private sealed class CachedState
    {
        public string? TypeMappingsJson { get; set; }

        public List<MorphTypeMapping> TypeMappings { get; set; } = new();

        public string? ReferencesJson { get; set; }

        public List<MorphReference> References { get; set; } = new();

        public Dictionary<(Type DependentType, string RelationshipName), MorphReference> ReferencesByDependent { get; set; } = new();

        public Dictionary<(Type DependentType, string InverseRelationshipName, MorphMultiplicity Multiplicity), List<ReferenceAssociation>> InverseAssociations { get; set; } = new();

        public string? ManyToManyJson { get; set; }

        public List<MorphManyToManyRelation> ManyToManyRelations { get; set; } = new();

        public Dictionary<string, List<MorphManyToManyRelation>> ManyToManyByRelationship { get; set; } = new(StringComparer.Ordinal);

        public Dictionary<string, List<MorphManyToManyRelation>> ManyToManyByInverseRelationship { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed record ReferenceAssociation(MorphReference Reference, MorphAssociation Association);
}



using System.Linq.Expressions;
using EntityFrameworkCore.PolymorphicRelationships.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntityFrameworkCore.PolymorphicRelationships;

public sealed class PolymorphicModelBuilder
{
    private readonly ModelBuilder _modelBuilder;

    internal PolymorphicModelBuilder(ModelBuilder modelBuilder)
    {
        _modelBuilder = modelBuilder;
    }

    public PolymorphicModelBuilder MorphMap<TEntity>(string alias)
        where TEntity : class
    {
        return MorphMap(typeof(TEntity), alias);
    }

    internal PolymorphicModelBuilder MorphMap(Type entityType, string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new ArgumentException("Morph alias cannot be empty.", nameof(alias));
        }

        var mappings = PolymorphicModelMetadata.GetOrCreateTypeMappings(_modelBuilder.Model);
        var existing = mappings.FirstOrDefault(mapping => mapping.ClrType == entityType);
        var duplicateAlias = mappings.FirstOrDefault(mapping => mapping.ClrType != entityType
            && string.Equals(mapping.Alias, alias, StringComparison.Ordinal));

        if (duplicateAlias is not null)
        {
            throw new InvalidOperationException($"Morph alias '{alias}' is already registered for '{duplicateAlias.ClrType.Name}'. Morph aliases must be unique.");
        }

        if (existing is null)
        {
            mappings.Add(new PolymorphicModelMetadata.MorphTypeMapping(entityType, alias));
        }
        else
        {
            existing.Alias = alias;
        }

        PolymorphicModelMetadata.SyncTypeMappings(_modelBuilder.Model, mappings);

        return this;
    }

    public MorphEntityBuilder<TEntity> Entity<TEntity>()
        where TEntity : class
    {
        return new MorphEntityBuilder<TEntity>(_modelBuilder, _modelBuilder.Entity<TEntity>());
    }

    internal void RegisterMorphTo(Type dependentType, string relationshipName, string typePropertyName, string idPropertyName, Type idPropertyType)
    {
        if (string.IsNullOrWhiteSpace(relationshipName))
        {
            throw new ArgumentException("Relationship name cannot be empty.", nameof(relationshipName));
        }

        var entityBuilder = _modelBuilder.Entity(dependentType);
        entityBuilder.Property(typeof(string), typePropertyName);
        entityBuilder.Property(idPropertyType, idPropertyName);
        entityBuilder.HasIndex(typePropertyName, idPropertyName);

        var references = PolymorphicModelMetadata.GetOrCreateReferences(_modelBuilder.Model);
        var reference = references.FirstOrDefault(candidate => candidate.DependentType == dependentType
            && string.Equals(candidate.RelationshipName, relationshipName, StringComparison.Ordinal));

        if (reference is null)
        {
            references.Add(new PolymorphicModelMetadata.MorphReference(dependentType, relationshipName, typePropertyName, idPropertyName, idPropertyType));
        }

        PolymorphicModelMetadata.SyncReferences(_modelBuilder.Model, references);
    }

    internal void RegisterAssociation(
        Type dependentType,
        string relationshipName,
        Type principalType,
        string inverseRelationshipName,
        MorphMultiplicity multiplicity,
        string? ownerKeyPropertyName,
        PolymorphicDeleteBehavior deleteBehavior)
    {
        var principalEntityType = _modelBuilder.Model.FindEntityType(principalType)
            ?? throw new InvalidOperationException($"Entity '{principalType.Name}' must be part of the EF model before it can participate in a polymorphic relationship.");

        var references = PolymorphicModelMetadata.GetOrCreateReferences(_modelBuilder.Model);
        var reference = references.FirstOrDefault(candidate => candidate.DependentType == dependentType
            && string.Equals(candidate.RelationshipName, relationshipName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"The morphTo relationship '{dependentType.Name}.{relationshipName}' must be registered before inverse attribute conventions can be applied.");

        var resolvedOwnerKey = string.IsNullOrWhiteSpace(ownerKeyPropertyName)
            ? ExpressionHelpers.GetSingleKeyPropertyName(principalEntityType)
            : ownerKeyPropertyName;

        var alias = PolymorphicModelMetadata.GetAlias(_modelBuilder.Model, principalType);
        var existing = reference.Associations.FirstOrDefault(candidate => candidate.PrincipalType == principalType
            && string.Equals(candidate.InverseRelationshipName, inverseRelationshipName, StringComparison.Ordinal)
            && candidate.Multiplicity == multiplicity);

        if (existing is not null)
        {
            reference.Associations.Remove(existing);
        }

        reference.Associations.Add(new PolymorphicModelMetadata.MorphAssociation(
            principalType,
            inverseRelationshipName,
            resolvedOwnerKey,
            alias,
            multiplicity,
            deleteBehavior));

        PolymorphicModelMetadata.SyncReferences(_modelBuilder.Model, references);
    }

    internal void RegisterMorphToManyConvention(
        Type principalType,
        Type relatedType,
        Type pivotType,
        string relationshipName,
        string inverseRelationshipName,
        string morphName,
        string? principalKeyPropertyName,
        string? relatedKeyPropertyName,
        PolymorphicDeleteBehavior deleteBehavior)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(morphName);

        var principalEntityType = _modelBuilder.Model.FindEntityType(principalType)
            ?? throw new InvalidOperationException($"Entity '{principalType.Name}' must be part of the EF model before it can participate in a polymorphic many-to-many relationship.");

        var relatedEntityType = _modelBuilder.Model.FindEntityType(relatedType)
            ?? throw new InvalidOperationException($"Entity '{relatedType.Name}' must be part of the EF model before it can participate in a polymorphic many-to-many relationship.");

        var principalKey = string.IsNullOrWhiteSpace(principalKeyPropertyName)
            ? ExpressionHelpers.GetSingleKeyPropertyName(principalEntityType)
            : principalKeyPropertyName;

        var relatedKey = string.IsNullOrWhiteSpace(relatedKeyPropertyName)
            ? ExpressionHelpers.GetSingleKeyPropertyName(relatedEntityType)
            : relatedKeyPropertyName;

        var principalKeyType = principalEntityType.FindProperty(principalKey)?.ClrType
            ?? throw new InvalidOperationException($"Property '{principalKey}' was not found on '{principalType.Name}'.");

        var relatedKeyType = relatedEntityType.FindProperty(relatedKey)?.ClrType
            ?? throw new InvalidOperationException($"Property '{relatedKey}' was not found on '{relatedType.Name}'.");

        var pivotBuilder = _modelBuilder.Entity(pivotType);
        var pivotColumns = EntityTypeBuilderExtensions.HasMorphToManyColumns(pivotBuilder, morphName, relatedType, principalKeyType, relatedKeyType);

        RegisterMorphToMany(
            principalType,
            relatedType,
            pivotType,
            relationshipName,
            inverseRelationshipName,
            pivotColumns.TypeColumnName,
            pivotColumns.IdColumnName,
            principalKeyType,
            pivotColumns.RelatedIdColumnName,
            relatedKeyType,
            principalKey,
            relatedKey,
            deleteBehavior);
    }

    public PolymorphicModelBuilder MorphToMany<TPrincipal, TRelated, TPivot, TPrincipalKey, TRelatedKey>(
        string relationshipName,
        string inverseRelationshipName,
        Expression<Func<TPivot, string?>> typeProperty,
        Expression<Func<TPivot, TPrincipalKey>> idProperty,
        Expression<Func<TPivot, TRelatedKey>> relatedIdProperty,
        Expression<Func<TPrincipal, TPrincipalKey>>? principalKey = null,
        Expression<Func<TRelated, TRelatedKey>>? relatedKey = null,
        PolymorphicDeleteBehavior deleteBehavior = PolymorphicDeleteBehavior.Cascade)
        where TPrincipal : class
        where TRelated : class
        where TPivot : class
    {
        if (string.IsNullOrWhiteSpace(relationshipName))
        {
            throw new ArgumentException("Relationship name cannot be empty.", nameof(relationshipName));
        }

        if (string.IsNullOrWhiteSpace(inverseRelationshipName))
        {
            throw new ArgumentException("Inverse relationship name cannot be empty.", nameof(inverseRelationshipName));
        }

        var principalEntityType = _modelBuilder.Model.FindEntityType(typeof(TPrincipal))
            ?? throw new InvalidOperationException($"Entity '{typeof(TPrincipal).Name}' must be part of the EF model before it can participate in a polymorphic many-to-many relationship.");

        var relatedEntityType = _modelBuilder.Model.FindEntityType(typeof(TRelated))
            ?? throw new InvalidOperationException($"Entity '{typeof(TRelated).Name}' must be part of the EF model before it can participate in a polymorphic many-to-many relationship.");

        var pivotBuilder = _modelBuilder.Entity<TPivot>();
        var typePropertyName = ExpressionHelpers.GetPropertyName(typeProperty);
        var idPropertyName = ExpressionHelpers.GetPropertyName(idProperty);
        var relatedIdPropertyName = ExpressionHelpers.GetPropertyName(relatedIdProperty);

        pivotBuilder.Property(typePropertyName);
        pivotBuilder.Property(idPropertyName);
        pivotBuilder.Property(relatedIdPropertyName);
        RegisterMorphToMany(
            typeof(TPrincipal),
            typeof(TRelated),
            typeof(TPivot),
            relationshipName,
            inverseRelationshipName,
            typePropertyName,
            idPropertyName,
            typeof(TPrincipalKey),
            relatedIdPropertyName,
            typeof(TRelatedKey),
            principalKey is null ? ExpressionHelpers.GetSingleKeyPropertyName(principalEntityType) : ExpressionHelpers.GetPropertyName(principalKey),
            relatedKey is null ? ExpressionHelpers.GetSingleKeyPropertyName(relatedEntityType) : ExpressionHelpers.GetPropertyName(relatedKey),
            deleteBehavior);

        return this;
    }

    public PolymorphicModelBuilder MorphedByMany<TRelated, TPrincipal, TPivot, TRelatedKey, TPrincipalKey>(
        string inverseRelationshipName,
        string relationshipName,
        Expression<Func<TPivot, string?>> typeProperty,
        Expression<Func<TPivot, TPrincipalKey>> idProperty,
        Expression<Func<TPivot, TRelatedKey>> relatedIdProperty,
        Expression<Func<TRelated, TRelatedKey>>? relatedKey = null,
        Expression<Func<TPrincipal, TPrincipalKey>>? principalKey = null,
        PolymorphicDeleteBehavior deleteBehavior = PolymorphicDeleteBehavior.Cascade)
        where TRelated : class
        where TPrincipal : class
        where TPivot : class
    {
        return MorphToMany<TPrincipal, TRelated, TPivot, TPrincipalKey, TRelatedKey>(
            relationshipName,
            inverseRelationshipName,
            typeProperty,
            idProperty,
            relatedIdProperty,
            principalKey,
            relatedKey,
            deleteBehavior);
    }

    public PolymorphicModelBuilder MorphToManyConvention<TPrincipal, TRelated, TPivot, TPrincipalKey, TRelatedKey>(
        string relationshipName,
        string inverseRelationshipName,
        string morphName,
        Expression<Func<TPrincipal, TPrincipalKey>>? principalKey = null,
        Expression<Func<TRelated, TRelatedKey>>? relatedKey = null,
        PolymorphicDeleteBehavior deleteBehavior = PolymorphicDeleteBehavior.Cascade)
        where TPrincipal : class
        where TRelated : class
        where TPivot : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(morphName);

        RegisterMorphToManyConvention(
            typeof(TPrincipal),
            typeof(TRelated),
            typeof(TPivot),
            relationshipName,
            inverseRelationshipName,
            morphName,
            principalKey is null ? null : ExpressionHelpers.GetPropertyName(principalKey),
            relatedKey is null ? null : ExpressionHelpers.GetPropertyName(relatedKey),
            deleteBehavior);

        return this;
    }

    public PolymorphicModelBuilder MorphedByManyConvention<TRelated, TPrincipal, TPivot, TRelatedKey, TPrincipalKey>(
        string inverseRelationshipName,
        string relationshipName,
        string morphName,
        Expression<Func<TRelated, TRelatedKey>>? relatedKey = null,
        Expression<Func<TPrincipal, TPrincipalKey>>? principalKey = null,
        PolymorphicDeleteBehavior deleteBehavior = PolymorphicDeleteBehavior.Cascade)
        where TRelated : class
        where TPrincipal : class
        where TPivot : class
    {
        return MorphToManyConvention<TPrincipal, TRelated, TPivot, TPrincipalKey, TRelatedKey>(
            relationshipName,
            inverseRelationshipName,
            morphName,
            principalKey,
            relatedKey,
            deleteBehavior);
    }

    internal void RegisterMorphToMany(
        Type principalType,
        Type relatedType,
        Type pivotType,
        string relationshipName,
        string inverseRelationshipName,
        string typePropertyName,
        string idPropertyName,
        Type pivotIdPropertyType,
        string relatedIdPropertyName,
        Type pivotRelatedIdPropertyType,
        string principalKeyPropertyName,
        string relatedKeyPropertyName,
        PolymorphicDeleteBehavior deleteBehavior)
    {
        var principalEntityType = _modelBuilder.Model.FindEntityType(principalType)
            ?? throw new InvalidOperationException($"Entity '{principalType.Name}' must be part of the EF model before it can participate in a polymorphic many-to-many relationship.");

        var relatedEntityType = _modelBuilder.Model.FindEntityType(relatedType)
            ?? throw new InvalidOperationException($"Entity '{relatedType.Name}' must be part of the EF model before it can participate in a polymorphic many-to-many relationship.");

        var pivotBuilder = _modelBuilder.Entity(pivotType);
        pivotBuilder.Property(typeof(string), typePropertyName);
        pivotBuilder.Property(pivotIdPropertyType, idPropertyName);
        pivotBuilder.Property(pivotRelatedIdPropertyType, relatedIdPropertyName);
        pivotBuilder.HasIndex(typePropertyName, idPropertyName);
        pivotBuilder.HasIndex(relatedIdPropertyName);

        var principalKeyClrType = principalEntityType.FindProperty(principalKeyPropertyName)?.ClrType
            ?? throw new InvalidOperationException($"Property '{principalKeyPropertyName}' was not found on '{principalType.Name}'.");

        var relatedKeyClrType = relatedEntityType.FindProperty(relatedKeyPropertyName)?.ClrType
            ?? throw new InvalidOperationException($"Property '{relatedKeyPropertyName}' was not found on '{relatedType.Name}'.");

        var relations = PolymorphicModelMetadata.GetOrCreateManyToManyRelations(_modelBuilder.Model);
        var existing = relations.FirstOrDefault(relation => relation.PrincipalType == principalType
            && relation.RelatedType == relatedType
            && relation.PivotType == pivotType
            && string.Equals(relation.RelationshipName, relationshipName, StringComparison.Ordinal)
            && string.Equals(relation.InverseRelationshipName, inverseRelationshipName, StringComparison.Ordinal));

        if (existing is not null)
        {
            relations.Remove(existing);
        }

        relations.Add(new PolymorphicModelMetadata.MorphManyToManyRelation(
            principalType,
            relatedType,
            pivotType,
            relationshipName,
            inverseRelationshipName,
            typePropertyName,
            idPropertyName,
            pivotIdPropertyType,
            relatedIdPropertyName,
            pivotRelatedIdPropertyType,
            principalKeyPropertyName,
            principalKeyClrType,
            relatedKeyPropertyName,
            relatedKeyClrType,
            PolymorphicModelMetadata.GetAlias(_modelBuilder.Model, principalType),
            deleteBehavior));

        PolymorphicModelMetadata.SyncManyToManyRelations(_modelBuilder.Model, relations);
    }
}

public sealed class MorphEntityBuilder<TEntity>
    where TEntity : class
{
    private readonly ModelBuilder _modelBuilder;
    private readonly EntityTypeBuilder<TEntity> _entityBuilder;

    internal MorphEntityBuilder(ModelBuilder modelBuilder, EntityTypeBuilder<TEntity> entityBuilder)
    {
        _modelBuilder = modelBuilder;
        _entityBuilder = entityBuilder;
    }

    public MorphToBuilder<TEntity, TKey> MorphTo<TKey>(
        string relationshipName,
        Expression<Func<TEntity, string?>> typeProperty,
        Expression<Func<TEntity, TKey>> idProperty)
    {
        if (string.IsNullOrWhiteSpace(relationshipName))
        {
            throw new ArgumentException("Relationship name cannot be empty.", nameof(relationshipName));
        }

        var typePropertyName = ExpressionHelpers.GetPropertyName(typeProperty);
        var idPropertyName = ExpressionHelpers.GetPropertyName(idProperty);

        _entityBuilder.Property(typePropertyName);
        _entityBuilder.Property(idPropertyName);
        _entityBuilder.HasIndex(typePropertyName, idPropertyName);

        return CreateMorphToBuilder<TKey>(relationshipName, typePropertyName, idPropertyName);
    }

    public MorphToBuilder<TEntity, TKey> MorphToConvention<TKey>(string relationshipName, string? morphName = null)
    {
        if (string.IsNullOrWhiteSpace(relationshipName))
        {
            throw new ArgumentException("Relationship name cannot be empty.", nameof(relationshipName));
        }

        var resolvedMorphName = string.IsNullOrWhiteSpace(morphName)
            ? LaravelMorphNaming.GetMorphName(relationshipName)
            : LaravelMorphNaming.GetMorphName(morphName);

        var typePropertyName = LaravelMorphNaming.GetMorphTypeColumnName(resolvedMorphName);
        var idPropertyName = LaravelMorphNaming.GetMorphIdColumnName(resolvedMorphName);

        _entityBuilder.Property<string?>(typePropertyName);
        _entityBuilder.Property<TKey>(idPropertyName);
        _entityBuilder.HasIndex(typePropertyName, idPropertyName);

        return CreateMorphToBuilder<TKey>(relationshipName, typePropertyName, idPropertyName);
    }

    private MorphToBuilder<TEntity, TKey> CreateMorphToBuilder<TKey>(string relationshipName, string typePropertyName, string idPropertyName)
    {
        if (string.IsNullOrWhiteSpace(relationshipName))
        {
            throw new ArgumentException("Relationship name cannot be empty.", nameof(relationshipName));
        }

        var references = PolymorphicModelMetadata.GetOrCreateReferences(_modelBuilder.Model);
        var reference = references.FirstOrDefault(candidate => candidate.DependentType == typeof(TEntity)
            && string.Equals(candidate.RelationshipName, relationshipName, StringComparison.Ordinal));

        if (reference is null)
        {
            reference = new PolymorphicModelMetadata.MorphReference(typeof(TEntity), relationshipName, typePropertyName, idPropertyName, typeof(TKey));
            references.Add(reference);
        }

        PolymorphicModelMetadata.SyncReferences(_modelBuilder.Model, references);

        return new MorphToBuilder<TEntity, TKey>(_modelBuilder, reference);
    }
}

public sealed class MorphToBuilder<TEntity, TKey>
    where TEntity : class
{
    private readonly ModelBuilder _modelBuilder;
    private readonly PolymorphicModelMetadata.MorphReference _reference;

    internal MorphToBuilder(ModelBuilder modelBuilder, PolymorphicModelMetadata.MorphReference reference)
    {
        _modelBuilder = modelBuilder;
        _reference = reference;
    }

    public MorphToBuilder<TEntity, TKey> MorphOne<TPrincipal>(
        string inverseRelationshipName,
        Expression<Func<TPrincipal, object?>>? ownerKey = null,
        PolymorphicDeleteBehavior deleteBehavior = PolymorphicDeleteBehavior.Cascade)
        where TPrincipal : class
    {
        RegisterAssociation<TPrincipal>(inverseRelationshipName, ownerKey, MorphMultiplicity.One, deleteBehavior);
        return this;
    }

    public MorphToBuilder<TEntity, TKey> MorphMany<TPrincipal>(
        string inverseRelationshipName,
        Expression<Func<TPrincipal, object?>>? ownerKey = null,
        PolymorphicDeleteBehavior deleteBehavior = PolymorphicDeleteBehavior.Cascade)
        where TPrincipal : class
    {
        RegisterAssociation<TPrincipal>(inverseRelationshipName, ownerKey, MorphMultiplicity.Many, deleteBehavior);
        return this;
    }

    private void RegisterAssociation<TPrincipal>(
        string inverseRelationshipName,
        Expression<Func<TPrincipal, object?>>? ownerKey,
        MorphMultiplicity multiplicity,
        PolymorphicDeleteBehavior deleteBehavior)
        where TPrincipal : class
    {
        if (string.IsNullOrWhiteSpace(inverseRelationshipName))
        {
            throw new ArgumentException("Inverse relationship name cannot be empty.", nameof(inverseRelationshipName));
        }

        new PolymorphicModelBuilder(_modelBuilder).RegisterAssociation(
            _reference.DependentType,
            _reference.RelationshipName,
            typeof(TPrincipal),
            inverseRelationshipName,
            multiplicity,
            ownerKey is null ? null : ExpressionHelpers.GetPropertyName(ownerKey),
            deleteBehavior);
    }
}



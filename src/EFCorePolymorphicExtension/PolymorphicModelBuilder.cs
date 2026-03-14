using System.Linq.Expressions;
using EFCorePolymorphicExtension.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFCorePolymorphicExtension;

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
        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new ArgumentException("Morph alias cannot be empty.", nameof(alias));
        }

        var mappings = PolymorphicModelMetadata.GetOrCreateTypeMappings(_modelBuilder.Model);
        var existing = mappings.FirstOrDefault(mapping => mapping.ClrType == typeof(TEntity));

        if (existing is null)
        {
            mappings.Add(new PolymorphicModelMetadata.MorphTypeMapping(typeof(TEntity), alias));
        }
        else
        {
            existing.Alias = alias;
        }

        return this;
    }

    public MorphEntityBuilder<TEntity> Entity<TEntity>()
        where TEntity : class
    {
        return new MorphEntityBuilder<TEntity>(_modelBuilder, _modelBuilder.Entity<TEntity>());
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
        RegisterMorphToMany<TPrincipal, TRelated, TPivot, TPrincipalKey, TRelatedKey>(
            relationshipName,
            inverseRelationshipName,
            principalEntityType,
            relatedEntityType,
            typePropertyName,
            idPropertyName,
            relatedIdPropertyName,
            principalKey,
            relatedKey,
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

        var pivotBuilder = _modelBuilder.Entity<TPivot>();
        var typePropertyName = LaravelMorphNaming.GetMorphTypeColumnName(morphName);
        var idPropertyName = LaravelMorphNaming.GetMorphIdColumnName(morphName);
        var relatedIdPropertyName = LaravelMorphNaming.GetForeignKeyColumnName(typeof(TRelated));

        pivotBuilder.Property<string?>(typePropertyName);
        pivotBuilder.Property<TPrincipalKey>(idPropertyName);
        pivotBuilder.Property<TRelatedKey>(relatedIdPropertyName);

        var principalEntityType = _modelBuilder.Model.FindEntityType(typeof(TPrincipal))
            ?? throw new InvalidOperationException($"Entity '{typeof(TPrincipal).Name}' must be part of the EF model before it can participate in a polymorphic many-to-many relationship.");

        var relatedEntityType = _modelBuilder.Model.FindEntityType(typeof(TRelated))
            ?? throw new InvalidOperationException($"Entity '{typeof(TRelated).Name}' must be part of the EF model before it can participate in a polymorphic many-to-many relationship.");

        RegisterMorphToMany<TPrincipal, TRelated, TPivot, TPrincipalKey, TRelatedKey>(
            relationshipName,
            inverseRelationshipName,
            principalEntityType,
            relatedEntityType,
            typePropertyName,
            idPropertyName,
            relatedIdPropertyName,
            principalKey,
            relatedKey,
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

    private void RegisterMorphToMany<TPrincipal, TRelated, TPivot, TPrincipalKey, TRelatedKey>(
        string relationshipName,
        string inverseRelationshipName,
        Microsoft.EntityFrameworkCore.Metadata.IReadOnlyEntityType principalEntityType,
        Microsoft.EntityFrameworkCore.Metadata.IReadOnlyEntityType relatedEntityType,
        string typePropertyName,
        string idPropertyName,
        string relatedIdPropertyName,
        Expression<Func<TPrincipal, TPrincipalKey>>? principalKey,
        Expression<Func<TRelated, TRelatedKey>>? relatedKey,
        PolymorphicDeleteBehavior deleteBehavior)
        where TPrincipal : class
        where TRelated : class
        where TPivot : class
    {
        var pivotBuilder = _modelBuilder.Entity<TPivot>();
        pivotBuilder.HasIndex(typePropertyName, idPropertyName);
        pivotBuilder.HasIndex(relatedIdPropertyName);

        var principalKeyPropertyName = principalKey is null
            ? ExpressionHelpers.GetSingleKeyPropertyName(principalEntityType)
            : ExpressionHelpers.GetPropertyName(principalKey);

        var relatedKeyPropertyName = relatedKey is null
            ? ExpressionHelpers.GetSingleKeyPropertyName(relatedEntityType)
            : ExpressionHelpers.GetPropertyName(relatedKey);

        var principalKeyClrType = principalEntityType.FindProperty(principalKeyPropertyName)?.ClrType
            ?? throw new InvalidOperationException($"Property '{principalKeyPropertyName}' was not found on '{typeof(TPrincipal).Name}'.");

        var relatedKeyClrType = relatedEntityType.FindProperty(relatedKeyPropertyName)?.ClrType
            ?? throw new InvalidOperationException($"Property '{relatedKeyPropertyName}' was not found on '{typeof(TRelated).Name}'.");

        var relations = PolymorphicModelMetadata.GetOrCreateManyToManyRelations(_modelBuilder.Model);
        var existing = relations.FirstOrDefault(relation => relation.PrincipalType == typeof(TPrincipal)
            && relation.RelatedType == typeof(TRelated)
            && relation.PivotType == typeof(TPivot)
            && string.Equals(relation.RelationshipName, relationshipName, StringComparison.Ordinal)
            && string.Equals(relation.InverseRelationshipName, inverseRelationshipName, StringComparison.Ordinal));

        if (existing is not null)
        {
            relations.Remove(existing);
        }

        relations.Add(new PolymorphicModelMetadata.MorphManyToManyRelation(
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
            principalKeyPropertyName,
            principalKeyClrType,
            relatedKeyPropertyName,
            relatedKeyClrType,
            PolymorphicModelMetadata.GetAlias(_modelBuilder.Model, typeof(TPrincipal)),
            deleteBehavior));
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

        var principalEntityType = _modelBuilder.Model.FindEntityType(typeof(TPrincipal))
            ?? throw new InvalidOperationException($"Entity '{typeof(TPrincipal).Name}' must be part of the EF model before it can participate in a polymorphic relationship.");

        var principalKeyPropertyName = ownerKey is null
            ? ExpressionHelpers.GetSingleKeyPropertyName(principalEntityType)
            : ExpressionHelpers.GetPropertyName(ownerKey);

        var alias = PolymorphicModelMetadata.GetAlias(_modelBuilder.Model, typeof(TPrincipal));

        var existing = _reference.Associations.FirstOrDefault(candidate => candidate.PrincipalType == typeof(TPrincipal)
            && string.Equals(candidate.InverseRelationshipName, inverseRelationshipName, StringComparison.Ordinal)
            && candidate.Multiplicity == multiplicity);

        if (existing is not null)
        {
            _reference.Associations.Remove(existing);
        }

        _reference.Associations.Add(new PolymorphicModelMetadata.MorphAssociation(
            typeof(TPrincipal),
            inverseRelationshipName,
            principalKeyPropertyName,
            alias,
            multiplicity,
            deleteBehavior));
    }
}


using EntityFrameworkCore.PolymorphicRelationships.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntityFrameworkCore.PolymorphicRelationships;

public static class EntityTypeBuilderExtensions
{
    public static MorphColumnNames HasMorphColumns<TKey>(this EntityTypeBuilder entityTypeBuilder, string morphName)
    {
        return HasMorphColumns(entityTypeBuilder, morphName, typeof(TKey));
    }

    internal static MorphColumnNames HasMorphColumns(this EntityTypeBuilder entityTypeBuilder, string morphName, Type keyType)
    {
        ArgumentNullException.ThrowIfNull(entityTypeBuilder);
        ArgumentNullException.ThrowIfNull(keyType);
        ArgumentException.ThrowIfNullOrWhiteSpace(morphName);

        var columnNames = new MorphColumnNames(
            LaravelMorphNaming.GetMorphTypeColumnName(morphName),
            LaravelMorphNaming.GetMorphIdColumnName(morphName));

        entityTypeBuilder.Property<string?>(columnNames.TypeColumnName);
        entityTypeBuilder.Property(keyType, columnNames.IdColumnName);
        entityTypeBuilder.HasIndex(columnNames.TypeColumnName, columnNames.IdColumnName);
        return columnNames;
    }

    public static MorphColumnNames HasMorphColumns<TEntity, TKey>(this EntityTypeBuilder<TEntity> entityTypeBuilder, string morphName)
        where TEntity : class
    {
        return HasMorphColumns<TKey>((EntityTypeBuilder)entityTypeBuilder, morphName);
    }

    public static MorphPivotColumnNames HasMorphToManyColumns<TPrincipalKey, TRelatedKey>(this EntityTypeBuilder entityTypeBuilder, string morphName, Type relatedType)
    {
        return HasMorphToManyColumns(entityTypeBuilder, morphName, relatedType, typeof(TPrincipalKey), typeof(TRelatedKey));
    }

    internal static MorphPivotColumnNames HasMorphToManyColumns(this EntityTypeBuilder entityTypeBuilder, string morphName, Type relatedType, Type principalKeyType, Type relatedKeyType)
    {
        ArgumentNullException.ThrowIfNull(entityTypeBuilder);
        ArgumentNullException.ThrowIfNull(relatedType);
        ArgumentNullException.ThrowIfNull(principalKeyType);
        ArgumentNullException.ThrowIfNull(relatedKeyType);
        ArgumentException.ThrowIfNullOrWhiteSpace(morphName);

        var columnNames = new MorphPivotColumnNames(
            LaravelMorphNaming.GetMorphTypeColumnName(morphName),
            LaravelMorphNaming.GetMorphIdColumnName(morphName),
            LaravelMorphNaming.GetForeignKeyColumnName(relatedType));

        entityTypeBuilder.Property<string?>(columnNames.TypeColumnName);
        entityTypeBuilder.Property(principalKeyType, columnNames.IdColumnName);
        entityTypeBuilder.Property(relatedKeyType, columnNames.RelatedIdColumnName);
        entityTypeBuilder.HasIndex(columnNames.TypeColumnName, columnNames.IdColumnName);
        entityTypeBuilder.HasIndex(columnNames.RelatedIdColumnName);
        return columnNames;
    }

    public static MorphPivotColumnNames HasMorphToManyColumns<TPivot, TPrincipalKey, TRelatedKey>(this EntityTypeBuilder<TPivot> entityTypeBuilder, string morphName, Type relatedType)
        where TPivot : class
    {
        return HasMorphToManyColumns<TPrincipalKey, TRelatedKey>((EntityTypeBuilder)entityTypeBuilder, morphName, relatedType);
    }
}


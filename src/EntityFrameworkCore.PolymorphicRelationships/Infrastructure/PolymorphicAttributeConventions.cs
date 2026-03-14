using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using EntityFrameworkCore.PolymorphicRelationships.Attributes;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

internal static class PolymorphicAttributeConventions
{
    public static void Apply(ModelBuilder modelBuilder, PolymorphicModelBuilder polymorphicModelBuilder)
    {
        var entityTypes = modelBuilder.Model.GetEntityTypes()
            .Select(entityType => entityType.ClrType)
            .Where(clrType => clrType is not null)
            .Distinct()
            .ToArray();

        foreach (var clrType in entityTypes)
        {
            if (clrType.GetCustomAttribute<MorphMapAttribute>(inherit: true) is { } morphMapAttribute)
            {
                polymorphicModelBuilder.MorphMap(clrType, morphMapAttribute.Alias);
            }
        }

        foreach (var clrType in entityTypes)
        {
            foreach (var property in GetCandidateProperties(clrType))
            {
                if (property.GetCustomAttribute<MorphToAttribute>(inherit: true) is { } morphToAttribute)
                {
                    var foreignKeyAttribute = property.GetCustomAttribute<ForeignKeyAttribute>(inherit: true)
                        ?? throw new InvalidOperationException($"Property '{clrType.Name}.{property.Name}' uses [MorphTo] and must also define [ForeignKey(nameof(IdProperty))].");

                    var idProperty = clrType.GetProperty(foreignKeyAttribute.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException($"Property '{clrType.Name}.{property.Name}' references foreign key '{foreignKeyAttribute.Name}', but that property was not found.");

                    polymorphicModelBuilder.RegisterMorphTo(
                        clrType,
                        property.Name,
                        morphToAttribute.TypePropertyName,
                        idProperty.Name,
                        idProperty.PropertyType);
                }
            }
        }

        foreach (var clrType in entityTypes)
        {
            foreach (var property in GetCandidateProperties(clrType))
            {
                foreach (var morphOneAttribute in property.GetCustomAttributes<MorphOneAttribute>(inherit: true))
                {
                    polymorphicModelBuilder.RegisterAssociation(
                        morphOneAttribute.DependentType,
                        morphOneAttribute.RelationshipName,
                        clrType,
                        property.Name,
                        MorphMultiplicity.One,
                        morphOneAttribute.OwnerKey,
                        morphOneAttribute.DeleteBehavior);
                }

                foreach (var morphManyAttribute in property.GetCustomAttributes<MorphManyAttribute>(inherit: true))
                {
                    polymorphicModelBuilder.RegisterAssociation(
                        morphManyAttribute.DependentType,
                        morphManyAttribute.RelationshipName,
                        clrType,
                        property.Name,
                        MorphMultiplicity.Many,
                        morphManyAttribute.OwnerKey,
                        morphManyAttribute.DeleteBehavior);
                }

                if (property.GetCustomAttribute<MorphToManyAttribute>(inherit: true) is { } morphToManyAttribute)
                {
                    polymorphicModelBuilder.RegisterMorphToManyConvention(
                        clrType,
                        morphToManyAttribute.RelatedType,
                        morphToManyAttribute.PivotType,
                        property.Name,
                        morphToManyAttribute.InverseRelationshipName,
                        morphToManyAttribute.MorphName,
                        morphToManyAttribute.PrincipalKey,
                        morphToManyAttribute.RelatedKey,
                        morphToManyAttribute.DeleteBehavior);
                }

                if (property.GetCustomAttribute<MorphedByManyAttribute>(inherit: true) is { } morphedByManyAttribute)
                {
                    polymorphicModelBuilder.RegisterMorphToManyConvention(
                        morphedByManyAttribute.PrincipalType,
                        clrType,
                        morphedByManyAttribute.PivotType,
                        morphedByManyAttribute.RelationshipName,
                        property.Name,
                        morphedByManyAttribute.MorphName,
                        morphedByManyAttribute.PrincipalKey,
                        morphedByManyAttribute.RelatedKey,
                        morphedByManyAttribute.DeleteBehavior);
                }
            }
        }
    }

    private static IEnumerable<PropertyInfo> GetCandidateProperties(Type clrType)
    {
        return clrType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
    }
}


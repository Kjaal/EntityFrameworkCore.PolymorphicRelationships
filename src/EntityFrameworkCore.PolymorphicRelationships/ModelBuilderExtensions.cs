using EntityFrameworkCore.PolymorphicRelationships.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.PolymorphicRelationships;

public static class ModelBuilderExtensions
{
    public static ModelBuilder UsePolymorphicRelationships(this ModelBuilder modelBuilder, Action<PolymorphicModelBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(configure);

        configure(new PolymorphicModelBuilder(modelBuilder));
        return modelBuilder;
    }

    public static ModelBuilder UseLaravelPolymorphicRelationships(this ModelBuilder modelBuilder, Action<PolymorphicModelBuilder> configure)
    {
        return modelBuilder.UsePolymorphicRelationships(configure);
    }

    public static ModelBuilder UsePolymorphicRelationshipAttributes(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var polymorphicModelBuilder = new PolymorphicModelBuilder(modelBuilder);
        PolymorphicAttributeConventions.Apply(modelBuilder, polymorphicModelBuilder);
        return modelBuilder;
    }

    public static ModelBuilder UseLaravelPolymorphicRelationshipAttributes(this ModelBuilder modelBuilder)
    {
        return modelBuilder.UsePolymorphicRelationshipAttributes();
    }
}



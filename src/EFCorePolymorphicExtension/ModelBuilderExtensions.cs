using Microsoft.EntityFrameworkCore;

namespace EFCorePolymorphicExtension;

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
}


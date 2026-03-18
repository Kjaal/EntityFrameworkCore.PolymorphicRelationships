using EntityFrameworkCore.PolymorphicRelationships.Infrastructure;
using EntityFrameworkCore.PolymorphicRelationships.Infrastructure.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EntityFrameworkCore.PolymorphicRelationships;

public static class DbContextOptionsBuilderExtensions
{
    private static readonly PolymorphicQueryExpressionInterceptor QueryExpressionInterceptor = new();

    public static DbContextOptionsBuilder UsePolymorphicRelationships(this DbContextOptionsBuilder optionsBuilder)
    {
        return optionsBuilder.UsePolymorphicRelationships(configure: null);
    }

    public static DbContextOptionsBuilder UsePolymorphicRelationships(this DbContextOptionsBuilder optionsBuilder, Action<PolymorphicOptionsBuilder>? configure)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        var polymorphicOptionsBuilder = new PolymorphicOptionsBuilder();
        configure?.Invoke(polymorphicOptionsBuilder);

        var infrastructure = (IDbContextOptionsBuilderInfrastructure)optionsBuilder;
        infrastructure.AddOrUpdateExtension(new PolymorphicRelationalOptionsExtension(polymorphicOptionsBuilder.ExperimentalSelectProjectionSupportEnabled));
        optionsBuilder.AddInterceptors(QueryExpressionInterceptor);
        optionsBuilder.AddInterceptors(new PolymorphicNavigationSyncInterceptor());
        optionsBuilder.AddInterceptors(new PolymorphicCascadeDeleteInterceptor());
        optionsBuilder.AddInterceptors(new PolymorphicIntegrityValidationInterceptor());
        return optionsBuilder;
    }

    public static DbContextOptionsBuilder<TContext> UsePolymorphicRelationships<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext
    {
        UsePolymorphicRelationships((DbContextOptionsBuilder)optionsBuilder);
        return optionsBuilder;
    }

    public static DbContextOptionsBuilder<TContext> UsePolymorphicRelationships<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder, Action<PolymorphicOptionsBuilder>? configure)
        where TContext : DbContext
    {
        UsePolymorphicRelationships((DbContextOptionsBuilder)optionsBuilder, configure);
        return optionsBuilder;
    }

    public static DbContextOptionsBuilder UseLaravelPolymorphicRelationships(this DbContextOptionsBuilder optionsBuilder)
    {
        return optionsBuilder.UsePolymorphicRelationships();
    }

    public static DbContextOptionsBuilder<TContext> UseLaravelPolymorphicRelationships<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext
    {
        return optionsBuilder.UsePolymorphicRelationships();
    }
}



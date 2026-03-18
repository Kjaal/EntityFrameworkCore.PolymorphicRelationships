using EntityFrameworkCore.PolymorphicRelationships.Infrastructure;
using EntityFrameworkCore.PolymorphicRelationships.Infrastructure.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EntityFrameworkCore.PolymorphicRelationships;

public static class DbContextOptionsBuilderExtensions
{
    private static readonly PolymorphicQueryExpressionInterceptor QueryExpressionInterceptor = new();
    private static readonly PolymorphicNavigationSyncInterceptor NavigationSyncInterceptor = new();
    private static readonly PolymorphicCascadeDeleteInterceptor CascadeDeleteInterceptor = new();
    private static readonly PolymorphicIntegrityValidationInterceptor IntegrityValidationInterceptor = new();

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

        var configuredInterceptors = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()?.Interceptors
            ?? Enumerable.Empty<IInterceptor>();
        var interceptorsToAdd = new List<IInterceptor>(4);

        if (!configuredInterceptors.OfType<PolymorphicQueryExpressionInterceptor>().Any())
        {
            interceptorsToAdd.Add(QueryExpressionInterceptor);
        }

        if (!configuredInterceptors.OfType<PolymorphicNavigationSyncInterceptor>().Any())
        {
            interceptorsToAdd.Add(NavigationSyncInterceptor);
        }

        if (!configuredInterceptors.OfType<PolymorphicCascadeDeleteInterceptor>().Any())
        {
            interceptorsToAdd.Add(CascadeDeleteInterceptor);
        }

        if (!configuredInterceptors.OfType<PolymorphicIntegrityValidationInterceptor>().Any())
        {
            interceptorsToAdd.Add(IntegrityValidationInterceptor);
        }

        if (interceptorsToAdd.Count > 0)
        {
            optionsBuilder.AddInterceptors(interceptorsToAdd);
        }

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



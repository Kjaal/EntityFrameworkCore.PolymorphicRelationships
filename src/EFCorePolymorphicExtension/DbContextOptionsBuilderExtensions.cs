using EFCorePolymorphicExtension.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EFCorePolymorphicExtension;

public static class DbContextOptionsBuilderExtensions
{
    public static DbContextOptionsBuilder UsePolymorphicRelationships(this DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        optionsBuilder.AddInterceptors(new PolymorphicCascadeDeleteInterceptor());
        return optionsBuilder;
    }

    public static DbContextOptionsBuilder<TContext> UsePolymorphicRelationships<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext
    {
        UsePolymorphicRelationships((DbContextOptionsBuilder)optionsBuilder);
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


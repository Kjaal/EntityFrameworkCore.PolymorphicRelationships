using System.Linq.Expressions;
using EntityFrameworkCore.PolymorphicRelationships.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.PolymorphicRelationships;

public static class MorphIncludeExtensions
{
    public static MorphIncludeQuery<TEntity> IncludeMorph<TEntity, TProperty>(
        this DbSet<TEntity> dbSet,
        Expression<Func<TEntity, TProperty>> navigationExpression)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(dbSet);
        ArgumentNullException.ThrowIfNull(navigationExpression);

        var dbContext = ((IInfrastructure<IServiceProvider>)dbSet).Instance.GetRequiredService<ICurrentDbContext>().Context;
        return new MorphIncludeQuery<TEntity>(dbContext, dbSet, new[] { ExpressionHelpers.GetPropertyName(navigationExpression) });
    }
}

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
        return IncludeMorph(dbSet, navigationExpression, configure: null);
    }

    public static MorphIncludeQuery<TEntity> IncludeMorph<TEntity, TProperty>(
        this DbSet<TEntity> dbSet,
        Expression<Func<TEntity, TProperty>> navigationExpression,
        Action<MorphIncludePlan>? configure)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(dbSet);
        ArgumentNullException.ThrowIfNull(navigationExpression);

        var dbContext = ((IInfrastructure<IServiceProvider>)dbSet).Instance.GetRequiredService<ICurrentDbContext>().Context;
        var plan = configure is null ? null : BuildPlan(configure);
        return new MorphIncludeQuery<TEntity>(
            dbContext,
            dbSet,
            new[] { new MorphIncludeQuery<TEntity>.MorphIncludeRequest(ExpressionHelpers.GetPropertyName(navigationExpression), plan) });
    }

    private static MorphIncludePlan BuildPlan(Action<MorphIncludePlan> configure)
    {
        var plan = new MorphIncludePlan();
        configure(plan);
        return plan;
    }
}

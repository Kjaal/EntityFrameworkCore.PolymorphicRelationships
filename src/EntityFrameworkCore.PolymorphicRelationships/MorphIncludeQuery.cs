using System.Linq.Expressions;
using EntityFrameworkCore.PolymorphicRelationships.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.PolymorphicRelationships;

public sealed class MorphIncludeQuery<TEntity>
    where TEntity : class
{
    private readonly DbContext _dbContext;
    private readonly IQueryable<TEntity> _query;
    private readonly List<string> _propertyNames;

    internal MorphIncludeQuery(DbContext dbContext, IQueryable<TEntity> query, IEnumerable<string> propertyNames)
    {
        _dbContext = dbContext;
        _query = query;
        _propertyNames = propertyNames.ToList();
    }

    public MorphIncludeQuery<TEntity> IncludeMorph<TProperty>(Expression<Func<TEntity, TProperty>> navigationExpression)
    {
        ArgumentNullException.ThrowIfNull(navigationExpression);
        _propertyNames.Add(ExpressionHelpers.GetPropertyName(navigationExpression));
        return this;
    }

    public MorphIncludeQuery<TEntity> Where(Expression<Func<TEntity, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new MorphIncludeQuery<TEntity>(_dbContext, _query.Where(predicate), _propertyNames);
    }

    public MorphIncludeQuery<TEntity> AsNoTracking()
    {
        return new MorphIncludeQuery<TEntity>(_dbContext, _query.AsNoTracking(), _propertyNames);
    }

    public async Task<List<TEntity>> ToListAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _query.ToListAsync(cancellationToken);
        await ApplyIncludesAsync(entities, cancellationToken);
        return entities;
    }

    public async Task<TEntity> SingleAsync(CancellationToken cancellationToken = default)
    {
        var entity = await _query.SingleAsync(cancellationToken);
        await ApplyIncludesAsync(new[] { entity }, cancellationToken);
        return entity;
    }

    public async Task<TEntity?> SingleOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        var entity = await _query.SingleOrDefaultAsync(cancellationToken);
        if (entity is not null)
        {
            await ApplyIncludesAsync(new[] { entity }, cancellationToken);
        }

        return entity;
    }

    public async Task<TEntity> FirstAsync(CancellationToken cancellationToken = default)
    {
        var entity = await _query.FirstAsync(cancellationToken);
        await ApplyIncludesAsync(new[] { entity }, cancellationToken);
        return entity;
    }

    public async Task<TEntity?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        var entity = await _query.FirstOrDefaultAsync(cancellationToken);
        if (entity is not null)
        {
            await ApplyIncludesAsync(new[] { entity }, cancellationToken);
        }

        return entity;
    }

    private async Task ApplyIncludesAsync(IReadOnlyList<TEntity> entities, CancellationToken cancellationToken)
    {
        if (entities.Count == 0 || _propertyNames.Count == 0)
        {
            return;
        }

        foreach (var propertyName in _propertyNames.Distinct(StringComparer.Ordinal))
        {
            await MorphIncludeLoader.ApplyAsync(_dbContext, entities, propertyName, cancellationToken);
        }
    }
}

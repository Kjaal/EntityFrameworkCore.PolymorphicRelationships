using System.Linq.Expressions;
using EntityFrameworkCore.PolymorphicRelationships.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.PolymorphicRelationships;

public sealed class MorphIncludeQuery<TEntity>
    where TEntity : class
{
    private readonly DbContext _dbContext;
    private readonly IQueryable<TEntity> _query;
    private readonly List<MorphIncludeRequest> _requests;
    private readonly bool _asNoTracking;

    internal MorphIncludeQuery(DbContext dbContext, IQueryable<TEntity> query, IEnumerable<MorphIncludeRequest> requests, bool asNoTracking = false)
    {
        _dbContext = dbContext;
        _query = query;
        _requests = requests.ToList();
        _asNoTracking = asNoTracking;
    }

    public MorphIncludeQuery<TEntity> IncludeMorph<TProperty>(Expression<Func<TEntity, TProperty>> navigationExpression)
    {
        ArgumentNullException.ThrowIfNull(navigationExpression);
        return IncludeMorph(navigationExpression, configure: null);
    }

    public MorphIncludeQuery<TEntity> IncludeMorph<TProperty>(Expression<Func<TEntity, TProperty>> navigationExpression, Action<MorphIncludePlan>? configure)
    {
        ArgumentNullException.ThrowIfNull(navigationExpression);
        var plan = CreatePlan(configure);
        var requests = _requests.ToList();
        requests.Add(new MorphIncludeRequest(ExpressionHelpers.GetPropertyName(navigationExpression), plan));
        return new MorphIncludeQuery<TEntity>(_dbContext, _query, requests, _asNoTracking);
    }

    public MorphIncludeQuery<TEntity> Where(Expression<Func<TEntity, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new MorphIncludeQuery<TEntity>(_dbContext, _query.Where(predicate), _requests, _asNoTracking);
    }

    public MorphIncludeQuery<TEntity> AsNoTracking()
    {
        return new MorphIncludeQuery<TEntity>(_dbContext, _query.AsNoTracking(), _requests, asNoTracking: true);
    }

    public MorphIncludeQuery<TEntity> OrderBy<TProperty>(Expression<Func<TEntity, TProperty>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return new MorphIncludeQuery<TEntity>(_dbContext, _query.OrderBy(keySelector), _requests, _asNoTracking);
    }

    public MorphIncludeQuery<TEntity> OrderByDescending<TProperty>(Expression<Func<TEntity, TProperty>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return new MorphIncludeQuery<TEntity>(_dbContext, _query.OrderByDescending(keySelector), _requests, _asNoTracking);
    }

    public MorphIncludeQuery<TEntity> Skip(int count)
    {
        return new MorphIncludeQuery<TEntity>(_dbContext, _query.Skip(count), _requests, _asNoTracking);
    }

    public MorphIncludeQuery<TEntity> Take(int count)
    {
        return new MorphIncludeQuery<TEntity>(_dbContext, _query.Take(count), _requests, _asNoTracking);
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

    public async Task<TEntity[]> ToArrayAsync(CancellationToken cancellationToken = default)
    {
        var entities = await ToListAsync(cancellationToken);
        return entities.ToArray();
    }

    public async Task<List<TResult>> SelectAsync<TResult>(Func<TEntity, TResult> selector, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var entities = await ToListAsync(cancellationToken);
        return entities.Select(selector).ToList();
    }

    private async Task ApplyIncludesAsync(IReadOnlyList<TEntity> entities, CancellationToken cancellationToken)
    {
        if (entities.Count == 0 || _requests.Count == 0)
        {
            return;
        }

        foreach (var request in _requests
                     .GroupBy(item => item.PropertyName, StringComparer.Ordinal)
                     .Select(group => group.Last()))
        {
            await MorphIncludeLoader.ApplyAsync(_dbContext, entities, request, _asNoTracking, cancellationToken);
        }
    }

    private static MorphIncludePlan? CreatePlan(Action<MorphIncludePlan>? configure)
    {
        if (configure is null)
        {
            return null;
        }

        var plan = new MorphIncludePlan();
        configure(plan);
        return plan;
    }

    internal sealed record MorphIncludeRequest(string PropertyName, MorphIncludePlan? Plan);
}

using EntityFrameworkCore.PolymorphicRelationships.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.PolymorphicRelationships;

public sealed class MorphBatchLoadPlan<TDependent>
    where TDependent : class
{
    private readonly List<IMorphBatchLoadRegistration> _registrations = new();
    private readonly bool _asNoTracking;

    internal MorphBatchLoadPlan(bool asNoTracking = false)
    {
        _asNoTracking = asNoTracking;
    }

    public MorphBatchLoadPlan<TDependent> For<TPrincipal>(Func<IQueryable<TPrincipal>, IQueryable<TPrincipal>> queryTransform)
        where TPrincipal : class
    {
        ArgumentNullException.ThrowIfNull(queryTransform);
        _registrations.Add(new MorphBatchLoadRegistration<TPrincipal>(queryTransform, _asNoTracking));
        return this;
    }

    internal IMorphBatchLoadRegistration? FindRegistration(Type principalType)
    {
        return _registrations.FirstOrDefault(registration => registration.CanLoad(principalType));
    }

    internal interface IMorphBatchLoadRegistration
    {
        bool CanLoad(Type principalType);

        Task<IReadOnlyList<object>> LoadAsync(DbContext dbContext, string propertyName, Type propertyType, IEnumerable<object> values, CancellationToken cancellationToken);
    }

    private sealed class MorphBatchLoadRegistration<TPrincipal>(Func<IQueryable<TPrincipal>, IQueryable<TPrincipal>> queryTransform, bool asNoTracking) : IMorphBatchLoadRegistration
        where TPrincipal : class
    {
        public bool CanLoad(Type principalType)
        {
            return typeof(TPrincipal).IsAssignableFrom(principalType);
        }

        public async Task<IReadOnlyList<object>> LoadAsync(DbContext dbContext, string propertyName, Type propertyType, IEnumerable<object> values, CancellationToken cancellationToken)
        {
            var query = dbContext.Set<TPrincipal>().AsQueryable();
            if (asNoTracking)
            {
                query = query.AsNoTracking();
            }

            query = queryTransform(query);
            return await PolymorphicQueryableLoader.ListByPropertyValuesAsync(query, propertyName, propertyType, values, cancellationToken);
        }
    }
}


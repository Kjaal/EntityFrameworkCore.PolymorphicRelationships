using EntityFrameworkCore.PolymorphicRelationships.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.PolymorphicRelationships;

public sealed class MorphBatchLoadPlan<TDependent>
    where TDependent : class
{
    private readonly List<IMorphBatchLoadRegistration> _registrations = new();

    public MorphBatchLoadPlan<TDependent> For<TPrincipal>(Func<IQueryable<TPrincipal>, IQueryable<TPrincipal>> queryTransform)
        where TPrincipal : class
    {
        ArgumentNullException.ThrowIfNull(queryTransform);
        _registrations.Add(new MorphBatchLoadRegistration<TPrincipal>(queryTransform));
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

    private sealed class MorphBatchLoadRegistration<TPrincipal>(Func<IQueryable<TPrincipal>, IQueryable<TPrincipal>> queryTransform) : IMorphBatchLoadRegistration
        where TPrincipal : class
    {
        public bool CanLoad(Type principalType)
        {
            return typeof(TPrincipal).IsAssignableFrom(principalType);
        }

        public async Task<IReadOnlyList<object>> LoadAsync(DbContext dbContext, string propertyName, Type propertyType, IEnumerable<object> values, CancellationToken cancellationToken)
        {
            var query = queryTransform(dbContext.Set<TPrincipal>().AsQueryable());
            return await PolymorphicQueryableLoader.ListByPropertyValuesAsync(query, propertyName, propertyType, values, cancellationToken);
        }
    }
}


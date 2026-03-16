namespace EntityFrameworkCore.PolymorphicRelationships;

public sealed class MorphIncludePlan
{
    private readonly Dictionary<Type, Delegate> _registrations = new();

    public MorphIncludePlan For<TRelated>(Func<IQueryable<TRelated>, IQueryable<TRelated>> queryTransform)
        where TRelated : class
    {
        ArgumentNullException.ThrowIfNull(queryTransform);
        _registrations[typeof(TRelated)] = queryTransform;
        return this;
    }

    internal Func<IQueryable<TRelated>, IQueryable<TRelated>>? GetQueryTransform<TRelated>()
        where TRelated : class
    {
        if (_registrations.TryGetValue(typeof(TRelated), out var registration))
        {
            return (Func<IQueryable<TRelated>, IQueryable<TRelated>>)registration;
        }

        return null;
    }

    internal IEnumerable<KeyValuePair<Type, Delegate>> GetRegistrations()
    {
        return _registrations;
    }
}

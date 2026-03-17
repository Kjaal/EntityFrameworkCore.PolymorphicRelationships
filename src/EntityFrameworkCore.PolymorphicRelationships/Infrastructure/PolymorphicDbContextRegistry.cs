using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

internal static class PolymorphicDbContextRegistry
{
    private static readonly ConcurrentDictionary<Guid, Registration> Contexts = new();

    public static Guid Register(DbContext dbContext)
    {
        var id = dbContext.ContextId.InstanceId;
        Contexts[id] = new Registration(dbContext.GetService<IDbContextOptions>(), CreateFactory(dbContext));
        return id;
    }

    public static DbContext CreateCompanionContext(Guid contextId)
    {
        if (Contexts.TryGetValue(contextId, out var registration))
        {
            return registration.Factory(registration.Options);
        }

        throw new InvalidOperationException($"No active DbContext was registered for polymorphic projection id '{contextId}'.");
    }

    private static Func<IDbContextOptions, DbContext> CreateFactory(DbContext dbContext)
    {
        var dbContextType = dbContext.GetType();
        var constructor = dbContextType.GetConstructors()
            .SingleOrDefault(ctor =>
            {
                var parameters = ctor.GetParameters();
                return parameters.Length == 1
                    && typeof(DbContextOptions).IsAssignableFrom(parameters[0].ParameterType);
            })
            ?? throw new InvalidOperationException($"DbContext '{dbContextType.Name}' must expose a single-parameter constructor accepting DbContextOptions for native polymorphic Select projections.");

        var options = Expression.Parameter(typeof(IDbContextOptions), "options");
        var body = Expression.New(constructor, Expression.Convert(options, constructor.GetParameters()[0].ParameterType));
        var cast = Expression.Convert(body, typeof(DbContext));
        return Expression.Lambda<Func<IDbContextOptions, DbContext>>(cast, options).Compile();
    }

    private sealed record Registration(IDbContextOptions Options, Func<IDbContextOptions, DbContext> Factory);
}

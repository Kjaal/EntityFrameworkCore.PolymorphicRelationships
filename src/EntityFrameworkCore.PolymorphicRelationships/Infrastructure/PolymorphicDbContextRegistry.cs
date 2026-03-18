using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

internal static class PolymorphicDbContextRegistry
{
    private static readonly ConcurrentDictionary<Guid, WeakReference<DbContext>> Contexts = new();
    private static readonly ConditionalWeakTable<DbContext, Registration> Registrations = new();

    public static Guid Register(DbContext dbContext)
    {
        CleanupDeadEntries();

        var id = dbContext.ContextId.InstanceId;
        Contexts[id] = new WeakReference<DbContext>(dbContext);
        Registrations.Remove(dbContext);
        Registrations.Add(dbContext, new Registration(dbContext.GetService<IDbContextOptions>(), CreateFactory(dbContext)));
        return id;
    }

    public static DbContext CreateCompanionContext(Guid contextId)
    {
        CleanupDeadEntries();

        if (Contexts.TryGetValue(contextId, out var reference)
            && reference.TryGetTarget(out var dbContext)
            && Registrations.TryGetValue(dbContext, out var registration))
        {
            return registration.Factory(registration.Options);
        }

        Contexts.TryRemove(contextId, out _);

        throw new InvalidOperationException($"No active DbContext was registered for polymorphic projection id '{contextId}'.");
    }

    private static void CleanupDeadEntries()
    {
        foreach (var entry in Contexts)
        {
            if (!entry.Value.TryGetTarget(out _))
            {
                Contexts.TryRemove(entry.Key, out _);
            }
        }
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

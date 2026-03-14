using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

internal static class PolymorphicQueryExecutor
{
    private static readonly MethodInfo SingleOrDefaultByPropertyMethod = typeof(PolymorphicQueryExecutor)
        .GetMethod(nameof(SingleOrDefaultByPropertyCore), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo SingleOrDefaultByPropertyAsyncMethod = typeof(PolymorphicQueryExecutor)
        .GetMethod(nameof(SingleOrDefaultByPropertyCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo SingleOrDefaultByTwoPropertiesMethod = typeof(PolymorphicQueryExecutor)
        .GetMethod(nameof(SingleOrDefaultByTwoPropertiesCore), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo SingleOrDefaultByTwoPropertiesAsyncMethod = typeof(PolymorphicQueryExecutor)
        .GetMethod(nameof(SingleOrDefaultByTwoPropertiesCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo FirstOrDefaultByTwoPropertiesOrderedMethod = typeof(PolymorphicQueryExecutor)
        .GetMethod(nameof(FirstOrDefaultByTwoPropertiesOrderedCore), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo FirstOrDefaultByTwoPropertiesOrderedAsyncMethod = typeof(PolymorphicQueryExecutor)
        .GetMethod(nameof(FirstOrDefaultByTwoPropertiesOrderedCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ListByPropertyMethod = typeof(PolymorphicQueryExecutor)
        .GetMethod(nameof(ListByPropertyCore), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ListByPropertyAsyncMethod = typeof(PolymorphicQueryExecutor)
        .GetMethod(nameof(ListByPropertyCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ListByTwoPropertiesMethod = typeof(PolymorphicQueryExecutor)
        .GetMethod(nameof(ListByTwoPropertiesCore), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ListByTwoPropertiesAsyncMethod = typeof(PolymorphicQueryExecutor)
        .GetMethod(nameof(ListByTwoPropertiesCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ListByPropertyValuesMethod = typeof(PolymorphicQueryExecutor)
        .GetMethod(nameof(ListByPropertyValuesCore), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ListByPropertyValuesAsyncMethod = typeof(PolymorphicQueryExecutor)
        .GetMethod(
            nameof(ListByPropertyValuesCoreAsync),
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(DbContext), typeof(string), typeof(object[]), typeof(CancellationToken) },
            modifiers: null)!;

    private static readonly MethodInfo ListByPropertyValuesAsyncNoTrackingMethod = typeof(PolymorphicQueryExecutor)
        .GetMethod(
            nameof(ListByPropertyValuesCoreAsync),
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(DbContext), typeof(string), typeof(object[]), typeof(CancellationToken), typeof(bool) },
            modifiers: null)!;

    public static object? SingleOrDefaultByProperty(DbContext dbContext, Type entityType, string propertyName, Type propertyType, object propertyValue)
    {
        return SingleOrDefaultByPropertyMethod
            .MakeGenericMethod(entityType, propertyType)
            .Invoke(null, new[] { dbContext, propertyName, propertyValue });
    }

    public static Task<object?> SingleOrDefaultByPropertyAsync(DbContext dbContext, Type entityType, string propertyName, Type propertyType, object propertyValue, CancellationToken cancellationToken)
    {
        return (Task<object?>)SingleOrDefaultByPropertyAsyncMethod
            .MakeGenericMethod(entityType, propertyType)
            .Invoke(null, new object?[] { dbContext, propertyName, propertyValue, cancellationToken })!;
    }

    public static object? SingleOrDefaultByTwoProperties(
        DbContext dbContext,
        Type entityType,
        string firstPropertyName,
        Type firstPropertyType,
        object firstPropertyValue,
        string secondPropertyName,
        Type secondPropertyType,
        object secondPropertyValue)
    {
        return SingleOrDefaultByTwoPropertiesMethod
            .MakeGenericMethod(entityType, firstPropertyType, secondPropertyType)
            .Invoke(null, new[] { dbContext, firstPropertyName, firstPropertyValue, secondPropertyName, secondPropertyValue });
    }

    public static Task<object?> SingleOrDefaultByTwoPropertiesAsync(
        DbContext dbContext,
        Type entityType,
        string firstPropertyName,
        Type firstPropertyType,
        object firstPropertyValue,
        string secondPropertyName,
        Type secondPropertyType,
        object secondPropertyValue,
        CancellationToken cancellationToken)
    {
        return (Task<object?>)SingleOrDefaultByTwoPropertiesAsyncMethod
            .MakeGenericMethod(entityType, firstPropertyType, secondPropertyType)
            .Invoke(null, new object?[] { dbContext, firstPropertyName, firstPropertyValue, secondPropertyName, secondPropertyValue, cancellationToken })!;
    }

    public static object? FirstOrDefaultByTwoPropertiesOrdered(
        DbContext dbContext,
        Type entityType,
        string firstPropertyName,
        Type firstPropertyType,
        object firstPropertyValue,
        string secondPropertyName,
        Type secondPropertyType,
        object secondPropertyValue,
        string orderPropertyName,
        Type orderPropertyType,
        bool descending)
    {
        return FirstOrDefaultByTwoPropertiesOrderedMethod
            .MakeGenericMethod(entityType, firstPropertyType, secondPropertyType, orderPropertyType)
            .Invoke(null, new object?[]
            {
                dbContext,
                firstPropertyName,
                firstPropertyValue,
                secondPropertyName,
                secondPropertyValue,
                orderPropertyName,
                descending,
            });
    }

    public static Task<object?> FirstOrDefaultByTwoPropertiesOrderedAsync(
        DbContext dbContext,
        Type entityType,
        string firstPropertyName,
        Type firstPropertyType,
        object firstPropertyValue,
        string secondPropertyName,
        Type secondPropertyType,
        object secondPropertyValue,
        string orderPropertyName,
        Type orderPropertyType,
        bool descending,
        CancellationToken cancellationToken)
    {
        return (Task<object?>)FirstOrDefaultByTwoPropertiesOrderedAsyncMethod
            .MakeGenericMethod(entityType, firstPropertyType, secondPropertyType, orderPropertyType)
            .Invoke(null, new object?[]
            {
                dbContext,
                firstPropertyName,
                firstPropertyValue,
                secondPropertyName,
                secondPropertyValue,
                orderPropertyName,
                descending,
                cancellationToken,
            })!;
    }

    public static IReadOnlyList<object> ListByProperty(
        DbContext dbContext,
        Type entityType,
        string propertyName,
        Type propertyType,
        object propertyValue)
    {
        return (IReadOnlyList<object>)ListByPropertyMethod
            .MakeGenericMethod(entityType, propertyType)
            .Invoke(null, new[] { dbContext, propertyName, propertyValue })!;
    }

    public static Task<IReadOnlyList<object>> ListByPropertyAsync(
        DbContext dbContext,
        Type entityType,
        string propertyName,
        Type propertyType,
        object propertyValue,
        CancellationToken cancellationToken)
    {
        return (Task<IReadOnlyList<object>>)ListByPropertyAsyncMethod
            .MakeGenericMethod(entityType, propertyType)
            .Invoke(null, new object?[] { dbContext, propertyName, propertyValue, cancellationToken })!;
    }

    public static IReadOnlyList<object> ListByTwoProperties(
        DbContext dbContext,
        Type entityType,
        string firstPropertyName,
        Type firstPropertyType,
        object firstPropertyValue,
        string secondPropertyName,
        Type secondPropertyType,
        object secondPropertyValue)
    {
        return (IReadOnlyList<object>)ListByTwoPropertiesMethod
            .MakeGenericMethod(entityType, firstPropertyType, secondPropertyType)
            .Invoke(null, new[] { dbContext, firstPropertyName, firstPropertyValue, secondPropertyName, secondPropertyValue })!;
    }

    public static Task<IReadOnlyList<object>> ListByTwoPropertiesAsync(
        DbContext dbContext,
        Type entityType,
        string firstPropertyName,
        Type firstPropertyType,
        object firstPropertyValue,
        string secondPropertyName,
        Type secondPropertyType,
        object secondPropertyValue,
        CancellationToken cancellationToken)
    {
        return (Task<IReadOnlyList<object>>)ListByTwoPropertiesAsyncMethod
            .MakeGenericMethod(entityType, firstPropertyType, secondPropertyType)
            .Invoke(null, new object?[] { dbContext, firstPropertyName, firstPropertyValue, secondPropertyName, secondPropertyValue, cancellationToken })!;
    }

    public static IReadOnlyList<object> ListByPropertyValues(
        DbContext dbContext,
        Type entityType,
        string propertyName,
        Type propertyType,
        IEnumerable<object> propertyValues)
    {
        return (IReadOnlyList<object>)ListByPropertyValuesMethod
            .MakeGenericMethod(entityType, propertyType)
            .Invoke(null, new object?[] { dbContext, propertyName, propertyValues.ToArray() })!;
    }

    public static Task<IReadOnlyList<object>> ListByPropertyValuesAsync(
        DbContext dbContext,
        Type entityType,
        string propertyName,
        Type propertyType,
        IEnumerable<object> propertyValues,
        CancellationToken cancellationToken)
    {
        return (Task<IReadOnlyList<object>>)ListByPropertyValuesAsyncMethod
            .MakeGenericMethod(entityType, propertyType)
            .Invoke(null, new object?[] { dbContext, propertyName, propertyValues.ToArray(), cancellationToken })!;
    }

    public static Task<IReadOnlyList<object>> ListByPropertyValuesAsync(
        DbContext dbContext,
        Type entityType,
        string propertyName,
        Type propertyType,
        IEnumerable<object> propertyValues,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        if (!asNoTracking)
        {
            return ListByPropertyValuesAsync(dbContext, entityType, propertyName, propertyType, propertyValues, cancellationToken);
        }

        return (Task<IReadOnlyList<object>>)ListByPropertyValuesAsyncNoTrackingMethod
            .MakeGenericMethod(entityType, propertyType)
            .Invoke(null, new object?[] { dbContext, propertyName, propertyValues.ToArray(), cancellationToken, true })!;
    }

    public static Task<IReadOnlyList<object>> ListByPropertyValuesUntrackedAsync(
        DbContext dbContext,
        Type entityType,
        string propertyName,
        Type propertyType,
        IEnumerable<object> propertyValues,
        CancellationToken cancellationToken)
    {
        return (Task<IReadOnlyList<object>>)ListByPropertyValuesAsyncNoTrackingMethod
            .MakeGenericMethod(entityType, propertyType)
            .Invoke(null, new object?[] { dbContext, propertyName, propertyValues.ToArray(), cancellationToken, true })!;
    }

    private static object? SingleOrDefaultByPropertyCore<TEntity, TProperty>(DbContext dbContext, string propertyName, object propertyValue)
        where TEntity : class
    {
        return PolymorphicQueryableLoader
            .WherePropertyEquals(dbContext.Set<TEntity>(), propertyName, typeof(TProperty), propertyValue)
            .SingleOrDefault();
    }

    private static async Task<object?> SingleOrDefaultByPropertyCoreAsync<TEntity, TProperty>(DbContext dbContext, string propertyName, object propertyValue, CancellationToken cancellationToken)
        where TEntity : class
    {
        return await PolymorphicQueryableLoader
            .WherePropertyEquals(dbContext.Set<TEntity>(), propertyName, typeof(TProperty), propertyValue)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static object? SingleOrDefaultByTwoPropertiesCore<TEntity, TPropertyOne, TPropertyTwo>(
        DbContext dbContext,
        string firstPropertyName,
        object firstPropertyValue,
        string secondPropertyName,
        object secondPropertyValue)
        where TEntity : class
    {
        var query = FilterByTwoProperties<TEntity, TPropertyOne, TPropertyTwo>(dbContext, firstPropertyName, firstPropertyValue, secondPropertyName, secondPropertyValue);
        return query.SingleOrDefault();
    }

    private static async Task<object?> SingleOrDefaultByTwoPropertiesCoreAsync<TEntity, TPropertyOne, TPropertyTwo>(
        DbContext dbContext,
        string firstPropertyName,
        object firstPropertyValue,
        string secondPropertyName,
        object secondPropertyValue,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var query = FilterByTwoProperties<TEntity, TPropertyOne, TPropertyTwo>(dbContext, firstPropertyName, firstPropertyValue, secondPropertyName, secondPropertyValue);
        return await query.SingleOrDefaultAsync(cancellationToken);
    }

    private static object? FirstOrDefaultByTwoPropertiesOrderedCore<TEntity, TPropertyOne, TPropertyTwo, TOrder>(
        DbContext dbContext,
        string firstPropertyName,
        object firstPropertyValue,
        string secondPropertyName,
        object secondPropertyValue,
        string orderPropertyName,
        bool descending)
        where TEntity : class
    {
        var query = FilterByTwoProperties<TEntity, TPropertyOne, TPropertyTwo>(dbContext, firstPropertyName, firstPropertyValue, secondPropertyName, secondPropertyValue);
        return PolymorphicQueryableLoader.OrderByProperty(query, orderPropertyName, typeof(TOrder), descending).FirstOrDefault();
    }

    private static async Task<object?> FirstOrDefaultByTwoPropertiesOrderedCoreAsync<TEntity, TPropertyOne, TPropertyTwo, TOrder>(
        DbContext dbContext,
        string firstPropertyName,
        object firstPropertyValue,
        string secondPropertyName,
        object secondPropertyValue,
        string orderPropertyName,
        bool descending,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var query = FilterByTwoProperties<TEntity, TPropertyOne, TPropertyTwo>(dbContext, firstPropertyName, firstPropertyValue, secondPropertyName, secondPropertyValue);
        return await PolymorphicQueryableLoader.OrderByProperty(query, orderPropertyName, typeof(TOrder), descending).FirstOrDefaultAsync(cancellationToken);
    }

    private static IReadOnlyList<object> ListByPropertyCore<TEntity, TProperty>(DbContext dbContext, string propertyName, object propertyValue)
        where TEntity : class
    {
        return PolymorphicQueryableLoader.WherePropertyEquals(dbContext.Set<TEntity>(), propertyName, typeof(TProperty), propertyValue)
            .Cast<object>()
            .ToList();
    }

    private static async Task<IReadOnlyList<object>> ListByPropertyCoreAsync<TEntity, TProperty>(DbContext dbContext, string propertyName, object propertyValue, CancellationToken cancellationToken)
        where TEntity : class
    {
        var entities = await PolymorphicQueryableLoader.WherePropertyEquals(dbContext.Set<TEntity>(), propertyName, typeof(TProperty), propertyValue)
            .ToListAsync(cancellationToken);

        return entities.Cast<object>().ToList();
    }

    private static IReadOnlyList<object> ListByTwoPropertiesCore<TEntity, TPropertyOne, TPropertyTwo>(
        DbContext dbContext,
        string firstPropertyName,
        object firstPropertyValue,
        string secondPropertyName,
        object secondPropertyValue)
        where TEntity : class
    {
        var query = FilterByTwoProperties<TEntity, TPropertyOne, TPropertyTwo>(dbContext, firstPropertyName, firstPropertyValue, secondPropertyName, secondPropertyValue);
        return query.Cast<object>().ToList();
    }

    private static async Task<IReadOnlyList<object>> ListByTwoPropertiesCoreAsync<TEntity, TPropertyOne, TPropertyTwo>(
        DbContext dbContext,
        string firstPropertyName,
        object firstPropertyValue,
        string secondPropertyName,
        object secondPropertyValue,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var query = FilterByTwoProperties<TEntity, TPropertyOne, TPropertyTwo>(dbContext, firstPropertyName, firstPropertyValue, secondPropertyName, secondPropertyValue);
        var entities = await query.ToListAsync(cancellationToken);
        return entities.Cast<object>().ToList();
    }

    private static IReadOnlyList<object> ListByPropertyValuesCore<TEntity, TProperty>(DbContext dbContext, string propertyName, object[] propertyValues)
        where TEntity : class
    {
        if (propertyValues.Length == 0)
        {
            return Array.Empty<object>();
        }

        return PolymorphicQueryableLoader.WherePropertyIn(dbContext.Set<TEntity>(), propertyName, typeof(TProperty), propertyValues)
            .Cast<object>()
            .ToList();
    }

    private static async Task<IReadOnlyList<object>> ListByPropertyValuesCoreAsync<TEntity, TProperty>(DbContext dbContext, string propertyName, object[] propertyValues, CancellationToken cancellationToken)
        where TEntity : class
    {
        return await ListByPropertyValuesCoreAsync<TEntity, TProperty>(dbContext, propertyName, propertyValues, cancellationToken, asNoTracking: false);
    }

    private static async Task<IReadOnlyList<object>> ListByPropertyValuesCoreAsync<TEntity, TProperty>(DbContext dbContext, string propertyName, object[] propertyValues, CancellationToken cancellationToken, bool asNoTracking)
        where TEntity : class
    {
        if (propertyValues.Length == 0)
        {
            return Array.Empty<object>();
        }

        var query = asNoTracking ? dbContext.Set<TEntity>().AsNoTracking() : dbContext.Set<TEntity>().AsQueryable();
        var entities = await PolymorphicQueryableLoader.WherePropertyIn(query, propertyName, typeof(TProperty), propertyValues)
            .ToListAsync(cancellationToken);

        return entities.Cast<object>().ToList();
    }

    private static IQueryable<TEntity> FilterByTwoProperties<TEntity, TPropertyOne, TPropertyTwo>(
        DbContext dbContext,
        string firstPropertyName,
        object firstPropertyValue,
        string secondPropertyName,
        object secondPropertyValue)
        where TEntity : class
    {
        var query = PolymorphicQueryableLoader.WherePropertyEquals(
            dbContext.Set<TEntity>(),
            firstPropertyName,
            typeof(TPropertyOne),
            firstPropertyValue);

        return PolymorphicQueryableLoader.WherePropertyEquals(
            query,
            secondPropertyName,
            typeof(TPropertyTwo),
            secondPropertyValue);
    }
}



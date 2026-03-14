using System.Linq.Expressions;
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
        .GetMethod(nameof(ListByPropertyValuesCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

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

    private static object? SingleOrDefaultByPropertyCore<TEntity, TProperty>(DbContext dbContext, string propertyName, object propertyValue)
        where TEntity : class
    {
        var typedValue = (TProperty?)PolymorphicValueConverter.ConvertForAssignment(propertyValue, typeof(TProperty));
        var predicate = BuildEqualsExpression<TEntity, TProperty>(propertyName, typedValue);
        return dbContext.Set<TEntity>().SingleOrDefault(predicate);
    }

    private static async Task<object?> SingleOrDefaultByPropertyCoreAsync<TEntity, TProperty>(DbContext dbContext, string propertyName, object propertyValue, CancellationToken cancellationToken)
        where TEntity : class
    {
        var typedValue = (TProperty?)PolymorphicValueConverter.ConvertForAssignment(propertyValue, typeof(TProperty));
        var predicate = BuildEqualsExpression<TEntity, TProperty>(propertyName, typedValue);
        return await dbContext.Set<TEntity>().SingleOrDefaultAsync(predicate, cancellationToken);
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
        return ApplyOrdering<TEntity, TOrder>(query, orderPropertyName, descending).FirstOrDefault();
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
        return await ApplyOrdering<TEntity, TOrder>(query, orderPropertyName, descending).FirstOrDefaultAsync(cancellationToken);
    }

    private static IReadOnlyList<object> ListByPropertyCore<TEntity, TProperty>(DbContext dbContext, string propertyName, object propertyValue)
        where TEntity : class
    {
        var typedValue = (TProperty?)PolymorphicValueConverter.ConvertForAssignment(propertyValue, typeof(TProperty));
        return dbContext.Set<TEntity>()
            .Where(BuildEqualsExpression<TEntity, TProperty>(propertyName, typedValue))
            .Cast<object>()
            .ToList();
    }

    private static async Task<IReadOnlyList<object>> ListByPropertyCoreAsync<TEntity, TProperty>(DbContext dbContext, string propertyName, object propertyValue, CancellationToken cancellationToken)
        where TEntity : class
    {
        var typedValue = (TProperty?)PolymorphicValueConverter.ConvertForAssignment(propertyValue, typeof(TProperty));
        var entities = await dbContext.Set<TEntity>()
            .Where(BuildEqualsExpression<TEntity, TProperty>(propertyName, typedValue))
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

        return dbContext.Set<TEntity>()
            .Where(BuildContainsExpression<TEntity, TProperty>(propertyName, propertyValues))
            .Cast<object>()
            .ToList();
    }

    private static async Task<IReadOnlyList<object>> ListByPropertyValuesCoreAsync<TEntity, TProperty>(DbContext dbContext, string propertyName, object[] propertyValues, CancellationToken cancellationToken)
        where TEntity : class
    {
        if (propertyValues.Length == 0)
        {
            return Array.Empty<object>();
        }

        var entities = await dbContext.Set<TEntity>()
            .Where(BuildContainsExpression<TEntity, TProperty>(propertyName, propertyValues))
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
        var firstTypedValue = (TPropertyOne?)PolymorphicValueConverter.ConvertForAssignment(firstPropertyValue, typeof(TPropertyOne));
        var secondTypedValue = (TPropertyTwo?)PolymorphicValueConverter.ConvertForAssignment(secondPropertyValue, typeof(TPropertyTwo));

        return dbContext.Set<TEntity>()
            .Where(BuildEqualsExpression<TEntity, TPropertyOne>(firstPropertyName, firstTypedValue))
            .Where(BuildEqualsExpression<TEntity, TPropertyTwo>(secondPropertyName, secondTypedValue));
    }

    private static IOrderedQueryable<TEntity> ApplyOrdering<TEntity, TProperty>(IQueryable<TEntity> query, string propertyName, bool descending)
    {
        var orderExpression = BuildPropertyAccessExpression<TEntity, TProperty>(propertyName);
        return descending
            ? query.OrderByDescending(orderExpression)
            : query.OrderBy(orderExpression);
    }

    private static Expression<Func<TEntity, bool>> BuildEqualsExpression<TEntity, TProperty>(string propertyName, TProperty? propertyValue)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var property = Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            new[] { typeof(TProperty) },
            parameter,
            Expression.Constant(propertyName));

        var equals = Expression.Equal(property, PolymorphicValueConverter.BuildTypedConstantExpression(propertyValue, typeof(TProperty)));
        return Expression.Lambda<Func<TEntity, bool>>(equals, parameter);
    }

    private static Expression<Func<TEntity, bool>> BuildContainsExpression<TEntity, TProperty>(string propertyName, IEnumerable<object> propertyValues)
    {
        var typedValues = propertyValues
            .Select(value => (TProperty?)PolymorphicValueConverter.ConvertForAssignment(value, typeof(TProperty)))
            .Distinct()
            .ToArray();

        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var property = Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            new[] { typeof(TProperty) },
            parameter,
            Expression.Constant(propertyName));

        var contains = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            new[] { typeof(TProperty) },
            Expression.Constant(typedValues),
            property);

        return Expression.Lambda<Func<TEntity, bool>>(contains, parameter);
    }

    private static Expression<Func<TEntity, TProperty>> BuildPropertyAccessExpression<TEntity, TProperty>(string propertyName)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var property = Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            new[] { typeof(TProperty) },
            parameter,
            Expression.Constant(propertyName));

        return Expression.Lambda<Func<TEntity, TProperty>>(property, parameter);
    }
}



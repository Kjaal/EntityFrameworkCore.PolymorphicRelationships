using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

internal static class PolymorphicQueryableLoader
{
    private static readonly MethodInfo WherePropertyEqualsMethod = typeof(PolymorphicQueryableLoader)
        .GetMethod(nameof(WherePropertyEqualsCore), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo WherePropertyInMethod = typeof(PolymorphicQueryableLoader)
        .GetMethod(nameof(WherePropertyInCore), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo OrderByPropertyMethod = typeof(PolymorphicQueryableLoader)
        .GetMethod(nameof(OrderByPropertyCore), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static async Task<IReadOnlyList<object>> ListByPropertyValuesAsync<TEntity>(
        IQueryable<TEntity> query,
        string propertyName,
        Type propertyType,
        IEnumerable<object> values,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var convertedValues = values
            .Select(value => PolymorphicValueConverter.ConvertForAssignment(value, propertyType))
            .Where(value => value is not null)
            .Distinct()
            .ToArray();

        if (convertedValues.Length == 0)
        {
            return Array.Empty<object>();
        }

        var typedArray = Array.CreateInstance(propertyType, convertedValues.Length);
        for (var index = 0; index < convertedValues.Length; index++)
        {
            typedArray.SetValue(convertedValues[index], index);
        }

        var entities = await WherePropertyIn(query, propertyName, propertyType, convertedValues).ToListAsync(cancellationToken);
        return entities.Cast<object>().ToList();
    }

    public static IQueryable<TEntity> WherePropertyEquals<TEntity>(
        IQueryable<TEntity> query,
        string propertyName,
        Type propertyType,
        object? value)
        where TEntity : class
    {
        return (IQueryable<TEntity>)WherePropertyEqualsMethod
            .MakeGenericMethod(typeof(TEntity), propertyType)
            .Invoke(null, new object?[] { query, propertyName, value })!;
    }

    public static IQueryable<TEntity> WherePropertyIn<TEntity>(
        IQueryable<TEntity> query,
        string propertyName,
        Type propertyType,
        IEnumerable<object?> values)
        where TEntity : class
    {
        return (IQueryable<TEntity>)WherePropertyInMethod
            .MakeGenericMethod(typeof(TEntity), propertyType)
            .Invoke(null, new object?[] { query, propertyName, values.ToArray() })!;
    }

    public static IOrderedQueryable<TEntity> OrderByProperty<TEntity>(
        IQueryable<TEntity> query,
        string propertyName,
        Type propertyType,
        bool descending)
        where TEntity : class
    {
        return (IOrderedQueryable<TEntity>)OrderByPropertyMethod
            .MakeGenericMethod(typeof(TEntity), propertyType)
            .Invoke(null, new object?[] { query, propertyName, descending })!;
    }

    private static IQueryable<TEntity> WherePropertyEqualsCore<TEntity, TProperty>(
        IQueryable<TEntity> query,
        string propertyName,
        object? value)
        where TEntity : class
    {
        var typedValue = (TProperty?)PolymorphicValueConverter.ConvertForAssignment(value, typeof(TProperty));

        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var property = Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            new[] { typeof(TProperty) },
            parameter,
            Expression.Constant(propertyName));

        var equals = Expression.Equal(property, PolymorphicValueConverter.BuildTypedConstantExpression(typedValue, typeof(TProperty)));
        var predicate = Expression.Lambda<Func<TEntity, bool>>(equals, parameter);
        return query.Where(predicate);
    }

    private static IQueryable<TEntity> WherePropertyInCore<TEntity, TProperty>(
        IQueryable<TEntity> query,
        string propertyName,
        object[] values)
        where TEntity : class
    {
        var typedValues = values
            .Select(value => (TProperty?)PolymorphicValueConverter.ConvertForAssignment(value, typeof(TProperty)))
            .Where(value => value is not null)
            .Cast<TProperty>()
            .Distinct()
            .ToArray();

        if (typedValues.Length == 0)
        {
            return query.Where(_ => false);
        }

        return query.Where(entity => typedValues.Contains(EF.Property<TProperty>(entity, propertyName)));
    }

    private static IOrderedQueryable<TEntity> OrderByPropertyCore<TEntity, TProperty>(
        IQueryable<TEntity> query,
        string propertyName,
        bool descending)
        where TEntity : class
    {
        return descending
            ? query.OrderByDescending(entity => EF.Property<TProperty>(entity, propertyName))
            : query.OrderBy(entity => EF.Property<TProperty>(entity, propertyName));
    }
}


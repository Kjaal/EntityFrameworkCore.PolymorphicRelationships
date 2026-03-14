using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

internal static class PolymorphicQueryableLoader
{
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

        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var property = Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            new[] { propertyType },
            parameter,
            Expression.Constant(propertyName));

        var contains = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            new[] { propertyType },
            Expression.Constant(typedArray, propertyType.MakeArrayType()),
            property);

        var predicate = Expression.Lambda<Func<TEntity, bool>>(contains, parameter);
        var entities = await query.Where(predicate).ToListAsync(cancellationToken);
        return entities.Cast<object>().ToList();
    }
}


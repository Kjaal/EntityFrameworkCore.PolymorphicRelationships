using System.Globalization;
using System.Linq.Expressions;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

internal static class PolymorphicValueConverter
{
    public static object? ConvertForAssignment(object? value, Type targetType)
    {
        if (value is null)
        {
            return null;
        }

        var nonNullableType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (nonNullableType.IsInstanceOfType(value))
        {
            return value;
        }

        if (nonNullableType.IsEnum)
        {
            return value is string stringValue
                ? Enum.Parse(nonNullableType, stringValue, ignoreCase: true)
                : Enum.ToObject(nonNullableType, value);
        }

        if (nonNullableType == typeof(Guid))
        {
            return value is Guid guid ? guid : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!);
        }

        return Convert.ChangeType(value, nonNullableType, CultureInfo.InvariantCulture);
    }

    public static Expression BuildTypedConstantExpression(object? value, Type targetType)
    {
        if (value is null)
        {
            return Expression.Constant(null, targetType);
        }

        var nonNullableType = Nullable.GetUnderlyingType(targetType);

        if (nonNullableType is null)
        {
            return Expression.Constant(ConvertForAssignment(value, targetType), targetType);
        }

        var converted = ConvertForAssignment(value, nonNullableType);
        return Expression.Convert(Expression.Constant(converted, nonNullableType), targetType);
    }
}



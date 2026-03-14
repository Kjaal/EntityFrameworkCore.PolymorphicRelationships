using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCorePolymorphicExtension.Infrastructure;

internal static class ExpressionHelpers
{
    public static string GetPropertyName<TDeclaring, TProperty>(Expression<Func<TDeclaring, TProperty>> expression)
    {
        return expression.Body switch
        {
            MemberExpression memberExpression => memberExpression.Member.Name,
            UnaryExpression { Operand: MemberExpression memberExpression } => memberExpression.Member.Name,
            _ => throw new ArgumentException("Expression must point to a property.", nameof(expression)),
        };
    }

    public static string GetSingleKeyPropertyName(IReadOnlyEntityType entityType)
    {
        var primaryKey = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException($"Entity '{entityType.DisplayName()}' must define a primary key or provide an explicit owner key.");

        if (primaryKey.Properties.Count != 1)
        {
            throw new NotSupportedException($"Entity '{entityType.DisplayName()}' uses a composite key. This starter implementation currently supports only single-column morph keys.");
        }

        return primaryKey.Properties[0].Name;
    }
}


using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure.Query;

internal sealed class PolymorphicMethodCallTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo MorphCollectionCountMethod = typeof(PolymorphicSqlMarkerMethods)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method.Name == nameof(PolymorphicSqlMarkerMethods.MorphCollectionCount));

    private static readonly MethodInfo MorphCollectionAnyMethod = typeof(PolymorphicSqlMarkerMethods)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method.Name == nameof(PolymorphicSqlMarkerMethods.MorphCollectionAny));

    private static readonly MethodInfo MorphOwnerPropertyMethod = typeof(PolymorphicSqlMarkerMethods)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method.Name == nameof(PolymorphicSqlMarkerMethods.MorphOwnerProperty));

    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method.IsGenericMethod)
        {
            method = method.GetGenericMethodDefinition();
        }

        if (method == MorphCollectionCountMethod || method == MorphCollectionAnyMethod || method == MorphOwnerPropertyMethod)
        {
            throw new NotSupportedException("Provider-aware relational translation for polymorphic query markers is not implemented yet.");
        }

        return null;
    }
}

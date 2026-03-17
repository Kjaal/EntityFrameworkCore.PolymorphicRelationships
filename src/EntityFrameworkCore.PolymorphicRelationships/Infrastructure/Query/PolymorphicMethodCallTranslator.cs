using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure.Query;

internal sealed class PolymorphicMethodCallTranslator(
    ISqlExpressionFactory sqlExpressionFactory,
    ICurrentDbContext currentDbContext,
    IRelationalTypeMappingSource typeMappingSource) : IMethodCallTranslator
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
            var genericMethod = method;
            method = method.GetGenericMethodDefinition();

            if (method == MorphCollectionCountMethod)
            {
                return TranslateMorphCollectionCount(arguments);
            }

            if (method == MorphCollectionAnyMethod)
            {
                return TranslateMorphCollectionAny(arguments);
            }

            if (method == MorphOwnerPropertyMethod)
            {
                return TranslateMorphOwnerProperty(genericMethod, arguments);
            }
        }

        return null;
    }

    private SqlExpression? TranslateMorphCollectionCount(IReadOnlyList<SqlExpression> arguments)
    {
        if (arguments.Count != 4)
        {
            return null;
        }

        if (!TryResolveMorphMany(arguments, out var association, out var reference, out var dependentEntityType, out var ownerId, out var dependentKeyColumn, out var dependentTypeColumn, out var selectExpression))
        {
            return null;
        }

        var alias = sqlExpressionFactory.Constant(association.Alias, typeMappingSource.FindMapping(typeof(string)));
        var predicate = sqlExpressionFactory.AndAlso(
            sqlExpressionFactory.Equal(dependentTypeColumn, alias),
            sqlExpressionFactory.Equal(dependentKeyColumn, ownerId));

        selectExpression.ClearOrdering();
        selectExpression.ApplyPredicate(predicate);

        var dependentKeyProperty = dependentEntityType.FindProperty(reference.IdPropertyName)!;
        var countExpression = sqlExpressionFactory.Function(
            "COUNT",
            new[] { dependentKeyColumn },
            nullable: false,
            argumentsPropagateNullability: new[] { false },
            typeof(int),
            dependentKeyProperty.GetRelationalTypeMapping());

        selectExpression.AddToProjection(countExpression);
        return new ScalarSubqueryExpression(selectExpression);
    }

    private SqlExpression? TranslateMorphOwnerProperty(MethodInfo genericMethod, IReadOnlyList<SqlExpression> arguments)
    {
        if (arguments.Count != 6)
        {
            return null;
        }

        var dependentTypeName = (arguments[2] as SqlConstantExpression)?.Value as string;
        var relationshipName = (arguments[3] as SqlConstantExpression)?.Value as string;
        var ownerClrTypeName = (arguments[4] as SqlConstantExpression)?.Value as string;
        var propertyName = (arguments[5] as SqlConstantExpression)?.Value as string;

        if (string.IsNullOrWhiteSpace(dependentTypeName)
            || string.IsNullOrWhiteSpace(relationshipName)
            || string.IsNullOrWhiteSpace(ownerClrTypeName)
            || string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        var dependentType = Type.GetType(dependentTypeName, throwOnError: false);
        var ownerClrType = Type.GetType(ownerClrTypeName, throwOnError: false);
        if (dependentType is null || ownerClrType is null)
        {
            return null;
        }

        var model = currentDbContext.Context.Model;
        var reference = PolymorphicModelMetadata.GetRequiredReference(model, dependentType, relationshipName);
        var association = reference.Associations.FirstOrDefault(candidate => candidate.PrincipalType.IsAssignableFrom(ownerClrType)
            || ownerClrType.IsAssignableFrom(candidate.PrincipalType));
        if (association is null)
        {
            return null;
        }

        var ownerEntityType = model.FindEntityType(association.PrincipalType);
        var ownerProperty = ownerEntityType?.FindProperty(propertyName);
        var ownerKeyProperty = ownerEntityType?.FindProperty(association.PrincipalKeyPropertyName);
        if (ownerEntityType is null || ownerProperty is null || ownerKeyProperty is null)
        {
            return null;
        }

        var selectExpression = sqlExpressionFactory.Select(ownerEntityType);
        var table = selectExpression.Tables.Single();
        var storeObject = StoreObjectIdentifier.Table(ownerEntityType.GetTableName()!, ownerEntityType.GetSchema());

        var ownerKeyColumn = selectExpression.CreateColumnExpression(
            table,
            ownerKeyProperty.GetColumnName(storeObject)!,
            ownerKeyProperty.ClrType,
            ownerKeyProperty.GetRelationalTypeMapping(),
            ownerKeyProperty.IsColumnNullable(storeObject));

        var ownerValueColumn = selectExpression.CreateColumnExpression(
            table,
            ownerProperty.GetColumnName(storeObject)!,
            ownerProperty.ClrType,
            ownerProperty.GetRelationalTypeMapping(),
            ownerProperty.IsColumnNullable(storeObject));

        var ownerId = sqlExpressionFactory.ApplyTypeMapping(arguments[0], ownerKeyProperty.GetRelationalTypeMapping());
        var ownerType = sqlExpressionFactory.ApplyTypeMapping(arguments[1], typeMappingSource.FindMapping(typeof(string)));

        var predicate = sqlExpressionFactory.AndAlso(
            sqlExpressionFactory.Equal(ownerKeyColumn, ownerId),
            sqlExpressionFactory.Equal(ownerType, sqlExpressionFactory.Constant(association.Alias, typeMappingSource.FindMapping(typeof(string)))));

        selectExpression.ClearOrdering();
        selectExpression.ApplyPredicate(predicate);
        selectExpression.AddToProjection(ownerValueColumn);

        return new ScalarSubqueryExpression(selectExpression);
    }

    private SqlExpression? TranslateMorphCollectionAny(IReadOnlyList<SqlExpression> arguments)
    {
        if (arguments.Count != 4)
        {
            return null;
        }

        if (!TryResolveMorphMany(arguments, out var association, out _, out _, out var ownerId, out var dependentKeyColumn, out var dependentTypeColumn, out var selectExpression))
        {
            return null;
        }

        var alias = sqlExpressionFactory.Constant(association.Alias, typeMappingSource.FindMapping(typeof(string)));
        var predicate = sqlExpressionFactory.AndAlso(
            sqlExpressionFactory.Equal(dependentTypeColumn, alias),
            sqlExpressionFactory.Equal(dependentKeyColumn, ownerId));

        selectExpression.ClearOrdering();
        selectExpression.ApplyPredicate(predicate);
        return sqlExpressionFactory.Exists(selectExpression);
    }

    private bool TryResolveMorphMany(
        IReadOnlyList<SqlExpression> arguments,
        out PolymorphicModelMetadata.MorphAssociation association,
        out PolymorphicModelMetadata.MorphReference reference,
        out Microsoft.EntityFrameworkCore.Metadata.IEntityType dependentEntityType,
        out SqlExpression ownerId,
        out ColumnExpression dependentKeyColumn,
        out ColumnExpression dependentTypeColumn,
        out SelectExpression selectExpression)
    {
        association = null!;
        reference = null!;
        dependentEntityType = null!;
        ownerId = null!;
        dependentKeyColumn = null!;
        dependentTypeColumn = null!;
        selectExpression = null!;

        var principalTypeName = (arguments[1] as SqlConstantExpression)?.Value as string;
        var dependentTypeName = (arguments[2] as SqlConstantExpression)?.Value as string;
        var relationshipName = (arguments[3] as SqlConstantExpression)?.Value as string;

        if (string.IsNullOrWhiteSpace(principalTypeName)
            || string.IsNullOrWhiteSpace(dependentTypeName)
            || string.IsNullOrWhiteSpace(relationshipName))
        {
            return false;
        }

        var principalType = Type.GetType(principalTypeName, throwOnError: false);
        var dependentType = Type.GetType(dependentTypeName, throwOnError: false);
        if (principalType is null || dependentType is null)
        {
            return false;
        }

        var model = currentDbContext.Context.Model;
        (reference, association) = PolymorphicModelMetadata.GetRequiredInverse(model, principalType, dependentType, relationshipName, MorphMultiplicity.Many);
        dependentEntityType = model.FindEntityType(dependentType)!;
        if (dependentEntityType is null)
        {
            return false;
        }

        var dependentKeyProperty = dependentEntityType.FindProperty(reference.IdPropertyName);
        var dependentTypeProperty = dependentEntityType.FindProperty(reference.TypePropertyName);
        if (dependentKeyProperty is null || dependentTypeProperty is null)
        {
            return false;
        }

        selectExpression = sqlExpressionFactory.Select(dependentEntityType);
        var table = selectExpression.Tables.Single();
        var storeObject = StoreObjectIdentifier.Table(dependentEntityType.GetTableName()!, dependentEntityType.GetSchema());

        dependentKeyColumn = selectExpression.CreateColumnExpression(
            table,
            dependentKeyProperty.GetColumnName(storeObject)!,
            dependentKeyProperty.ClrType,
            dependentKeyProperty.GetRelationalTypeMapping(),
            dependentKeyProperty.IsColumnNullable(storeObject));

        dependentTypeColumn = selectExpression.CreateColumnExpression(
            table,
            dependentTypeProperty.GetColumnName(storeObject)!,
            dependentTypeProperty.ClrType,
            dependentTypeProperty.GetRelationalTypeMapping(),
            dependentTypeProperty.IsColumnNullable(storeObject));

        ownerId = sqlExpressionFactory.ApplyTypeMapping(arguments[0], dependentKeyProperty.GetRelationalTypeMapping());
        return true;
    }
}

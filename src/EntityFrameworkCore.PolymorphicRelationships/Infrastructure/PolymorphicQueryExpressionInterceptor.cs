using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

public sealed class PolymorphicQueryExpressionInterceptor : IQueryExpressionInterceptor
{
    private static readonly MethodInfo QueryableWhereMethod = GetQueryableMethod(nameof(Queryable.Where), 2, 1, 2);
    private static readonly MethodInfo QueryableOrderByMethod = GetQueryableMethod(nameof(Queryable.OrderBy), 2, 2, 2);
    private static readonly MethodInfo QueryableOrderByDescendingMethod = GetQueryableMethod(nameof(Queryable.OrderByDescending), 2, 2, 2);
    private static readonly MethodInfo QueryableThenByMethod = GetQueryableMethod(nameof(Queryable.ThenBy), 2, 2, 2);
    private static readonly MethodInfo QueryableThenByDescendingMethod = GetQueryableMethod(nameof(Queryable.ThenByDescending), 2, 2, 2);
    private static readonly MethodInfo QueryableSelectMethod = GetQueryableMethod(nameof(Queryable.Select), 2, 2, 2);
    private static readonly MethodInfo QueryableCountMethod = GetQueryableMethod(nameof(Queryable.Count), 1, 1);
    private static readonly MethodInfo QueryableAnyMethod = GetQueryableMethod(nameof(Queryable.Any), 1, 1);
    private static readonly MethodInfo QueryableFirstOrDefaultMethod = GetQueryableMethod(nameof(Queryable.FirstOrDefault), 1, 1);

    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        if (eventData.Context is null)
        {
            return queryExpression;
        }

        var contextId = PolymorphicDbContextRegistry.Register(eventData.Context);
        return new PolymorphicQueryRewriter(contextId, eventData.Context).Visit(queryExpression);
    }

    private static MethodInfo GetQueryableMethod(string methodName, int parameterCount, int genericArgumentCount, params int[] lambdaArgumentCounts)
    {
        return typeof(Queryable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == methodName
                && method.IsGenericMethodDefinition
                && method.GetGenericArguments().Length == genericArgumentCount
                && method.GetParameters().Length == parameterCount
                && MatchesLambdaArity(method, lambdaArgumentCounts));
    }

    private static bool MatchesLambdaArity(MethodInfo methodInfo, int[] lambdaArgumentCounts)
    {
        if (lambdaArgumentCounts.Length == 0)
        {
            return true;
        }

        var actualCounts = methodInfo.GetParameters()
            .Where(parameter => parameter.ParameterType.IsGenericType && parameter.ParameterType.GetGenericTypeDefinition() == typeof(Expression<>))
            .Select(parameter => parameter.ParameterType.GetGenericArguments()[0])
            .Where(type => type.IsGenericType && type.Name.StartsWith("Func`", StringComparison.Ordinal))
            .Select(type => type.GetGenericArguments().Length)
            .ToArray();

        return actualCounts.SequenceEqual(lambdaArgumentCounts);
    }

    private sealed class PolymorphicQueryRewriter(Guid contextId, DbContext dbContext) : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(Queryable)
                && node.Arguments.Count == 2
                && IsSupportedQueryableMethod(node.Method.Name))
            {
                var source = Visit(node.Arguments[0]);
                var lambda = (LambdaExpression)StripQuote(node.Arguments[1]);
                Expression rewrittenBody = node.Method.Name == nameof(Queryable.Select)
                    ? new ProjectionMemberRewriter(contextId, dbContext, lambda.Parameters).Visit(lambda.Body)!
                    : new QueryMemberRewriter(dbContext, lambda.Parameters).Visit(lambda.Body)!;

                var rewrittenLambda = Expression.Lambda(rewrittenBody, lambda.Parameters);

                return Expression.Call(node.Method, source, Expression.Quote(rewrittenLambda));
            }

            return base.VisitMethodCall(node);
        }

        private static bool IsSupportedQueryableMethod(string methodName)
        {
            return methodName == nameof(Queryable.Select)
                || methodName == nameof(Queryable.Where)
                || methodName == nameof(Queryable.OrderBy)
                || methodName == nameof(Queryable.OrderByDescending)
                || methodName == nameof(Queryable.ThenBy)
                || methodName == nameof(Queryable.ThenByDescending);
        }

        private static Expression StripQuote(Expression expression)
        {
            while (expression.NodeType == ExpressionType.Quote)
            {
                expression = ((UnaryExpression)expression).Operand;
            }

            return expression;
        }
    }

    private sealed class ProjectionMemberRewriter(Guid contextId, DbContext dbContext, IReadOnlyCollection<ParameterExpression> parameters) : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression is null || node.Member.MemberType != System.Reflection.MemberTypes.Property)
            {
                return base.VisitMember(node);
            }

            if (!parameters.Contains(GetRootParameter(node.Expression)))
            {
                return base.VisitMember(node);
            }

            var propertyName = node.Member.Name;
            var relationship = PolymorphicRelationshipResolver.Resolve(dbContext.Model, node.Expression.Type, propertyName);
            if (relationship.Kind == PolymorphicRelationshipResolver.RelationshipType.Unknown)
            {
                return base.VisitMember(node);
            }

            var sourceExpression = Visit(node.Expression);
            return Expression.Call(
                typeof(PolymorphicProjectionAccessor),
                nameof(PolymorphicProjectionAccessor.GetNavigation),
                new[] { node.Expression.Type, node.Type },
                sourceExpression,
                Expression.Constant(contextId),
                Expression.Constant(propertyName));
        }

        private static ParameterExpression? GetRootParameter(Expression expression)
        {
            while (expression is MemberExpression memberExpression)
            {
                expression = memberExpression.Expression!;
            }

            return expression as ParameterExpression;
        }
    }

    private sealed class QueryMemberRewriter(DbContext dbContext, IReadOnlyCollection<ParameterExpression> parameters) : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression node)
        {
            if (TryRewriteCollectionCount(node, out var rewrittenCount))
            {
                return rewrittenCount;
            }

            if (TryRewriteMorphOwnerProperty(node, out var rewrittenOwnerProperty))
            {
                return rewrittenOwnerProperty;
            }

            return base.VisitMember(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (TryRewriteCollectionAny(node, out var rewrittenAny))
            {
                return rewrittenAny;
            }

            return base.VisitMethodCall(node);
        }

        private bool TryRewriteCollectionCount(MemberExpression node, out Expression rewritten)
        {
            rewritten = null!;

            if (!string.Equals(node.Member.Name, nameof(ICollection<object>.Count), StringComparison.Ordinal)
                || node.Expression is not MemberExpression collectionExpression)
            {
                return false;
            }

            if (!TryResolveCollectionRelationship(collectionExpression, out var relationshipKind))
            {
                return false;
            }

            rewritten = BuildCollectionAggregateSubquery(collectionExpression.Expression!, collectionExpression.Member.Name, relationshipKind, useAny: false);
            return true;
        }

        private bool TryRewriteCollectionAny(MethodCallExpression node, out Expression rewritten)
        {
            rewritten = null!;

            if ((node.Method.DeclaringType != typeof(Enumerable) && node.Method.DeclaringType != typeof(Queryable))
                || node.Method.Name != nameof(Queryable.Any)
                || node.Arguments.Count != 1
                || node.Arguments[0] is not MemberExpression collectionExpression)
            {
                return false;
            }

            if (!TryResolveCollectionRelationship(collectionExpression, out var relationshipKind))
            {
                return false;
            }

            rewritten = BuildCollectionAggregateSubquery(collectionExpression.Expression!, collectionExpression.Member.Name, relationshipKind, useAny: true);
            return true;
        }

        private bool TryRewriteMorphOwnerProperty(MemberExpression node, out Expression rewritten)
        {
            rewritten = null!;

            if (!TryGetOwnerNavigation(node.Expression, out var ownerNavigation, out var targetOwnerType))
            {
                return false;
            }

            if (!parameters.Contains(GetRootParameter(ownerNavigation.Expression!)))
            {
                return false;
            }

            var relationship = PolymorphicRelationshipResolver.Resolve(dbContext.Model, ownerNavigation.Expression!.Type, ownerNavigation.Member.Name);
            if (relationship.Kind != PolymorphicRelationshipResolver.RelationshipType.MorphOwner)
            {
                return false;
            }

            var reference = PolymorphicModelMetadata.GetRequiredReference(dbContext.Model, ownerNavigation.Expression.Type, ownerNavigation.Member.Name);
            var association = reference.Associations.FirstOrDefault(candidate => candidate.PrincipalType.IsAssignableFrom(targetOwnerType)
                || targetOwnerType.IsAssignableFrom(candidate.PrincipalType))
                ?? throw new InvalidOperationException($"No morph owner association for '{ownerNavigation.Member.Name}' matches type '{targetOwnerType.Name}'.");

            var ownerSource = CreateQueryRootExpression(targetOwnerType);
            var ownerParameter = Expression.Parameter(targetOwnerType, "owner");

            var ownerKey = BuildMappedPropertyAccess(targetOwnerType, ownerParameter, association.PrincipalKeyPropertyName, GetPropertyType(targetOwnerType, association.PrincipalKeyPropertyName));
            var dependentId = BuildMappedPropertyAccess(ownerNavigation.Expression.Type, Visit(ownerNavigation.Expression), reference.IdPropertyName, reference.IdPropertyType);
            if (ownerKey.Type != dependentId.Type)
            {
                dependentId = Expression.Convert(dependentId, ownerKey.Type);
            }

            var ownerPredicate = Expression.Lambda(
                Expression.Equal(ownerKey, dependentId),
                ownerParameter);

            var filteredOwners = Expression.Call(
                QueryableWhereMethod.MakeGenericMethod(targetOwnerType),
                ownerSource,
                Expression.Quote(ownerPredicate));

            var selectedMember = Expression.MakeMemberAccess(ownerParameter, node.Member);
            var selectLambda = Expression.Lambda(selectedMember, ownerParameter);
            var selectCall = Expression.Call(
                QueryableSelectMethod.MakeGenericMethod(targetOwnerType, node.Type),
                filteredOwners,
                Expression.Quote(selectLambda));

            rewritten = Expression.Call(QueryableFirstOrDefaultMethod.MakeGenericMethod(node.Type), selectCall);
            return true;
        }

        private Expression BuildCollectionAggregateSubquery(Expression ownerExpression, string relationshipName, PolymorphicRelationshipResolver.RelationshipKind relationshipKind, bool useAny)
        {
            return relationshipKind.Kind switch
            {
                PolymorphicRelationshipResolver.RelationshipType.MorphMany => BuildMorphManyAggregate(ownerExpression, relationshipName, relationshipKind.RelatedType!, useAny),
                _ => throw new InvalidOperationException($"Property '{ownerExpression.Type.Name}.{relationshipName}' is not a supported translated collection polymorphic relationship."),
            };
        }

        private Expression BuildMorphManyAggregate(Expression ownerExpression, string relationshipName, Type dependentType, bool useAny)
        {
            var (reference, association) = PolymorphicModelMetadata.GetRequiredInverse(
                dbContext.Model,
                ownerExpression.Type,
                dependentType,
                relationshipName,
                MorphMultiplicity.Many);

            var dependentSource = CreateQueryRootExpression(dependentType);
            var dependentParameter = Expression.Parameter(dependentType, "dependent");

            var aliasPredicate = Expression.Equal(
                BuildMappedPropertyAccess(dependentType, dependentParameter, reference.TypePropertyName, typeof(string)),
                Expression.Constant(association.Alias));

            var ownerKey = BuildMappedPropertyAccess(ownerExpression.Type, Visit(ownerExpression), association.PrincipalKeyPropertyName, GetPropertyType(ownerExpression.Type, association.PrincipalKeyPropertyName));
            var dependentKey = BuildMappedPropertyAccess(dependentType, dependentParameter, reference.IdPropertyName, reference.IdPropertyType);
            if (ownerKey.Type != dependentKey.Type)
            {
                ownerKey = Expression.Convert(ownerKey, dependentKey.Type);
            }

            var predicate = Expression.Lambda(
                Expression.AndAlso(aliasPredicate, Expression.Equal(dependentKey, ownerKey)),
                dependentParameter);

            var filtered = Expression.Call(
                QueryableWhereMethod.MakeGenericMethod(dependentType),
                dependentSource,
                Expression.Quote(predicate));

            return useAny
                ? Expression.Call(QueryableAnyMethod.MakeGenericMethod(dependentType), filtered)
                : Expression.Call(QueryableCountMethod.MakeGenericMethod(dependentType), filtered);
        }

        private bool TryResolveCollectionRelationship(MemberExpression collectionExpression, out PolymorphicRelationshipResolver.RelationshipKind relationshipKind)
        {
            relationshipKind = default;

            if (collectionExpression.Expression is null || !parameters.Contains(GetRootParameter(collectionExpression.Expression)))
            {
                return false;
            }

            relationshipKind = PolymorphicRelationshipResolver.Resolve(dbContext.Model, collectionExpression.Expression.Type, collectionExpression.Member.Name);
            return relationshipKind.Kind == PolymorphicRelationshipResolver.RelationshipType.MorphMany;
        }

        private static bool TryGetOwnerNavigation(Expression? expression, out MemberExpression ownerNavigation, out Type targetOwnerType)
        {
            if (expression is MemberExpression memberExpression)
            {
                ownerNavigation = memberExpression;
                targetOwnerType = memberExpression.Type;
                return true;
            }

            if (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.TypeAs } unaryExpression
                && unaryExpression.Operand is MemberExpression operandMember)
            {
                ownerNavigation = operandMember;
                targetOwnerType = unaryExpression.Type;
                return true;
            }

            ownerNavigation = null!;
            targetOwnerType = null!;
            return false;
        }

        private Expression CreateQueryRootExpression(Type entityType)
        {
            var set = typeof(DbContext)
                .GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!
                .MakeGenericMethod(entityType)
                .Invoke(dbContext, null)!;

            return ((IQueryable)set).Expression;
        }

        private static Expression BuildMappedPropertyAccess(Type declaringType, Expression instance, string propertyName, Type propertyType)
        {
            var property = declaringType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            return property is not null
                ? Expression.Property(instance, property)
                : Expression.Call(
                    typeof(EF),
                    nameof(EF.Property),
                    new[] { propertyType },
                    instance,
                    Expression.Constant(propertyName));
        }

        private Type GetPropertyType(Type entityType, string propertyName)
        {
            return dbContext.Model.FindEntityType(entityType)?.FindProperty(propertyName)?.ClrType
                ?? entityType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.PropertyType
                ?? throw new InvalidOperationException($"Property '{propertyName}' was not found on '{entityType.Name}'.");
        }

        private static ParameterExpression? GetRootParameter(Expression expression)
        {
            while (expression is MemberExpression memberExpression)
            {
                expression = memberExpression.Expression!;
            }

            return expression as ParameterExpression;
        }
    }
}

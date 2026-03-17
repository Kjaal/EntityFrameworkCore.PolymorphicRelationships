using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

public sealed class PolymorphicQueryExpressionInterceptor : IQueryExpressionInterceptor
{
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        if (eventData.Context is null)
        {
            return queryExpression;
        }

        return new PolymorphicSelectProjectionVisitor(eventData.Context).Visit(queryExpression);
    }

    private sealed class PolymorphicSelectProjectionVisitor(DbContext dbContext) : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(Queryable)
                && node.Method.Name == nameof(Queryable.Select)
                && node.Arguments.Count == 2)
            {
                var source = Visit(node.Arguments[0]);
                var lambda = (LambdaExpression)StripQuote(node.Arguments[1]);
                var rewrittenLambda = Expression.Lambda(
                    new ProjectionMemberRewriter(dbContext, lambda.Parameters).Visit(lambda.Body)!,
                    lambda.Parameters);

                return Expression.Call(node.Method, source, Expression.Quote(rewrittenLambda));
            }

            return base.VisitMethodCall(node);
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

    private sealed class ProjectionMemberRewriter(DbContext dbContext, IReadOnlyCollection<ParameterExpression> parameters) : ExpressionVisitor
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
                Expression.Constant(dbContext),
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
}

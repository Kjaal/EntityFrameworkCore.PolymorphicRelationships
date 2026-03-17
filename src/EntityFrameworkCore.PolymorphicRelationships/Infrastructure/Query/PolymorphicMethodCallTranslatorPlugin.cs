using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure.Query;

internal sealed class PolymorphicMethodCallTranslatorPlugin(
    ISqlExpressionFactory sqlExpressionFactory,
    ICurrentDbContext currentDbContext,
    IRelationalTypeMappingSource typeMappingSource) : IMethodCallTranslatorPlugin
{
    public IEnumerable<IMethodCallTranslator> Translators { get; } = new IMethodCallTranslator[]
    {
        new PolymorphicMethodCallTranslator(sqlExpressionFactory, currentDbContext, typeMappingSource),
    };
}

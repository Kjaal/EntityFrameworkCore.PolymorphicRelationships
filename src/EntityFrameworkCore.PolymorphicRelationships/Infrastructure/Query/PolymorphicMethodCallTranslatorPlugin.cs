using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure.Query;

internal sealed class PolymorphicMethodCallTranslatorPlugin : IMethodCallTranslatorPlugin
{
    public IEnumerable<IMethodCallTranslator> Translators { get; } = new IMethodCallTranslator[]
    {
        new PolymorphicMethodCallTranslator(),
    };
}

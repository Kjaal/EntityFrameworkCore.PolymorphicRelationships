namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

internal sealed class PolymorphicGroupJoinResult<TPrincipal, TDependent>
    where TPrincipal : class
    where TDependent : class
{
    public PolymorphicGroupJoinResult(TPrincipal principal, IEnumerable<TDependent> related)
    {
        Principal = principal;
        Related = related;
    }

    public TPrincipal Principal { get; }

    public IEnumerable<TDependent> Related { get; }
}

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure.Query;

public static class PolymorphicSqlMarkerMethods
{
    public static int MorphCollectionCount<TPrincipal>(TPrincipal principal, string relationshipName)
        where TPrincipal : class
    {
        throw new NotSupportedException("This method is intended for use only inside LINQ queries and must be translated by EF Core.");
    }

    public static bool MorphCollectionAny<TPrincipal>(TPrincipal principal, string relationshipName)
        where TPrincipal : class
    {
        throw new NotSupportedException("This method is intended for use only inside LINQ queries and must be translated by EF Core.");
    }

    public static TProperty? MorphOwnerProperty<TDependent, TProperty>(TDependent dependent, string relationshipName, string propertyName)
        where TDependent : class
    {
        throw new NotSupportedException("This method is intended for use only inside LINQ queries and must be translated by EF Core.");
    }
}

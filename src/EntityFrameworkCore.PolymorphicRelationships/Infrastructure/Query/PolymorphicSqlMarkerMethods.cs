namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure.Query;

public static class PolymorphicSqlMarkerMethods
{
    public static int MorphCollectionCount<TKey>(TKey ownerKey, string principalTypeName, string dependentTypeName, string relationshipName)
    {
        throw new NotSupportedException("This method is intended for use only inside LINQ queries and must be translated by EF Core.");
    }

    public static bool MorphCollectionAny<TKey>(TKey ownerKey, string principalTypeName, string dependentTypeName, string relationshipName)
    {
        throw new NotSupportedException("This method is intended for use only inside LINQ queries and must be translated by EF Core.");
    }

    public static TProperty? MorphOwnerProperty<TKey, TProperty>(
        TKey ownerKey,
        string? ownerType,
        string dependentTypeName,
        string relationshipName,
        string ownerClrTypeName,
        string propertyName)
    {
        throw new NotSupportedException("This method is intended for use only inside LINQ queries and must be translated by EF Core.");
    }
}

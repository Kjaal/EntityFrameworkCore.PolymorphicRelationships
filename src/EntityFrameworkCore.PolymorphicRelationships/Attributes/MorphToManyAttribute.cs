namespace EntityFrameworkCore.PolymorphicRelationships.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class MorphToManyAttribute(Type relatedType, Type pivotType, string inverseRelationshipName, string morphName) : Attribute
{
    public Type RelatedType { get; } = relatedType;

    public Type PivotType { get; } = pivotType;

    public string InverseRelationshipName { get; } = inverseRelationshipName;

    public string MorphName { get; } = morphName;

    public string? PrincipalKey { get; init; }

    public string? RelatedKey { get; init; }

    public PolymorphicDeleteBehavior DeleteBehavior { get; init; } = PolymorphicDeleteBehavior.Cascade;
}


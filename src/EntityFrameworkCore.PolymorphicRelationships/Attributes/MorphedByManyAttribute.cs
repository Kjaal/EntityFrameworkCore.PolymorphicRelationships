namespace EntityFrameworkCore.PolymorphicRelationships.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class MorphedByManyAttribute(Type principalType, Type pivotType, string relationshipName, string morphName) : Attribute
{
    public Type PrincipalType { get; } = principalType;

    public Type PivotType { get; } = pivotType;

    public string RelationshipName { get; } = relationshipName;

    public string MorphName { get; } = morphName;

    public string? PrincipalKey { get; init; }

    public string? RelatedKey { get; init; }

    public PolymorphicDeleteBehavior DeleteBehavior { get; init; } = PolymorphicDeleteBehavior.Cascade;
}


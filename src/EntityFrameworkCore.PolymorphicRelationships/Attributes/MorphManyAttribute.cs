namespace EntityFrameworkCore.PolymorphicRelationships.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class MorphManyAttribute(Type dependentType, string relationshipName) : Attribute
{
    public Type DependentType { get; } = dependentType;

    public string RelationshipName { get; } = relationshipName;

    public string? OwnerKey { get; init; }

    public PolymorphicDeleteBehavior DeleteBehavior { get; init; } = PolymorphicDeleteBehavior.Cascade;
}


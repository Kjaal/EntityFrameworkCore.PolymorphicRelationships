namespace EntityFrameworkCore.PolymorphicRelationships.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class MorphToAttribute(string typePropertyName) : Attribute
{
    public string TypePropertyName { get; } = typePropertyName;
}


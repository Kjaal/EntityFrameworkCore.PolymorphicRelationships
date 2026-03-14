namespace EntityFrameworkCore.PolymorphicRelationships.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class MorphMapAttribute(string alias) : Attribute
{
    public string Alias { get; } = alias;
}


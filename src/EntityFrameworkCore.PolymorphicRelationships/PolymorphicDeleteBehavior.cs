namespace EntityFrameworkCore.PolymorphicRelationships;

public enum PolymorphicDeleteBehavior
{
    None = 0,
    Cascade = 1,
}

public enum MorphMultiplicity
{
    One = 0,
    Many = 1,
}

public enum MorphOneOfManyAggregate
{
    Min = 0,
    Max = 1,
}



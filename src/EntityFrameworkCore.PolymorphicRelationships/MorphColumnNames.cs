namespace EntityFrameworkCore.PolymorphicRelationships;

public readonly record struct MorphColumnNames(string TypeColumnName, string IdColumnName);

public readonly record struct MorphPivotColumnNames(string TypeColumnName, string IdColumnName, string RelatedIdColumnName);


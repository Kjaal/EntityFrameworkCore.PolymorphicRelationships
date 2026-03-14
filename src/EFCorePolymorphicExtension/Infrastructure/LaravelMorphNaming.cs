namespace EFCorePolymorphicExtension.Infrastructure;

internal static class LaravelMorphNaming
{
    public static string GetMorphName(string relationshipNameOrMorphName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipNameOrMorphName);
        return ToSnakeCase(relationshipNameOrMorphName);
    }

    public static string GetMorphTypeColumnName(string relationshipNameOrMorphName)
    {
        return $"{GetMorphName(relationshipNameOrMorphName)}_type";
    }

    public static string GetMorphIdColumnName(string relationshipNameOrMorphName)
    {
        return $"{GetMorphName(relationshipNameOrMorphName)}_id";
    }

    public static string GetForeignKeyColumnName(Type clrType)
    {
        ArgumentNullException.ThrowIfNull(clrType);
        return $"{ToSnakeCase(clrType.Name)}_id";
    }

    public static string ToSnakeCase(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var buffer = new System.Text.StringBuilder(value.Length + 8);

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];

            if (character == '-' || character == ' ')
            {
                buffer.Append('_');
                continue;
            }

            if (char.IsUpper(character))
            {
                var hasPrevious = index > 0;
                var previous = hasPrevious ? value[index - 1] : '\0';
                var next = index + 1 < value.Length ? value[index + 1] : '\0';

                if (hasPrevious && previous != '_' && (!char.IsUpper(previous) || (next != '\0' && char.IsLower(next))))
                {
                    buffer.Append('_');
                }

                buffer.Append(char.ToLowerInvariant(character));
                continue;
            }

            buffer.Append(char.ToLowerInvariant(character));
        }

        return buffer.ToString();
    }
}

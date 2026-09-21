namespace TaskFlow.Domain.Common;

internal static class DomainGuard
{
    public static Guid NotEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", paramName);
        }

        return value;
    }

    public static string RequiredText(string? value, int maxLength, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);

        if (value.Length == 0)
        {
            throw new ArgumentException("Value must not be empty.", paramName);
        }

        if (value.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(paramName, value.Length, $"Value length must be between 1 and {maxLength} characters.");
        }

        return value;
    }

    public static string TrimmedRequiredText(string? value, int maxLength, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);

        string trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Value must not be empty after trimming.", paramName);
        }

        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(paramName, trimmed.Length, $"Value length must be between 1 and {maxLength} characters after trimming.");
        }

        return trimmed;
    }

    public static string? OptionalText(string? value, int maxLength, string paramName)
    {
        if (value is not null && value.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(paramName, value.Length, $"Value length must not exceed {maxLength} characters.");
        }

        return value;
    }

    public static TEnum DefinedEnum<TEnum>(TEnum value, string paramName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Unknown enum value.");
        }

        return value;
    }

    public static DateTimeOffset UpdateTime(DateTimeOffset value, DateTimeOffset createdAt, string paramName)
    {
        if (value < createdAt)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Update time must not be earlier than creation time.");
        }

        return value;
    }
}

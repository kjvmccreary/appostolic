namespace Appostolic.Api.Application.Validation;

public static class Guard
{
    public static T NotNull<T>(T value, string paramName)
    {
        if (value is null)
            throw new ArgumentException($"{paramName} is required", paramName);
        return value;
    }

    public static string NotNullOrWhiteSpace(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{paramName} is required", paramName);
        return value!;
    }

    public static T InRange<T>(T value, T minInclusive, T maxInclusive, string paramName) where T : IComparable<T>
    {
        if (value.CompareTo(minInclusive) < 0 || value.CompareTo(maxInclusive) > 0)
            throw new ArgumentOutOfRangeException(paramName, value, $"{paramName} must be between {minInclusive} and {maxInclusive}");
        return value;
    }

    public static string MaxLength(string value, int maxLength, string paramName)
    {
        if (value is null)
            throw new ArgumentException($"{paramName} is required", paramName);
        if (value.Length > maxLength)
            throw new ArgumentOutOfRangeException(paramName, value, $"{paramName} must be <= {maxLength} characters");
        return value;
    }
}

using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Contracts.Auth;

public static class PasswordPolicyRules
{
    public const int RequiredLength = 12;
    public const int RequiredUniqueChars = 6;
    public const string ValidationMessage =
        "Password must contain at least 12 characters, 6 unique characters, an uppercase letter, a lowercase letter, a number, and a symbol.";

    public static bool IsCompliant(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < RequiredLength)
        {
            return false;
        }

        var uniqueCharacters = new HashSet<char>();
        var hasUppercase = false;
        var hasLowercase = false;
        var hasDigit = false;
        var hasSymbol = false;

        foreach (char character in password)
        {
            uniqueCharacters.Add(character);
            hasUppercase |= char.IsUpper(character);
            hasLowercase |= char.IsLower(character);
            hasDigit |= char.IsDigit(character);
            hasSymbol |= !char.IsLetterOrDigit(character);
        }

        return uniqueCharacters.Count >= RequiredUniqueChars &&
               hasUppercase &&
               hasLowercase &&
               hasDigit &&
               hasSymbol;
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class PasswordPolicyAttribute : ValidationAttribute
{
    public PasswordPolicyAttribute()
        : base(PasswordPolicyRules.ValidationMessage)
    {
    }

    public override bool IsValid(object? value) =>
        value is null || value is string password && PasswordPolicyRules.IsCompliant(password);
}

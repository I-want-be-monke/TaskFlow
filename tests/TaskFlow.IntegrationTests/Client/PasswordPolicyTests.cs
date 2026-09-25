using System.ComponentModel.DataAnnotations;
using TaskFlow.Client.Ui;
using TaskFlow.Contracts.Auth;

namespace TaskFlow.IntegrationTests.Client;

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("alllowercasepassword")]
    [InlineData("ALLUPPERCASEPASSWORD1!")]
    [InlineData("NoDigitsHere!Abc")]
    [InlineData("NoSymbolsHere123A")]
    [InlineData("Aa1!Aa1!Aa1!")]
    [InlineData("Short1!Aa")]
    public void RegisterFormModel_RejectsPasswordThatViolatesServerPolicy(string password)
    {
        var model = new RegisterFormModel
        {
            UserName = "alice",
            Password = password,
            ConfirmPassword = password,
        };
        var results = new List<ValidationResult>();

        bool valid = Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(results, result => result.ErrorMessage == PasswordPolicyRules.ValidationMessage);
    }

    [Fact]
    public void RegisterFormModel_AcceptsPasswordThatMatchesServerPolicy()
    {
        const string password = "TaskFlow!2026A";
        var model = new RegisterFormModel
        {
            UserName = "alice",
            Password = password,
            ConfirmPassword = password,
        };
        var results = new List<ValidationResult>();

        bool valid = Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);

        Assert.True(valid, string.Join(Environment.NewLine, results.Select(result => result.ErrorMessage)));
        Assert.True(PasswordPolicyRules.IsCompliant(password));
    }
}

using System.ComponentModel.DataAnnotations;
using TaskFlow.Client.Ui;

namespace TaskFlow.IntegrationTests.Client;

public sealed class UiFormValidationTests
{
    [Fact]
    public void LoginForm_RequiresUserNameAndPassword()
    {
        var model = new LoginFormModel();

        IReadOnlyList<ValidationResult> errors = Validate(model);

        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(LoginFormModel.UserName)));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(LoginFormModel.Password)));
    }

    [Fact]
    public void RegisterForm_RequiresMatchingPasswords()
    {
        var model = new RegisterFormModel
        {
            UserName = "alice",
            Password = "secret1",
            ConfirmPassword = "secret2",
        };

        IReadOnlyList<ValidationResult> errors = Validate(model);

        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(RegisterFormModel.ConfirmPassword)));
    }

    [Fact]
    public void ProjectTaskAndTagForms_EnforceDocumentedLengthBounds()
    {
        var project = new ProjectFormModel { Name = new string('p', 121) };
        var task = new TaskFormModel { Title = new string('t', 201) };
        var tag = new TagFormModel { Name = new string('x', 65) };

        Assert.NotEmpty(Validate(project));
        Assert.NotEmpty(Validate(task));
        Assert.NotEmpty(Validate(tag));
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }
}

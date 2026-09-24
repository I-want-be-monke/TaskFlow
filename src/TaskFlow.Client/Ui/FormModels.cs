using System.ComponentModel.DataAnnotations;
using TaskFlow.Contracts.Auth;

namespace TaskFlow.Client.Ui;

public sealed class LoginFormModel
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string UserName { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public sealed class RegisterFormModel
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [PasswordPolicy]
    public string Password { get; set; } = string.Empty;

    [Required]
    [Compare(nameof(Password))]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class ProjectFormModel
{
    [Required]
    [StringLength(120, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }
}

public sealed class TaskFormModel
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [StringLength(4000)]
    public string? Description { get; set; }

    [Required]
    public string Status { get; set; } = "Todo";

    [Required]
    public string Priority { get; set; } = "Medium";

    public DateTime? DueAtLocal { get; set; }
}

public sealed class TagFormModel
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;
}

public static class TaskFormOptions
{
    public static readonly IReadOnlyList<string> Statuses = ["Todo", "InProgress", "Done"];
    public static readonly IReadOnlyList<string> Priorities = ["Low", "Medium", "High"];
}

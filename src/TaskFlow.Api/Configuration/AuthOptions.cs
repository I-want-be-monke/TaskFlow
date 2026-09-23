namespace TaskFlow.Api.Configuration;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public bool AllowRegistration { get; init; } = true;
}

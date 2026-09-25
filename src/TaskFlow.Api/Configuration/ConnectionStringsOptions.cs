namespace TaskFlow.Api.Configuration;

public sealed class ConnectionStringsOptions
{
    public const string SectionName = "ConnectionStrings";

    public string Postgres { get; init; } = string.Empty;
}

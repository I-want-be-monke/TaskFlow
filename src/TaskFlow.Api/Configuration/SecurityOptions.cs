using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.Configuration;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    [Required]
    public RateLimitSettings RateLimit { get; init; } = new();
}

public sealed class RateLimitSettings
{
    [Range(1, 10000)]
    public int ApiPermitLimit { get; init; } = 100;

    [Range(1, 1000)]
    public int LoginPermitLimit { get; init; } = 10;

    [Range(1, 3600)]
    public int WindowSeconds { get; init; } = 60;
}

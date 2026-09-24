using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.Configuration;

public sealed class RequestLimitOptions
{
    // The architecture keeps these values under Security__* environment keys.
    public const string SectionName = "Security";

    [Range(1024, 10 * 1024 * 1024)]
    public long MaxRequestBodyBytes { get; init; } = 1024 * 1024;

    [Range(1, 300)]
    public int RequestTimeoutSeconds { get; init; } = 30;
}

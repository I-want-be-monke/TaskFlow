using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.Configuration;

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    [Required, StringLength(64, MinimumLength = 1)]
    public string ServiceVersion { get; init; } = "dev";

    [Range(1, 60000)]
    public int SlowDbThresholdMs { get; init; } = 500;
}

using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.Configuration;

public sealed class ProxyOptions
{
    public const string SectionName = "Proxy";

    public string[] KnownProxies { get; init; } = [];

    public string[] KnownNetworks { get; init; } = [];

    [Range(1, 5)]
    public int ForwardLimit { get; init; } = 1;
}

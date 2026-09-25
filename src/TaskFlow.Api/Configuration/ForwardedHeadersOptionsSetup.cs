using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace TaskFlow.Api.Configuration;

public sealed class ForwardedHeadersOptionsSetup(IOptions<ProxyOptions> proxyOptions)
    : IConfigureOptions<ForwardedHeadersOptions>
{
    public void Configure(ForwardedHeadersOptions options)
    {
        ProxyOptions proxy = proxyOptions.Value;
        bool hasTrustedForwarder = proxy.KnownProxies.Length > 0 || proxy.KnownNetworks.Length > 0;

        options.ForwardLimit = proxy.ForwardLimit;
        options.RequireHeaderSymmetry = true;
        options.ForwardedHeaders = hasTrustedForwarder
            ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            : ForwardedHeaders.None;

        if (!hasTrustedForwarder)
        {
            return;
        }

        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (string value in proxy.KnownProxies)
        {
            options.KnownProxies.Add(IPAddress.Parse(value));
        }

        foreach (string value in proxy.KnownNetworks)
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(value));
        }
    }
}

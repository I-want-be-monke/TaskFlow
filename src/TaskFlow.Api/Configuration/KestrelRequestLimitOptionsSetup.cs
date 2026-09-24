using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace TaskFlow.Api.Configuration;

public sealed class KestrelRequestLimitOptionsSetup(IOptions<RequestLimitOptions> requestLimitOptions)
    : IConfigureOptions<KestrelServerOptions>
{
    public void Configure(KestrelServerOptions options)
    {
        options.Limits.MaxRequestBodySize = requestLimitOptions.Value.MaxRequestBodyBytes;
    }
}

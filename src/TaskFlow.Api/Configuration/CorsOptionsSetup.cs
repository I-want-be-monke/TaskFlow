using Microsoft.Extensions.Options;
using AspNetCorsOptions = Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions;

namespace TaskFlow.Api.Configuration;

public sealed class CorsOptionsSetup(IOptions<CorsOptions> taskFlowCorsOptions)
    : IConfigureOptions<AspNetCorsOptions>
{
    public const string DevelopmentPolicyName = "TaskFlowDevelopmentCors";

    public void Configure(AspNetCorsOptions options)
    {
        string[] origins = taskFlowCorsOptions.Value.AllowedOrigins;
        options.AddPolicy(DevelopmentPolicyName, policy =>
        {
            if (origins.Length == 0)
            {
                policy.SetIsOriginAllowed(_ => false);
                return;
            }

            policy.WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    }
}

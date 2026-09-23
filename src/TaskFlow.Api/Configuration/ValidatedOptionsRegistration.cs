using System.Net;
using Microsoft.Extensions.Options;

namespace TaskFlow.Api.Configuration;

public static class ValidatedOptionsRegistration
{
    public static IServiceCollection AddValidatedTaskFlowOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ConnectionStringsOptions>()
            .Bind(configuration.GetSection(ConnectionStringsOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Postgres),
                "ConnectionStrings:Postgres is required.")
            .ValidateOnStart();

        services.AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => options.RateLimit.ApiPermitLimit is >= 1 and <= 10000
                    && options.RateLimit.LoginPermitLimit is >= 1 and <= 1000
                    && options.RateLimit.WindowSeconds is >= 1 and <= 3600
                    && options.RateLimit.LoginPermitLimit <= options.RateLimit.ApiPermitLimit,
                "Security:RateLimit values are out of range or LoginPermitLimit exceeds ApiPermitLimit.")
            .ValidateOnStart();

        services.AddOptions<RequestLimitOptions>()
            .Bind(configuration.GetSection(RequestLimitOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => options.MaxRequestBodyBytes >= 1024,
                "Security:MaxRequestBodyBytes must be at least 1024 bytes.")
            .ValidateOnStart();

        services.AddOptions<CorsOptions>()
            .Bind(configuration.GetSection(CorsOptions.SectionName))
            .Validate(ValidateCors, "Cors:AllowedOrigins must contain exact HTTP(S) origins without wildcards, paths, query strings, fragments, or trailing slashes.")
            .ValidateOnStart();

        services.AddOptions<ProxyOptions>()
            .Bind(configuration.GetSection(ProxyOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(ValidateProxy, "Proxy:KnownProxies must be IP addresses and Proxy:KnownNetworks must be valid CIDR networks.")
            .ValidateOnStart();

        services.AddOptions<ObservabilityOptions>()
            .Bind(configuration.GetSection(ObservabilityOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => options.ServiceVersion == options.ServiceVersion.Trim()
                    && (options.InstanceId is null || options.InstanceId == options.InstanceId.Trim()),
                "Observability values must not contain leading or trailing whitespace.")
            .ValidateOnStart();

        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateOnStart();

        return services;
    }

    private static bool ValidateCors(CorsOptions options)
    {
        foreach (string origin in options.AllowedOrigins)
        {
            if (string.IsNullOrWhiteSpace(origin)
                || origin.Contains('*')
                || origin.EndsWith('/')
                || !Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || uri.AbsolutePath != "/"
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment)
                || string.IsNullOrWhiteSpace(uri.Host))
            {
                return false;
            }
        }

        return options.AllowedOrigins.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            == options.AllowedOrigins.Length;
    }

    private static bool ValidateProxy(ProxyOptions options)
    {
        return options.KnownProxies.All(value => IPAddress.TryParse(value, out _))
            && options.KnownNetworks.All(value => System.Net.IPNetwork.TryParse(value, out _));
    }
}

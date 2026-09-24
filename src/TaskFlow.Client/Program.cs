using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Client.Auth;
using TaskFlow.Client.Http;
using TaskFlow.Client.Projects;
using TaskFlow.Client.Security;
using TaskFlow.Client.Tags;
using TaskFlow.Client.Tasks;
using TaskFlow.Client;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddAuthorizationCore();

Uri baseAddress = new(builder.HostEnvironment.BaseAddress);
builder.Services.AddScoped(_ => new RawApiHttpClient(new HttpClient { BaseAddress = baseAddress }));
builder.Services.AddScoped<ApiProblemReader>();
builder.Services.AddScoped<ApiAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(services =>
    services.GetRequiredService<ApiAuthenticationStateProvider>());
builder.Services.AddScoped<AntiforgeryTokenProvider>();
builder.Services.AddScoped(services =>
{
    var handler = new AntiforgeryHandler(
        services.GetRequiredService<AntiforgeryTokenProvider>(),
        services.GetRequiredService<ApiAuthenticationStateProvider>())
    {
        InnerHandler = new HttpClientHandler(),
    };

    var client = new HttpClient(handler)
    {
        BaseAddress = baseAddress,
    };

    return new ApiHttpClient(client, services.GetRequiredService<ApiProblemReader>());
});

builder.Services.AddScoped<AuthApiClient>();
builder.Services.AddScoped<ProjectsApiClient>();
builder.Services.AddScoped<TasksApiClient>();
builder.Services.AddScoped<TagsApiClient>();
builder.Services.AddScoped<ClientBootstrapper>();

WebAssemblyHost host = builder.Build();
await host.Services.GetRequiredService<ClientBootstrapper>().InitializeAsync();
await host.RunAsync();

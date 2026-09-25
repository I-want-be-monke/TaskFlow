#!/usr/bin/env python3
from pathlib import Path
import json
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
CLIENT = ROOT / "src" / "TaskFlow.Client"
TESTS = ROOT / "tests" / "TaskFlow.IntegrationTests" / "Client"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(f"Stage 12 verification failed: {message}")


def read(path: Path) -> str:
    require(path.is_file(), f"missing {path.relative_to(ROOT)}")
    return path.read_text(encoding="utf-8")


project_text = read(CLIENT / "TaskFlow.Client.csproj")
project = ET.parse(CLIENT / "TaskFlow.Client.csproj")
require('Sdk="Microsoft.NET.Sdk.BlazorWebAssembly"' in project_text, "Client must be a standalone Blazor WebAssembly project")
require(not project.findall(".//ProjectReference"), "Client must not reference server projects")
require(not project.findall(".//FrameworkReference"), "standalone Client must not depend on Microsoft.AspNetCore.App framework reference")
package_refs = {node.attrib["Include"] for node in project.findall(".//PackageReference")}
for package in [
    "Microsoft.AspNetCore.Components.Authorization",
    "Microsoft.AspNetCore.Components.WebAssembly",
    "Microsoft.AspNetCore.Components.WebAssembly.DevServer",
]:
    require(package in package_refs, f"Client missing package {package}")

central = read(ROOT / "Directory.Packages.props")
for package in package_refs:
    require(f'PackageVersion Include="{package}" Version="10.0.12"' in central,
            f"{package} must be centrally pinned to 10.0.12")

required_files = [
    "Program.cs",
    "App.razor",
    "wwwroot/index.html",
    "Http/ApiProblemReader.cs",
    "Http/ApiProblemException.cs",
    "Security/AntiforgeryTokenProvider.cs",
    "Security/AntiforgeryHandler.cs",
    "Auth/ApiAuthenticationStateProvider.cs",
    "Auth/AuthApiClient.cs",
    "Auth/ClientBootstrapper.cs",
    "Projects/ProjectsApiClient.cs",
    "Tasks/TasksApiClient.cs",
    "Tags/TagsApiClient.cs",
]
for relative in required_files:
    require((CLIENT / relative).is_file(), f"missing Client foundation file {relative}")

source_suffixes = {".cs", ".razor", ".html", ".css", ".json", ".xml", ".csproj"}
all_client_cs = "\n".join(
    path.read_text(encoding="utf-8")
    for path in sorted(CLIENT.rglob("*.cs"))
    if not {"bin", "obj"}.intersection(path.parts)
)
all_client_text = "\n".join(
    path.read_text(encoding="utf-8")
    for path in sorted(CLIENT.rglob("*"))
    if path.is_file()
    and path.suffix in source_suffixes
    and not {"bin", "obj"}.intersection(path.parts)
)
for forbidden in [
    "TaskFlow.Api",
    "TaskFlow.Application",
    "TaskFlow.Domain",
    "TaskFlow.Infrastructure",
    "localStorage",
    "sessionStorage",
    "Authorization: Bearer",
    "Bearer ",
    "refresh_token",
    "refreshToken",
    "Polly",
    "RetryAsync",
]:
    require(forbidden not in all_client_text, f"Client contains forbidden dependency/storage/retry token: {forbidden}")

# Razor owns presentation only; HTTP construction belongs to API clients/handlers.
for razor in CLIENT.rglob("*.razor"):
    text = razor.read_text(encoding="utf-8")
    for forbidden in ["HttpClient", "HttpRequestMessage", "ApiHttpClient", "RawApiHttpClient", "SendAsync("]:
        require(forbidden not in text, f"{razor.relative_to(ROOT)} builds HTTP requests directly")
    require("/api/v1/" not in text, f"{razor.relative_to(ROOT)} must call typed API clients instead of API routes")

program = read(CLIENT / "Program.cs")
for token in [
    "AddAuthorizationCore",
    "ApiAuthenticationStateProvider",
    "AntiforgeryTokenProvider",
    "AntiforgeryHandler",
    "ProjectsApiClient",
    "TasksApiClient",
    "TagsApiClient",
    "ClientBootstrapper",
    "builder.HostEnvironment.BaseAddress",
]:
    require(token in program, f"Client composition root missing {token}")

provider = read(CLIENT / "Auth" / "ApiAuthenticationStateProvider.cs")
require('"/api/v1/auth/me"' in provider, "auth state must be restored through /auth/me")
require("ClaimsIdentity" in provider and "ClaimTypes.NameIdentifier" in provider,
        "authenticated client principal must be rebuilt from server-issued user data")

bootstrap = read(CLIENT / "Auth" / "ClientBootstrapper.cs")
require("_antiforgeryTokenProvider.RefreshAsync" in bootstrap,
        "initial bootstrap must fetch antiforgery token")
require("_authenticationStateProvider.RefreshAsync" in bootstrap,
        "initial bootstrap must restore auth state")

antiforgery = read(CLIENT / "Security" / "AntiforgeryHandler.cs")
for method in ["HttpMethod.Post", "HttpMethod.Put", "HttpMethod.Patch", "HttpMethod.Delete"]:
    require(method in antiforgery, f"antiforgery handler missing unsafe method {method}")
for token in ["X-XSRF-TOKEN", "GetTokenAsync", "StatusCode == HttpStatusCode.Unauthorized", "MarkAnonymous", "_tokenProvider.Clear()"]:
    require(token in antiforgery, f"antiforgery/session handler missing {token}")
require("base.SendAsync(request, cancellationToken)" in antiforgery,
        "handler must send each request exactly once through the inner handler")
require(antiforgery.count("base.SendAsync(request, cancellationToken)") == 1,
        "unsafe requests must not be automatically retried")

token_provider = read(CLIENT / "Security" / "AntiforgeryTokenProvider.cs")
for token in ['"/api/v1/auth/antiforgery"', "private string? _token", "SemaphoreSlim", "Clear()"]:
    require(token in token_provider, f"antiforgery token provider missing {token}")
for forbidden in ["IJSRuntime", "localStorage", "sessionStorage"]:
    require(forbidden not in token_provider, f"antiforgery token must remain in tab memory, found {forbidden}")

auth_client = read(CLIENT / "Auth" / "AuthApiClient.cs")
for token in [
    '"/api/v1/auth/register"',
    '"/api/v1/auth/login"',
    '"/api/v1/auth/logout"',
    "_antiforgeryTokenProvider.Clear()",
    "_antiforgeryTokenProvider.RefreshAsync",
    "_authenticationStateProvider.RefreshAsync",
]:
    require(token in auth_client, f"auth client lifecycle missing {token}")

for relative, class_name in [
    ("Projects/ProjectsApiClient.cs", "ProjectsApiClient"),
    ("Tasks/TasksApiClient.cs", "TasksApiClient"),
    ("Tags/TagsApiClient.cs", "TagsApiClient"),
]:
    text = read(CLIENT / relative)
    require(class_name in text and "ApiHttpClient" in text, f"{class_name} must use the shared API boundary")
    require("/api/v1/" in text, f"{class_name} must use relative /api/v1 routes")
    require("http://" not in text and "https://" not in text, f"{class_name} must not bake an API hostname")

problem = read(CLIENT / "Http" / "ApiProblemReader.cs")
for token in ["code", "traceId", "detail", "response.StatusCode"]:
    require(token in problem, f"ProblemDetails reader missing {token}")
conflict = read(CLIENT / "Http" / "ApiProblemException.cs")
require("IsVersionConflict" in conflict and "HttpStatusCode.Conflict" not in conflict,
        "typed API problem must expose conflict semantics without server references")

# Lock graph for real WASM project must be present and pinned to the release matching SDK 10.0.401/runtime 10.0.12.
lock = json.loads(read(CLIENT / "packages.lock.json"))
locked = lock["dependencies"]["net10.0"]
for package in [
    "Microsoft.AspNetCore.App.Internal.Assets",
    "Microsoft.AspNetCore.Components.Authorization",
    "Microsoft.AspNetCore.Components.WebAssembly",
    "Microsoft.AspNetCore.Components.WebAssembly.DevServer",
    "Microsoft.NET.ILLink.Tasks",
    "Microsoft.NET.Sdk.WebAssembly.Pack",
]:
    require(locked.get(package, {}).get("resolved") == "10.0.12", f"Client lock version changed for {package}")
require("net10.0/browser-wasm" in lock["dependencies"], "Client lock must include browser-wasm target")

integration_project = read(ROOT / "tests" / "TaskFlow.IntegrationTests" / "TaskFlow.IntegrationTests.csproj")
require("TaskFlow.Client/TaskFlow.Client.csproj" in integration_project,
        "IntegrationTests must reference Client for executable frontend boundary tests")

required_tests = {
    "ApiProblemReaderTests.cs": ["ProblemDetails_IsParsedWithStableCodeAndTraceId"],
    "ApiAuthenticationStateProviderTests.cs": ["RefreshAsync_RestoresAuthenticatedStateThroughAuthMe"],
    "AntiforgeryHandlerTests.cs": [
        "UnsafeRequest_ReceivesAntiforgeryHeader",
        "SafeRequest_DoesNotReceiveAntiforgeryHeader",
        "UnauthorizedResponse_ChangesAuthenticationStateToAnonymous",
    ],
    "ApiConflictSurfaceTests.cs": ["ProjectVersionConflict_IsSurfacedAsTypedProblemForUi"],
}
for filename, names in required_tests.items():
    text = read(TESTS / filename)
    for name in names:
        require(name in text, f"frontend tests missing {name}")

# Ensure all hard-coded client API URLs are relative and stay inside v1.
for path in CLIENT.rglob("*.cs"):
    text = path.read_text(encoding="utf-8")
    for url in re.findall(r'"(/api/[^"?]+)', text):
        require(url.startswith("/api/v1/"), f"non-v1 client API path in {path.relative_to(ROOT)}: {url}")

print("Stage 12 Blazor WASM client foundation verification passed.")

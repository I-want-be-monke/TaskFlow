#!/usr/bin/env python3
from pathlib import Path
import json
import re

ROOT = Path(__file__).resolve().parents[1]
API = ROOT / "src" / "TaskFlow.Api"
TESTS = ROOT / "tests" / "TaskFlow.IntegrationTests" / "Api"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(f"Stage 8 verification failed: {message}")


def read(path: Path) -> str:
    require(path.is_file(), f"missing {path.relative_to(ROOT)}")
    return path.read_text(encoding="utf-8")


program = read(API / "Program.cs")
for token in [
    "AddIdentityCore<ApplicationUser>",
    ".AddSignInManager()",
    ".AddEntityFrameworkStores<TaskFlowDbContext>()",
    "IdentityConstants.ApplicationScheme",
    ".AddIdentityCookies()",
    "ConfigureApplicationCookie",
    'options.Cookie.Name = "__Host-TaskFlow.Auth"',
    "options.Cookie.HttpOnly = true",
    "CookieSecurePolicy.Always",
    "SameSiteMode.Strict",
    'options.Cookie.Path = "/"',
    "options.ExpireTimeSpan = TimeSpan.FromHours(8)",
    "options.SlidingExpiration = true",
    "options.Lockout.MaxFailedAccessAttempts = 5",
    "options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15)",
    "options.Events.OnRedirectToLogin",
    "options.Events.OnRedirectToAccessDenied",
    ".AddDataProtection()",
    '.SetApplicationName("TaskFlow")',
    ".PersistKeysToDbContext<TaskFlowDbContext>()",
    "AddAntiforgery",
    'options.HeaderName = "X-XSRF-TOKEN"',
    'options.Cookie.Name = "__Host-TaskFlow.Antiforgery"',
    "SetFallbackPolicy",
    "RequireAuthenticatedUser()",
    "UseAuthentication()",
    "UseAuthorization()",
    "AddScoped<ApiAntiforgeryFilter>()",
    "AddService<ApiAntiforgeryFilter>()",
]:
    require(token in program, f"Program.cs missing security token: {token}")

require(program.index("UseAuthentication()") < program.index("UseAuthorization()") < program.index("MapControllers()"),
        "middleware order must be authentication -> authorization -> controllers")
require("options.Events = new CookieAuthenticationEvents" not in program,
        "ConfigureApplicationCookie must preserve Identity's security-stamp validation event")
for forbidden in ["AllowAnyOrigin", "Request.Headers[\"X-User", "Bearer ", "localStorage", "sessionStorage"]:
    require(forbidden not in program, f"Stage 8 production security must not contain {forbidden}")

filter_source = read(API / "Security" / "ApiAntiforgeryFilter.cs")
for token in [
    "IAntiforgery",
    "ValidateRequestAsync",
    "AntiforgeryValidationException",
    '"security.csrf_validation_failed"',
    '"application/problem+json"',
    "HttpMethods.Get",
    "HttpMethods.Head",
    "HttpMethods.Options",
]:
    require(token in filter_source, f"antiforgery filter missing {token}")
require("HttpMethods.Post" not in filter_source and "HttpMethods.Put" not in filter_source and "HttpMethods.Delete" not in filter_source,
        "unsafe methods must not be treated as CSRF-safe")


writer = read(API / "Security" / "AuthenticationProblemWriter.cs")
for token in [
    "ApiProblemDetails.FromError",
    'contentType: "application/problem+json"',
    "httpContext.RequestAborted",
]:
    require(token in writer, f"authentication problem writer missing {token}")

controller = read(API / "Controllers" / "AuthController.cs")
for route in [
    '[Route("api/v1/auth")]',
    '[HttpGet("antiforgery")]',
    '[HttpPost("register")]',
    '[HttpPost("login")]',
    '[HttpPost("logout")]',
    '[HttpGet("me")]',
]:
    require(route in controller, f"missing auth route {route}")
require(controller.count("[AllowAnonymous]") == 3,
        "only antiforgery/register/login must be explicitly anonymous at Stage 8")
all_controller_source = "\n".join(path.read_text(encoding="utf-8") for path in (API / "Controllers").glob("*.cs"))
require(all_controller_source.count("[AllowAnonymous]") == 3,
        "no business controller may bypass the fallback authorization policy")
for token in [
    "UserManager<ApplicationUser>",
    "SignInManager<ApplicationUser>",
    "userManager.CreateAsync(user, request.Password)",
    "PasswordSignInAsync",
    "lockoutOnFailure: true",
    "SignInAsync(user, isPersistent: false)",
    "SignOutAsync()",
    "GetUserAsync(User)",
    '"auth.invalid_credentials"',
    "authOptions.Value.AllowRegistration",
    '"auth.registration_disabled"',
]:
    require(token in controller, f"auth controller missing {token}")
for forbidden in ["PasswordHasher", "SHA256", "Rfc2898", "Bearer", "RefreshToken", "Request.Headers"]:
    require(forbidden not in controller, f"auth controller must not contain custom/insecure auth mechanism: {forbidden}")

contracts = read(API / "Contracts" / "Auth" / "AuthContracts.cs")
for token in ["RegisterRequest", "LoginRequest", "AuthUserResponse", "AntiforgeryResponse"]:
    require(token in contracts, f"missing auth DTO {token}")
for forbidden in ["PasswordHash", "SecurityStamp", "Cookie", "BearerToken", "RefreshToken"]:
    require(forbidden not in contracts, f"auth DTO leaks server-controlled secret field {forbidden}")

actor = read(API / "Auth" / "HttpContextCurrentActor.cs")
require("ClaimTypes.NameIdentifier" in actor, "current actor must use authenticated identity claim")
for forbidden in ["X-User-Id", "Request.Headers", "Query["]:
    require(forbidden not in actor, "current actor must not accept identity from request-controlled metadata")

for test_file in [
    "AuthSecurityTests.cs",
    "AuthAuthorizationMetadataTests.cs",
    "TaskFlowWebApplicationFactory.cs",
]:
    require((TESTS / test_file).is_file(), f"missing Stage 8 security test file {test_file}")

tests = read(TESTS / "AuthSecurityTests.cs")
for token in [
    "FallbackPolicy_RejectsAnonymousBusinessEndpoint",
    "RegisterLoginLogoutMe_UseCookieSession",
    "AntiforgeryToken_MustBeRefreshedAfterAuthenticationStateChanges",
    "AuthenticationCookie_HasRequiredSecurityAttributes",
    "UnsafeRequestWithoutAntiforgery_ReturnsRfc7807",
    "SafeGet_DoesNotRequireAntiforgeryButUnsafePostPutDeleteDo",
    "InvalidLoginResponses_DoNotRevealWhetherUserExists",
    "AccountLockout_TriggersAfterConfiguredFailuresAndKeepsExternalResponseGeneric",
    "BolaMatrix_ForeignProjectTaskTagAndRelationReturnNotFound",
    "CookieAndAntiforgeryTokens_WorkAcrossApiReplicasSharingPostgresKeyRing",
    "RegistrationCanBeDisabledByConfiguration",
    '"security.csrf_validation_failed"',
    '"auth.invalid_credentials"',
    "AccessFailedCount",
]:
    require(token in tests, f"Stage 8 security tests missing {token}")

packages = read(ROOT / "Directory.Packages.props")
require('Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12"' in packages,
        "stable ASP.NET Core 10 test-host package must be centrally pinned")

csproj = read(ROOT / "tests" / "TaskFlow.IntegrationTests" / "TaskFlow.IntegrationTests.csproj")
require('<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />' in csproj,
        "IntegrationTests must use WebApplicationFactory for HTTP security contract tests")

try:
    lock = json.loads(read(ROOT / "tests" / "TaskFlow.IntegrationTests" / "packages.lock.json"))["dependencies"]["net10.0"]
except (json.JSONDecodeError, KeyError):
    raise SystemExit("Stage 8 verification failed: invalid IntegrationTests packages.lock.json")
entry = lock.get("Microsoft.AspNetCore.Mvc.Testing", {})
require(entry.get("type") == "Direct" and entry.get("resolved") == "10.0.12",
        "IntegrationTests lock file must pin Microsoft.AspNetCore.Mvc.Testing 10.0.12")
for name, package in lock.items():
    for dependency in package.get("dependencies", {}):
        require(dependency in lock or dependency.startswith("TaskFlow."),
                f"lock graph is missing dependency {dependency} required by {name}")

# No Stage 8 schema migration is expected: Identity + Data Protection schema already belongs to Stage 5.
migrations = list((ROOT / "src" / "TaskFlow.Infrastructure" / "Persistence" / "Migrations").glob("*.cs"))
require(any("InitialCreate" in path.name for path in migrations), "initial schema migration is missing")
require(not any("Stage8" in path.name or "Auth" in path.name for path in migrations),
        "Stage 8 must not invent a second Identity/Data Protection schema migration")

print("Stage 8 authentication/authorization/CSRF verification passed.")

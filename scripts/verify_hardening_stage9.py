#!/usr/bin/env python3
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
API = ROOT / "src" / "TaskFlow.Api"
TESTS = ROOT / "tests" / "TaskFlow.IntegrationTests" / "Api"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(f"Stage 9 verification failed: {message}")


def read(path: Path) -> str:
    require(path.is_file(), f"missing {path.relative_to(ROOT)}")
    return path.read_text(encoding="utf-8")


for name in [
    "SecurityOptions.cs",
    "CorsOptions.cs",
    "ProxyOptions.cs",
    "ObservabilityOptions.cs",
    "RequestLimitOptions.cs",
]:
    require((API / "Configuration" / name).is_file(), f"missing typed options {name}")

registration = read(API / "Configuration" / "ValidatedOptionsRegistration.cs")
for token in [
    "ValidateDataAnnotations()",
    ".Validate(",
    "ValidateOnStart()",
    "SecurityOptions",
    "CorsOptions",
    "ProxyOptions",
    "ObservabilityOptions",
    "RequestLimitOptions",
]:
    require(token in registration, f"validated options registration missing {token}")
require("AllowAnyOrigin" not in registration, "CORS validation must not permit wildcard origin")

program = read(API / "Program.cs")
for token in [
    "AddValidatedTaskFlowOptions",
    "AddRequestTimeouts()",
    "AddRateLimiter()",
    "AddHealthChecks()",
    "PostgresReadinessHealthCheck",
    "UseForwardedHeaders()",
    "UseExceptionHandler()",
    "UseRouting()",
    "UseMiddleware<RequestBodyLimitMiddleware>()",
    "UseRequestTimeouts()",
    "UseCors(CorsOptionsSetup.DevelopmentPolicyName)",
    "UseAuthentication()",
    "UseRateLimiter()",
    "UseAuthorization()",
    'MapHealthChecks("/health/live"',
    'MapHealthChecks("/health/ready"',
    "DisableRateLimiting()",
]:
    require(token in program, f"Program.cs missing Stage 9 token: {token}")
for forbidden in ["Database.Migrate", "MigrateAsync(", "EnsureCreated", "AllowAnyOrigin"]:
    require(forbidden not in program, f"API startup/hardening must not contain {forbidden}")

ordered = [
    "UseForwardedHeaders()",
    "UseExceptionHandler()",
    "UseRouting()",
    "UseMiddleware<RequestBodyLimitMiddleware>()",
    "UseRequestTimeouts()",
    "UseAuthentication()",
    "UseRateLimiter()",
    "UseAuthorization()",
    "MapControllers()",
]
positions = [program.index(token) for token in ordered]
require(positions == sorted(positions), "middleware order does not match Stage 9 baseline")
require("if (app.Environment.IsDevelopment())" in program, "CORS must only run in Development")
require(program.count(".AllowAnonymous()") == 1, "only /health/live may bypass fallback authorization in Program.cs")

body_limit = read(API / "Middleware" / "RequestBodyLimitMiddleware.cs")
for token in [
    "IHttpMaxRequestBodySizeFeature",
    "MaxRequestBodyBytes",
    "Status413PayloadTooLarge",
    '"http.request_too_large"',
    '"application/problem+json"',
]:
    require(token in body_limit, f"request body limiter missing {token}")

timeout_setup = read(API / "Configuration" / "RequestTimeoutOptionsSetup.cs")
for token in ["RequestTimeoutPolicy", "RequestTimeoutSeconds", "Status503ServiceUnavailable", '"http.request_timeout"']:
    require(token in timeout_setup, f"request timeout setup missing {token}")

rate = read(API / "RateLimiting" / "RateLimiterOptionsSetup.cs")
for token in [
    "PartitionedRateLimiter.Create<HttpContext, string>",
    "ClaimTypes.NameIdentifier",
    "RemoteIpAddress",
    "LoginPermitLimit",
    "ApiPermitLimit",
    "Status429TooManyRequests",
    '"http.rate_limit_exceeded"',
    "QueueLimit = 0",
]:
    require(token in rate, f"rate limiter missing {token}")

auth_controller = read(API / "Controllers" / "AuthController.cs")
require(auth_controller.count("EnableRateLimiting(RateLimitPolicies.Authentication)") == 2,
        "register and login must both use strict authentication limiter")
require("authOptions.Value.AllowRegistration" in auth_controller,
        "Auth:AllowRegistration must be applied after Stage 8 audit")

proxy = read(API / "Configuration" / "ForwardedHeadersOptionsSetup.cs")
for token in [
    "ForwardedHeaders.XForwardedFor",
    "ForwardedHeaders.XForwardedProto",
    "KnownProxies.Clear()",
    "KnownIPNetworks.Clear()",
    "IPNetwork.Parse",
    "ForwardedHeaders.None",
]:
    require(token in proxy, f"trusted proxy setup missing {token}")
require("options.KnownNetworks" not in proxy, "obsolete ForwardedHeadersOptions.KnownNetworks must not be used on .NET 10")

cors = read(API / "Configuration" / "CorsOptionsSetup.cs")
for token in ["WithOrigins(origins)", "AllowCredentials()", "AllowAnyHeader()", "AllowAnyMethod()"]:
    require(token in cors, f"development exact CORS policy missing {token}")
require("AllowAnyOrigin" not in cors, "credentialed CORS must never use AllowAnyOrigin")

health = read(API / "Health" / "PostgresReadinessHealthCheck.cs")
require("Database.CanConnectAsync" in health, "readiness must check PostgreSQL connectivity")

pagination = read(ROOT / "src" / "TaskFlow.Application" / "Common" / "Pagination" / "Pagination.cs")
require("MaximumPageSize = 100" in pagination, "pagination must remain bounded at PageSize <= 100")

edge = read(ROOT / "deploy" / "reverse-proxy" / "security-headers.conf")
for token in ["Content-Security-Policy", "X-Content-Type-Options", "Referrer-Policy", "Permissions-Policy", "frame-ancestors 'none'"]:
    require(token in edge, f"edge security headers contract missing {token}")

stage9_tests = read(TESTS / "HardeningAndHealthTests.cs")
for token in [
    "InvalidOptions_FailApplicationStartup",
    "InvalidCustomCorsOptions_FailApplicationStartup",
    "RequestBodyOverConfiguredLimit_Returns413ProblemDetails",
    "StrictAuthenticationLimiter_Returns429AfterConfiguredBudget",
    "LiveHealth_DoesNotDependOnPostgres_WhileReadinessCheckDoes",
    "ReadyHealth_WithAuthenticatedSessionAndPostgres_Returns200",
    "ForwardedHeaders_AreAcceptedOnlyFromAllowlistedProxy",
    "RequestTimeoutOptions_AreBoundFromValidatedConfiguration",
]:
    require(token in stage9_tests, f"Stage 9 tests missing {token}")

stage8_tests = read(TESTS / "AuthSecurityTests.cs")
require("RegistrationCanBeDisabledByConfiguration" in stage8_tests,
        "Stage 8 audit must cover Auth:AllowRegistration configuration")

example = read(ROOT / ".env.example")
for token in [
    "Security__RateLimit__ApiPermitLimit",
    "Security__RateLimit__LoginPermitLimit",
    "Security__RequestTimeoutSeconds",
    "Security__MaxRequestBodyBytes",
    "Cors__AllowedOrigins__0",
    "Auth__AllowRegistration",
]:
    require(token in example, f".env.example missing {token}")

print("Stage 9 hardening/configuration/health verification passed.")

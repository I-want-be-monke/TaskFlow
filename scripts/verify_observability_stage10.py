#!/usr/bin/env python3
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
API = ROOT / "src" / "TaskFlow.Api"
INFRA = ROOT / "src" / "TaskFlow.Infrastructure"
MIGRATOR = ROOT / "src" / "TaskFlow.DbMigrator"
TESTS = ROOT / "tests" / "TaskFlow.IntegrationTests" / "Api"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(f"Stage 10 verification failed: {message}")


def read(path: Path) -> str:
    require(path.is_file(), f"missing {path.relative_to(ROOT)}")
    return path.read_text(encoding="utf-8")


catalog = read(INFRA / "Observability" / "TaskFlowLogEvents.cs")
expected_events = {
    "RequestCompleted": 1001,
    "RequestRejected": 1002,
    "UnhandledException": 1003,
    "ProjectCreated": 2001,
    "ProjectArchived": 2002,
    "TaskCreated": 2101,
    "TaskUpdated": 2102,
    "TagCreated": 2201,
    "DatabaseUnavailable": 3001,
    "SlowDatabaseOperation": 3002,
    "ConcurrencyConflict": 3003,
    "LoginSucceeded": 4001,
    "LoginFailed": 4002,
    "AccountLockedOut": 4003,
    "Logout": 4004,
    "AuthorizationDenied": 4010,
    "CsrfValidationFailed": 4011,
    "RateLimitRejected": 4012,
    "SecurityConfigurationError": 4013,
    "ApplicationStarted": 5001,
    "ApplicationStopping": 5002,
    "ApplicationStopped": 5003,
    "MigrationStarted": 5101,
    "MigrationCompleted": 5102,
    "MigrationFailed": 5103,
}
ids = []
for name, event_id in expected_events.items():
    token = f"new({event_id}, nameof({name}))"
    require(token in catalog, f"event catalog missing stable {name}={event_id}")
    ids.append(event_id)
require(len(ids) == len(set(ids)), "EventId catalog contains duplicates")

formatter = read(INFRA / "Observability" / "TaskFlowJsonConsoleFormatter.cs")
for field in [
    '"schema_version"', '"@timestamp"', '"log_level"', '"event_id"', '"event_name"',
    '"message"', '"service_name"', '"service_version"', '"deployment_environment"',
    '"instance_id"', '"trace_id"', '"span_id"', '"request_id"',
]:
    require(field in formatter, f"formatter missing required field {field}")
for token in ["WriteLine(JsonSerializer.Serialize", "AllowedStructuredFields", "SanitizeMessage", "exception_stack_trace"]:
    require(token in formatter, f"formatter missing {token}")
for forbidden in ["logEntry.Exception.Message", "logEntry.Exception.ToString()", "JsonSerializer.Serialize(logEntry.State"]:
    require(forbidden not in formatter, f"formatter must not serialize sensitive exception/state data: {forbidden}")

logging_ext = read(INFRA / "Observability" / "TaskFlowLoggingExtensions.cs")
for token in ["AddConsoleFormatter<TaskFlowJsonConsoleFormatter", "IncludeScopes = true", "TASKFLOW_INSTANCE_ID", "HOSTNAME"]:
    require(token in logging_ext, f"logging registration missing {token}")

program = read(API / "Program.cs")
for token in [
    "ClearProviders()",
    "AddTaskFlowJsonConsole(",
    'serviceName: "TaskFlow.Api"',
    'AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning)',
    'AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None)',
    'AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning)',
    "EnableSensitiveDataLogging(false)",
    "SlowDatabaseCommandInterceptor",
    "UseMiddleware<RequestLoggingMiddleware>()",
    "TaskFlowLogEvents.ApplicationStarted",
    "TaskFlowLogEvents.ApplicationStopping",
    "TaskFlowLogEvents.ApplicationStopped",
]:
    require(token in program, f"Program.cs missing observability token: {token}")
require(program.index("UseMiddleware<RequestLoggingMiddleware>()") < program.index("UseExceptionHandler()"),
        "request completion middleware must wrap exception handling to observe final 500 status")
for forbidden in ["AddFile", "WriteTo.File", "Serilog", "NLog", "EnableSensitiveDataLogging(true)"]:
    require(forbidden not in program, f"production API logging must not use {forbidden}")

request_logging = read(API / "Middleware" / "RequestLoggingMiddleware.cs")
require(request_logging.count("TaskFlowLogEvents.RequestCompleted") == 1,
        "RequestLoggingMiddleware must emit exactly one RequestCompleted event")
for token in ["Stopwatch.GetElapsedTime", "http_method", "http_route", "http_status_code", "duration_ms", "request_id"]:
    require(token in request_logging, f"RequestCompleted contract missing {token}")
for forbidden in ["Request.Path", "QueryString", "Request.Body", "Response.Body", "Headers["]:
    require(forbidden not in request_logging, f"request logging must not capture {forbidden}")

exception_handler = read(API / "Errors" / "GlobalExceptionHandler.cs")
require(exception_handler.count("TaskFlowLogEvents.UnhandledException") == 1,
        "unexpected request exception must have one explicit boundary Error event")
require("logger.LogError" in exception_handler, "unexpected exception must be Error")
require("Request.Path" not in exception_handler and "QueryString" not in exception_handler,
        "exception logging must use route template, not raw URL")

security_logger = read(API / "Observability" / "SecurityEventLogger.cs")
for name in [
    "LoginSucceeded", "LoginFailed", "AccountLockedOut", "Logout",
    "AuthorizationDenied", "CsrfValidationFailed", "RateLimitRejected", "ConfigurationError",
]:
    require(f"void {name}" in security_logger, f"security event logger missing {name}")
for field in ["security_event_id", "outcome", "reason_code", "client_ip", "http_route", "user_id"]:
    require(field in security_logger, f"security event contract missing {field}")

for path, tokens in {
    API / "Controllers" / "AuthController.cs": ["LoginSucceeded", "LoginFailed", "AccountLockedOut", "securityEvents.Logout"],
    API / "Security" / "ApiAntiforgeryFilter.cs": ["CsrfValidationFailed"],
    API / "RateLimiting" / "RateLimiterOptionsSetup.cs": ["RateLimitRejected"],
    API / "Program.cs": ["AuthorizationDenied", "ConfigurationError"],
}.items():
    text = read(path)
    for token in tokens:
        require(token in text, f"{path.relative_to(ROOT)} missing security event {token}")

app_events = read(API / "Observability" / "ApplicationEventLogger.cs")
for token in ["ProjectCreated", "ProjectArchived", "TaskCreated", "TaskUpdated", "TagCreated"]:
    require(token in app_events, f"application-significant logging missing {token}")
for controller, token in [
    ("ProjectsController.cs", "applicationEvents.ProjectCreated"),
    ("ProjectsController.cs", "applicationEvents.ProjectArchived"),
    ("TasksController.cs", "applicationEvents.TaskCreated"),
    ("TasksController.cs", "applicationEvents.TaskUpdated"),
    ("TagsController.cs", "applicationEvents.TagCreated"),
]:
    require(token in read(API / "Controllers" / controller), f"{controller} missing {token}")

slow_db = read(INFRA / "Persistence" / "Interceptors" / "SlowDatabaseCommandInterceptor.cs")
for token in ["SlowDatabaseOperation", "eventData.Duration", "thresholdMilliseconds", "db_operation", "duration_ms"]:
    require(token in slow_db, f"slow DB logging missing {token}")
for forbidden in ["CommandText", ".Parameters", "ParameterValue"]:
    require(forbidden not in slow_db, f"slow DB event must not log SQL/bind values: {forbidden}")

uow = read(INFRA / "Persistence" / "UnitOfWork.cs")
for token in ["ConcurrencyConflict", "DatabaseUnavailable", "error_type"]:
    require(token in uow, f"persistence outcome logging missing {token}")
require("LogError(exception" not in uow, "absorbed DB failures must not log potentially sensitive exception messages")

health = read(API / "Health" / "PostgresReadinessHealthCheck.cs")
require("TaskFlowLogEvents.DatabaseUnavailable" in health, "readiness DB failure must use structured persistence event")

migrator = "\n".join(path.read_text(encoding="utf-8") for path in sorted(MIGRATOR.glob("*.cs")))
for token in ["AddTaskFlowJsonConsole", 'serviceName: "TaskFlow.DbMigrator"', "operation_id", "ApplicationStarted"]:
    require(token in migrator, f"DbMigrator must use the shared log schema: missing {token}")
require("Console.WriteLine" not in migrator and "Console.Error" not in migrator,
        "DbMigrator must not bypass structured logger")

for cs in list((ROOT / "src").rglob("*.cs")):
    text = cs.read_text(encoding="utf-8")
    require("EnableSensitiveDataLogging(true)" not in text,
            f"sensitive EF logging enabled in {cs.relative_to(ROOT)}")
    if cs.name == "TaskFlowJsonConsoleFormatter.cs":
        continue
    for line in text.splitlines():
        if ".Log" not in line and "logger.Log" not in line:
            continue
        lower = line.lower()
        for sensitive in ["password", "authorization", "cookie", "antiforgery", "connectionstring", "querystring"]:
            require(sensitive not in lower,
                    f"sensitive token appears directly in log call in {cs.relative_to(ROOT)}: {sensitive}")

observability_tests = read(TESTS / "ObservabilityLoggingTests.cs")
for token in [
    "Formatter_WritesOneJsonLineWithStableRequiredFields",
    "Formatter_DropsSensitiveStructuredFieldsAndExceptionMessage",
    "RequestLoggingMiddleware_EmitsExactlyOneCompletionEventAndNeverErrorFor500",
    "UnexpectedException_ProducesOneBoundaryErrorEventAndSafeProblemDetails",
    "EventCatalog_HasStableIdsAndNames",
]:
    require(token in observability_tests, f"Stage 10 tests missing {token}")
for token in ["password", "cookie", "antiforgery", "connectionString"]:
    require(token in observability_tests, f"redaction test missing {token}")

props = read(ROOT / "Directory.Packages.props")
require('Microsoft.Extensions.Logging.Console" Version="10.0.12"' in props,
        "custom formatter package version must be centrally pinned")
infra_project = read(INFRA / "TaskFlow.Infrastructure.csproj")
require('PackageReference Include="Microsoft.Extensions.Logging.Console"' in infra_project,
        "Infrastructure must declare the shared console formatter dependency")

print("Stage 10 structured logging/observability verification passed.")

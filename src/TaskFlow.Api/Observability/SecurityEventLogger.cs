using Microsoft.Extensions.Logging;
using TaskFlow.Infrastructure.Observability;

namespace TaskFlow.Api.Observability;

public sealed class SecurityEventLogger(ILogger<SecurityEventLogger> logger)
{
    public void LoginSucceeded(HttpContext context, Guid userId, string reasonCode = "credentials_valid") =>
        Log(context, LogLevel.Information, TaskFlowLogEvents.LoginSucceeded, "success", reasonCode, userId);

    public void LoginFailed(HttpContext context, string reasonCode = "invalid_credentials") =>
        Log(context, LogLevel.Warning, TaskFlowLogEvents.LoginFailed, "failure", reasonCode, null);

    public void AccountLockedOut(HttpContext context) =>
        Log(context, LogLevel.Warning, TaskFlowLogEvents.AccountLockedOut, "failure", "account_locked_out", null);

    public void Logout(HttpContext context) =>
        Log(context, LogLevel.Information, TaskFlowLogEvents.Logout, "success", "session_ended", HttpLogContext.UserId(context));

    public void AuthorizationDenied(HttpContext context, string reasonCode) =>
        Log(context, LogLevel.Warning, TaskFlowLogEvents.AuthorizationDenied, "denied", reasonCode, HttpLogContext.UserId(context));

    public void CsrfValidationFailed(HttpContext context) =>
        Log(context, LogLevel.Warning, TaskFlowLogEvents.CsrfValidationFailed, "rejected", "invalid_antiforgery_token", HttpLogContext.UserId(context));

    public void RateLimitRejected(HttpContext context) =>
        Log(context, LogLevel.Warning, TaskFlowLogEvents.RateLimitRejected, "rejected", "rate_limit_exceeded", HttpLogContext.UserId(context));

    public void ConfigurationError(string reasonCode) =>
        logger.LogCritical(
            TaskFlowLogEvents.SecurityConfigurationError,
            "Security configuration validation failed. SecurityEventId={security_event_id} Outcome={outcome} Reason={reason_code}",
            TaskFlowLogEvents.SecurityConfigurationError.Id,
            "failure",
            reasonCode);

    private void Log(
        HttpContext context,
        LogLevel level,
        EventId eventId,
        string outcome,
        string reasonCode,
        Guid? userId)
    {
        logger.Log(
            level,
            eventId,
            "Security event recorded. SecurityEventId={security_event_id} Outcome={outcome} Reason={reason_code} ClientIp={client_ip} Route={http_route} UserId={user_id}",
            eventId.Id,
            outcome,
            reasonCode,
            HttpLogContext.ClientIp(context),
            HttpLogContext.RouteTemplate(context),
            userId);
    }
}

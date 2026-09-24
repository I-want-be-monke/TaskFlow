using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TaskFlow.Api.Configuration;
using TaskFlow.Api.Contracts.Auth;
using TaskFlow.Api.Errors;
using TaskFlow.Api.RateLimiting;
using TaskFlow.Application.Common.Errors;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    IAntiforgery antiforgery,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    TimeProvider timeProvider,
    IOptions<AuthOptions> authOptions) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("antiforgery")]
    [ProducesResponseType(typeof(AntiforgeryResponse), StatusCodes.Status200OK)]
    public ActionResult<AntiforgeryResponse> Antiforgery()
    {
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(HttpContext);
        if (string.IsNullOrWhiteSpace(tokens.RequestToken))
        {
            throw new InvalidOperationException("The antiforgery system did not issue a request token.");
        }

        return Ok(new AntiforgeryResponse(tokens.RequestToken));
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthUserResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<AuthUserResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!authOptions.Value.AllowRegistration)
        {
            return this.ToProblem(new Error(
                new ErrorCode("auth.registration_disabled"),
                ErrorType.Forbidden,
                "Registration is disabled."));
        }

        string userName = request.UserName.Trim();
        if (userName.Length is < 1 or > 64)
        {
            return this.ToProblem(new Error(new ErrorCode("auth.invalid_user_name"), ErrorType.Validation, "User name must contain between 1 and 64 characters."));
        }

        var user = new ApplicationUser(Guid.NewGuid(), userName, timeProvider.GetUtcNow());
        IdentityResult createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return IdentityValidationProblem(createResult);
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        return StatusCode(StatusCodes.Status201Created, new AuthUserResponse(user.Id, user.UserName!));
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthUserResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthUserResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string userName = request.UserName.Trim();
        SignInResult signInResult = await signInManager.PasswordSignInAsync(
            userName,
            request.Password,
            isPersistent: false,
            lockoutOnFailure: true);

        if (!signInResult.Succeeded)
        {
            return this.ToProblem(new Error(new ErrorCode("auth.invalid_credentials"), ErrorType.Unauthenticated, "Invalid credentials."));
        }

        ApplicationUser? user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            await signInManager.SignOutAsync();
            return this.ToProblem(new Error(new ErrorCode("auth.invalid_credentials"), ErrorType.Unauthenticated, "Invalid credentials."));
        }

        return Ok(new AuthUserResponse(user.Id, user.UserName!));
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await signInManager.SignOutAsync();
        return NoContent();
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(AuthUserResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthUserResponse>> Me(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplicationUser? user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return this.ToProblem(new Error(new ErrorCode("auth.session_missing"), ErrorType.Unauthenticated, "Authentication is required."));
        }

        return Ok(new AuthUserResponse(user.Id, user.UserName!));
    }

    private ObjectResult IdentityValidationProblem(IdentityResult result)
    {
        string detail = string.Join(
            ' ',
            result.Errors
                .Select(error => error.Description)
                .Where(description => !string.IsNullOrWhiteSpace(description)));

        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = "The account could not be created.";
        }

        return this.ToProblem(new Error(new ErrorCode("auth.registration_failed"), ErrorType.Validation, detail));
    }
}

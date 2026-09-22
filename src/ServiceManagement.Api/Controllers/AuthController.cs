using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ServiceManagement.Api.Configuration;
using ServiceManagement.Api.Models;
using ServiceManagement.Application.Auth;

namespace ServiceManagement.Api.Controllers;

/// <summary>Authentication endpoints: login, token rotation, logout and password change.</summary>
[ApiVersion("1.0")]
public sealed class AuthController : ApiControllerBase
{
    public AuthController(ISender mediator)
        : base(mediator)
    {
    }

    /// <summary>Exchanges credentials for an access/refresh token pair.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType(typeof(ApiResponse<AuthTokenDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<AuthTokenDto>>> Login(
        [FromBody] LoginCommand command,
        CancellationToken cancellationToken)
        => Success(await Mediator.Send(command, cancellationToken));

    /// <summary>Rotates a refresh token and issues a new access token.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType(typeof(ApiResponse<AuthTokenDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AuthTokenDto>>> Refresh(
        [FromBody] RefreshTokenCommand command,
        CancellationToken cancellationToken)
        => Success(await Mediator.Send(command, cancellationToken));

    /// <summary>Revokes the supplied refresh token. Idempotent.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> Logout(
        [FromBody] LogoutCommand command,
        CancellationToken cancellationToken)
    {
        await Mediator.Send(command, cancellationToken);
        return Acknowledge("Signed out.");
    }

    /// <summary>Changes the password of the authenticated caller and signs other sessions out.</summary>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<object>>> ChangePassword(
        [FromBody] ChangePasswordCommand command,
        CancellationToken cancellationToken)
    {
        await Mediator.Send(command, cancellationToken);
        return Acknowledge("Password changed. Other sessions have been signed out.");
    }
}

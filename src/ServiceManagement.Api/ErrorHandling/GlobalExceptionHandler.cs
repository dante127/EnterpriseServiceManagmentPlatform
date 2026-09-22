using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Api.Models;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Domain.Exceptions;
using ServiceManagement.Infrastructure.Diagnostics;

namespace ServiceManagement.Api.ErrorHandling;

/// <summary>
/// Central translation of exceptions into the API response contract.
/// Internal details (stack traces, SQL text, connection strings) are logged but
/// never returned to the caller.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var correlationId = httpContext.Items[CorrelationIdDefaults.ItemsKey] as string;

        var (statusCode, message, errors) = Map(exception);

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                exception,
                "Unhandled exception while processing {Method} {Path} (correlation {CorrelationId}).",
                httpContext.Request.Method,
                httpContext.Request.Path,
                correlationId);
        }
        else
        {
            _logger.LogWarning(
                "Handled {ExceptionType} on {Method} {Path}: {Message} (correlation {CorrelationId}).",
                exception.GetType().Name,
                httpContext.Request.Method,
                httpContext.Request.Path,
                exception.Message,
                correlationId);
        }

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json";

        await httpContext.Response.WriteAsJsonAsync(
            ApiResponse<object>.Fail(message, errors, correlationId),
            cancellationToken);

        return true;
    }

    private static (int StatusCode, string Message, IReadOnlyList<ApiError> Errors) Map(Exception exception)
        => exception switch
        {
            ValidationException validationException => (
                StatusCodes.Status400BadRequest,
                "Validation failed",
                BuildFieldErrors(validationException)),

            DomainRuleViolationException domainRule => (
                StatusCodes.Status400BadRequest,
                domainRule.Message,
                []),

            InvalidStatusTransitionException transition => (
                StatusCodes.Status409Conflict,
                transition.Message,
                []),

            NotFoundException notFound => (
                StatusCodes.Status404NotFound,
                notFound.Message,
                []),

            ConflictException conflict => (
                StatusCodes.Status409Conflict,
                conflict.Message,
                []),

            // Optimistic concurrency clash: two users edited the same ticket.
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "The record was modified by another user. Reload it and try again.",
                []),

            ForbiddenException forbidden => (
                StatusCodes.Status403Forbidden,
                forbidden.Message,
                []),

            UnauthorizedException => (
                StatusCodes.Status401Unauthorized,
                "Authentication failed.",
                []),

            UnauthorizedAccessException => (
                StatusCodes.Status401Unauthorized,
                "Authentication failed.",
                []),

            DomainException domain => (
                StatusCodes.Status400BadRequest,
                domain.Message,
                []),

            _ => (
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred. Please contact support with the correlation id.",
                [])
        };

    private static IReadOnlyList<ApiError> BuildFieldErrors(ValidationException exception)
        => exception.Errors
            .Select(failure => new ApiError(
                string.IsNullOrWhiteSpace(failure.PropertyName) ? "request" : failure.PropertyName,
                failure.ErrorMessage))
            .ToList();
}

using System.Text.Json;
using FileAccessGovernance.QueryApi.Dtos;

namespace FileAccessGovernance.QueryApi.Middleware;

/// <summary>Design doc §6 — maps unhandled exceptions to the {error:{code,message}} shape
/// from §4 instead of leaking a stack trace to the caller.</summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;

            var envelope = new ErrorEnvelope(new ErrorDetail("INTERNAL_ERROR", "An unexpected error occurred."));
            await context.Response.WriteAsync(JsonSerializer.Serialize(envelope));
        }
    }
}

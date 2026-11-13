using System.Diagnostics;
using System.Text.Json;
using BatteryMes.Core.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BatteryMes.Api.Middleware;

/// <summary>
/// Global exception handler — converts unhandled exceptions into the
/// standard ApiResponse error envelope. Must be registered FIRST in the
/// pipeline so it wraps everything downstream.
/// </summary>
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (Exception ex)
        {
            var traceId = Activity.Current?.Id ?? ctx.TraceIdentifier;
            var (status, code, message) = Map(ex);

            if (status >= 500)
                logger.LogError(ex, "Unhandled exception on {Method} {Path} [trace {TraceId}]",
                    ctx.Request.Method, ctx.Request.Path, traceId);
            else
                logger.LogWarning(ex, "Request error {Code} on {Method} {Path} [trace {TraceId}]",
                    code, ctx.Request.Method, ctx.Request.Path, traceId);

            if (ctx.Response.HasStarted) throw; // can't rewrite a started response

            ctx.Response.Clear();
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";

            var body = status == 401
                ? (object)new { error = message }
                : ApiResponse<object>.Fail(new ApiError(code, message, traceId));

            await ctx.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOpts));
        }
    }

    private static (int Status, string Code, string Message) Map(Exception ex) => ex switch
    {
        KeyNotFoundException => (404, "NOT_FOUND", ex.Message),
        FileNotFoundException => (404, "NOT_FOUND", ex.Message),
        ArgumentException or ArgumentNullException => (400, "BAD_REQUEST", ex.Message),
        InvalidOperationException => (400, "INVALID_OPERATION", ex.Message),
        UnauthorizedAccessException => (401, "UNAUTHORIZED", "Authentication required"),
        SqlSugar.SqlSugarException sqlEx when sqlEx.Message.Contains("connection", StringComparison.OrdinalIgnoreCase)
            => (503, "DATABASE_UNAVAILABLE", "Database is unreachable"),
        _ => (500, "INTERNAL_ERROR", "An unexpected error occurred")
    };
}

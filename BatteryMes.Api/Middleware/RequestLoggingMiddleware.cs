using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BatteryMes.Api.Middleware;

/// <summary>
/// Structured request logging — one line per request with method, path,
/// status code, duration, and trace id. Registered after the exception
/// handler so it sees the final status code.
/// </summary>
public class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await next(ctx);
        }
        finally
        {
            sw.Stop();
            var status = ctx.Response.StatusCode;
            var level = status >= 500 ? LogLevel.Error
                : status >= 400 ? LogLevel.Warning
                : LogLevel.Information;

            logger.Log(level,
                "HTTP {Method} {Path}{Query} -> {StatusCode} in {ElapsedMs}ms [trace {TraceId}]",
                ctx.Request.Method, ctx.Request.Path.Value, ctx.Request.QueryString.Value,
                status, sw.ElapsedMilliseconds, ctx.TraceIdentifier);
        }
    }
}

namespace Deskverse.Api;

using System.Net;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Gatekeeper for every <c>/api</c> route: loopback connections only, plus a
/// constant-time bearer-token check. Rejections never echo token material.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ApiAuthenticationMiddleware
{
    private const string BearerPrefix = "Bearer ";

    private readonly RequestDelegate _next;
    private readonly ApiTokenStore _tokens;

    public ApiAuthenticationMiddleware(RequestDelegate next, ApiTokenStore tokens)
    {
        _next = next;
        _tokens = tokens;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Defense in depth: even though Kestrel only binds the loopback adapter,
        // refuse anything that did not originate from this machine.
        var remote = context.Connection.RemoteIpAddress;
        if (remote is null || !IPAddress.IsLoopback(remote))
        {
            await DenyAsync(context, StatusCodes.Status403Forbidden, "This API serves loopback clients only.")
                .ConfigureAwait(false);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
            && _tokens.IsValid(header[BearerPrefix.Length..].Trim()))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        context.Response.Headers["WWW-Authenticate"] = "Bearer";
        await DenyAsync(context, StatusCodes.Status401Unauthorized, "A valid bearer token is required.")
            .ConfigureAwait(false);
    }

    private static async Task DenyAsync(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new ErrorDto(message)).ConfigureAwait(false);
    }
}

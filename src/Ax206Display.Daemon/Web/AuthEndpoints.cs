using Ax206Display.Config.Models;
using Ax206Display.Config.Secrets;
using Ax206Display.Config.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ax206Display.Daemon.Web;

/// <summary>
/// Sets up or changes the web UI's login (see <see cref="WebAuthConfig"/>).
/// Both endpoints are themselves subject to <see cref="WebAuthMiddleware"/>
/// like every other route: while unconfigured that's a no-op (so the
/// first-time setup form works with no credentials yet), and once a password
/// exists, changing it requires already being authenticated as that user -
/// there's no separate "forgot password" path by design, since the whole
/// point is that only someone who can already reach the panel/VM should be
/// able to reset it (see docs/linux-daemon.md for the actual recovery step:
/// clearing WebAuth from config.json directly).
/// </summary>
public static class AuthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/auth/status", GetStatusAsync);
        app.MapPost("/api/auth/setup", SetupAsync);
        app.MapPut("/api/auth/credentials", ChangeCredentialsAsync);
    }

    private static async Task<IResult> GetStatusAsync(ConfigService configService, CancellationToken cancellationToken)
    {
        var config = await configService.LoadAsync(cancellationToken);
        return Results.Ok(new { configured = config.WebAuth is not null, username = config.WebAuth?.Username });
    }

    private static async Task<IResult> SetupAsync(CredentialsRequest request, ConfigService configService, CancellationToken cancellationToken)
    {
        var config = await configService.LoadAsync(cancellationToken);
        if (config.WebAuth is not null)
        {
            // Reachable in a narrow race (two browser tabs both open the
            // unconfigured setup form) rather than as a real attack surface -
            // WebAuthMiddleware already blocks this route the instant WebAuth
            // is non-null, same as every other route.
            return Results.Conflict(new { detail = "A password is already set. Use the change-password form instead." });
        }

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return Results.BadRequest(new { detail = "Username and password are required." });
        }

        if (request.Password.Length < 8)
        {
            return Results.BadRequest(new { detail = "Password must be at least 8 characters." });
        }

        var webAuth = new WebAuthConfig { Username = request.Username.Trim(), PasswordHash = WebPasswordHasher.Hash(request.Password) };
        await configService.SaveAsync(config with { WebAuth = webAuth }, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> ChangeCredentialsAsync(CredentialsRequest request, ConfigService configService, CancellationToken cancellationToken)
    {
        // No explicit "is this really them" re-check here beyond what
        // WebAuthMiddleware already required to let this request through -
        // that's already proof of the current password, same as every other
        // authenticated route.
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return Results.BadRequest(new { detail = "Username and password are required." });
        }

        if (request.Password.Length < 8)
        {
            return Results.BadRequest(new { detail = "Password must be at least 8 characters." });
        }

        var config = await configService.LoadAsync(cancellationToken);
        var webAuth = new WebAuthConfig { Username = request.Username.Trim(), PasswordHash = WebPasswordHasher.Hash(request.Password) };
        await configService.SaveAsync(config with { WebAuth = webAuth }, cancellationToken);
        return Results.Ok();
    }

    private sealed record CredentialsRequest(string Username, string Password);
}

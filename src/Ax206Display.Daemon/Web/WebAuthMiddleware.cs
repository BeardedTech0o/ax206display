using System.Text;
using Ax206Display.Config.Secrets;
using Ax206Display.Config.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Ax206Display.Daemon.Web;

/// <summary>
/// Gates every request (static pages, every /api/* route, no exceptions -
/// registered first in the pipeline in HostFactory, before static files or
/// endpoint routing) behind HTTP Basic Auth once a password has been set -
/// see <see cref="Config.Models.WebAuthConfig"/>. Before one is set, this is
/// a no-op: every request passes through unauthenticated, exactly as the web
/// UI behaved before login support existed, so upgrading never locks anyone
/// out and the first-run setup form (setup.html, AuthEndpoints.SetupAsync)
/// is itself reachable with no credentials.
/// </summary>
public static class WebAuthMiddleware
{
    public static void Use(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var configService = context.RequestServices.GetRequiredService<ConfigService>();
            var config = await configService.LoadAsync(context.RequestAborted);

            if (config.WebAuth is not { } webAuth)
            {
                await next(context);
                return;
            }

            if (!TryGetBasicAuthCredentials(context.Request, out var username, out var password)
                || username != webAuth.Username
                || !WebPasswordHasher.Verify(password, webAuth.PasswordHash))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Basic realm=\"Ax206Display\"";
                await context.Response.WriteAsync("Login required.");
                return;
            }

            await next(context);
        });
    }

    private static bool TryGetBasicAuthCredentials(HttpRequest request, out string username, out string password)
    {
        username = string.Empty;
        password = string.Empty;

        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..]));
            var separatorIndex = decoded.IndexOf(':');
            if (separatorIndex < 0)
            {
                return false;
            }

            username = decoded[..separatorIndex];
            password = decoded[(separatorIndex + 1)..];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

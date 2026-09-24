using System.Security.Claims;
using Ax206Display.Server.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Ax206Display.Server.Api;

public static class AuthEndpoints
{
    public const string StampClaimType = "ax206:stamp";
    public const string LoginRateLimitPolicy = "login";

    public sealed record LoginRequest(string? Password);

    public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

    public static void MapAuthEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/auth/status", (HttpContext context) =>
            Results.Ok(new { authenticated = context.User.Identity?.IsAuthenticated == true }))
            .AllowAnonymous();

        api.MapPost("/auth/login", async (LoginRequest request, HttpContext context, WebPasswordStore passwords) =>
        {
            if (!passwords.Verify(request.Password))
            {
                return Results.Json(new { error = "Wrong password." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            await SignInAsync(context, passwords);
            return Results.NoContent();
        })
            .AllowAnonymous()
            .RequireRateLimiting(LoginRateLimitPolicy);

        api.MapPost("/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        api.MapPost("/auth/password", async (ChangePasswordRequest request, HttpContext context, WebPasswordStore passwords) =>
        {
            if (!passwords.Verify(request.CurrentPassword))
            {
                return Results.BadRequest(new { error = "Current password is wrong." });
            }

            if (request.NewPassword is not { Length: >= WebPasswordStore.MinimumLength })
            {
                return Results.BadRequest(new { error = $"New password must be at least {WebPasswordStore.MinimumLength} characters." });
            }

            passwords.SetPassword(request.NewPassword);

            // The stamp just changed, which signs out every other session;
            // re-issue this one's cookie so the person who made the change
            // stays signed in.
            await SignInAsync(context, passwords);
            return Results.NoContent();
        })
            .RequireRateLimiting(LoginRateLimitPolicy);
    }

    private static Task SignInAsync(HttpContext context, WebPasswordStore passwords)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "admin"), new Claim(StampClaimType, passwords.Stamp)],
            CookieAuthenticationDefaults.AuthenticationScheme);

        return context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }
}

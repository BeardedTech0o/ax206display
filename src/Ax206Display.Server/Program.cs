using System.Threading.RateLimiting;
using Ax206Display.Config.Services;
using Ax206Display.Engine.Composition;
using Ax206Display.Engine.Integrations;
using Ax206Display.Server;
using Ax206Display.Server.Api;
using Ax206Display.Server.Auth;
using Ax206Display.Server.Devices;
using Ax206Display.Server.Preview;
using Ax206Display.Transport.Discovery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;

var paths = Ax206DisplayPaths.ResolveDefault();

if (args is ["set-password", ..])
{
    return SetPasswordCommand.Run(paths);
}

SecureDirectory.EnsureExists(paths.DataDirectory);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // systemd starts services with / as the working directory; wwwroot
    // lives next to the binary, not there.
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Host.UseSystemd();

// Override with ASPNETCORE_URLS or --urls (e.g. http://127.0.0.1:8206 to
// keep the UI off the LAN behind a reverse proxy).
if (string.IsNullOrEmpty(builder.Configuration["urls"]))
{
    builder.WebHost.UseUrls("http://0.0.0.0:8206");
}

builder.Services.AddAx206DisplayCore(paths);
builder.Services.Replace(ServiceDescriptor.Singleton<IAx206DeviceDiscovery>(UsbDiscoveryFactory.Create));
builder.Services.AddSingleton<IntegrationSetupService>();
builder.Services.AddSingleton<PreviewRenderer>();
builder.Services.AddSingleton(new WebPasswordStore(paths.DataDirectory));

// Cookie encryption keys: persisted so a restart doesn't sign everyone out,
// and kept in the data directory because the service account has no home.
builder.Services.AddDataProtection()
    .SetApplicationName("ax206display")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(paths.DataDirectory, "web-keys")));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "ax206display.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;

        // An API answers 401/403, not a redirect to a login page.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };

        // A cookie issued before the last password change is dead.
        options.Events.OnValidatePrincipal = async context =>
        {
            var passwords = context.HttpContext.RequestServices.GetRequiredService<WebPasswordStore>();
            if (context.Principal?.FindFirst(AuthEndpoints.StampClaimType)?.Value != passwords.Stamp)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthEndpoints.LoginRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
});

var app = builder.Build();

var passwords = app.Services.GetRequiredService<WebPasswordStore>();
if (passwords.EnsureInitialized() || File.Exists(passwords.InitialPasswordFilePath))
{
    ServerLog.InitialPasswordAvailable(app.Logger, passwords.InitialPasswordFilePath);
}

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.ContentSecurityPolicy = "default-src 'self'; img-src 'self' blob: data:; style-src 'self'; script-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    headers.XContentTypeOptions = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// CSRF: the auth cookie is already SameSite=Strict; on top of that every
// state-changing API call must carry a custom header, which a cross-site
// form post can't add and a cross-site fetch can't add without a CORS
// preflight this server never approves.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api")
        && !HttpMethods.IsGet(context.Request.Method)
        && !HttpMethods.IsHead(context.Request.Method)
        && context.Request.Headers["X-Requested-With"] != "ax206display")
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = "Missing X-Requested-With header." });
        return;
    }

    await next();
});

var api = app.MapGroup("/api").RequireAuthorization();
api.MapAuthEndpoints();
api.MapDeviceEndpoints();
api.MapCatalogEndpoints();
api.MapIntegrationEndpoints();
api.MapGet("/status", (Ax206Display.Engine.Services.DisplayManagerHostedService displayManager) => Results.Ok(new
{
    connectedDevices = displayManager.ConnectedDeviceIds.Count,
    dataDirectory = paths.DataDirectory,
    version = typeof(Program).Assembly.GetName().Version?.ToString(3),
    host = Environment.MachineName,
}));

await app.RunAsync();
return 0;

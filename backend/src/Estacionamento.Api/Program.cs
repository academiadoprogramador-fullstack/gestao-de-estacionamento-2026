using Estacionamento.Api.Configuration;
using Estacionamento.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

RuntimeConfiguration.ValidateRequired(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("Identity")!;
var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"]!;
Directory.CreateDirectory(keyRingPath);
if (!OperatingSystem.IsWindows())
{
    File.SetUnixFileMode(keyRingPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
}

builder.Services.AddDbContext<IdentityDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddDataProtection()
    .SetApplicationName("GestaoEstacionamento.Local")
    .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
builder.Services.AddIdentityCore<EmployeeAccount>()
    .AddRoles<EmployeeRole>()
    .AddEntityFrameworkStores<IdentityDbContext>()
    .AddSignInManager();
builder.Services.Configure<IdentityOptions>(options =>
{
    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
});
builder.Services.AddScoped<SyntheticAccountProvisioner>();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "Estacionamento.Session";
    options.Cookie.Path = "/api";
    options.Cookie.Domain = null;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromSeconds(
        builder.Configuration.GetValue<int?>("Authentication:IdleTimeoutSeconds") ?? 1200);
    options.SlidingExpiration = true;
    options.LoginPath = "/api/v1/session/login";
    options.AccessDeniedPath = "/api/v1/session/login";
});
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "Estacionamento.Antiforgery";
    options.Cookie.Path = "/api";
    options.Cookie.Domain = null;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddRateLimiter(options =>
    options.AddFixedWindowLimiter("login", limiter =>
    {
        limiter.PermitLimit = 5;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    }));
builder.Services.AddControllers();

var app = builder.Build();

var provisionOnly = args.Contains("--provision-synthetic-accounts", StringComparer.Ordinal);
if (provisionOnly && !app.Environment.IsDevelopment())
{
    throw new InvalidOperationException("Synthetic accounts can only be provisioned in Development.");
}

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await db.Database.MigrateAsync();
    var provisioner = scope.ServiceProvider.GetRequiredService<SyntheticAccountProvisioner>();
    await provisioner.ProvisionAsync(app.Configuration);
}

if (provisionOnly)
{
    return;
}

var antiforgery = app.Services.GetRequiredService<IAntiforgery>();
var expectedSpaOrigin = app.Configuration["Security:SpaOrigin"]!;
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1
});
app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1/session"))
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "private, no-store, no-cache, max-age=0";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Task.CompletedTask;
        });
    }

    await next();
});
app.Use(async (context, next) =>
{
    var method = context.Request.Method;
    if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)
        || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
    {
        await next();
        return;
    }

    if (!string.Equals(context.Request.Headers.Origin, expectedSpaOrigin, StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    try
    {
        await antiforgery.ValidateRequestAsync(context);
    }
    catch (AntiforgeryValidationException exception)
    {
        app.Logger.LogWarning(
            "Antiforgery validation rejected {Method} {Path}: {Reason}",
            method,
            context.Request.Path,
            exception.Message);
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    await next();
});

app.UseAuthorization();
app.MapGet("/healthz", () => Results.Ok(new { status = "ready" })).AllowAnonymous();
app.MapControllers();

app.Run();

public partial class Program { }

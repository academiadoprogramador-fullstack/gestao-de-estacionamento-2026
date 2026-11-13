using Estacionamento.Api.Configuration;
using Estacionamento.Infrastructure.Identity;
using Estacionamento.Application.Abstractions;
using Estacionamento.Infrastructure.Security;
using Estacionamento.Infrastructure.Persistence;
using Estacionamento.Api.Middleware;
using Estacionamento.Api.OpenApi;
using Estacionamento.Application.Employees;
using Estacionamento.Application.Parking;
using Estacionamento.Application.Billing;
using Estacionamento.Application.Idempotency;
using Estacionamento.Application.Security;
using Estacionamento.Api.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using Estacionamento.Api.Filters;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

var isOpenApiGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
if (!isOpenApiGeneration)
{
    RuntimeConfiguration.ValidateRequired(builder.Configuration);
}

var connectionString = builder.Configuration.GetConnectionString("Identity")
    ?? (isOpenApiGeneration ? "Server=localhost;Database=OpenApiDesignTime;User Id=sa;Password=OpenApiDesignTimeOnly!;TrustServerCertificate=True" : null)!;
var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"]
    ?? (isOpenApiGeneration ? Path.Combine(Path.GetTempPath(), "estacionamento-openapi-keyring") : null)!;
if (!isOpenApiGeneration)
{
    Directory.CreateDirectory(keyRingPath);
    if (!OperatingSystem.IsWindows())
    {
        File.SetUnixFileMode(keyRingPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}

builder.Services.AddDbContextFactory<IdentityDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddScoped<IEmployeeDirectory, EfEmployeeDirectory>();
builder.Services.AddScoped<EmployeeAdministration>();
builder.Services.AddScoped<ISpaceDirectory, EfSpaceDirectory>();
builder.Services.AddScoped<SpaceAvailability>();
builder.Services.AddScoped<IDailyRateDirectory, EfDailyRateDirectory>();
builder.Services.AddScoped<DailyRateAdministration>();
builder.Services.AddScoped<ICheckInStore, EfCheckInStore>();
builder.Services.AddScoped<VehicleCheckIn>();
builder.Services.AddScoped<IAttendanceDirectory, EfAttendanceDirectory>();
builder.Services.AddScoped<AttendanceQuery>();
builder.Services.AddSingleton<IUseCaseAuthorizer, RoleUseCaseAuthorizer>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentActor, HttpCurrentActor>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<EfAuditStore>();
builder.Services.AddScoped<IAuditWriter>(services => services.GetRequiredService<EfAuditStore>());
builder.Services.AddScoped<IAuditReader>(services => services.GetRequiredService<EfAuditStore>());
builder.Services.AddDataProtection()
    .SetApplicationName("GestaoEstacionamento.Local")
    .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
builder.Services.AddSingleton<IFieldProtector, DataProtectionFieldProtector>();
builder.Services.AddSingleton<ICheckInContentHmac, ConfiguredCheckInContentHmac>();
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
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
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
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var http = context.HttpContext;
        var correlationId = http.Response.Headers["X-Correlation-ID"].ToString();
        http.Response.ContentType = "application/problem+json";
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too Many Requests",
            Type = "https://httpstatuses.com/429",
            Instance = http.Request.Path,
            Extensions = { ["code"] = "login_rate_limited", ["correlationId"] = correlationId }
        };
        await http.Response.WriteAsync(
            System.Text.Json.JsonSerializer.Serialize(problem, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
            cancellationToken);
    };
    options.AddFixedWindowLimiter("login", limiter =>
    {
        limiter.PermitLimit = 5;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});
builder.Services.AddControllers(options => options.Filters.Add<ApiProblemDetailsFilter>());
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<ProblemDetailsOpenApiDocumentTransformer>());

var app = builder.Build();
app.UseMiddleware<PrivateResponseMiddleware>();
app.Use(async (context, next) =>
{
    await next();
    if (!context.Request.Path.StartsWithSegments("/api/v1/session")
        && !context.Response.HasStarted
        && context.Response.StatusCode >= 400
        && string.IsNullOrEmpty(context.Response.ContentType)
        && (context.Response.ContentLength is null or 0))
    {
        var status = context.Response.StatusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = status,
            Title = Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(status),
            Type = $"https://httpstatuses.com/{status}",
            Instance = context.Request.Path,
            Extensions =
            {
                ["code"] = status switch
                {
                    StatusCodes.Status401Unauthorized => "unauthenticated",
                    StatusCodes.Status403Forbidden => "forbidden",
                    StatusCodes.Status404NotFound => "not_found",
                    StatusCodes.Status429TooManyRequests => "rate_limited",
                    _ => "request_rejected"
                },
                ["correlationId"] = context.Response.Headers["X-Correlation-ID"].ToString()
            }
        });
    }
});

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
if (app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("OpenApi:Expose"))
{
    app.MapOpenApi().RequireAuthorization(
        new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .RequireRole(Estacionamento.Infrastructure.Identity.EmployeeRoles.Administrator)
            .Build());
}

app.MapGet("/healthz", () => Results.Ok(new { status = "ready" })).AllowAnonymous();
app.MapControllers();

app.Run();

public partial class Program { }

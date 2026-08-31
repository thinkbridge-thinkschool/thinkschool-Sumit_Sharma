using System.Text;
using System.Text.Json.Serialization;
using Hangfire;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Auth;
using QuotesApi.BackgroundJobs;
using QuotesApi.Data;
using QuotesApi.Extensions;
using Serilog;

const string InternalJwtScheme = "InternalJwt";
const string EntraJwtScheme = "EntraJwt";
const string NoCredentialsScheme = "NoCredentials";
const string PolicySchemeName = "PolicyScheme";

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

var jwtOptions = JwtAuthenticationOptionsFactory.Create(
    Options.Create(
        builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
            ?? new JwtOptions()),
    builder.Configuration);

builder.Services
    .AddAuthentication(PolicySchemeName)
    .AddJwtBearer(InternalJwtScheme, options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,

                ValidIssuer = jwtOptions.Issuer,

                ValidAudience = jwtOptions.Audience,

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtOptions.Key))
            };
    })
    .AddJwtBearer(EntraJwtScheme, options =>
    {
        options.Authority = jwtOptions.EntraAuthority;

        // A Managed-Identity-issued app-only v2.0 token for this API's
        // "api://<clientId>" App ID URI actually carries the bare clientId
        // as its "aud" claim, not the URI form - accept both, since a
        // delegated (user) token acquired via the URI-form scope can still
        // carry the URI form. Previously this object was constructed fresh
        // with no ValidAudience(s) at all - with ValidateAudience = true and
        // nothing to match, every real Entra token failed audience
        // validation; this path had never been exercised end-to-end before.
        var clientId = jwtOptions.EntraAudience.Replace("api://", string.Empty);

        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,

                ValidIssuer = jwtOptions.EntraAuthority,
                ValidAudiences = [jwtOptions.EntraAudience, clientId]
            };
    })
    .AddScheme<AuthenticationSchemeOptions, NoCredentialsAuthenticationHandler>(
        NoCredentialsScheme,
        displayName: null,
        configureOptions: _ => { })
    .AddPolicyScheme(PolicySchemeName, PolicySchemeName, options =>
    {
        options.ForwardDefaultSelector = context =>
            JwtSchemeSelector.SelectScheme(
                context.Request.Headers.Authorization.ToString(),
                jwtOptions.Issuer,
                jwtOptions.EntraAuthority,
                InternalJwtScheme,
                EntraJwtScheme,
                NoCredentialsScheme);
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanEditQuotes", policy =>
        policy.RequireAssertion(context =>
            // Internal dev-token scheme (delegated-style "scope" claim) —
            // unchanged, still used by every day's Angular app directly.
            context.User.HasClaim("scope", "quotes.write") ||
            // Entra app-only scheme (Day-17 Managed Identity flow): a
            // client-credentials/MI token carries permissions in a "roles"
            // claim - but JwtBearer's default inbound claim mapping
            // rewrites that to ClaimTypes.Role before it ever reaches here
            // (confirmed live: HasClaim("roles", ...) never matched a real
            // Entra token even though the raw JWT payload clearly had
            // "roles": ["Quotes.Write"] - IsInRole checks the identity's
            // actual RoleClaimType, which is what the mapped claim uses).
            // Additive only - does not replace the check above.
            context.User.IsInRole("Quotes.Write")));

    options.AddPolicy("CanDeleteOwnCollection", policy =>
        policy.Requirements.Add(new CollectionOwnerRequirement()));
});

builder.Services.AddProblemDetails();

builder.Services.AddExceptionHandler(options =>
{
    options.ExceptionHandler = async context =>
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Detail = "The server encountered an unexpected error."
        };

        context.Response.StatusCode =
            StatusCodes.Status500InternalServerError;

        await context.Response.WriteAsJsonAsync(problemDetails);
    };
});

builder.Services.AddInfrastructure(
    builder.Configuration);

builder.Services.AddExternalQuoteClient(
    builder.Configuration);

builder.Services.AddTelemetry(
    builder.Configuration);

builder.Services.AddHealthChecks();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Give an in-flight background work item real time to finish on shutdown
// instead of the framework's 5s default (see QueuedHostedService.StopAsync).
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(20));

builder.Services.AddBackgroundJobs();

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseInMemoryStorage());

builder.Services.AddHangfireServer();

var app = builder.Build();

app.UseTraceIdEnrichment();
app.UseSerilogRequestLogging();

app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<AppDbContext>();

    db.Database.Migrate();
}

app.MapHealthChecks("/health");

app.MapAuthEndpoints();
app.MapQuoteEndpoints();
app.MapCollectionEndpoints();
app.MapExternalQuoteEndpoints();
app.MapBackgroundJobEndpoints();

app.MapHangfireDashboard("/hangfire");

// Scheduled work, the Hangfire way — contrast with PeriodicStatsHostedService
// above. Minutely (not nightly) purely so the dashboard's execution history
// fills in during a short local demo; a production cron schedule would use
// Cron.Daily() or similar.
RecurringJob.AddOrUpdate<HangfireRecurringJobs>(
    "quote-digest",
    job => job.LogQuoteDigestAsync(),
    Cron.Minutely());

app.Run();

// Exposes Program to integration tests.
public partial class Program
{
}

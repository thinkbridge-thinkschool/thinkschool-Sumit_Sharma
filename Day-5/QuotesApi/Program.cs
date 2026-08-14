using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Auth;
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
        options.Audience = jwtOptions.EntraAudience;

        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,

                ValidIssuer = jwtOptions.EntraAuthority
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
        policy.RequireClaim("scope", "quotes.write"));

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

app.Run();

// Exposes Program to integration tests.
public partial class Program
{
}

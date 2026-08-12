using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Auth;
using QuotesApi.Data;
using QuotesApi.Extensions;

const string InternalJwtScheme = "InternalJwt";
const string EntraJwtScheme = "EntraJwt";
const string NoCredentialsScheme = "NoCredentials";
const string PolicySchemeName = "PolicyScheme";

var builder = WebApplication.CreateBuilder(args);

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "JWT key is not configured.");

if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "JWT key must be at least 256 bits.");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException(
        "JWT issuer is not configured.");

var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException(
        "JWT audience is not configured.");

var entraAuthority = builder.Configuration["Entra:Authority"]
    ?? throw new InvalidOperationException(
        "Entra authority is not configured.");

var entraAudience = builder.Configuration["Entra:Audience"]
    ?? throw new InvalidOperationException(
        "Entra audience is not configured.");

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

                ValidIssuer = jwtIssuer,

                ValidAudience = jwtAudience,

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey))
            };
    })
    .AddJwtBearer(EntraJwtScheme, options =>
    {
        options.Authority = entraAuthority;
        options.Audience = entraAudience;

        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,

                ValidIssuer = entraAuthority
            };
    })
    .AddScheme<AuthenticationSchemeOptions, NoCredentialsAuthenticationHandler>(
        NoCredentialsScheme,
        displayName: null,
        configureOptions: _ => { })
    .AddPolicyScheme(PolicySchemeName, PolicySchemeName, options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var header = context.Request.Headers.Authorization
                .ToString();

            if (!header.StartsWith(
                    "Bearer ",
                    StringComparison.OrdinalIgnoreCase))
            {
                return NoCredentialsScheme;
            }

            var token = header["Bearer ".Length..].Trim();
            var jwtHandler = new JwtSecurityTokenHandler();

            if (!jwtHandler.CanReadToken(token))
                return NoCredentialsScheme;

            var issuer = jwtHandler.ReadJwtToken(token).Issuer;

            if (issuer == jwtIssuer)
                return InternalJwtScheme;

            if (issuer == entraAuthority)
                return EntraJwtScheme;

            return NoCredentialsScheme;
        };
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

var app = builder.Build();

app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<AppDbContext>();

    db.Database.Migrate();
}

app.MapAuthEndpoints();
app.MapQuoteEndpoints();
app.MapCollectionEndpoints();

app.Run();

// Exposes Program to integration tests.
public partial class Program
{
}

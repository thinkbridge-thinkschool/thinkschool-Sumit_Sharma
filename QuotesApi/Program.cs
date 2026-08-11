using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Data;
using QuotesApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "JWT key is not configured.");

if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "JWT key must be at least 256 bits.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,

                ValidIssuer =
                    builder.Configuration["Jwt:Issuer"],

                ValidAudience =
                    builder.Configuration["Jwt:Audience"],

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey))
            };
    });

builder.Services.AddAuthorization();

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

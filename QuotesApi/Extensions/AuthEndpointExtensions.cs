using Microsoft.AspNetCore.Mvc;
using QuotesApi.Auth;

namespace QuotesApi.Extensions;

public static class AuthEndpointExtensions
{
    public static void MapAuthEndpoints(
        this WebApplication app)
    {
        app.MapPost("/api/auth/login", async (
            LoginRequest request,
            IAuthService authService,
            CancellationToken cancellationToken) =>
        {
            var token = await authService.LoginAsync(
                request.Email,
                request.Password,
                cancellationToken);

            if (token is null)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(new
            {
                accessToken = token
            });
        });
    }
}

public record LoginRequest(
    string Email,
    string Password);

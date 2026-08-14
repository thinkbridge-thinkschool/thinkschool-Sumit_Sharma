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
            var result = await authService.LoginAsync(
                request.Email,
                request.Password,
                cancellationToken);

            return result is null
                ? Results.Unauthorized()
                : Results.Ok(new
                {
                    access_token = result.AccessToken,
                    refresh_token = result.RefreshToken,
                    expires_in = result.ExpiresIn
                });
        });

        app.MapPost("/api/auth/refresh", async (
            RefreshRequest request,
            IAuthService authService,
            CancellationToken cancellationToken) =>
        {
            var result = await authService.RefreshAsync(
                request.RefreshToken,
                cancellationToken);

            return result is null
                ? Results.Unauthorized()
                : Results.Ok(new
                {
                    access_token = result.AccessToken,
                    refresh_token = result.RefreshToken,
                    expires_in = result.ExpiresIn
                });
        });

        app.MapPost("/api/auth/logout", async (
            RefreshRequest request,
            IAuthService authService,
            CancellationToken cancellationToken) =>
        {
            await authService.LogoutAsync(
                request.RefreshToken,
                cancellationToken);

            return Results.NoContent();
        });
    }
}

public sealed record LoginRequest(
    string Email,
    string Password);

public sealed record RefreshRequest(
    string RefreshToken);

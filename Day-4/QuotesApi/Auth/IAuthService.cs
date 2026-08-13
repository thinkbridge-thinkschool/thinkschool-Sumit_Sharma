namespace QuotesApi.Auth;

public interface IAuthService
{
    Task<TokenPair?> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken);

    Task<TokenPair?> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken);

    Task LogoutAsync(
        string refreshToken,
        CancellationToken cancellationToken);
}

public sealed record TokenPair(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn);

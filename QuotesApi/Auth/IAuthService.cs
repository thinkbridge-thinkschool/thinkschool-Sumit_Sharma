namespace QuotesApi.Auth;

public interface IAuthService
{
    Task<string?> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken);
}

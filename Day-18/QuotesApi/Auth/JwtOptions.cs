namespace QuotesApi.Auth;

public sealed record JwtOptions
{
    public string? Key { get; init; }
    public string? Issuer { get; init; }
    public string? Audience { get; init; }
}

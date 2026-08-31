using System.IdentityModel.Tokens.Jwt;

namespace QuotesApi.Auth;

public static class JwtSchemeSelector
{
    public static string SelectScheme(
        string authorizationHeader,
        string internalIssuer,
        string entraIssuer,
        string internalScheme,
        string entraScheme,
        string noCredentialsScheme)
    {
        if (!authorizationHeader.StartsWith(
                "Bearer ",
                StringComparison.OrdinalIgnoreCase))
        {
            return noCredentialsScheme;
        }

        var token = authorizationHeader["Bearer ".Length..].Trim();
        var jwtHandler = new JwtSecurityTokenHandler();

        if (!jwtHandler.CanReadToken(token))
            return noCredentialsScheme;

        var issuer = jwtHandler.ReadJwtToken(token).Issuer;

        if (issuer == internalIssuer)
            return internalScheme;

        if (issuer == entraIssuer)
            return entraScheme;

        return noCredentialsScheme;
    }
}

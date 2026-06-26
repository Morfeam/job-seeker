using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

namespace job_seeker.Server.Authorization;

public class TokenHandler
{
    private readonly IConfiguration _config;
    private const string PasswordResetPurpose = "password-reset";

    public TokenHandler(IConfiguration config)
    {
        _config = config;
    }

    public string CreateToken(string customerId)
    {
        var expiryMinutes = int.Parse(_config["jwt-expiration-in-minutes"]!);
        return GenerateToken(
            subject: customerId,
            expiry: DateTime.UtcNow.AddMinutes(expiryMinutes),
            extraClaims: null,
            signingKey: GetAccessTokenSigningKey());
    }

    public string CreatePasswordResetToken(string customerId)
    {
        var expiryMinutes = int.Parse(_config["jwt-password-expiration-in-minutes"] ?? "15");
        return GenerateToken(
            subject: customerId,
            expiry: DateTime.UtcNow.AddMinutes(expiryMinutes),
            extraClaims: new Dictionary<string, object> { ["purpose"] = PasswordResetPurpose },
            signingKey: GetResetTokenSigningKey());
    }

    public async Task<string?> ValidatePasswordResetTokenAsync(string token)
    {
        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(token, GetResetTokenValidationParameters());
        if (!result.IsValid) return null;

        var jwt = result.SecurityToken as JsonWebToken;
        if (jwt == null) return null;

        if (!jwt.TryGetClaim("purpose", out var purposeClaim) || purposeClaim.Value != PasswordResetPurpose)
            return null;

        return jwt.Subject;
    }

    // Used only to validate password-reset tokens (TokenHandler.ValidatePasswordResetTokenAsync above);
    // access tokens are validated separately by the JwtBearer middleware in Program.cs.
    // Reset tokens are signed with a different secret (jwt-reset-secret) than access tokens
    // (jwt-secret) so that a leak of one secret can't be used to forge the other token type —
    // password-reset tokens directly change a customer's password, a more severe blast radius
    // than impersonating a session.
    private TokenValidationParameters GetResetTokenValidationParameters() => new()
    {
        IssuerSigningKey = GetResetTokenSigningKey(),
        ValidIssuer = _config["jwt-issuer"],
        ValidAudience = _config["jwt-audience"],
        ClockSkew = TimeSpan.Zero,
    };

    private string GenerateToken(string subject, DateTime expiry, Dictionary<string, object>? extraClaims, SymmetricSecurityKey signingKey)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, subject)]),
            Expires = expiry,
            Issuer = _config["jwt-issuer"],
            Audience = _config["jwt-audience"],
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256),
            Claims = extraClaims
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private SymmetricSecurityKey GetAccessTokenSigningKey() =>
        new(Encoding.UTF8.GetBytes(RequireConfig("jwt-secret")));

    private SymmetricSecurityKey GetResetTokenSigningKey() =>
        new(Encoding.UTF8.GetBytes(RequireConfig("jwt-reset-secret")));

    private string RequireConfig(string key) =>
        _config[key] ?? throw new InvalidOperationException($"Missing required configuration value '{key}'.");
}

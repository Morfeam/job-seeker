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
            extraClaims: null);
    }

    public string CreatePasswordResetToken(string customerId)
    {
        var expiryMinutes = int.Parse(_config["jwt-password-expiration-in-minutes"] ?? "15");
        return GenerateToken(
            subject: customerId,
            expiry: DateTime.UtcNow.AddMinutes(expiryMinutes),
            extraClaims: new Dictionary<string, object> { ["purpose"] = PasswordResetPurpose });
    }

    public async Task<string?> ValidatePasswordResetTokenAsync(string token)
    {
        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(token, GetValidationParameters());
        if (!result.IsValid) return null;

        var jwt = result.SecurityToken as JsonWebToken;
        if (jwt == null) return null;

        if (!jwt.TryGetClaim("purpose", out var purposeClaim) || purposeClaim.Value != PasswordResetPurpose)
            return null;

        return jwt.Subject;
    }

    public TokenValidationParameters GetValidationParameters()
    {
        return new TokenValidationParameters
        {
            IssuerSigningKey = GetSigningKey(),
            ValidIssuer = _config["jwt-issuer"],
            ValidAudience = _config["jwt-audience"],
            ClockSkew = TimeSpan.Zero
        };
    }

    private string GenerateToken(string subject, DateTime expiry, Dictionary<string, object>? extraClaims)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, subject)]),
            Expires = expiry,
            Issuer = _config["jwt-issuer"],
            Audience = _config["jwt-audience"],
            SigningCredentials = new SigningCredentials(GetSigningKey(), SecurityAlgorithms.HmacSha256),
            Claims = extraClaims
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private SymmetricSecurityKey GetSigningKey() =>
        new(Encoding.UTF8.GetBytes(_config["jwt-secret"]!));
}

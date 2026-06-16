using System.Security.Cryptography;

namespace job_seeker.Server.Operations.Auth;

public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 64;
    private const int Iterations = 350_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA512;

    public static string GenerateSalt()
    {
        var salt = new byte[SaltSize];
        RandomNumberGenerator.Fill(salt);
        return Convert.ToBase64String(salt);
    }

    public static string GenerateHash(string password, string saltBase64)
    {
        var salt = Convert.FromBase64String(saltBase64);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);
        return Convert.ToBase64String(hash);
    }

    public static bool Verify(string password, string saltBase64, string hashBase64)
    {
        var hash = GenerateHash(password, saltBase64);
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromBase64String(hash),
            Convert.FromBase64String(hashBase64));
    }
}

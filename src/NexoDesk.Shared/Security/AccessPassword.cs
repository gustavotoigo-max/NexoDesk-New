using System.Security.Cryptography;
using System.Text;
using NexoDesk.Shared.Messages;

namespace NexoDesk.Shared.Security;

/// <summary>Somente o verificador salgado permanece no remoto. Nunca armazenar a senha original.</summary>
public sealed record PasswordVerifier(string Salt, string Hash, int Iterations);
public sealed record PasswordChallenge(string Salt, int Iterations, string Nonce);

public static class AccessPassword
{
    public const int Iterations = 600_000;

    public static PasswordVerifier Create(string password)
    {
        if (password.Length is < 12 or > 128 || password.All(char.IsWhiteSpace))
            throw new ArgumentException("Use uma senha de 12 a 128 caracteres.");
        byte[] salt = RandomNumberGenerator.GetBytes(32);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        try { return new(Convert.ToBase64String(salt), Convert.ToBase64String(hash), Iterations); }
        finally { CryptographicOperations.ZeroMemory(hash); }
    }

    public static PasswordChallenge Challenge(PasswordVerifier verifier) =>
        new(verifier.Salt, verifier.Iterations, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    public static string Prove(string password, PasswordChallenge challenge, string clientFingerprint, string serverFingerprint, SessionMode mode)
    {
        ValidateChallenge(challenge);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(challenge.Salt),
            challenge.Iterations, HashAlgorithmName.SHA256, 32);
        try { return Convert.ToBase64String(HMACSHA256.HashData(key, Transcript(challenge, clientFingerprint, serverFingerprint, mode))); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public static bool Verify(PasswordVerifier verifier, PasswordChallenge challenge, string? proof, string clientFingerprint, string serverFingerprint, SessionMode mode)
    {
        if (proof is null || proof.Length != 44 || verifier.Salt != challenge.Salt || verifier.Iterations != challenge.Iterations)
            return false;
        byte[]? key = null;
        try
        {
            ValidateChallenge(challenge);
            key = Convert.FromBase64String(verifier.Hash);
            if (key.Length != 32) return false;
            byte[] expected = HMACSHA256.HashData(key, Transcript(challenge, clientFingerprint, serverFingerprint, mode));
            return CryptographicOperations.FixedTimeEquals(expected, Convert.FromBase64String(proof));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException) { return false; }
        finally { if (key is not null) CryptographicOperations.ZeroMemory(key); }
    }

    public static void ValidateChallenge(PasswordChallenge challenge)
    {
        if (challenge.Iterations is < 100_000 or > 1_000_000 || challenge.Salt.Length != 44 ||
            challenge.Nonce.Length != 44 || Convert.FromBase64String(challenge.Salt).Length != 32 ||
            Convert.FromBase64String(challenge.Nonce).Length != 32)
            throw new ArgumentException("Desafio de senha inválido.");
    }

    private static byte[] Transcript(PasswordChallenge challenge, string client, string server, SessionMode mode) =>
        Encoding.UTF8.GetBytes($"NexoDesk-password-v1|{challenge.Nonce}|{CertificateFingerprint.Normalize(client)}|{CertificateFingerprint.Normalize(server)}|{(int)mode}");
}
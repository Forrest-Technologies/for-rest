using System.Security.Cryptography;

namespace ForRest.Services.AI.OAuth;

/// <summary>
/// A PKCE (RFC 7636) verifier/challenge pair. The verifier is a high-entropy base64url string;
/// the challenge is the base64url-encoded SHA-256 digest of the verifier (method S256).
/// </summary>
public sealed record PkceCodes(string CodeVerifier, string CodeChallenge)
{
    #region Constants

    public const string ChallengeMethod = "S256";

    #endregion

    #region Public Methods

    /// <summary>
    /// Generates a fresh verifier/challenge pair. The verifier length (in raw bytes) is clamped
    /// to the RFC range that yields a 43-128 character base64url string.
    /// </summary>
    public static PkceCodes Generate(int verifierByteLength = 32)
    {
        // 32 bytes -> 43 base64url chars (the RFC minimum); 96 bytes -> 128 chars (the maximum).
        int clamped = Math.Clamp(verifierByteLength, 32, 96);
        byte[] verifierBytes = RandomNumberGenerator.GetBytes(clamped);
        string verifier = Base64UrlEncode(verifierBytes);
        return FromVerifier(verifier);
    }

    /// <summary>
    /// Derives the challenge for an existing verifier. Useful for tests and for re-deriving from
    /// a stashed verifier.
    /// </summary>
    public static PkceCodes FromVerifier(string codeVerifier)
    {
        if (string.IsNullOrWhiteSpace(codeVerifier))
        {
            throw new ArgumentException("Code verifier must not be empty.", nameof(codeVerifier));
        }

        byte[] digest = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return new(codeVerifier, Base64UrlEncode(digest));
    }

    public static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    #endregion
}

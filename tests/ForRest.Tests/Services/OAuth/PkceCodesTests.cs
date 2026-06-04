using System.Security.Cryptography;
using System.Text;
using ForRest.Services.AI.OAuth;

namespace ForRest.Tests.Services.OAuth;

[TestClass]
public sealed class PkceCodesTests
{
    [TestMethod]
    public void Generate_produces_base64url_verifier_within_rfc_length()
    {
        var codes = PkceCodes.Generate();

        Assert.IsTrue(codes.CodeVerifier.Length is >= 43 and <= 128, $"verifier length {codes.CodeVerifier.Length}");
        Assert.IsTrue(IsBase64Url(codes.CodeVerifier), "verifier must be base64url");
        Assert.IsTrue(IsBase64Url(codes.CodeChallenge), "challenge must be base64url");
    }

    [TestMethod]
    public void Generate_clamps_byte_length_to_valid_range()
    {
        var tooSmall = PkceCodes.Generate(1);
        var tooLarge = PkceCodes.Generate(1000);

        Assert.IsTrue(tooSmall.CodeVerifier.Length >= 43);
        Assert.IsTrue(tooLarge.CodeVerifier.Length <= 128);
    }

    [TestMethod]
    public void Challenge_is_s256_of_verifier()
    {
        var codes = PkceCodes.Generate();

        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(codes.CodeVerifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        Assert.AreEqual(expected, codes.CodeChallenge);
        Assert.AreEqual("S256", PkceCodes.ChallengeMethod);
    }

    [TestMethod]
    public void FromVerifier_derives_same_challenge_as_known_vector()
    {
        // RFC 7636 Appendix B test vector.
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string expectedChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        var codes = PkceCodes.FromVerifier(verifier);

        Assert.AreEqual(expectedChallenge, codes.CodeChallenge);
    }

    [TestMethod]
    public void Generate_produces_distinct_verifiers()
    {
        var a = PkceCodes.Generate();
        var b = PkceCodes.Generate();

        Assert.AreNotEqual(a.CodeVerifier, b.CodeVerifier);
    }

    [TestMethod]
    public void FromVerifier_rejects_empty()
    {
        Assert.ThrowsExactly<ArgumentException>(() => PkceCodes.FromVerifier("  "));
    }

    private static bool IsBase64Url(string value)
    {
        foreach (var c in value)
        {
            bool ok = c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';
            if (!ok)
            {
                return false;
            }
        }

        return value.Length > 0;
    }
}

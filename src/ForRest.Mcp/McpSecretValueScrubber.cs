using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ForRest.Mcp;

/// <summary>
/// Replaces known secret values with <c>***</c> in text leaving for external MCP
/// agents. Declaration-level redaction (<see cref="McpSecretRedactor"/>,
/// <see cref="McpRawRequestRedactor"/>) hides where secrets are defined, but a
/// script can still copy a secret into its own output — console logs, test
/// failure messages, stash cells — or a server can echo it back in a response.
/// The host supplies the run's known secret values; this scrubber removes them
/// (and their Base64 / URL-encoded / JSON-escaped forms, since scripts routinely
/// re-encode credentials) from any payload before it crosses the boundary.
/// </summary>
public static class McpSecretValueScrubber
{
    #region Private Fields

    private const string RedactedValue = "***";

    // Matches the encoder ForRestMcpTools uses to serialize payloads, so the escaped form
    // of a secret inside a JSON string is caught as well as the raw form.
    private static readonly JsonSerializerOptions EncodingOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    #endregion

    #region Public Methods

    /// <summary>Returns <paramref name="text"/> with every known secret value (and its common encodings) replaced by <c>***</c>.</summary>
    public static string Scrub(string? text, IReadOnlyCollection<string>? secretValues)
    {
        if (string.IsNullOrEmpty(text) || secretValues is null || secretValues.Count == 0)
        {
            return text ?? string.Empty;
        }

        return ScrubKnownForms(text, BuildNeedles(secretValues));
    }

    #endregion

    #region Internal Methods

    internal static Func<string, string> CreateScrubber(IReadOnlyCollection<string> secretValues)
    {
        var needles = BuildNeedles(secretValues);
        return value => ScrubKnownForms(value, needles);
    }

    #endregion

    #region Private Methods

    private static IReadOnlyList<string> BuildNeedles(IReadOnlyCollection<string> secretValues)
    {
        List<string> needles = [];
        foreach (var value in secretValues)
        {
            if (!string.IsNullOrEmpty(value))
            {
                AddNeedleVariants(needles, value);
            }
        }

        return needles.Distinct(StringComparer.Ordinal).OrderByDescending(static needle => needle.Length).ToArray();
    }

    private static string ScrubKnownForms(string text, IReadOnlyList<string> needles)
    {
        var scrubbed = text;
        foreach (var needle in needles)
        {
            scrubbed = scrubbed.Replace(needle, RedactedValue, StringComparison.Ordinal);
        }

        return scrubbed;
    }

    private static void AddNeedleVariants(List<string> needles, string value)
    {
        AddWithJsonEscapedForm(needles, value);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        AddWithJsonEscapedForm(needles, base64);
        AddWithJsonEscapedForm(needles, base64.TrimEnd('='));
        var base64Url = base64.Replace('+', '-').Replace('/', '_');
        AddWithJsonEscapedForm(needles, base64Url);
        AddWithJsonEscapedForm(needles, base64Url.TrimEnd('='));
        AddUrlEncodedForms(needles, Uri.EscapeDataString(value));
        AddUrlEncodedForms(needles, WebUtility.UrlEncode(value));
    }

    private static void AddUrlEncodedForms(List<string> needles, string encoded)
    {
        AddWithJsonEscapedForm(needles, encoded);
        var characters = encoded.ToCharArray();
        for (var index = 0; index + 2 < characters.Length; index++)
        {
            if (characters[index] == '%' && Uri.IsHexDigit(characters[index + 1]) && Uri.IsHexDigit(characters[index + 2]))
            {
                characters[index + 1] = char.ToLowerInvariant(characters[index + 1]);
                characters[index + 2] = char.ToLowerInvariant(characters[index + 2]);
                index += 2;
            }
        }

        AddWithJsonEscapedForm(needles, new string(characters));
    }

    private static void AddWithJsonEscapedForm(List<string> needles, string value)
    {
        needles.Add(value);

        string serialized = JsonSerializer.Serialize(value, EncodingOptions);
        string jsonEscaped = serialized[1..^1];
        if (!string.Equals(jsonEscaped, value, StringComparison.Ordinal))
        {
            needles.Add(jsonEscaped);
        }
    }

    #endregion
}

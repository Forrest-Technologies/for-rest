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

    // Values shorter than this are skipped: scrubbing trivial strings like "1" or "true"
    // would shred unrelated output, and such values carry no real secrecy.
    private const int MinimumSecretLength = 6;

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

        List<string> needles = [];
        foreach (string value in secretValues)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < MinimumSecretLength)
            {
                continue;
            }

            AddNeedleVariants(needles, value);
        }

        if (needles.Count == 0)
        {
            return text;
        }

        string scrubbed = text;
        foreach (string needle in needles.Distinct(StringComparer.Ordinal).OrderByDescending(static needle => needle.Length))
        {
            scrubbed = scrubbed.Replace(needle, RedactedValue, StringComparison.Ordinal);
        }

        return scrubbed;
    }

    #endregion

    #region Private Methods

    private static void AddNeedleVariants(List<string> needles, string value)
    {
        AddWithJsonEscapedForm(needles, value);
        AddWithJsonEscapedForm(needles, Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));

        string urlEncoded = Uri.EscapeDataString(value);
        if (!string.Equals(urlEncoded, value, StringComparison.Ordinal))
        {
            AddWithJsonEscapedForm(needles, urlEncoded);
        }
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

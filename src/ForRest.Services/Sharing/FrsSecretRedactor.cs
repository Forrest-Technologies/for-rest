using System.Text.RegularExpressions;

namespace ForRest.Services.Sharing;

/// <summary>
/// Redacts <c>secret name = value</c> declarations in ForRest scripts so shared
/// or exported documents never carry raw secret values. Mirrors the redaction
/// performed on the MCP and in-app AI paths so all sharing surfaces stay consistent.
/// </summary>
public static partial class FrsSecretRedactor
{
    #region Private Fields

    private const string RedactedSecretMarker = "\"***\"";

    [GeneratedRegex(
        @"^(?<indent>[ \t]*)secret[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \t]*=[ \t]*(?<value>.+?)[ \t]*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex SecretDeclarationPattern();

    #endregion

    #region Public Methods

    /// <summary>Replaces every secret declaration's right-hand side with <c>"***"</c>, preserving indentation and name.</summary>
    public static string Redact(string? source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return source ?? string.Empty;
        }

        return SecretDeclarationPattern().Replace(source, static match =>
        {
            string indent = match.Groups["indent"].Value;
            string name = match.Groups["name"].Value;
            return $"{indent}secret {name} = {RedactedSecretMarker}";
        });
    }

    /// <summary>Returns true when the source contains at least one secret declaration.</summary>
    public static bool ContainsSecrets(string? source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return false;
        }

        return SecretDeclarationPattern().IsMatch(source);
    }

    #endregion
}

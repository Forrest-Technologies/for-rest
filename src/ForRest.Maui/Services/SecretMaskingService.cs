using System.Text.RegularExpressions;

namespace ForRest.Maui.Services;

/// <summary>
/// Replaces the right-hand side of every <c>secret &lt;name&gt; = …</c>
/// declaration in a script source with a fixed mask. Used by the editor
/// presentation layer to hide secret values when the request editor is
/// not actively being edited.
///
/// The mask matches the redaction the AI tool path uses
/// (<see cref="ForRest.Services.AI.AiActiveDocumentToolService"/>) so the
/// user sees the same opaque marker the model sees.
/// </summary>
public static class SecretMaskingService
{
    #region Private Fields

    private static readonly Regex SecretDeclarationPattern = new(
        @"^(?<indent>[ \t]*)secret[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \t]*=[ \t]*(?<value>.+?)[ \t]*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private const string MaskedValue = "\"***\"";

    #endregion

    #region Public Methods

    /// <summary>
    /// Returns <paramref name="sourceText"/> with every secret value
    /// replaced by a fixed mask. The keyword and identifier are kept so
    /// the user can still tell which secret lives where.
    /// </summary>
    public static string MaskSecrets(string? sourceText)
    {
        if (string.IsNullOrEmpty(sourceText))
        {
            return sourceText ?? string.Empty;
        }

        return SecretDeclarationPattern.Replace(sourceText, match =>
        {
            string indent = match.Groups["indent"].Value;
            string name = match.Groups["name"].Value;
            return $"{indent}secret {name} = {MaskedValue}";
        });
    }

    /// <summary>
    /// Returns true when <paramref name="sourceText"/> contains at least
    /// one secret declaration. Lets the view model skip the focus-masking
    /// dance entirely for documents that have no secrets.
    /// </summary>
    public static bool ContainsSecrets(string? sourceText)
    {
        return !string.IsNullOrEmpty(sourceText) && SecretDeclarationPattern.IsMatch(sourceText);
    }

    #endregion
}

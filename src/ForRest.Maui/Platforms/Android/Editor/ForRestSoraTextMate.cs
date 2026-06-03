using System.Diagnostics;
using IO.Github.Rosemoe.Sora.Langs.Textmate;
using IO.Github.Rosemoe.Sora.Langs.Textmate.Registry;
using IO.Github.Rosemoe.Sora.Langs.Textmate.Registry.Model;
using IO.Github.Rosemoe.Sora.Langs.Textmate.Registry.Provider;
using IO.Github.Rosemoe.Sora.Widget;
using IO.Github.Rosemoe.Sora.Widget.Schemes;
using Org.Eclipse.Tm4e.Core.Registry;

namespace ForRest.Maui.Platforms.Android.Editor;

/// <summary>
/// TextMate grammar / theme integration for the Sora editor. Loads the bundled ForRest grammars
/// (forrest, json, settings-toml) and theme JSON (the five ForRest themes) from the app's Android
/// assets and exposes them as Sora languages and colour schemes. Every entry point is defensive:
/// when the TextMate stack is unavailable the caller falls back to plain text + programmatic chrome,
/// so a grammar/theme problem never breaks editing.
/// </summary>
internal static class ForRestSoraTextMate
{
    #region Constants

    private const string LanguagesConfigPath = "sora/textmate/languages.json";
    private const string ThemeDirectory = "sora/textmate/themes";

    private static readonly Dictionary<string, string> ScopeByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["forrest"] = "source.forrest",
        ["json"] = "source.json",
        ["settings-toml"] = "source.toml",
    };

    private static readonly string[] ThemeKeys =
    [
        "forrest-light", "forrest-azure", "forrest-dark", "forrest-black", "forrest-amber",
    ];

    #endregion

    #region Private Fields

    private static readonly object SyncRoot = new();
    private static bool registrationAttempted;
    private static bool registrationSucceeded;

    #endregion

    #region Public Methods

    public static bool TryApplyLanguage(CodeEditor editor, string languageId)
    {
        if (!ScopeByLanguage.TryGetValue(languageId, out string? scope))
        {
            return false;
        }

        if (!EnsureRegistered())
        {
            return false;
        }

        try
        {
            TextMateLanguage language = TextMateLanguage.Create(scope, true);
            editor.EditorLanguage = language;
            return true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[ForRestSoraTextMate] Failed to apply language '{languageId}'.{Environment.NewLine}{exception}");
            return false;
        }
    }

    public static EditorColorScheme? TryBuildColorScheme(string themeKey)
    {
        if (!EnsureRegistered())
        {
            return null;
        }

        try
        {
            ThemeRegistry.Instance.SetTheme(themeKey);
            return TextMateColorScheme.Create(ThemeRegistry.Instance);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[ForRestSoraTextMate] Failed to build colour scheme for '{themeKey}'.{Environment.NewLine}{exception}");
            return null;
        }
    }

    public static void TryApplyCompletions(CodeEditor editor, string languageId, string languageHelpJson)
    {
        // TextMate languages already provide identifier auto-completion. The richer language-help
        // catalogue (snippets, summaries) is surfaced through the same JSON the desktop build uses;
        // wiring it into a Sora CompletionPublisher is tracked as a follow-up so the core editor and
        // highlighting can land first. Intentionally a no-op when no catalogue is present.
    }

    #endregion

    #region Registration

    private static bool EnsureRegistered()
    {
        if (registrationAttempted)
        {
            return registrationSucceeded;
        }

        lock (SyncRoot)
        {
            if (registrationAttempted)
            {
                return registrationSucceeded;
            }

            registrationAttempted = true;
            try
            {
                FileProviderRegistry.Instance.AddFileProvider(
                    new AssetsFileResolver(global::Android.App.Application.Context.Assets));
                GrammarRegistry.Instance.LoadGrammars(LanguagesConfigPath);
                LoadThemes();
                registrationSucceeded = true;
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[ForRestSoraTextMate] TextMate registration failed; using plain-text fallback.{Environment.NewLine}{exception}");
                registrationSucceeded = false;
            }

            return registrationSucceeded;
        }
    }

    private static void LoadThemes()
    {
        foreach (string themeKey in ThemeKeys)
        {
            string path = $"{ThemeDirectory}/{themeKey}.json";
            try
            {
                IThemeSource source = IThemeSource.FromInputStream(
                    FileProviderRegistry.Instance.TryGetInputStream(path), path, null);
                ThemeRegistry.Instance.LoadTheme(new ThemeModel(source, themeKey));
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[ForRestSoraTextMate] Failed to load theme '{themeKey}'.{Environment.NewLine}{exception}");
            }
        }
    }

    #endregion
}

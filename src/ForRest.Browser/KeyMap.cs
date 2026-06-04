namespace ForRest.Browser;

/// <summary>A resolved keyboard key ready to dispatch over CDP.</summary>
public readonly record struct KeyStroke(string Key, string Code, int VirtualKeyCode, string? Text);

/// <summary>
/// Resolves script-friendly key names (e.g. <c>Enter</c>, <c>Tab</c>, <c>a</c>) into the fields CDP's
/// <c>Input.dispatchKeyEvent</c> expects. Named keys send no text (so they act as control keys);
/// single printable characters send their text so they are inserted.
/// </summary>
public static class KeyMap
{
    #region Private Fields

    private static readonly Dictionary<string, KeyStroke> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = new("Enter", "Enter", 13, null),
        ["Return"] = new("Enter", "Enter", 13, null),
        ["Tab"] = new("Tab", "Tab", 9, null),
        ["Escape"] = new("Escape", "Escape", 27, null),
        ["Esc"] = new("Escape", "Escape", 27, null),
        ["Backspace"] = new("Backspace", "Backspace", 8, null),
        ["Delete"] = new("Delete", "Delete", 46, null),
        ["Space"] = new(" ", "Space", 32, " "),
        ["ArrowUp"] = new("ArrowUp", "ArrowUp", 38, null),
        ["ArrowDown"] = new("ArrowDown", "ArrowDown", 40, null),
        ["ArrowLeft"] = new("ArrowLeft", "ArrowLeft", 37, null),
        ["ArrowRight"] = new("ArrowRight", "ArrowRight", 39, null),
        ["Home"] = new("Home", "Home", 36, null),
        ["End"] = new("End", "End", 35, null),
        ["PageUp"] = new("PageUp", "PageUp", 33, null),
        ["PageDown"] = new("PageDown", "PageDown", 34, null),
    };

    #endregion

    #region Public Methods

    public static KeyStroke Resolve(string key)
    {
        string trimmed = key.Trim();

        // Use the final segment of a chord (e.g. "Ctrl+Enter") as the dispatched key.
        int plus = trimmed.LastIndexOf('+');
        if (plus > 0 && plus < trimmed.Length - 1)
        {
            trimmed = trimmed[(plus + 1)..].Trim();
        }

        if (Named.TryGetValue(trimmed, out KeyStroke stroke))
        {
            return stroke;
        }

        if (trimmed.Length == 1)
        {
            char character = trimmed[0];
            string code = char.IsLetter(character)
                ? "Key" + char.ToUpperInvariant(character)
                : char.IsDigit(character) ? "Digit" + character : string.Empty;
            return new KeyStroke(trimmed, code, char.ToUpperInvariant(character), trimmed);
        }

        return new KeyStroke(trimmed, string.Empty, 0, null);
    }

    #endregion
}

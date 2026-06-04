namespace ForRest.Browser;

/// <summary>
/// The strategy used to locate an element inside the automated browser page.
/// </summary>
public enum BrowserTargetKind
{
    Css,
    Xpath,
    Text,
    TestId,
    Role,
}

/// <summary>
/// A resilient, serializable description of how to find a single element. Authored either by the
/// recorder (which prefers the most stable strategy available) or by hand in a script.
/// </summary>
public sealed record BrowserTarget
{
    #region Properties

    public BrowserTargetKind Kind { get; init; }

    /// <summary>Selector text, xpath, exact text, test id, or ARIA role depending on <see cref="Kind"/>.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Accessible name used only when <see cref="Kind"/> is <see cref="BrowserTargetKind.Role"/>.</summary>
    public string? Name { get; init; }

    #endregion

    #region Public Methods

    public static BrowserTarget Css(string selector) => new() { Kind = BrowserTargetKind.Css, Value = selector };

    public static BrowserTarget Xpath(string xpath) => new() { Kind = BrowserTargetKind.Xpath, Value = xpath };

    public static BrowserTarget Text(string text) => new() { Kind = BrowserTargetKind.Text, Value = text };

    public static BrowserTarget TestId(string id) => new() { Kind = BrowserTargetKind.TestId, Value = id };

    public static BrowserTarget Role(string role, string? name = null) =>
        new() { Kind = BrowserTargetKind.Role, Value = role, Name = name };

    /// <summary>
    /// Parses a compact script-facing target expression. Supported forms:
    /// <c>css=...</c>, <c>xpath=...</c>, <c>text=...</c>, <c>testid=...</c>,
    /// <c>role=button</c> or <c>role=button:Save</c>. Bare values are inferred:
    /// a leading <c>//</c> or <c>(</c> is treated as xpath, otherwise the value is treated as css.
    /// </summary>
    public static BrowserTarget Parse(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new ArgumentException("A browser target expression is required.", nameof(expression));
        }

        string trimmed = expression.Trim();
        int separator = trimmed.IndexOf('=');
        if (separator > 0)
        {
            string prefix = trimmed[..separator].Trim().ToLowerInvariant();
            string value = trimmed[(separator + 1)..].Trim();
            switch (prefix)
            {
                case "css":
                    return Css(value);
                case "xpath":
                    return Xpath(value);
                case "text":
                    return Text(value);
                case "testid":
                case "test-id":
                case "data-testid":
                    return TestId(value);
                case "role":
                    int nameSeparator = value.IndexOf(':');
                    return nameSeparator > 0
                        ? Role(value[..nameSeparator].Trim(), value[(nameSeparator + 1)..].Trim())
                        : Role(value);
            }
        }

        if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
            trimmed.StartsWith("(", StringComparison.Ordinal))
        {
            return Xpath(trimmed);
        }

        return Css(trimmed);
    }

    public override string ToString() => Kind == BrowserTargetKind.Role && !string.IsNullOrEmpty(Name)
        ? $"role={Value}:{Name}"
        : $"{Kind.ToString().ToLowerInvariant()}={Value}";

    #endregion
}

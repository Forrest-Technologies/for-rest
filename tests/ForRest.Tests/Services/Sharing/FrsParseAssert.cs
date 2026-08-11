namespace ForRest.Tests.Services.Sharing;

/// <summary>Asserts that generated .frs content actually parses with the real ForRest script parser.</summary>
internal static class FrsParseAssert
{
    #region Public Methods

    public static void Parses(string source)
    {
        ForRestScriptParser parser = new();
        ForRestScriptParseResult result = parser.Parse(source);
        string diagnosticSummary = string.Join(
            "; ",
            result.Diagnostics.Select(static diagnostic => $"{diagnostic.Severity} line {diagnostic.Line}: {diagnostic.Message}"));

        Assert.IsTrue(result.Succeeded, $"Generated .frs did not parse. Diagnostics: {diagnosticSummary}\nSource:\n{source}");
        Assert.IsFalse(
            result.Diagnostics.Any(static diagnostic => diagnostic.Severity == ForRestScriptDiagnosticSeverity.Error),
            $"Generated .frs produced error diagnostics: {diagnosticSummary}\nSource:\n{source}");
    }

    #endregion
}

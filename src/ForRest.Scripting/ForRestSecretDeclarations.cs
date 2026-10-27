using System.Text;
using System.Text.RegularExpressions;

namespace ForRest.Scripting;

/// <summary>A complete secret declaration and its original source range.</summary>
public sealed record ForRestSecretDeclaration(int Start, int Length, string Indent, string Name, string Text);

/// <summary>Finds secret declarations using the flow compiler's statement boundaries.</summary>
public static partial class ForRestSecretDeclarations
{
    #region Private Fields

    // Incomplete identifiers still need privacy while a document is being edited.
    // Statement validation remains the compiler's responsibility.
    [GeneratedRegex(@"^(?<indent>[ \t]*)secret[ \t]+(?<name>[^\s=]+)[ \t]*=[ \t]*", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex DeclarationStartPattern();

    #endregion

    #region Public Methods

    public static IReadOnlyList<ForRestSecretDeclaration> Read(string? source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return [];
        }

        var lines = source.Split('\n');
        var starts = new int[lines.Length];
        for (var index = 1; index < lines.Length; index++)
        {
            starts[index] = starts[index - 1] + lines[index - 1].Length + 1;
        }

        List<ForRestSecretDeclaration> declarations = [];
        var previousEnd = 0;
        foreach (Match match in DeclarationStartPattern().Matches(source))
        {
            if (match.Index < previousEnd)
            {
                continue;
            }

            var line = Array.BinarySearch(starts, match.Index);
            var count = ForRestFlowScriptCompiler.GetStatementLineCount(lines, line);
            var lastLine = Math.Min(lines.Length - 1, line + Math.Max(1, count) - 1);
            var end = starts[lastLine] + lines[lastLine].TrimEnd('\r').Length;
            declarations.Add(new(match.Index, end - match.Index, match.Groups["indent"].Value,
                match.Groups["name"].Value, source[match.Index..end]));
            previousEnd = end;
        }

        return declarations;
    }

    public static bool HasRepeatedNames(string? source)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        return Read(source).Any(declaration => !names.Add(declaration.Name));
    }

    public static string Replace(string? source, Func<ForRestSecretDeclaration, string> replacement)
    {
        if (string.IsNullOrEmpty(source))
        {
            return source ?? string.Empty;
        }

        var declarations = Read(source);
        if (declarations.Count == 0)
        {
            return source;
        }

        var result = new StringBuilder();
        var position = 0;
        foreach (var declaration in declarations)
        {
            result.Append(source, position, declaration.Start - position);
            result.Append(replacement(declaration));
            position = declaration.Start + declaration.Length;
        }

        result.Append(source, position, source.Length - position);
        return result.ToString();
    }

    #endregion
}

using System.Text.RegularExpressions;

namespace ForRest.Scripting;

public sealed class ForRestScriptDocumentTextService
{
    private static readonly Regex MetaSectionPattern = CreateSectionPattern("meta");
    private static readonly Regex VarsSectionPattern = CreateSectionPattern("vars");
    private static readonly Regex HeadersSectionPattern = CreateSectionPattern("headers");
    private static readonly Regex TestsSectionPattern = CreateSectionPattern("tests");
    private static readonly Regex BodySectionPattern = new(
        "(?ms)^[ \\t]*body[ \\t]+(?<mode>[A-Za-z_][\\w]*)[ \\t]+\"\"\"\\s*\\r?\\n(?<body>.*?)\\r?\\n\"\"\"[ \\t]*(?:\\r?\\n|$)",
        RegexOptions.Compiled);
    private static readonly Regex MetaNamePattern = new(
        "(?m)^[ \\t]*name[ \\t]*=[ \\t]*\"(?<name>(?:[^\"\\\\]|\\\\.)*)\"[ \\t]*$",
        RegexOptions.Compiled);

    public ForRestScriptEditableSections Extract(string source)
    {
        var normalized = Normalize(source);
        return new()
        {
            Name = ExtractName(normalized),
            Variables = ExtractSectionBody(normalized, VarsSectionPattern),
            Headers = ExtractSectionBody(normalized, HeadersSectionPattern),
            Tests = ExtractSectionBody(normalized, TestsSectionPattern),
            BodyMode = ExtractBodyMode(normalized),
            Body = ExtractBody(normalized),
        };
    }

    public string UpsertMetaName(string source, string name)
    {
        var normalized = Normalize(source);
        var metaBody = ExtractSectionBody(normalized, MetaSectionPattern);

        if (string.IsNullOrWhiteSpace(metaBody))
        {
            return InsertAtStart(normalized, BuildSection("meta", BuildMetaNameLine(name)));
        }

        var updatedBody = MetaNamePattern.IsMatch(metaBody)
            ? MetaNamePattern.Replace(metaBody, _ => BuildMetaNameLine(name), 1)
            : string.Join('\n', TrimBlankLines(metaBody).Append(BuildMetaNameLine(name)));

        return UpsertStandardSection(normalized, "meta", updatedBody, MetaSectionPattern);
    }

    public string UpsertVariables(string source, string body)
    {
        return UpsertStandardSection(Normalize(source), "vars", body, VarsSectionPattern);
    }

    public string UpsertHeaders(string source, string body)
    {
        return UpsertStandardSection(Normalize(source), "headers", body, HeadersSectionPattern);
    }

    public string UpsertTests(string source, string body)
    {
        return UpsertStandardSection(Normalize(source), "tests", body, TestsSectionPattern);
    }

    public string UpsertBody(string source, RequestBodyMode mode, string body)
    {
        var normalized = Normalize(source);
        var replacement = BuildBodySection(mode, body);
        var match = BodySectionPattern.Match(normalized);
        if (match.Success)
        {
            return ReplaceMatch(normalized, match, replacement);
        }

        return AppendSection(normalized, replacement);
    }

    private static string ExtractName(string source)
    {
        var metaBody = ExtractSectionBody(source, MetaSectionPattern);
        if (string.IsNullOrWhiteSpace(metaBody))
        {
            return string.Empty;
        }

        var match = MetaNamePattern.Match(metaBody);
        return match.Success ? Regex.Unescape(match.Groups["name"].Value) : string.Empty;
    }

    private static RequestBodyMode ExtractBodyMode(string source)
    {
        var match = BodySectionPattern.Match(source);
        if (!match.Success)
        {
            return RequestBodyMode.Json;
        }

        return match.Groups["mode"].Value.Trim().ToLowerInvariant() switch
        {
            "json" => RequestBodyMode.Json,
            "raw" => RequestBodyMode.RawText,
            "text" => RequestBodyMode.RawText,
            _ => RequestBodyMode.Json
        };
    }

    private static string ExtractBody(string source)
    {
        var match = BodySectionPattern.Match(source);
        if (!match.Success)
        {
            return string.Empty;
        }

        return TrimSharedIndent(match.Groups["body"].Value).Trim('\n');
    }

    private static string ExtractSectionBody(string source, Regex pattern)
    {
        var match = pattern.Match(source);
        if (!match.Success)
        {
            return string.Empty;
        }

        return TrimSharedIndent(match.Groups["body"].Value).Trim('\n');
    }

    private static string UpsertStandardSection(string source, string sectionName, string body, Regex pattern)
    {
        var replacement = BuildSection(sectionName, body);
        var match = pattern.Match(source);
        if (match.Success)
        {
            return ReplaceMatch(source, match, replacement);
        }

        return AppendSection(source, replacement);
    }

    private static string ReplaceMatch(string source, Match match, string replacement)
    {
        return Normalize(source[..match.Index] + replacement + source[(match.Index + match.Length)..]).Trim('\n');
    }

    private static string AppendSection(string source, string section)
    {
        var normalized = Normalize(source).Trim('\n');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return section;
        }

        return $"{normalized}\n\n{section}";
    }

    private static string InsertAtStart(string source, string section)
    {
        var normalized = Normalize(source).Trim('\n');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return section;
        }

        return $"{section}\n\n{normalized}";
    }

    private static string BuildSection(string sectionName, string body)
    {
        var lines = TrimBlankLines(body);
        if (lines.Length == 0)
        {
            return $"{sectionName} {{\n}}";
        }

        return $"{sectionName} {{\n{string.Join('\n', lines.Select(static line => $"  {line}"))}\n}}";
    }

    private static string BuildBodySection(RequestBodyMode mode, string body)
    {
        var bodyMode = mode switch
        {
            RequestBodyMode.RawText => "raw",
            RequestBodyMode.Json => "json",
            _ => "text",
        };

        var lines = Normalize(body).Trim('\n').Split('\n');
        var indentedBody = lines.Length == 1 && string.IsNullOrEmpty(lines[0])
            ? string.Empty
            : string.Join('\n', lines.Select(static line => $"  {line}"));

        return string.IsNullOrEmpty(indentedBody)
            ? $"body {bodyMode} \"\"\"\n\"\"\""
            : $"body {bodyMode} \"\"\"\n{indentedBody}\n\"\"\"";
    }

    private static string Normalize(string source)
    {
        return (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static string TrimSharedIndent(string source)
    {
        var lines = Normalize(source).Split('\n');
        var nonEmptyLines = lines.Where(static line => !string.IsNullOrWhiteSpace(line)).ToList();
        if (nonEmptyLines.Count == 0)
        {
            return string.Empty;
        }

        var trimWidth = nonEmptyLines.Min(
            static line => line.TakeWhile(static character => character is ' ' or '\t').Count());

        return string.Join(
            '\n',
            lines.Select(line => line.Length >= trimWidth ? line[trimWidth..] : line));
    }

    private static string[] TrimBlankLines(string source)
    {
        return Normalize(source)
            .Split('\n')
            .SkipWhile(static line => string.IsNullOrWhiteSpace(line))
            .Reverse()
            .SkipWhile(static line => string.IsNullOrWhiteSpace(line))
            .Reverse()
            .ToArray();
    }

    private static string EscapeString(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string BuildMetaNameLine(string name)
    {
        return $"name = \"{EscapeString(name)}\"";
    }

    private static Regex CreateSectionPattern(string sectionName)
    {
        return new Regex(
            "(?ms)^[ \\t]*"
            + Regex.Escape(sectionName)
            + "[ \\t]*\\{\\s*\\r?\\n(?<body>.*?)\\r?\\n\\}[ \\t]*(?:\\r?\\n|$)",
            RegexOptions.Compiled);
    }
}

public sealed record ForRestScriptEditableSections
{
    public string Name { get; init; } = string.Empty;

    public string Variables { get; init; } = string.Empty;

    public string Headers { get; init; } = string.Empty;

    public RequestBodyMode BodyMode { get; init; } = RequestBodyMode.Json;

    public string Body { get; init; } = string.Empty;

    public string Tests { get; init; } = string.Empty;
}

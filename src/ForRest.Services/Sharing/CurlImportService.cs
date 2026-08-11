namespace ForRest.Services.Sharing;

public interface ICurlImportService
{
    /// <summary>Returns true when the text looks like a curl command line (allowing leading shell prompt characters).</summary>
    bool LooksLikeCurl(string? text);

    /// <summary>Converts a curl command line into an idiomatic .frs request script.</summary>
    string ConvertToScript(string curlCommand, out string suggestedName);
}

public sealed class CurlImportService : ICurlImportService
{
    #region Private Fields

    /// <summary>Flags curl treats as taking a value which For-Rest does not map onto the script.</summary>
    private static readonly HashSet<string> ignoredValueFlags = new(StringComparer.Ordinal)
    {
        "-o", "--output",
        "-w", "--write-out",
        "-c", "--cookie-jar",
        "-x", "--proxy",
        "-F", "--form",
        "-T", "--upload-file",
        "--connect-timeout",
        "--retry",
        "--retry-delay",
        "--retry-max-time",
        "--cacert",
        "--capath",
        "--cert",
        "--key",
        "--limit-rate",
        "--resolve",
        "--interface",
        "--dns-servers",
        "--unix-socket",
        "--ciphers",
        "--proxy-user",
        "--range",
        "-r",
    };

    #endregion

    #region Public Methods

    public bool LooksLikeCurl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = StripPromptPrefix(text.Trim());
        return trimmed.StartsWith("curl ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("curl.exe ", StringComparison.OrdinalIgnoreCase);
    }

    public string ConvertToScript(string curlCommand, out string suggestedName)
    {
        ArgumentNullException.ThrowIfNull(curlCommand);

        List<string> tokens = Tokenize(JoinContinuations(StripPromptPrefix(curlCommand.Trim())));
        ParsedCurl parsed = ParseTokens(tokens);
        suggestedName = BuildSuggestedName(parsed);
        return Render(parsed, suggestedName);
    }

    #endregion

    #region Private Methods

    private static string StripPromptPrefix(string text)
    {
        string result = text;
        while (result.Length > 0 && (result[0] == '$' || result[0] == '>'))
        {
            result = result[1..].TrimStart();
        }

        return result;
    }

    private static string JoinContinuations(string text)
    {
        return text
            .Replace("\\\r\n", " ", StringComparison.Ordinal)
            .Replace("\\\n", " ", StringComparison.Ordinal)
            .Replace("`\r\n", " ", StringComparison.Ordinal)
            .Replace("`\n", " ", StringComparison.Ordinal);
    }

    private static List<string> Tokenize(string text)
    {
        List<string> tokens = [];
        StringBuilder current = new();
        bool hasToken = false;
        int index = 0;

        while (index < text.Length)
        {
            char character = text[index];
            if (char.IsWhiteSpace(character))
            {
                if (hasToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }

                index++;
                continue;
            }

            hasToken = true;
            if (character == '\'')
            {
                index = ReadSingleQuoted(text, index + 1, current);
                continue;
            }

            if (character == '"')
            {
                index = ReadDoubleQuoted(text, index + 1, current);
                continue;
            }

            if (character == '$' && index + 1 < text.Length && text[index + 1] == '\'')
            {
                index = ReadAnsiCQuoted(text, index + 2, current);
                continue;
            }

            if (character == '\\' && index + 1 < text.Length)
            {
                current.Append(text[index + 1]);
                index += 2;
                continue;
            }

            current.Append(character);
            index++;
        }

        if (hasToken)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static int ReadSingleQuoted(string text, int index, StringBuilder current)
    {
        while (index < text.Length && text[index] != '\'')
        {
            current.Append(text[index]);
            index++;
        }

        return index + 1;
    }

    private static int ReadDoubleQuoted(string text, int index, StringBuilder current)
    {
        while (index < text.Length && text[index] != '"')
        {
            char character = text[index];
            if (character == '\\' && index + 1 < text.Length && (text[index + 1] == '"' || text[index + 1] == '\\' || text[index + 1] == '$' || text[index + 1] == '`'))
            {
                current.Append(text[index + 1]);
                index += 2;
                continue;
            }

            current.Append(character);
            index++;
        }

        return index + 1;
    }

    private static int ReadAnsiCQuoted(string text, int index, StringBuilder current)
    {
        while (index < text.Length && text[index] != '\'')
        {
            char character = text[index];
            if (character != '\\' || index + 1 >= text.Length)
            {
                current.Append(character);
                index++;
                continue;
            }

            index++;
            current.Append(text[index] switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '0' => '\0',
                'a' => '\a',
                'b' => '\b',
                'f' => '\f',
                'v' => '\v',
                '\\' => '\\',
                '\'' => '\'',
                '"' => '"',
                _ => text[index],
            });
            index++;
        }

        return index + 1;
    }

    private static ParsedCurl ParseTokens(IReadOnlyList<string> tokens)
    {
        ParsedCurl parsed = new();
        int index = 0;
        if (index < tokens.Count &&
            (string.Equals(tokens[index], "curl", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tokens[index], "curl.exe", StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        while (index < tokens.Count)
        {
            string token = tokens[index];
            index++;

            switch (token)
            {
                case "-X" or "--request":
                    parsed.ExplicitMethod = NextValue(tokens, ref index)?.ToUpperInvariant();
                    break;
                case "-H" or "--header":
                    AddHeader(parsed, NextValue(tokens, ref index));
                    break;
                case "-d" or "--data" or "--data-raw" or "--data-binary" or "--data-ascii":
                    AddData(parsed, NextValue(tokens, ref index), urlEncode: false);
                    break;
                case "--data-urlencode":
                    AddData(parsed, NextValue(tokens, ref index), urlEncode: true);
                    break;
                case "--json":
                    parsed.JsonFlag = true;
                    AddData(parsed, NextValue(tokens, ref index), urlEncode: false);
                    break;
                case "-u" or "--user":
                    SetBasicAuth(parsed, NextValue(tokens, ref index));
                    break;
                case "--oauth2-bearer":
                    parsed.BearerToken = NextValue(tokens, ref index);
                    break;
                case "-A" or "--user-agent":
                    parsed.Headers.Add(("User-Agent", NextValue(tokens, ref index) ?? string.Empty));
                    break;
                case "-e" or "--referer":
                    parsed.Headers.Add(("Referer", NextValue(tokens, ref index) ?? string.Empty));
                    break;
                case "-b" or "--cookie":
                    parsed.Headers.Add(("Cookie", NextValue(tokens, ref index) ?? string.Empty));
                    break;
                case "-L" or "--location":
                    parsed.FollowRedirects = true;
                    break;
                case "-k" or "--insecure":
                    parsed.Insecure = true;
                    break;
                case "-m" or "--max-time":
                    SetTimeout(parsed, NextValue(tokens, ref index));
                    break;
                case "--url":
                    parsed.Url = NextValue(tokens, ref index);
                    break;
                case "--compressed" or "-s" or "--silent" or "-S" or "--show-error" or "-v" or "--verbose"
                    or "-i" or "--include" or "-f" or "--fail" or "-g" or "--globoff" or "--http1.1"
                    or "--http2" or "--http3" or "-#" or "--progress-bar" or "--no-progress-meter":
                    parsed.IgnoredFlags.Add(token);
                    break;
                default:
                    HandleUnknownToken(parsed, tokens, token, ref index);
                    break;
            }
        }

        return parsed;
    }

    private static void HandleUnknownToken(ParsedCurl parsed, IReadOnlyList<string> tokens, string token, ref int index)
    {
        if (!token.StartsWith('-') || token.Length <= 1)
        {
            // Bare token: treat the first one as the URL.
            parsed.Url ??= token;
            return;
        }

        parsed.IgnoredFlags.Add(token);
        if (ignoredValueFlags.Contains(token))
        {
            NextValue(tokens, ref index);
        }
    }

    private static string? NextValue(IReadOnlyList<string> tokens, ref int index)
    {
        if (index >= tokens.Count)
        {
            return null;
        }

        string value = tokens[index];
        index++;
        return value;
    }

    private static void AddHeader(ParsedCurl parsed, string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return;
        }

        int separatorIndex = header.IndexOf(':');
        string name = separatorIndex < 0 ? header.Trim() : header[..separatorIndex].Trim();
        string value = separatorIndex < 0 ? string.Empty : header[(separatorIndex + 1)..].Trim();
        if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
        {
            parsed.ContentType = value;
            return;
        }

        parsed.Headers.Add((name, value));
    }

    private static void AddData(ParsedCurl parsed, string? data, bool urlEncode)
    {
        if (data is null)
        {
            return;
        }

        string value = data;
        if (urlEncode)
        {
            int separatorIndex = data.IndexOf('=');
            value = separatorIndex < 0
                ? Uri.EscapeDataString(data)
                : $"{data[..separatorIndex]}={Uri.EscapeDataString(data[(separatorIndex + 1)..])}";
        }

        parsed.DataSegments.Add(value);
    }

    private static void SetBasicAuth(ParsedCurl parsed, string? credentials)
    {
        if (string.IsNullOrEmpty(credentials))
        {
            return;
        }

        int separatorIndex = credentials.IndexOf(':');
        parsed.Username = separatorIndex < 0 ? credentials : credentials[..separatorIndex];
        parsed.Password = separatorIndex < 0 ? string.Empty : credentials[(separatorIndex + 1)..];
    }

    private static void SetTimeout(ParsedCurl parsed, string? seconds)
    {
        if (double.TryParse(seconds, System.Globalization.CultureInfo.InvariantCulture, out double parsedSeconds) && parsedSeconds > 0)
        {
            parsed.TimeoutMilliseconds = (int)Math.Round(parsedSeconds * 1000);
        }
    }

    private static string BuildSuggestedName(ParsedCurl parsed)
    {
        string method = ResolveMethod(parsed);
        Uri? uri = TryParseUri(parsed.Url);
        if (uri is null)
        {
            return string.IsNullOrWhiteSpace(parsed.Url) ? $"{method} request" : $"{method} {parsed.Url}";
        }

        string path = uri.AbsolutePath;
        return string.IsNullOrEmpty(path) || path == "/"
            ? $"{method} {uri.Host}"
            : $"{method} {uri.Host} {path}";
    }

    private static Uri? TryParseUri(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        string candidate = url.Contains("://", StringComparison.Ordinal) ? url : $"https://{url}";
        return Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) ? uri : null;
    }

    private static string ResolveMethod(ParsedCurl parsed)
    {
        if (!string.IsNullOrWhiteSpace(parsed.ExplicitMethod))
        {
            return parsed.ExplicitMethod;
        }

        return parsed.DataSegments.Count > 0 || parsed.JsonFlag ? "POST" : "GET";
    }

    private static string Render(ParsedCurl parsed, string suggestedName)
    {
        string method = ResolveMethod(parsed);
        string body = string.Join("&", parsed.DataSegments);
        bool hasBody = parsed.DataSegments.Count > 0;
        bool jsonBody = hasBody
            && (parsed.JsonFlag
                || (parsed.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) ?? false)
                || (parsed.ContentType is null && FrsScriptWriter.LooksLikeJson(body)));

        List<string> lines =
        [
            $"name \"{FrsScriptWriter.Escape(suggestedName)}\"",
            $"method {method}",
            $"url \"{FrsScriptWriter.Escape(parsed.Url ?? string.Empty)}\"",
        ];

        if (parsed.TimeoutMilliseconds is int timeout)
        {
            lines.Add($"timeout {timeout}");
        }

        if (parsed.FollowRedirects)
        {
            lines.Add("redirects true");
        }

        if (parsed.Insecure)
        {
            lines.Add("ssl false");
        }

        if (parsed.ContentType is not null && !jsonBody)
        {
            lines.Add($"content_type \"{FrsScriptWriter.Escape(parsed.ContentType)}\"");
        }
        else if (hasBody && !jsonBody && parsed.ContentType is null)
        {
            lines.Add("content_type \"application/x-www-form-urlencoded\"");
        }

        List<string> headers = [.. parsed.Headers.Select(static header => FrsScriptWriter.HeaderLine(header.Name, header.Value))];
        if (parsed.JsonFlag && !parsed.Headers.Any(static header => string.Equals(header.Name, "Accept", StringComparison.OrdinalIgnoreCase)))
        {
            headers.Insert(0, FrsScriptWriter.HeaderLine("Accept", "application/json"));
        }

        if (headers.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(headers);
        }

        if (parsed.Username is not null)
        {
            lines.Add(string.Empty);
            lines.AddRange(FrsScriptWriter.BasicAuthBlock(parsed.Username, parsed.Password ?? string.Empty));
        }
        else if (parsed.BearerToken is not null)
        {
            lines.Add(string.Empty);
            lines.AddRange(FrsScriptWriter.BearerAuthBlock(parsed.BearerToken));
        }

        if (hasBody)
        {
            lines.Add(string.Empty);
            lines.AddRange(FrsScriptWriter.BodyBlock(body, jsonBody));
        }

        if (parsed.IgnoredFlags.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"# note: ignored curl flags: {string.Join(", ", parsed.IgnoredFlags.Distinct(StringComparer.Ordinal))}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    #endregion

    #region Private Types

    private sealed class ParsedCurl
    {
        public string? Url { get; set; }
        public string? ExplicitMethod { get; set; }
        public string? ContentType { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? BearerToken { get; set; }
        public bool JsonFlag { get; set; }
        public bool FollowRedirects { get; set; }
        public bool Insecure { get; set; }
        public int? TimeoutMilliseconds { get; set; }
        public List<(string Name, string Value)> Headers { get; } = [];
        public List<string> DataSegments { get; } = [];
        public List<string> IgnoredFlags { get; } = [];
    }

    #endregion
}

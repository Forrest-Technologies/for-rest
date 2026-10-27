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

    private static readonly HashSet<string> valueFlags = new(
    [
        "-X", "--request", "-H", "--header", "-d", "--data", "--data-raw", "--data-binary", "--data-ascii",
        "--data-urlencode", "--json", "-u", "--user", "--oauth2-bearer", "-A", "--user-agent", "-e", "--referer",
        "-b", "--cookie", "-m", "--max-time", "--url",
    ], StringComparer.Ordinal);

    private const string ShortSwitches = "GILksSvifg#";

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

        return HasCurlCommandPrefix(text.Trim());
    }

    public string ConvertToScript(string curlCommand, out string suggestedName)
    {
        ArgumentNullException.ThrowIfNull(curlCommand);

        List<string> tokens = Tokenize(JoinContinuations(StripPromptPrefix(curlCommand.Trim())));
        ParsedCurl parsed = ParseTokens(tokens);
        if (string.IsNullOrWhiteSpace(parsed.Url))
        {
            throw new FormatException("The curl command must include a URL.");
        }

        suggestedName = BuildSuggestedName(parsed);
        return Render(parsed, suggestedName);
    }

    #endregion

    #region Internal Methods

    internal static bool HasCurlCommandPrefix(string text)
    {
        var candidate = StripPromptPrefix(text);
        var commandLength = candidate.StartsWith("curl.exe", StringComparison.OrdinalIgnoreCase) ? 8 : 4;
        return candidate.Length > commandLength
            && candidate.StartsWith("curl", StringComparison.OrdinalIgnoreCase)
            && char.IsWhiteSpace(candidate[commandLength]);
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

            if (character == '\\')
            {
                if (index + 1 >= text.Length)
                {
                    throw new FormatException("The curl command ends with an incomplete escape sequence.");
                }

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

        if (index >= text.Length)
        {
            throw new FormatException("The curl command contains an unterminated quoted value.");
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

        if (index >= text.Length)
        {
            throw new FormatException("The curl command contains an unterminated quoted value.");
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

        if (index >= text.Length)
        {
            throw new FormatException("The curl command contains an unterminated quoted value.");
        }

        return index + 1;
    }

    private static bool TryExpandOption(string token, out IReadOnlyList<string> expanded)
    {
        expanded = [];
        if (token.Length <= 2 || token[0] != '-')
        {
            return false;
        }

        if (token.StartsWith("--", StringComparison.Ordinal))
        {
            var separator = token.IndexOf('=');
            if (separator < 0)
            {
                return false;
            }

            var flag = token[..separator];
            if (!valueFlags.Contains(flag) && !ignoredValueFlags.Contains(flag))
            {
                return false;
            }

            expanded = [flag, token[(separator + 1)..]];
            return true;
        }

        List<string> flags = [];
        for (var index = 1; index < token.Length; index++)
        {
            var flag = $"-{token[index]}";
            if (valueFlags.Contains(flag) || ignoredValueFlags.Contains(flag))
            {
                flags.Add(flag);
                if (index + 1 < token.Length)
                {
                    flags.Add(token[(index + 1)..]);
                }

                expanded = flags;
                return true;
            }

            if (!ShortSwitches.Contains(token[index]))
            {
                return false;
            }

            flags.Add(flag);
        }

        expanded = flags;
        return true;
    }

    private static ParsedCurl ParseTokens(IReadOnlyList<string> tokens)
    {
        ParsedCurl parsed = new();
        int index = 0;
        var pending = new Queue<string>();
        if (index < tokens.Count &&
            (string.Equals(tokens[index], "curl", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tokens[index], "curl.exe", StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        while (index < tokens.Count || pending.Count > 0)
        {
            var token = pending.Count > 0 ? pending.Dequeue() : tokens[index++];
            if (TryExpandOption(token, out var expanded))
            {
                token = expanded[0];
                foreach (var value in expanded.Skip(1))
                {
                    pending.Enqueue(value);
                }
            }

            switch (token)
            {
                case "-G" or "--get":
                    parsed.UseQueryData = true;
                    break;
                case "--no-get":
                    parsed.UseQueryData = false;
                    break;
                case "-I" or "--head":
                    parsed.HeadOnly = true;
                    break;
                case "--no-head":
                    parsed.HeadOnly = false;
                    break;
                case "-X" or "--request":
                    parsed.ExplicitMethod = NextValue(tokens, ref index, pending)?.ToUpperInvariant();
                    break;
                case "-H" or "--header":
                    AddHeader(parsed, NextValue(tokens, ref index, pending));
                    break;
                case "-d" or "--data" or "--data-raw" or "--data-binary" or "--data-ascii":
                    AddData(parsed, NextValue(tokens, ref index, pending), urlEncode: false);
                    break;
                case "--data-urlencode":
                    AddData(parsed, NextValue(tokens, ref index, pending), urlEncode: true);
                    break;
                case "--json":
                    parsed.JsonFlag = true;
                    AddData(parsed, NextValue(tokens, ref index, pending), urlEncode: false);
                    break;
                case "-u" or "--user":
                    SetBasicAuth(parsed, NextValue(tokens, ref index, pending));
                    break;
                case "--oauth2-bearer":
                    parsed.BearerToken = NextValue(tokens, ref index, pending);
                    break;
                case "-A" or "--user-agent":
                    parsed.Headers.Add(("User-Agent", NextValue(tokens, ref index, pending) ?? string.Empty));
                    break;
                case "-e" or "--referer":
                    parsed.Headers.Add(("Referer", NextValue(tokens, ref index, pending) ?? string.Empty));
                    break;
                case "-b" or "--cookie":
                    parsed.Headers.Add(("Cookie", NextValue(tokens, ref index, pending) ?? string.Empty));
                    break;
                case "-L" or "--location":
                    parsed.FollowRedirects = true;
                    break;
                case "-k" or "--insecure":
                    parsed.Insecure = true;
                    break;
                case "-m" or "--max-time":
                    SetTimeout(parsed, NextValue(tokens, ref index, pending));
                    break;
                case "--url":
                    parsed.Url = NextValue(tokens, ref index, pending);
                    break;
                case "--compressed" or "-s" or "--silent" or "-S" or "--show-error" or "-v" or "--verbose"
                    or "-i" or "--include" or "-f" or "--fail" or "-g" or "--globoff" or "--http1.1"
                    or "--http2" or "--http3" or "-#" or "--progress-bar" or "--no-progress-meter":
                    parsed.IgnoredFlags.Add(token);
                    break;
                default:
                    HandleUnknownToken(parsed, tokens, token, ref index, pending);
                    break;
            }
        }

        if (parsed.HeadOnly && parsed.DataSegments.Count > 0 && !parsed.UseQueryData)
        {
            throw new FormatException("curl --head cannot send body data. Use --get for query parameters.");
        }

        return parsed;
    }

    private static void HandleUnknownToken(ParsedCurl parsed, IReadOnlyList<string> tokens, string token, ref int index,
        Queue<string> pending)
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
            NextValue(tokens, ref index, pending);
        }
    }

    private static string? NextValue(IReadOnlyList<string> tokens, ref int index, Queue<string> pending)
    {
        if (pending.TryDequeue(out var attached))
        {
            return attached;
        }

        if (index >= tokens.Count)
        {
            throw new FormatException($"The curl option '{tokens[index - 1]}' requires a value.");
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

        if (parsed.HeadOnly)
        {
            return "HEAD";
        }

        return !parsed.UseQueryData && (parsed.DataSegments.Count > 0 || parsed.JsonFlag) ? "POST" : "GET";
    }

    private static string RenderUrl(ParsedCurl parsed)
    {
        var url = parsed.Url ?? string.Empty;
        var query = string.Join("&", parsed.DataSegments);
        if (!parsed.UseQueryData || string.IsNullOrEmpty(url) || string.IsNullOrEmpty(query))
        {
            return url;
        }

        var fragmentIndex = url.IndexOf('#');
        var fragment = fragmentIndex >= 0 ? url[fragmentIndex..] : string.Empty;
        var requestUrl = fragmentIndex >= 0 ? url[..fragmentIndex] : url;
        var separator = requestUrl.EndsWith('?') || requestUrl.EndsWith('&')
            ? string.Empty
            : requestUrl.Contains('?') ? "&" : "?";
        return $"{requestUrl}{separator}{query}{fragment}";
    }

    private static string AddSecret(List<string> lines, string name, string value)
    {
        lines.Add($"secret {name} = {JsonSerializer.Serialize(value)}");
        return "{{" + name + "}}";
    }

    private static string Render(ParsedCurl parsed, string suggestedName)
    {
        string method = ResolveMethod(parsed);
        string body = string.Join("&", parsed.DataSegments);
        var hasBody = !parsed.UseQueryData && parsed.DataSegments.Count > 0;
        var contentType = parsed.ContentType ?? (parsed.JsonFlag ? "application/json" : null);
        var jsonBody = hasBody
            && ((contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) ?? false)
                || (contentType is null && FrsScriptWriter.LooksLikeJson(body)));

        List<string> lines =
        [
            $"name \"{FrsScriptWriter.Escape(suggestedName)}\"",
            $"method {method}",
            $"url \"{FrsScriptWriter.Escape(RenderUrl(parsed))}\"",
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

        if (hasBody && contentType is not null && !jsonBody)
        {
            lines.Add($"content_type \"{FrsScriptWriter.Escape(contentType)}\"");
        }
        else if (hasBody && !jsonBody && contentType is null)
        {
            lines.Add("content_type \"application/x-www-form-urlencoded\"");
        }

        List<string> headers = [];
        var secretHeaderIndex = 0;
        foreach (var header in parsed.Headers)
        {
            var value = HttpCredentialHeaders.IsSensitive(header.Name)
                ? AddSecret(lines, $"curl_header_{++secretHeaderIndex}", header.Value)
                : header.Value;
            headers.Add(FrsScriptWriter.HeaderLine(header.Name, value));
        }
        if (!hasBody && contentType is not null)
        {
            headers.Insert(0, FrsScriptWriter.HeaderLine("Content-Type", contentType));
        }

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
            lines.AddRange(FrsScriptWriter.BasicAuthBlock(AddSecret(lines, "curl_username", parsed.Username),
                AddSecret(lines, "curl_password", parsed.Password ?? string.Empty)));
        }
        else if (parsed.BearerToken is not null)
        {
            lines.Add(string.Empty);
            lines.AddRange(FrsScriptWriter.BearerAuthBlock(AddSecret(lines, "curl_bearer_token", parsed.BearerToken)));
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
        public bool UseQueryData { get; set; }
        public bool HeadOnly { get; set; }
        public bool FollowRedirects { get; set; }
        public bool Insecure { get; set; }
        public int? TimeoutMilliseconds { get; set; }
        public List<(string Name, string Value)> Headers { get; } = [];
        public List<string> DataSegments { get; } = [];
        public List<string> IgnoredFlags { get; } = [];
    }

    #endregion
}

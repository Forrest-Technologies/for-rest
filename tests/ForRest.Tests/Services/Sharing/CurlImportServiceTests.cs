using ForRest.Services.Sharing;

namespace ForRest.Tests.Services.Sharing;

[TestClass]
public sealed class CurlImportServiceTests
{
    private readonly CurlImportService service = new();

    [TestMethod]
    public void LooksLikeCurl_accepts_plain_and_prompt_prefixed_commands()
    {
        Assert.IsTrue(service.LooksLikeCurl("curl https://example.com"));
        Assert.IsTrue(service.LooksLikeCurl("$ curl https://example.com"));
        Assert.IsTrue(service.LooksLikeCurl("> curl.exe https://example.com"));
        Assert.IsFalse(service.LooksLikeCurl("wget https://example.com"));
        Assert.IsFalse(service.LooksLikeCurl(null));
        Assert.IsFalse(service.LooksLikeCurl("   "));
        Assert.IsFalse(service.LooksLikeCurl("curling is a sport"));
    }

    [TestMethod]
    [DataRow("curl\thttps://example.com")]
    [DataRow("curl.exe\nhttps://example.com")]
    [DataRow("$ curl\r\nhttps://example.com")]
    public void Command_detection_accepts_shell_whitespace(string command)
    {
        Assert.IsTrue(service.LooksLikeCurl(command));
        Assert.AreEqual(ImportContentClassifier.ContentKind.CurlCommand,
            ImportContentClassifier.ClassifyText(command));
    }

    [TestMethod]
    [DataRow("curl 'https://example.com")]
    [DataRow("curl \"https://example.com")]
    [DataRow("curl $'https://example.com")]
    [DataRow("curl https://example.com -H X-Trace:\\")]
    public void Incomplete_shell_tokens_are_rejected(string command)
    {
        Assert.ThrowsExactly<FormatException>(() => service.ConvertToScript(command, out _));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("curl")]
    [DataRow("curl -s")]
    [DataRow("curl --url ''")]
    [DataRow("curl ''")]
    public void Commands_without_a_URL_are_rejected(string command)
    {
        Assert.ThrowsExactly<FormatException>(() => service.ConvertToScript(command, out _));
    }

    [TestMethod]
    [DataRow("-X")]
    [DataRow("--header")]
    [DataRow("--data")]
    [DataRow("--data-urlencode")]
    [DataRow("--json")]
    [DataRow("--user")]
    [DataRow("--oauth2-bearer")]
    [DataRow("--user-agent")]
    [DataRow("--referer")]
    [DataRow("--cookie")]
    [DataRow("--max-time")]
    [DataRow("--url")]
    [DataRow("--output")]
    public void Options_without_a_required_value_are_rejected(string option)
    {
        Assert.ThrowsExactly<FormatException>(() => service.ConvertToScript(
            $"curl https://example.com {option}", out _));
    }

    [TestMethod]
    [DataRow("-XPUT", "method PUT")]
    [DataRow("-H'X-Test: hello world'", "header \"X-Test\" = \"hello world\"")]
    [DataRow("-d'a=1'", "body text")]
    [DataRow("-ureader:fake-value", "username = \"reader\"")]
    [DataRow("-A'Test Agent'", "header \"User-Agent\" = \"Test Agent\"")]
    [DataRow("-ehttps://ref.example.test/", "header \"Referer\" = \"https://ref.example.test/\"")]
    [DataRow("-bsession=fake", "header \"Cookie\" = \"session=fake\"")]
    [DataRow("-m0.5", "timeout 500")]
    public void Short_option_values_can_be_attached_without_a_space(string option, string expected)
    {
        var script = service.ConvertToScript($"curl {option} https://api.example.test/items", out _);

        StringAssert.Contains(script, expected);
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    [DataRow("--request=PUT", "method PUT")]
    [DataRow("--header='X-Test: hello world'", "header \"X-Test\" = \"hello world\"")]
    [DataRow("--data=a=1", "body text")]
    [DataRow("--data-urlencode='q=hello world'", "q=hello%20world")]
    [DataRow("--json='{}'", "body json")]
    [DataRow("--user=reader:fake-value", "username = \"reader\"")]
    [DataRow("--oauth2-bearer=fake-token", "token = \"fake-token\"")]
    [DataRow("--max-time=0.5", "timeout 500")]
    public void Long_option_values_accept_an_equals_separator(string option, string expected)
    {
        var script = service.ConvertToScript($"curl {option} https://api.example.test/items", out _);

        StringAssert.Contains(script, expected);
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Combined_short_flags_keep_redirects_and_a_following_attached_value()
    {
        var script = service.ConvertToScript("curl -sSLkXPUT https://api.example.test/items", out _);

        StringAssert.Contains(script, "redirects true");
        StringAssert.Contains(script, "ssl false");
        StringAssert.Contains(script, "method PUT");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void A_cluster_can_take_its_final_option_value_from_the_next_token()
    {
        var script = service.ConvertToScript("curl -sSX PUT https://api.example.test/items", out _);

        StringAssert.Contains(script, "method PUT");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Clustered_data_options_do_not_expand_their_attached_body_text()
    {
        var script = service.ConvertToScript("curl -sSLd'--request=DELETE' https://api.example.test/items", out _);

        StringAssert.Contains(script, "method POST");
        StringAssert.Contains(script, "redirects true");
        StringAssert.Contains(script, "--request=DELETE");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Inline_URL_and_empty_data_values_are_preserved()
    {
        var script = service.ConvertToScript("curl --url=https://api.example.test/items --data=", out _);

        StringAssert.Contains(script, "url \"https://api.example.test/items\"");
        StringAssert.Contains(script, "method POST");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    [DataRow("-d '--request=DELETE'")]
    [DataRow("--data=--request=DELETE")]
    public void Option_like_data_values_are_not_reinterpreted_as_options(string option)
    {
        var script = service.ConvertToScript($"curl {option} https://api.example.test/items", out _);

        StringAssert.Contains(script, "method POST");
        StringAssert.Contains(script, "--request=DELETE");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    [DataRow("-oout.json")]
    [DataRow("--output=out.json")]
    public void Attached_ignored_option_values_are_consumed(string option)
    {
        var script = service.ConvertToScript($"curl {option} https://api.example.test/items", out _);

        StringAssert.Contains(script, "url \"https://api.example.test/items\"");
        Assert.DoesNotContain("out.json", script);
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Simple_get_produces_parsable_script_with_url_and_name()
    {
        string script = service.ConvertToScript("curl https://api.example.com/users", out string suggestedName);

        Assert.AreEqual("GET api.example.com /users", suggestedName);
        StringAssert.Contains(script, "method GET");
        StringAssert.Contains(script, "url \"https://api.example.com/users\"");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Explicit_method_and_multiple_headers_are_emitted()
    {
        string command = "curl -X PUT -H 'Accept: application/xml' -H \"X-Trace: abc\" https://api.example.com/items/1";

        string script = service.ConvertToScript(command, out _);

        StringAssert.Contains(script, "method PUT");
        StringAssert.Contains(script, "header \"Accept\" = \"application/xml\"");
        StringAssert.Contains(script, "header \"X-Trace\" = \"abc\"");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Data_implies_post_and_multiple_data_joins_with_ampersand()
    {
        string command = "curl -d 'a=1' -d 'b=2' https://api.example.com/form";

        string script = service.ConvertToScript(command, out string suggestedName);

        StringAssert.Contains(script, "method POST");
        StringAssert.Contains(script, "content_type \"application/x-www-form-urlencoded\"");
        StringAssert.Contains(script, "body text \"\"\"");
        StringAssert.Contains(script, "a=1&b=2");
        StringAssert.StartsWith(suggestedName, "POST ");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Json_content_type_produces_pretty_printed_json_body()
    {
        string command = "curl -H 'Content-Type: application/json' -d '{\"name\":\"box\",\"size\":3}' https://api.example.com/items";

        string script = service.ConvertToScript(command, out _);

        StringAssert.Contains(script, "body json \"\"\"");
        StringAssert.Contains(script, "\"name\": \"box\"");
        Assert.IsFalse(script.Contains("header \"Content-Type\"", StringComparison.Ordinal), "Content-Type should be folded into body mode, not emitted as a header.");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Json_flag_implies_post_json_body_and_accept_header()
    {
        string script = service.ConvertToScript("curl --json '{\"a\":1}' https://api.example.com/things", out _);

        StringAssert.Contains(script, "method POST");
        StringAssert.Contains(script, "header \"Accept\" = \"application/json\"");
        StringAssert.Contains(script, "body json \"\"\"");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void User_flag_emits_basic_auth_block()
    {
        string script = service.ConvertToScript("curl -u alice:s3cret https://api.example.com/secure", out _);

        StringAssert.Contains(script, "auth {");
        StringAssert.Contains(script, "mode = basic");
        StringAssert.Contains(script, "username = \"alice\"");
        StringAssert.Contains(script, "password = \"s3cret\"");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Oauth2_bearer_emits_bearer_auth_block()
    {
        string script = service.ConvertToScript("curl --oauth2-bearer tok123 https://api.example.com/me", out _);

        StringAssert.Contains(script, "mode = bearer");
        StringAssert.Contains(script, "token = \"tok123\"");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Insecure_and_location_flags_map_to_ssl_and_redirects()
    {
        string script = service.ConvertToScript("curl -k -L https://self-signed.example.com", out _);

        StringAssert.Contains(script, "ssl false");
        StringAssert.Contains(script, "redirects true");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Max_time_maps_to_timeout_milliseconds()
    {
        string script = service.ConvertToScript("curl --max-time 30 https://api.example.com", out _);

        StringAssert.Contains(script, "timeout 30000");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void User_agent_referer_and_cookie_become_headers()
    {
        string command = "curl -A 'MyAgent/1.0' -e 'https://ref.example.com' -b 'session=abc' https://api.example.com";

        string script = service.ConvertToScript(command, out _);

        StringAssert.Contains(script, "header \"User-Agent\" = \"MyAgent/1.0\"");
        StringAssert.Contains(script, "header \"Referer\" = \"https://ref.example.com\"");
        StringAssert.Contains(script, "header \"Cookie\" = \"session=abc\"");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Backslash_line_continuations_are_joined()
    {
        string command = "curl -X POST \\\n  -H 'Accept: application/json' \\\n  https://api.example.com/users";

        string script = service.ConvertToScript(command, out _);

        StringAssert.Contains(script, "method POST");
        StringAssert.Contains(script, "url \"https://api.example.com/users\"");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Powershell_backtick_continuations_are_joined()
    {
        string command = "curl.exe -X DELETE `\n  https://api.example.com/users/9";

        string script = service.ConvertToScript(command, out _);

        StringAssert.Contains(script, "method DELETE");
        StringAssert.Contains(script, "url \"https://api.example.com/users/9\"");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Ansi_c_quotes_unescape_basic_sequences()
    {
        string script = service.ConvertToScript("curl -H $'X-Note: line\\tvalue' https://api.example.com", out _);

        // The ANSI-C \t is unescaped to a real tab, then sanitized to a space for the single-line header literal.
        // Without ANSI-C handling the output would contain a literal backslash-t sequence instead.
        StringAssert.Contains(script, "header \"X-Note\" = \"line value\"");
        Assert.IsFalse(script.Contains("\\t", StringComparison.Ordinal));
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Unknown_flags_are_skipped_and_reported_in_comment()
    {
        string command = "curl -o out.json --compressed --frobnicate https://api.example.com";

        string script = service.ConvertToScript(command, out _);

        StringAssert.Contains(script, "# note: ignored curl flags:");
        StringAssert.Contains(script, "-o");
        StringAssert.Contains(script, "--compressed");
        StringAssert.Contains(script, "--frobnicate");
        Assert.IsFalse(script.Contains("out.json", StringComparison.Ordinal), "The -o value should be consumed, not treated as the URL.");
        StringAssert.Contains(script, "url \"https://api.example.com\"");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Url_flag_and_data_urlencode_are_honored()
    {
        string command = "curl --url https://api.example.com/search --data-urlencode 'q=hello world'";

        string script = service.ConvertToScript(command, out _);

        StringAssert.Contains(script, "url \"https://api.example.com/search\"");
        StringAssert.Contains(script, "q=hello%20world");
        StringAssert.Contains(script, "method POST");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    [DataRow("-G")]
    [DataRow("--get")]
    public void Get_flag_moves_data_into_the_query_without_a_request_body(string flag)
    {
        var script = service.ConvertToScript(
            $"curl {flag} --data-urlencode 'q=hello world' https://api.example.com/search", out var name);

        StringAssert.Contains(script, "method GET");
        StringAssert.Contains(script, "url \"https://api.example.com/search?q=hello%20world\"");
        Assert.DoesNotContain("body ", script);
        StringAssert.StartsWith(name, "GET ");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    [DataRow("https://api.example.com/search?", "https://api.example.com/search?q=x")]
    [DataRow("https://api.example.com/search?active=1&", "https://api.example.com/search?active=1&q=x")]
    [DataRow("https://api.example.com/search?active=1", "https://api.example.com/search?active=1&q=x")]
    [DataRow("https://api.example.com/search#part", "https://api.example.com/search?q=x#part")]
    [DataRow("https://api.example.com/search?active=1#part", "https://api.example.com/search?active=1&q=x#part")]
    public void Get_flag_preserves_existing_queries_and_fragments(string url, string expected)
    {
        var script = service.ConvertToScript($"curl --get --data 'q=x' '{url}'", out _);

        StringAssert.Contains(script, $"url \"{expected}\"");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Explicit_method_is_preserved_when_get_moves_data_into_the_query()
    {
        var script = service.ConvertToScript(
            "curl --get --request DELETE --data 'id=3' https://api.example.com/items", out _);

        StringAssert.Contains(script, "method DELETE");
        StringAssert.Contains(script, "url \"https://api.example.com/items?id=3\"");
        Assert.DoesNotContain("body ", script);
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void No_get_restores_post_body_handling()
    {
        var script = service.ConvertToScript(
            "curl --get --no-get --data 'q=x' https://api.example.com/search", out _);

        StringAssert.Contains(script, "method POST");
        StringAssert.Contains(script, "url \"https://api.example.com/search\"");
        StringAssert.Contains(script, "body text");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    [DataRow("-I")]
    [DataRow("--head")]
    public void Head_flag_selects_head_method(string flag)
    {
        var script = service.ConvertToScript($"curl {flag} https://api.example.com/health", out _);

        StringAssert.Contains(script, "method HEAD");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Head_and_get_flags_move_data_to_a_head_request_query()
    {
        var script = service.ConvertToScript(
            "curl -I -G --data 'q=x' https://api.example.com/search", out _);

        StringAssert.Contains(script, "method HEAD");
        StringAssert.Contains(script, "url \"https://api.example.com/search?q=x\"");
        Assert.DoesNotContain("body ", script);
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Head_with_post_data_is_rejected_like_curl()
    {
        Assert.ThrowsExactly<FormatException>(() => service.ConvertToScript(
            "curl --head --data 'q=x' https://api.example.com/search", out _));
    }

    [TestMethod]
    public void No_head_restores_the_default_request_method()
    {
        var script = service.ConvertToScript("curl --head --no-head https://api.example.com/health", out _);

        StringAssert.Contains(script, "method GET");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Explicit_method_overrides_head_flag()
    {
        var script = service.ConvertToScript("curl --head --request OPTIONS https://api.example.com/health", out _);

        StringAssert.Contains(script, "method OPTIONS");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Empty_get_data_does_not_add_a_query_separator()
    {
        var script = service.ConvertToScript("curl --get --data '' https://api.example.com/search", out _);

        StringAssert.Contains(script, "url \"https://api.example.com/search\"");
        Assert.DoesNotContain("body ", script);
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Json_get_retains_its_content_type_without_a_body()
    {
        var script = service.ConvertToScript("curl --get --json '{}' https://api.example.com/search", out _);

        StringAssert.Contains(script, "method GET");
        StringAssert.Contains(script, "header \"Content-Type\" = \"application/json\"");
        StringAssert.Contains(script, "header \"Accept\" = \"application/json\"");
        Assert.DoesNotContain("body ", script);
        var compilation = new ForRestScriptCompiler(new()).Compile(script, new());
        Assert.IsTrue(compilation.Succeeded);
        Assert.IsNotNull(compilation.Payload);
        Assert.AreEqual(RequestBodyMode.None, compilation.Payload.Request.Body.Mode);
        Assert.HasCount(1, compilation.Payload.Request.Headers.Where(header => header.Key == "Content-Type"));
    }

    [TestMethod]
    public void Explicit_content_type_overrides_the_json_flag_default()
    {
        var script = service.ConvertToScript(
            "curl --json '{}' -H 'Content-Type: application/custom' https://api.example.com/items", out _);

        StringAssert.Contains(script, "content_type \"application/custom\"");
        StringAssert.Contains(script, "body text");
        FrsParseAssert.Parses(script);
    }

    [TestMethod]
    public void Root_path_url_suggests_host_only_name()
    {
        service.ConvertToScript("curl https://api.example.com/", out string suggestedName);

        Assert.AreEqual("GET api.example.com", suggestedName);
    }
}

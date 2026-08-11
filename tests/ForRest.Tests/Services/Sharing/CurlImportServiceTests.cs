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
    public void Root_path_url_suggests_host_only_name()
    {
        service.ConvertToScript("curl https://api.example.com/", out string suggestedName);

        Assert.AreEqual("GET api.example.com", suggestedName);
    }
}

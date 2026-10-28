using ForRest.Services.Sharing;

namespace ForRest.Tests.Services.Sharing;

[TestClass]
public sealed class CurlTimeoutImportTests
{
    #region Public Methods

    [TestMethod]
    [DataRow("1.25", 1250)]
    [DataRow("1e2", 100000)]
    [DataRow("0.0001", 1)]
    [DataRow("2147483.647", int.MaxValue)]
    [DataRow(".5", 500)]
    public void Finite_positive_timeouts_compile_to_representable_milliseconds(string seconds, int milliseconds)
    {
        var source = new CurlImportService().ConvertToScript($"curl --max-time {seconds} https://fixture.test/", out _);
        var compilation = new ForRestScriptCompiler(new()).Compile(source, new());

        Assert.IsTrue(compilation.Succeeded);
        Assert.AreEqual(milliseconds, compilation.Payload!.Request.TimeoutMilliseconds);
        StringAssert.Contains(source, $"timeout {milliseconds}");
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("NaN")]
    [DataRow("Infinity")]
    [DataRow("-Infinity")]
    [DataRow("1,5")]
    [DataRow("nonsense")]
    [DataRow("2147483.648")]
    [DataRow("1e100")]
    public void Unsupported_timeouts_fail_instead_of_overflowing_or_using_a_default(string seconds)
    {
        var exception = Assert.ThrowsExactly<FormatException>(() =>
            new CurlImportService().ConvertToScript($"curl --max-time {seconds} https://fixture.test/", out _));

        StringAssert.Contains(exception.Message, "--max-time");
    }

    [TestMethod]
    public void The_last_valid_timeout_wins()
    {
        var source = new CurlImportService().ConvertToScript("curl -m2 --max-time=0.25 https://fixture.test/", out _);
        var compilation = new ForRestScriptCompiler(new()).Compile(source, new());

        Assert.IsTrue(compilation.Succeeded);
        Assert.AreEqual(250, compilation.Payload!.Request.TimeoutMilliseconds);
    }

    [TestMethod]
    public void A_final_unlimited_timeout_is_not_silently_ignored()
    {
        Assert.ThrowsExactly<FormatException>(() => new CurlImportService().ConvertToScript(
            "curl -m2 --max-time=0 https://fixture.test/", out _));
    }

    [TestMethod]
    public void Timeout_parsing_uses_a_dot_even_with_a_decimal_comma_culture()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new("fr-FR");
            var source = new CurlImportService().ConvertToScript("curl -m1.25 https://fixture.test/", out _);
            var compilation = new ForRestScriptCompiler(new()).Compile(source, new());
            Assert.IsTrue(compilation.Succeeded);
            Assert.AreEqual(1250, compilation.Payload!.Request.TimeoutMilliseconds);
            Assert.ThrowsExactly<FormatException>(() => new CurlImportService().ConvertToScript(
                "curl -m1,25 https://fixture.test/", out _));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }

    #endregion
}

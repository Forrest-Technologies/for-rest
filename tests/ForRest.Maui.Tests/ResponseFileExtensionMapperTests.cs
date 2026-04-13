using ForRest.Maui.Services;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class ResponseFileExtensionMapperTests
{
    #region Public Methods

    [TestMethod]
    [DataRow("application/json", ".json")]
    [DataRow("text/html", ".html")]
    [DataRow("application/xml", ".xml")]
    [DataRow("text/xml", ".xml")]
    [DataRow("text/plain", ".txt")]
    [DataRow("text/css", ".css")]
    [DataRow("application/javascript", ".js")]
    [DataRow("text/javascript", ".js")]
    [DataRow("text/csv", ".csv")]
    [DataRow("application/pdf", ".pdf")]
    [DataRow("image/png", ".png")]
    [DataRow("image/jpeg", ".jpg")]
    [DataRow("image/gif", ".gif")]
    [DataRow("image/svg+xml", ".svg")]
    [DataRow("application/octet-stream", ".bin")]
    public void GetExtension_returns_expected_extension(string contentType, string expected)
    {
        Assert.AreEqual(expected, ResponseFileExtensionMapper.GetExtension(contentType));
    }

    [TestMethod]
    public void GetExtension_returns_txt_for_null()
    {
        Assert.AreEqual(".txt", ResponseFileExtensionMapper.GetExtension(null));
    }

    [TestMethod]
    public void GetExtension_returns_txt_for_empty_string()
    {
        Assert.AreEqual(".txt", ResponseFileExtensionMapper.GetExtension(""));
    }

    [TestMethod]
    [DataRow("application/json; charset=utf-8", ".json")]
    [DataRow("text/html; charset=iso-8859-1", ".html")]
    [DataRow("text/plain; boundary=something", ".txt")]
    public void GetExtension_strips_charset_parameter(string contentType, string expected)
    {
        Assert.AreEqual(expected, ResponseFileExtensionMapper.GetExtension(contentType));
    }

    [TestMethod]
    [DataRow("application/vnd.api+json", ".json")]
    [DataRow("application/hal+json", ".json")]
    [DataRow("application/xhtml+xml", ".xml")]
    [DataRow("application/soap+xml", ".xml")]
    public void GetExtension_handles_vendor_types(string contentType, string expected)
    {
        Assert.AreEqual(expected, ResponseFileExtensionMapper.GetExtension(contentType));
    }

    [TestMethod]
    public void GetExtension_returns_txt_for_unknown_type()
    {
        Assert.AreEqual(".txt", ResponseFileExtensionMapper.GetExtension("application/x-custom-format"));
    }

    #endregion
}

using System.Text;
using ForRest.Services.Sharing;

namespace ForRest.Tests.Services.Sharing;

[TestClass]
public sealed class ImportContentClassifierTests
{
    [TestMethod]
    public void ClassifyText_detects_curl_commands()
    {
        Assert.AreEqual(ImportContentClassifier.ContentKind.CurlCommand, ImportContentClassifier.ClassifyText("curl https://example.com"));
        Assert.AreEqual(ImportContentClassifier.ContentKind.CurlCommand, ImportContentClassifier.ClassifyText("$ curl -X POST https://example.com"));
        Assert.AreEqual(ImportContentClassifier.ContentKind.CurlCommand, ImportContentClassifier.ClassifyText("> curl.exe https://example.com"));
    }

    [TestMethod]
    public void ClassifyText_detects_postman_collections()
    {
        string json = """{"info":{"name":"c","schema":"https://schema.getpostman.com/json/collection/v2.1.0/collection.json"},"item":[]}""";

        Assert.AreEqual(ImportContentClassifier.ContentKind.PostmanCollection, ImportContentClassifier.ClassifyText(json));
        Assert.AreEqual(
            ImportContentClassifier.ContentKind.PostmanCollection,
            ImportContentClassifier.ClassifyText("""{"info":{"_postman_id":"abc"},"item":[]}"""));
    }

    [TestMethod]
    public void ClassifyText_detects_openapi_specs()
    {
        Assert.AreEqual(ImportContentClassifier.ContentKind.OpenApiSpec, ImportContentClassifier.ClassifyText("""{"openapi":"3.0.0","paths":{}}"""));
        Assert.AreEqual(ImportContentClassifier.ContentKind.OpenApiSpec, ImportContentClassifier.ClassifyText("""{"swagger":"2.0","paths":{}}"""));
    }

    [TestMethod]
    public void ClassifyText_detects_frs_scripts()
    {
        string frs = "name \"Test\"\nmethod GET\nurl \"https://example.com\"";

        Assert.AreEqual(ImportContentClassifier.ContentKind.Frs, ImportContentClassifier.ClassifyText(frs));
    }

    [TestMethod]
    public void ClassifyText_returns_unknown_for_everything_else()
    {
        Assert.AreEqual(ImportContentClassifier.ContentKind.Unknown, ImportContentClassifier.ClassifyText(null));
        Assert.AreEqual(ImportContentClassifier.ContentKind.Unknown, ImportContentClassifier.ClassifyText("   "));
        Assert.AreEqual(ImportContentClassifier.ContentKind.Unknown, ImportContentClassifier.ClassifyText("just some prose"));
        Assert.AreEqual(ImportContentClassifier.ContentKind.Unknown, ImportContentClassifier.ClassifyText("""{"random":"json"}"""));
        Assert.AreEqual(ImportContentClassifier.ContentKind.Unknown, ImportContentClassifier.ClassifyText("{not valid json"));
    }

    [TestMethod]
    public void ClassifyBytes_detects_zip_magic()
    {
        byte[] zipBytes = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00];

        Assert.AreEqual(ImportContentClassifier.ContentKind.WorkspaceArchiveZip, ImportContentClassifier.ClassifyBytes(zipBytes));
    }

    [TestMethod]
    public void ClassifyBytes_falls_back_to_text_classification()
    {
        byte[] curlBytes = Encoding.UTF8.GetBytes("curl https://example.com");

        Assert.AreEqual(ImportContentClassifier.ContentKind.CurlCommand, ImportContentClassifier.ClassifyBytes(curlBytes));
    }

    [TestMethod]
    public void ClassifyBytes_handles_utf8_bom()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("name \"Test\"\nurl \"https://example.com\"")];

        Assert.AreEqual(ImportContentClassifier.ContentKind.Frs, ImportContentClassifier.ClassifyBytes(bytes));
    }

    [TestMethod]
    public void ClassifyBytes_returns_unknown_for_empty_or_binary_input()
    {
        Assert.AreEqual(ImportContentClassifier.ContentKind.Unknown, ImportContentClassifier.ClassifyBytes([]));
        Assert.AreEqual(ImportContentClassifier.ContentKind.Unknown, ImportContentClassifier.ClassifyBytes([0xFF, 0xFE, 0xFD, 0x00, 0x80]));
    }
}

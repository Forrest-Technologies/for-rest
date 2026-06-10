using System.Net;

namespace ForRest.Tests.Domain;

[TestClass]
public sealed class PreparedRequestSnapshotBuilderTests
{
    #region Public Methods

    [TestMethod]
    public void Build_url_encodes_form_urlencoded_body()
    {
        var preparedRequest = new PreparedRequest
        {
            Method = HttpMethodKind.Post,
            Uri = new("https://api.example.test/form"),
            Body = new()
            {
                Mode = RequestBodyMode.FormUrlEncoded,
                FormValues =
                [
                    new()
                    {
                        Key = "search",
                        Value = "a&b c",
                    },
                ],
            },
        };

        var snapshot = PreparedRequestSnapshotBuilder.Build(preparedRequest);

        Assert.AreEqual($"search={WebUtility.UrlEncode("a&b c")}", snapshot.Body);
        Assert.AreEqual("application/x-www-form-urlencoded", snapshot.ContentType);
    }

    [TestMethod]
    public void Build_keeps_multipart_fields_readable()
    {
        var preparedRequest = new PreparedRequest
        {
            Method = HttpMethodKind.Post,
            Uri = new("https://api.example.test/form"),
            Body = new()
            {
                Mode = RequestBodyMode.MultipartFormData,
                FormValues =
                [
                    new()
                    {
                        Key = "note",
                        Value = "plain text",
                    },
                ],
            },
        };

        var snapshot = PreparedRequestSnapshotBuilder.Build(preparedRequest);

        Assert.AreEqual("note=plain text", snapshot.Body);
    }

    #endregion
}

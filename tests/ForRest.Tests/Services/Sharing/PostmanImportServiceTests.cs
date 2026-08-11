using System.Collections.Generic;
using ForRest.Services.Sharing;

namespace ForRest.Tests.Services.Sharing;

[TestClass]
public sealed class PostmanImportServiceTests
{
    private readonly PostmanImportService service = new();

    [TestMethod]
    public void LooksLikePostmanCollection_detects_schema_and_postman_id()
    {
        string withSchema = """{"info":{"name":"c","schema":"https://schema.getpostman.com/json/collection/v2.1.0/collection.json"},"item":[]}""";
        string withId = """{"info":{"name":"c","_postman_id":"abc-123"},"item":[]}""";

        Assert.IsTrue(service.LooksLikePostmanCollection(withSchema));
        Assert.IsTrue(service.LooksLikePostmanCollection(withId));
        Assert.IsFalse(service.LooksLikePostmanCollection("""{"openapi":"3.0.0"}"""));
        Assert.IsFalse(service.LooksLikePostmanCollection("not json"));
        Assert.IsFalse(service.LooksLikePostmanCollection(null));
    }

    [TestMethod]
    public void Malformed_json_throws_clear_format_exception()
    {
        FormatException exception = Assert.ThrowsExactly<FormatException>(() => service.Convert("{not valid", out _));

        StringAssert.Contains(exception.Message, "not valid JSON");
    }

    [TestMethod]
    public void Non_postman_json_throws_format_exception()
    {
        Assert.ThrowsExactly<FormatException>(() => service.Convert("""{"foo":1}""", out _));
    }

    [TestMethod]
    public void Nested_folders_flatten_into_document_names()
    {
        string json = """
        {
          "info": {"name": "My API", "_postman_id": "x", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"},
          "item": [
            {
              "name": "Users",
              "item": [
                {
                  "name": "Admin",
                  "item": [
                    {"name": "List admins", "request": {"method": "GET", "url": "https://api.example.com/admins"}}
                  ]
                }
              ]
            }
          ]
        }
        """;

        IReadOnlyList<PortableWorkspaceDocument> documents = service.Convert(json, out string collectionName);

        Assert.AreEqual("My API", collectionName);
        Assert.AreEqual(1, documents.Count);
        Assert.AreEqual("Users / Admin / List admins", documents[0].Name);
        StringAssert.Contains(documents[0].Source, "method GET");
        FrsParseAssert.Parses(documents[0].Source);
    }

    [TestMethod]
    public void Url_object_with_query_is_composed()
    {
        string json = """
        {
          "info": {"name": "c", "_postman_id": "x"},
          "item": [
            {
              "name": "Search",
              "request": {
                "method": "GET",
                "url": {
                  "protocol": "https",
                  "host": ["api", "example", "com"],
                  "path": ["v1", "search"],
                  "query": [
                    {"key": "q", "value": "widgets"},
                    {"key": "debug", "value": "1", "disabled": true}
                  ]
                }
              }
            }
          ]
        }
        """;

        IReadOnlyList<PortableWorkspaceDocument> documents = service.Convert(json, out _);

        StringAssert.Contains(documents[0].Source, "url \"https://api.example.com/v1/search?q=widgets\"");
        Assert.IsFalse(documents[0].Source.Contains("debug=1", StringComparison.Ordinal));
        FrsParseAssert.Parses(documents[0].Source);
    }

    [TestMethod]
    public void Disabled_headers_are_skipped()
    {
        string json = """
        {
          "info": {"name": "c", "_postman_id": "x"},
          "item": [
            {
              "name": "R",
              "request": {
                "method": "GET",
                "url": "https://api.example.com",
                "header": [
                  {"key": "X-Keep", "value": "yes"},
                  {"key": "X-Drop", "value": "no", "disabled": true}
                ]
              }
            }
          ]
        }
        """;

        IReadOnlyList<PortableWorkspaceDocument> documents = service.Convert(json, out _);

        StringAssert.Contains(documents[0].Source, "header \"X-Keep\" = \"yes\"");
        Assert.IsFalse(documents[0].Source.Contains("X-Drop", StringComparison.Ordinal));
        FrsParseAssert.Parses(documents[0].Source);
    }

    [TestMethod]
    public void Raw_json_body_is_emitted_as_json_body_block()
    {
        string json = """
        {
          "info": {"name": "c", "_postman_id": "x"},
          "item": [
            {
              "name": "Create",
              "request": {
                "method": "POST",
                "url": "https://api.example.com/items",
                "body": {
                  "mode": "raw",
                  "raw": "{\"name\":\"box\"}",
                  "options": {"raw": {"language": "json"}}
                }
              }
            }
          ]
        }
        """;

        IReadOnlyList<PortableWorkspaceDocument> documents = service.Convert(json, out _);

        StringAssert.Contains(documents[0].Source, "body json \"\"\"");
        StringAssert.Contains(documents[0].Source, "\"name\": \"box\"");
        FrsParseAssert.Parses(documents[0].Source);
    }

    [TestMethod]
    public void Raw_text_body_without_language_falls_back_to_text()
    {
        string json = """
        {
          "info": {"name": "c", "_postman_id": "x"},
          "item": [
            {
              "name": "Text",
              "request": {
                "method": "POST",
                "url": "https://api.example.com/notes",
                "body": {"mode": "raw", "raw": "hello plain text"}
              }
            }
          ]
        }
        """;

        IReadOnlyList<PortableWorkspaceDocument> documents = service.Convert(json, out _);

        StringAssert.Contains(documents[0].Source, "body text \"\"\"");
        StringAssert.Contains(documents[0].Source, "hello plain text");
        FrsParseAssert.Parses(documents[0].Source);
    }

    [TestMethod]
    public void Urlencoded_body_folds_to_text_body_with_form_content_type()
    {
        string json = """
        {
          "info": {"name": "c", "_postman_id": "x"},
          "item": [
            {
              "name": "Form",
              "request": {
                "method": "POST",
                "url": "https://api.example.com/form",
                "body": {
                  "mode": "urlencoded",
                  "urlencoded": [
                    {"key": "a", "value": "1"},
                    {"key": "b", "value": "2"},
                    {"key": "skip", "value": "x", "disabled": true}
                  ]
                }
              }
            }
          ]
        }
        """;

        IReadOnlyList<PortableWorkspaceDocument> documents = service.Convert(json, out _);

        StringAssert.Contains(documents[0].Source, "content_type \"application/x-www-form-urlencoded\"");
        StringAssert.Contains(documents[0].Source, "a=1&b=2");
        Assert.IsFalse(documents[0].Source.Contains("skip=x", StringComparison.Ordinal));
        FrsParseAssert.Parses(documents[0].Source);
    }

    [TestMethod]
    public void Formdata_body_becomes_comment_note()
    {
        string json = """
        {
          "info": {"name": "c", "_postman_id": "x"},
          "item": [
            {
              "name": "Upload",
              "request": {
                "method": "POST",
                "url": "https://api.example.com/upload",
                "body": {
                  "mode": "formdata",
                  "formdata": [{"key": "file"}, {"key": "label"}]
                }
              }
            }
          ]
        }
        """;

        IReadOnlyList<PortableWorkspaceDocument> documents = service.Convert(json, out _);

        StringAssert.Contains(documents[0].Source, "# note: multipart form-data body was not imported");
        StringAssert.Contains(documents[0].Source, "file, label");
        FrsParseAssert.Parses(documents[0].Source);
    }

    [TestMethod]
    public void Bearer_basic_and_apikey_auth_become_auth_blocks()
    {
        string json = """
        {
          "info": {"name": "c", "_postman_id": "x"},
          "item": [
            {
              "name": "Bearer",
              "request": {
                "method": "GET",
                "url": "https://api.example.com/a",
                "auth": {"type": "bearer", "bearer": [{"key": "token", "value": "tok-1"}]}
              }
            },
            {
              "name": "Basic",
              "request": {
                "method": "GET",
                "url": "https://api.example.com/b",
                "auth": {"type": "basic", "basic": [{"key": "username", "value": "alice"}, {"key": "password", "value": "pw"}]}
              }
            },
            {
              "name": "ApiKey",
              "request": {
                "method": "GET",
                "url": "https://api.example.com/c",
                "auth": {"type": "apikey", "apikey": [{"key": "key", "value": "X-Api-Key"}, {"key": "value", "value": "k-123"}, {"key": "in", "value": "header"}]}
              }
            }
          ]
        }
        """;

        IReadOnlyList<PortableWorkspaceDocument> documents = service.Convert(json, out _);

        Assert.AreEqual(3, documents.Count);
        StringAssert.Contains(documents[0].Source, "mode = bearer");
        StringAssert.Contains(documents[0].Source, "token = \"tok-1\"");
        StringAssert.Contains(documents[1].Source, "mode = basic");
        StringAssert.Contains(documents[1].Source, "username = \"alice\"");
        StringAssert.Contains(documents[1].Source, "password = \"pw\"");
        StringAssert.Contains(documents[2].Source, "mode = apikey");
        StringAssert.Contains(documents[2].Source, "name = \"X-Api-Key\"");
        StringAssert.Contains(documents[2].Source, "value = \"k-123\"");
        StringAssert.Contains(documents[2].Source, "location = header");
        foreach (PortableWorkspaceDocument document in documents)
        {
            FrsParseAssert.Parses(document.Source);
        }
    }

    [TestMethod]
    public void V20_object_shaped_auth_is_supported()
    {
        string json = """
        {
          "info": {"name": "c", "_postman_id": "x"},
          "item": [
            {
              "name": "Old",
              "request": {
                "method": "GET",
                "url": "https://api.example.com/old",
                "auth": {"type": "bearer", "bearer": {"token": "legacy-token"}}
              }
            }
          ]
        }
        """;

        IReadOnlyList<PortableWorkspaceDocument> documents = service.Convert(json, out _);

        StringAssert.Contains(documents[0].Source, "token = \"legacy-token\"");
        FrsParseAssert.Parses(documents[0].Source);
    }

    [TestMethod]
    public void Postman_variables_pass_through_unchanged()
    {
        string json = """
        {
          "info": {"name": "c", "_postman_id": "x"},
          "item": [
            {"name": "R", "request": {"method": "GET", "url": "{{baseUrl}}/users"}}
          ]
        }
        """;

        IReadOnlyList<PortableWorkspaceDocument> documents = service.Convert(json, out _);

        StringAssert.Contains(documents[0].Source, "url \"{{baseUrl}}/users\"");
        FrsParseAssert.Parses(documents[0].Source);
    }

    [TestMethod]
    public void Collection_variables_become_synthetic_variables_document()
    {
        string json = """
        {
          "info": {"name": "c", "_postman_id": "x"},
          "variable": [
            {"key": "baseUrl", "value": "https://api.example.com"},
            {"key": "apiVersion", "value": "v2"}
          ],
          "item": [
            {"name": "R", "request": {"method": "GET", "url": "{{baseUrl}}/{{apiVersion}}/users"}}
          ]
        }
        """;

        IReadOnlyList<PortableWorkspaceDocument> documents = service.Convert(json, out _);

        Assert.AreEqual(2, documents.Count);
        PortableWorkspaceDocument variablesDocument = documents[0];
        Assert.AreEqual("Collection variables", variablesDocument.Name);
        StringAssert.Contains(variablesDocument.Source, "request baseUrl = \"https://api.example.com\"");
        StringAssert.Contains(variablesDocument.Source, "request apiVersion = \"v2\"");
        FrsParseAssert.Parses(variablesDocument.Source);
        FrsParseAssert.Parses(documents[1].Source);
    }

    [TestMethod]
    public void Duplicate_request_names_get_unique_locations()
    {
        string json = """
        {
          "info": {"name": "c", "_postman_id": "x"},
          "item": [
            {"name": "Ping", "request": {"method": "GET", "url": "https://api.example.com/1"}},
            {"name": "Ping", "request": {"method": "GET", "url": "https://api.example.com/2"}}
          ]
        }
        """;

        IReadOnlyList<PortableWorkspaceDocument> documents = service.Convert(json, out _);

        Assert.AreEqual(2, documents.Count);
        Assert.AreNotEqual(documents[0].Location, documents[1].Location);
    }
}

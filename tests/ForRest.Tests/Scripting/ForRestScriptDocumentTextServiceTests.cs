namespace ForRest.Tests.Scripting;

[TestClass]
public sealed class ForRestScriptDocumentTextServiceTests
{
    [TestMethod]
    public void Extract_reads_helper_sections_from_document_source()
    {
        var service = new ForRestScriptDocumentTextService();
        var source =
            """"
            meta {
              name = "Create Echo"
            }

            vars {
              request resource_id = "42"
              runtime trace_id = guid()
            }

            request {
              method = POST
              url = "https://api.example.test/echo/{{resource_id}}"
            }

            headers {
              Accept = "application/json"
              X-Trace-Id = "{{trace_id}}"
            }

            body json """
            {
              "id": "{{resource_id}}"
            }
            """

            tests {
              status == 200 "returns 200"
            }
            """";

        var sections = service.Extract(source);

        Assert.AreEqual("Create Echo", sections.Name);
        StringAssert.Contains(sections.Variables, "request resource_id = \"42\"");
        StringAssert.Contains(sections.Headers, "X-Trace-Id = \"{{trace_id}}\"");
        Assert.AreEqual(RequestBodyMode.Json, sections.BodyMode);
        StringAssert.Contains(sections.Body, "\"id\": \"{{resource_id}}\"");
        StringAssert.Contains(sections.Tests, "status == 200 \"returns 200\"");
    }

    [TestMethod]
    public void Upsert_updates_target_sections_without_removing_other_blocks()
    {
        var service = new ForRestScriptDocumentTextService();
        var source =
            """
            meta {
              name = "Old"
            }

            request {
              method = GET
              url = "https://api.example.test/items"
            }

            retry {
              count = 2
              interval = 500
            }
            """;

        var updated = service.UpsertMetaName(source, "New Name");
        updated = service.UpsertVariables(updated, "runtime trace_id = guid()");
        updated = service.UpsertHeaders(updated, "Accept = \"application/json\"");
        updated = service.UpsertBody(updated, RequestBodyMode.Json, """{"id":"42"}""");
        updated = service.UpsertTests(updated, "status == 200 \"returns 200\"");

        StringAssert.Contains(updated, "name = \"New Name\"");
        StringAssert.Contains(updated, "vars {");
        StringAssert.Contains(updated, "headers {");
        StringAssert.Contains(updated, "body json \"\"\"");
        StringAssert.Contains(updated, "tests {");
        StringAssert.Contains(updated, "retry {");
        StringAssert.Contains(updated, "count = 2");
    }
}

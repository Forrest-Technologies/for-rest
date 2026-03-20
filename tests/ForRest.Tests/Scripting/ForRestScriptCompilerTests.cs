namespace ForRest.Tests.Scripting;

[TestClass]
public sealed class ForRestScriptCompilerTests
{
    [TestMethod]
    public void Compile_builds_request_runtime_seeds_tests_and_retry_metadata()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """"
            meta {
              name = "Create Echo"
            }

            vars {
              request resource_id = "42"
              runtime trace_id = "seed-123"
              runtime attempt = random(1, 3)
            }

            request {
              method = POST
              url = "https://api.example.test/echo/{{resource_id}}?trace={{trace_id}}"
              timeout = 15000
              redirects = false
              ssl = true
              history = false
              content_type = "application/json"
            }

            headers {
              Accept = "application/json"
              X-Trace-Id = "{{trace_id}}"
            }

            body json """
            {
              "id": "{{resource_id}}",
              "trace": "{{trace_id}}"
            }
            """

            extract {
              runtime created_id = json "$.payload.id"
            }

            tests {
              status == 201 "returns 201"
              json "$.payload.id" == "42" "payload id matches"
              header "Content-Type" contains "json" "json content"
            }

            repeat {
              count = 2
              delay = 10
              interval = 25
            }

            retry {
              count = 1
              interval = 50
            }
            """";

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Payload);
        Assert.AreEqual("Create Echo", result.Payload.Request.Name);
        Assert.AreEqual(HttpMethodKind.Post, result.Payload.Request.Method);
        Assert.AreEqual("https://api.example.test/echo/{{resource_id}}?trace={{trace_id}}", result.Payload.Request.UrlTemplate);
        Assert.AreEqual(2, result.Payload.Request.Schedule.RepeatCount);
        Assert.AreEqual(1, result.Payload.Request.Retry.Count);
        Assert.AreEqual("application/json", result.Payload.Request.Body.ContentType);
        Assert.AreEqual("42", result.Payload.Request.Variables.Single(static item => item.Key == "resource_id").Value);
        Assert.AreEqual("seed-123", result.Payload.RuntimeSeeds.Single(static item => item.Key == "trace_id").LiteralValue);
        Assert.AreEqual(ForRestRuntimeSeedKind.RandomNumber, result.Payload.RuntimeSeeds.Single(static item => item.Key == "attempt").Kind);
        StringAssert.Contains(result.Payload.Request.TestsScript, "response.Status == 201");
        StringAssert.Contains(result.Payload.Request.TestsScript, "json.Select(response.Json(), @\"$.payload.id\")");
        StringAssert.Contains(result.Payload.Request.TestsScript, "out string __headerRaw3");
    }
}

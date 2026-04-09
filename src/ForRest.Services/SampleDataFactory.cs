namespace ForRest.Services;

internal static class SampleDataFactory
{
    public static AppState Create()
    {
        var workspaceId = Guid.NewGuid();
        var environmentId = Guid.NewGuid();
        var collectionId = Guid.NewGuid();
        var authFolderId = Guid.NewGuid();

        var listPostsRequest = new RequestDefinition
        {
            WorkspaceId = workspaceId,
            Name = "List Posts",
            Method = HttpMethodKind.Get,
            UrlTemplate = "{{baseUrl}}/posts",
            Headers =
            [
                new()
                {
                    Key = "Accept",
                    Value = "application/json",
                },
            ],
            Body = new()
            {
                Mode = RequestBodyMode.None,
            },
            Schedule = new()
            {
                RepeatCount = 1,
            },
        };

        var createPostRequest = new RequestDefinition
        {
            WorkspaceId = workspaceId,
            Name = "Create Post",
            Method = HttpMethodKind.Post,
            UrlTemplate = "{{baseUrl}}/posts",
            Headers =
            [
                new()
                {
                    Key = "Accept",
                    Value = "application/json",
                },
                new()
                {
                    Key = "X-Workbench",
                    Value = "ForRest",
                },
            ],
            Body = new()
            {
                Mode = RequestBodyMode.Json,
                ContentType = "application/json",
                RawContent = """
                    {
                      "title": "{{demoTitle}}",
                      "body": "A request sent from For-Rest",
                      "userId": 1
                    }
                    """,
            },
            TestsScript = """
                tests.Equal(201, response.Status, "Create Post returns 201.");
                var document = json.Parse(response.Body);
                if (document?["id"] is not null)
                {
                    variables.Set("createdPostId", document["id"]!.ToJsonString().Trim('"'));
                    tests.Pass("Saved created post id into runtime variables.");
                }
                """,
        };

        var echoRequest = new RequestDefinition
        {
            WorkspaceId = workspaceId,
            Name = "Echo Headers",
            Method = HttpMethodKind.Get,
            UrlTemplate = "https://httpbin.org/anything?source=forrest",
            Headers =
            [
                new()
                {
                    Key = "Accept",
                    Value = "application/json",
                },
                new()
                {
                    Key = "X-Environment",
                    Value = "{{environmentName}}",
                },
            ],
            PreRequestScript = """
                request.SetHeader("X-Trace-Id", random.Guid().ToString());
                console.Log("Added X-Trace-Id header.");
                """,
            TestsScript = """
                tests.Equal(200, response.Status, "Echo request returns 200.");
                """,
        };

        var crudWorkflowRequest = new RequestDefinition
        {
            WorkspaceId = workspaceId,
            Name = "CRUD Workflow",
            Method = HttpMethodKind.Get,
            UrlTemplate = "{{baseUrl}}/posts/1",
            Headers =
            [
                new()
                {
                    Key = "Accept",
                    Value = "application/json",
                },
            ],
            PreRequestScript = """
                var __flow = new ForRest.Scripting.ForRestFlowRuntime(variables);
                // pipe: GET -> POST -> cleanup
                request.Method = "GET";
                request.Url = variables.RenderTemplate("{{baseUrl}}/posts/1");
                dynamic fetched = (await request.SendAsync());
                console.Log($"GET returned {fetched.Status}");

                request.Method = "POST";
                request.Url = variables.RenderTemplate("{{baseUrl}}/posts");
                request.ContentType = "application/json";
                request.Body = "{\"title\":\"ForRest CRUD\",\"body\":\"Created by pipe workflow\",\"userId\":1}";
                dynamic created = (await request.SendAsync());
                console.Log($"POST returned {created.Status}");
                """,
            TestsScript = """
                tests.Assert(response.Status >= 200 && response.Status < 300, "CRUD workflow completed successfully.");
                """,
        };

        var parallelHealthRequest = new RequestDefinition
        {
            WorkspaceId = workspaceId,
            Name = "Parallel Health Check",
            Method = HttpMethodKind.Get,
            UrlTemplate = "https://httpbin.org/status/200",
            Headers =
            [
                new()
                {
                    Key = "Accept",
                    Value = "application/json",
                },
            ],
            PreRequestScript = """
                var __flow = new ForRest.Scripting.ForRestFlowRuntime(variables);
                // parallel health checks
                var __par1_1 = request.Clone();
                __par1_1.Method = "GET";
                __par1_1.Url = "https://httpbin.org/status/200";
                var __parTask1_1 = __par1_1.SendAsync();
                var __par1_2 = request.Clone();
                __par1_2.Method = "GET";
                __par1_2.Url = "https://httpbin.org/status/200";
                var __parTask1_2 = __par1_2.SendAsync();
                await Task.WhenAll(__parTask1_1, __parTask1_2);
                dynamic health_a = __parTask1_1.Result;
                dynamic health_b = __parTask1_2.Result;
                stash.DeclareColumns("Endpoint", "Status");
                stash.Set("Endpoint", "httpbin-a"); stash.Set("Status", health_a.Status.ToString()); stash.Commit();
                stash.Set("Endpoint", "httpbin-b"); stash.Set("Status", health_b.Status.ToString()); stash.Commit();
                console.Log($"Health A: {health_a.Status}, Health B: {health_b.Status}");
                """,
            TestsScript = """
                tests.Pass("Parallel health checks completed.");
                """,
            MaxSendIterations = 4,
        };

        return new()
        {
            Profile = new()
            {
                Theme = ThemeKind.System,
                GlobalVariables =
                [
                    new()
                    {
                        Key = "demoTitle",
                        Value = "Seeded from For-Rest",
                        Scope = VariableScope.Global,
                    },
                ],
                LastWorkspaceId = workspaceId,
            },
            Workspaces =
            [
                new()
                {
                    Workspace = new()
                    {
                        Id = workspaceId,
                        Name = "Demo Workspace",
                        Description = "Seeded workspace for the initial For-Rest MVP slice.",
                        ActiveEnvironmentId = environmentId,
                        Theme = ThemeKind.System,
                        Variables =
                        [
                            new()
                            {
                                Key = "baseUrl",
                                Value = "https://jsonplaceholder.typicode.com",
                                Scope = VariableScope.Workspace,
                            },
                        ],
                    },
                    Environments =
                    [
                        new()
                        {
                            Id = environmentId,
                            WorkspaceId = workspaceId,
                            Name = "Local Sandbox",
                            IsActive = true,
                            Variables =
                            [
                                new()
                                {
                                    Key = "environmentName",
                                    Value = "Local Sandbox",
                                    Scope = VariableScope.Environment,
                                },
                            ],
                        },
                    ],
                    Nodes =
                    [
                        new()
                        {
                            Id = collectionId,
                            WorkspaceId = workspaceId,
                            Kind = WorkspaceNodeKind.Collection,
                            Name = "Quickstart",
                            SortOrder = 0,
                        },
                        new()
                        {
                            Id = authFolderId,
                            WorkspaceId = workspaceId,
                            ParentId = collectionId,
                            Kind = WorkspaceNodeKind.Folder,
                            Name = "Requests",
                            SortOrder = 0,
                        },
                        new()
                        {
                            WorkspaceId = workspaceId,
                            ParentId = authFolderId,
                            Kind = WorkspaceNodeKind.Request,
                            Name = listPostsRequest.Name,
                            SortOrder = 0,
                            Request = listPostsRequest,
                        },
                        new()
                        {
                            WorkspaceId = workspaceId,
                            ParentId = authFolderId,
                            Kind = WorkspaceNodeKind.Request,
                            Name = createPostRequest.Name,
                            SortOrder = 1,
                            Request = createPostRequest,
                        },
                        new()
                        {
                            WorkspaceId = workspaceId,
                            ParentId = authFolderId,
                            Kind = WorkspaceNodeKind.Request,
                            Name = echoRequest.Name,
                            SortOrder = 2,
                            Request = echoRequest,
                        },
                        new()
                        {
                            WorkspaceId = workspaceId,
                            ParentId = authFolderId,
                            Kind = WorkspaceNodeKind.Request,
                            Name = crudWorkflowRequest.Name,
                            SortOrder = 3,
                            Request = crudWorkflowRequest,
                        },
                        new()
                        {
                            WorkspaceId = workspaceId,
                            ParentId = authFolderId,
                            Kind = WorkspaceNodeKind.Request,
                            Name = parallelHealthRequest.Name,
                            SortOrder = 4,
                            Request = parallelHealthRequest,
                        },
                    ],
                    ExecutionPresets =
                    [
                        new()
                        {
                            WorkspaceId = workspaceId,
                            Name = "Smoke x3",
                            RepeatCount = 3,
                            IntervalMilliseconds = 1_000,
                        },
                    ],
                },
            ],
        };
    }
}

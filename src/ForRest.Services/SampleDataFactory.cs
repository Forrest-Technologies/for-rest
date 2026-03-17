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

        return new()
        {
            Profile = new()
            {
                Theme = ThemeKind.Dark,
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
                        Theme = ThemeKind.Dark,
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

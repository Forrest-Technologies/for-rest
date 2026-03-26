namespace ForRest.Models;

public enum HttpMethodKind
{
    Get,
    Post,
    Put,
    Patch,
    Delete,
    Head,
    Options,
}

public enum RequestBodyMode
{
    None,
    RawText,
    Json,
    FormUrlEncoded,
    MultipartFormData,
}

public enum AuthMode
{
    None,
    BearerToken,
    Basic,
    ApiKey,
    Header,
    Digest,
    Ntlm,
    Negotiate,
    OAuthClientCredentials,
    OAuthDeviceCode,
    OAuthIntegratedWindows,
}

public enum ApiKeyLocation
{
    Header,
    Query,
}

public enum VariableScope
{
    System,
    Global,
    Workspace,
    Environment,
    RequestLocal,
    Runtime,
}

public enum WorkspaceNodeKind
{
    Collection,
    Folder,
    Request,
}

public enum ThemeKind
{
    System,
    Light,
    Azure,
    Dark,
    Black,
    AmberDark,
    Custom,
}

public enum TestOutcomeState
{
    Passed,
    Failed,
    Skipped,
}

public enum ConsoleEntryLevel
{
    Info,
    Warning,
    Error,
}

public enum ExecutionState
{
    Idle,
    Running,
    Completed,
    Failed,
    Cancelled,
}

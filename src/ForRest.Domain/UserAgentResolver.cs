using ForRest.Models;

namespace ForRest.Domain;

public static class UserAgentResolver
{
    #region Public Methods

    public static string? Resolve(UserAgentKind kind, string customValue = "") => kind switch
    {
        UserAgentKind.Chrome => "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
        UserAgentKind.Firefox => "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0",
        UserAgentKind.Safari => "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_7_1) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Safari/605.1.15",
        UserAgentKind.Edge => "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0",
        UserAgentKind.Curl => "curl/8.7.1",
        UserAgentKind.Custom when !string.IsNullOrWhiteSpace(customValue) => customValue,
        _ => null,
    };

    #endregion
}

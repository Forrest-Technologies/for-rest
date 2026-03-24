using System;
using System.Collections.Generic;
using System.Linq;

namespace ForRest.Maui.Services;

public static class RequestWorkbenchDocumentFactory
{
    public static RequestWorkbenchDocumentState CreateNewRequest(
        RequestWorkbenchWorkspaceState workspace,
        Func<string, string> targetBuilder,
        string method = "GET",
        string? summary = null)
    {
        string title = BuildNextRequestTitle(workspace);
        string location = BuildNextRequestLocation(workspace, title);
        string target = targetBuilder(location);

        return new()
        {
            Title = title,
            Method = method,
            Summary = string.IsNullOrWhiteSpace(summary) ? "New request ready to edit and send" : summary,
            Location = location,
            RequestSource = BuildRequestEditorText(title, method, target),
            PreRequestScript = string.Empty,
        };
    }

    private static string BuildNextRequestTitle(RequestWorkbenchWorkspaceState workspace)
    {
        const string baseTitle = "New Request";
        HashSet<string> existingTitles = workspace.Documents
            .Select(static item => item.Title)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!existingTitles.Contains(baseTitle))
        {
            return baseTitle;
        }

        int suffix = 2;
        string candidate = $"{baseTitle} {suffix}";
        while (existingTitles.Contains(candidate))
        {
            suffix++;
            candidate = $"{baseTitle} {suffix}";
        }

        return candidate;
    }

    private static string BuildNextRequestLocation(RequestWorkbenchWorkspaceState workspace, string title)
    {
        string workspaceSlug = BuildSlug(workspace.Name, "workspace");
        string requestSlug = BuildSlug(title, "request");
        string baseLocation = $"/requests/{workspaceSlug}/{requestSlug}";
        HashSet<string> existingLocations = workspace.Documents
            .Select(static item => item.Location)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!existingLocations.Contains(baseLocation))
        {
            return baseLocation;
        }

        int suffix = 2;
        string candidate = $"{baseLocation}-{suffix}";
        while (existingLocations.Contains(candidate))
        {
            suffix++;
            candidate = $"{baseLocation}-{suffix}";
        }

        return candidate;
    }

    private static string BuildSlug(string value, string fallback)
    {
        char[] slugCharacters = (value ?? string.Empty)
            .ToLowerInvariant()
            .Select(static character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();

        string slug = string.Join(
            "-",
            new string(slugCharacters)
                .Split('-', StringSplitOptions.RemoveEmptyEntries));

        return string.IsNullOrWhiteSpace(slug) ? fallback : slug;
    }

    private static string BuildRequestEditorText(string title, string method, string target)
    {
        string escapedTitle = EscapeForForRestString(title);
        string escapedTarget = EscapeForForRestString(target);

        return string.Join(
            Environment.NewLine,
            [
                $"name \"{escapedTitle}\"",
                $"method {method}",
                $"url \"{escapedTarget}\"",
                "timeout 15000",
                "max_send_iterations 3",
                "redirects true",
                "ssl true",
                "history true",
                string.Empty,
                "runtime trace_id = guid()",
                string.Empty,
                "header \"Accept\" = \"application/json\"",
                "header \"X-Workspace\" = \"{{workspace_name}}\"",
                "header \"X-Environment\" = \"{{environment_name}}\"",
                "header \"X-Correlation-Id\" = \"{{trace_id}}\"",
                string.Empty,
                "# Write ForRest code here. request.send() returns the latest response snapshot.",
                "request.headers[\"X-Request-Source\"] = \"maui\"",
                "let sent = request.send()",
                string.Empty,
                "if sent.status == 200 {",
                "  runtime last_status = sent.status",
                "  foreach step in range(0, 2) {",
                "    log step",
                "  }",
                "} else {",
                "  warn sent.status",
                "}",
                string.Empty,
                "expect status == 200 \"returns 200\"",
                "expect header \"Content-Type\" contains \"json\" \"json response\"",
            ]);
    }

    private static string EscapeForForRestString(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}

using System.IO.Compression;

namespace ForRest.Services.Sharing;

public interface IWorkspaceArchiveService
{
    /// <summary>
    /// Builds a shareable zip archive containing a <c>manifest.toml</c> at the root plus one .frs
    /// file per document. Secrets are redacted unless <paramref name="includeSecrets"/> is true.
    /// </summary>
    byte[] ExportArchive(PortableWorkspace workspace, bool includeSecrets = false);

    /// <summary>
    /// Reads a workspace archive back into a <see cref="PortableWorkspace"/>. Archives without a
    /// manifest are accepted: every .frs entry becomes a document with a derived name and location.
    /// Throws <see cref="FormatException"/> with a human-friendly message for invalid archives.
    /// </summary>
    PortableWorkspace ImportArchive(byte[] zipBytes);
}

public sealed class WorkspaceArchiveService : IWorkspaceArchiveService
{
    #region Private Fields

    private const string ManifestEntryName = "manifest.toml";
    private const string FormatIdentifier = "forrest-workspace/1";
    private const int MaxEntryCount = 500;
    private const long MaxEntryBytes = 5 * 1024 * 1024;

    #endregion

    #region Public Methods

    public byte[] ExportArchive(PortableWorkspace workspace, bool includeSecrets = false)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        List<(string Path, PortableWorkspaceDocument Document)> entries = [];
        HashSet<string> usedPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (PortableWorkspaceDocument document in workspace.Documents)
        {
            string path = BuildUniqueEntryPath(document.Location, usedPaths);
            entries.Add((path, document));
        }

        using MemoryStream output = new();
        using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, ManifestEntryName, BuildManifest(workspace, entries, includeSecrets));

            foreach ((string path, PortableWorkspaceDocument document) in entries)
            {
                string source = includeSecrets ? document.Source : FrsSecretRedactor.Redact(document.Source);
                WriteEntry(archive, path, source);
            }
        }

        return output.ToArray();
    }

    public PortableWorkspace ImportArchive(byte[] zipBytes)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);

        using MemoryStream input = new(zipBytes);
        ZipArchive archive;
        try
        {
            archive = new(input, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException exception)
        {
            throw new FormatException("The file is not a valid zip archive.", exception);
        }

        using (archive)
        {
            if (archive.Entries.Count > MaxEntryCount)
            {
                throw new FormatException($"The archive contains more than {MaxEntryCount} entries and was rejected.");
            }

            Dictionary<string, string> sourcesByPath = new(StringComparer.OrdinalIgnoreCase);
            string? manifestText = null;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                EnsureSafeEntryPath(entry.FullName);
                if (entry.Length > MaxEntryBytes)
                {
                    throw new FormatException($"The archive entry '{entry.FullName}' is larger than 5 MB and was rejected.");
                }

                if (string.Equals(entry.FullName, ManifestEntryName, StringComparison.OrdinalIgnoreCase))
                {
                    manifestText = ReadEntry(entry);
                    continue;
                }

                if (entry.FullName.EndsWith(".frs", StringComparison.OrdinalIgnoreCase))
                {
                    sourcesByPath[NormalizeEntryPath(entry.FullName)] = ReadEntry(entry);
                }
            }

            if (sourcesByPath.Count == 0)
            {
                throw new FormatException("The archive does not contain any .frs request documents.");
            }

            return manifestText is null
                ? BuildWorkspaceWithoutManifest(sourcesByPath)
                : BuildWorkspaceFromManifest(manifestText, sourcesByPath);
        }
    }

    #endregion

    #region Private Methods

    private static string BuildUniqueEntryPath(string location, HashSet<string> usedPaths)
    {
        string trimmed = (location ?? string.Empty).Trim().TrimStart('/', '\\');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            trimmed = "requests/document";
        }

        trimmed = trimmed.Replace('\\', '/');
        string basePath = trimmed.EndsWith(".frs", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;

        string candidate = $"{basePath}.frs";
        int suffix = 2;
        while (!usedPaths.Add(candidate))
        {
            candidate = $"{basePath}-{suffix}.frs";
            suffix++;
        }

        return candidate;
    }

    private static string BuildManifest(
        PortableWorkspace workspace,
        IReadOnlyList<(string Path, PortableWorkspaceDocument Document)> entries,
        bool includeSecrets)
    {
        List<string> lines =
        [
            $"format = \"{FormatIdentifier}\"",
            $"name = \"{EscapeTomlString(workspace.Name)}\"",
            $"environment = \"{EscapeTomlString(workspace.SelectedEnvironment)}\"",
            $"secrets = \"{(includeSecrets ? "included" : "redacted")}\"",
        ];

        foreach ((string path, PortableWorkspaceDocument document) in entries)
        {
            lines.Add(string.Empty);
            lines.Add("[[document]]");
            lines.Add($"path = \"{EscapeTomlString(path)}\"");
            lines.Add($"location = \"{EscapeTomlString(document.Location)}\"");
            lines.Add($"name = \"{EscapeTomlString(document.Name)}\"");
        }

        return string.Join("\n", lines) + "\n";
    }

    private static PortableWorkspace BuildWorkspaceWithoutManifest(IReadOnlyDictionary<string, string> sourcesByPath)
    {
        List<PortableWorkspaceDocument> documents = [];
        foreach (KeyValuePair<string, string> entry in sourcesByPath.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            documents.Add(new(
                Name: BuildFileStem(entry.Key),
                Location: "/" + entry.Key[..^4],
                Source: entry.Value));
        }

        return new("Imported workspace", "Local", documents);
    }

    private static PortableWorkspace BuildWorkspaceFromManifest(string manifestText, IReadOnlyDictionary<string, string> sourcesByPath)
    {
        string workspaceName = "Imported workspace";
        string environment = "Local";
        List<PortableWorkspaceDocument> documents = [];
        HashSet<string> consumedPaths = new(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, string>? currentDocument = null;
        foreach (string rawLine in manifestText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
            {
                continue;
            }

            if (string.Equals(line, "[[document]]", StringComparison.OrdinalIgnoreCase))
            {
                AppendManifestDocument(currentDocument, sourcesByPath, documents, consumedPaths);
                currentDocument = new(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            int separatorIndex = line.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            string key = line[..separatorIndex].Trim();
            string value = UnquoteTomlString(line[(separatorIndex + 1)..].Trim());
            if (currentDocument is not null)
            {
                currentDocument[key] = value;
                continue;
            }

            switch (key.ToLowerInvariant())
            {
                case "name":
                    workspaceName = value;
                    break;
                case "environment":
                    environment = value;
                    break;
            }
        }

        AppendManifestDocument(currentDocument, sourcesByPath, documents, consumedPaths);

        // Any .frs entry the manifest does not mention still becomes a document so no content is lost.
        foreach (KeyValuePair<string, string> entry in sourcesByPath.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (consumedPaths.Contains(entry.Key))
            {
                continue;
            }

            documents.Add(new(BuildFileStem(entry.Key), "/" + entry.Key[..^4], entry.Value));
        }

        return new(workspaceName, environment, documents);
    }

    private static void AppendManifestDocument(
        Dictionary<string, string>? manifestDocument,
        IReadOnlyDictionary<string, string> sourcesByPath,
        List<PortableWorkspaceDocument> documents,
        HashSet<string> consumedPaths)
    {
        if (manifestDocument is null || !manifestDocument.TryGetValue("path", out string? path))
        {
            return;
        }

        string normalizedPath = NormalizeEntryPath(path);
        if (!sourcesByPath.TryGetValue(normalizedPath, out string? source))
        {
            return;
        }

        consumedPaths.Add(normalizedPath);
        string location = manifestDocument.TryGetValue("location", out string? manifestLocation) && !string.IsNullOrWhiteSpace(manifestLocation)
            ? manifestLocation
            : "/" + normalizedPath[..^4];
        string name = manifestDocument.TryGetValue("name", out string? manifestName) && !string.IsNullOrWhiteSpace(manifestName)
            ? manifestName
            : BuildFileStem(normalizedPath);

        documents.Add(new(name, location, source));
    }

    private static void EnsureSafeEntryPath(string entryPath)
    {
        string normalized = entryPath.Replace('\\', '/');
        bool isRooted = normalized.StartsWith('/') || (normalized.Length >= 2 && normalized[1] == ':');
        bool escapes = normalized.Split('/').Any(static segment => segment == "..");
        if (isRooted || escapes)
        {
            throw new FormatException($"The archive entry '{entryPath}' uses an unsafe path and was rejected.");
        }
    }

    private static string NormalizeEntryPath(string entryPath)
    {
        return entryPath.Replace('\\', '/').TrimStart('/');
    }

    private static string BuildFileStem(string entryPath)
    {
        string fileName = entryPath[(entryPath.LastIndexOf('/') + 1)..];
        return fileName.EndsWith(".frs", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^4]
            : fileName;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using StreamWriter writer = new(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using StreamReader reader = new(entry.Open(), Encoding.UTF8);
        string content = reader.ReadToEnd();
        if (Encoding.UTF8.GetByteCount(content) > MaxEntryBytes)
        {
            throw new FormatException($"The archive entry '{entry.FullName}' is larger than 5 MB and was rejected.");
        }

        return content;
    }

    private static string EscapeTomlString(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }

    private static string UnquoteTomlString(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length < 2 || !trimmed.StartsWith('"') || !trimmed.EndsWith('"'))
        {
            return trimmed;
        }

        string inner = trimmed[1..^1];
        StringBuilder builder = new(inner.Length);
        for (int index = 0; index < inner.Length; index++)
        {
            char character = inner[index];
            if (character != '\\' || index + 1 >= inner.Length)
            {
                builder.Append(character);
                continue;
            }

            index++;
            builder.Append(inner[index] switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '"' => '"',
                '\\' => '\\',
                _ => inner[index],
            });
        }

        return builder.ToString();
    }

    #endregion
}

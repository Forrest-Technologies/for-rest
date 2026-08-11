using System.Collections.Generic;
using System.IO.Compression;
using System.Text;
using ForRest.Services.Sharing;

namespace ForRest.Tests.Services.Sharing;

[TestClass]
public sealed class WorkspaceArchiveServiceTests
{
    private readonly WorkspaceArchiveService service = new();

    private static PortableWorkspace BuildSampleWorkspace()
    {
        return new(
            "Team API",
            "Staging",
            [
                new(
                    "List users",
                    "/requests/team-api/list-users",
                    "name \"List users\"\nmethod GET\nurl \"https://api.example.com/users\"\nsecret api_key = \"sk-live-999\""),
                new(
                    "Create user",
                    "/requests/team-api/create-user",
                    "name \"Create user\"\nmethod POST\nurl \"https://api.example.com/users\""),
            ]);
    }

    [TestMethod]
    public void Export_produces_zip_with_manifest_and_frs_entries()
    {
        byte[] archiveBytes = service.ExportArchive(BuildSampleWorkspace());

        using ZipArchive archive = new(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
        List<string> entryNames = [.. archive.Entries.Select(static entry => entry.FullName)];
        CollectionAssert.Contains(entryNames, "manifest.toml");
        CollectionAssert.Contains(entryNames, "requests/team-api/list-users.frs");
        CollectionAssert.Contains(entryNames, "requests/team-api/create-user.frs");
    }

    [TestMethod]
    public void Export_manifest_declares_format_name_environment_and_redaction()
    {
        byte[] archiveBytes = service.ExportArchive(BuildSampleWorkspace());

        string manifest = ReadEntry(archiveBytes, "manifest.toml");
        StringAssert.Contains(manifest, "format = \"forrest-workspace/1\"");
        StringAssert.Contains(manifest, "name = \"Team API\"");
        StringAssert.Contains(manifest, "environment = \"Staging\"");
        StringAssert.Contains(manifest, "secrets = \"redacted\"");
        StringAssert.Contains(manifest, "[[document]]");
        StringAssert.Contains(manifest, "location = \"/requests/team-api/list-users\"");
    }

    [TestMethod]
    public void Export_redacts_secrets_by_default()
    {
        byte[] archiveBytes = service.ExportArchive(BuildSampleWorkspace());

        string source = ReadEntry(archiveBytes, "requests/team-api/list-users.frs");
        StringAssert.Contains(source, "secret api_key = \"***\"");
        Assert.IsFalse(source.Contains("sk-live-999", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Export_with_include_secrets_preserves_values_and_flags_manifest()
    {
        byte[] archiveBytes = service.ExportArchive(BuildSampleWorkspace(), includeSecrets: true);

        StringAssert.Contains(ReadEntry(archiveBytes, "requests/team-api/list-users.frs"), "sk-live-999");
        StringAssert.Contains(ReadEntry(archiveBytes, "manifest.toml"), "secrets = \"included\"");
    }

    [TestMethod]
    public void Round_trip_preserves_names_locations_and_sources()
    {
        PortableWorkspace original = BuildSampleWorkspace();

        PortableWorkspace imported = service.ImportArchive(service.ExportArchive(original, includeSecrets: true));

        Assert.AreEqual(original.Name, imported.Name);
        Assert.AreEqual(original.SelectedEnvironment, imported.SelectedEnvironment);
        Assert.AreEqual(original.Documents.Count, imported.Documents.Count);
        foreach (PortableWorkspaceDocument document in original.Documents)
        {
            PortableWorkspaceDocument match = imported.Documents.Single(item => item.Location == document.Location);
            Assert.AreEqual(document.Name, match.Name);
            Assert.AreEqual(document.Source, match.Source);
        }
    }

    [TestMethod]
    public void Round_trip_preserves_odd_names_and_locations()
    {
        PortableWorkspace original = new(
            "Quotes \"and\" slashes",
            "Prod",
            [new("Name with / slash \"quotes\"", "/browser/deep/nested/x.frs", "name \"X\"\nmethod GET\nurl \"https://x.example.com\"")]);

        PortableWorkspace imported = service.ImportArchive(service.ExportArchive(original));

        Assert.AreEqual(original.Name, imported.Name);
        Assert.AreEqual(original.Documents[0].Name, imported.Documents[0].Name);
        Assert.AreEqual(original.Documents[0].Location, imported.Documents[0].Location);
    }

    [TestMethod]
    public void Export_keeps_existing_frs_suffix_in_entry_path()
    {
        PortableWorkspace workspace = new(
            "W",
            "Local",
            [new("X", "/browser/x.frs", "name \"X\"\nurl \"https://x.example.com\"")]);

        byte[] archiveBytes = service.ExportArchive(workspace);

        using ZipArchive archive = new(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
        Assert.IsNotNull(archive.GetEntry("browser/x.frs"));
        Assert.IsNull(archive.GetEntry("browser/x.frs.frs"));
    }

    [TestMethod]
    public void Import_without_manifest_derives_documents_from_frs_entries()
    {
        byte[] archiveBytes = BuildZip(
            ("requests/ws/foo.frs", "name \"Foo\"\nurl \"https://foo.example.com\""),
            ("bar.frs", "name \"Bar\"\nurl \"https://bar.example.com\""));

        PortableWorkspace imported = service.ImportArchive(archiveBytes);

        Assert.AreEqual("Imported workspace", imported.Name);
        Assert.AreEqual("Local", imported.SelectedEnvironment);
        Assert.AreEqual(2, imported.Documents.Count);
        PortableWorkspaceDocument foo = imported.Documents.Single(static item => item.Name == "foo");
        Assert.AreEqual("/requests/ws/foo", foo.Location);
        PortableWorkspaceDocument bar = imported.Documents.Single(static item => item.Name == "bar");
        Assert.AreEqual("/bar", bar.Location);
    }

    [TestMethod]
    public void Import_rejects_non_zip_content()
    {
        FormatException exception = Assert.ThrowsExactly<FormatException>(
            () => service.ImportArchive(Encoding.UTF8.GetBytes("this is not a zip")));

        StringAssert.Contains(exception.Message, "zip");
    }

    [TestMethod]
    public void Import_rejects_zip_with_no_frs_entries()
    {
        byte[] archiveBytes = BuildZip(("readme.txt", "hello"));

        FormatException exception = Assert.ThrowsExactly<FormatException>(() => service.ImportArchive(archiveBytes));

        StringAssert.Contains(exception.Message, ".frs");
    }

    [TestMethod]
    public void Import_rejects_zip_slip_paths()
    {
        byte[] archiveBytes = BuildZip(("../evil.frs", "name \"evil\""));

        FormatException exception = Assert.ThrowsExactly<FormatException>(() => service.ImportArchive(archiveBytes));

        StringAssert.Contains(exception.Message, "unsafe path");
    }

    [TestMethod]
    public void Import_rejects_entries_larger_than_five_megabytes()
    {
        byte[] archiveBytes = BuildZip(("big.frs", new string('a', (5 * 1024 * 1024) + 1)));

        FormatException exception = Assert.ThrowsExactly<FormatException>(() => service.ImportArchive(archiveBytes));

        StringAssert.Contains(exception.Message, "5 MB");
    }

    [TestMethod]
    public void Import_rejects_archives_with_too_many_entries()
    {
        (string, string)[] entries = [.. Enumerable.Range(0, 501).Select(static index => ($"r/doc-{index}.frs", "name \"x\""))];

        FormatException exception = Assert.ThrowsExactly<FormatException>(() => service.ImportArchive(BuildZip(entries)));

        StringAssert.Contains(exception.Message, "500");
    }

    [TestMethod]
    public void Import_keeps_frs_entries_the_manifest_does_not_mention()
    {
        byte[] archiveBytes = BuildZip(
            ("manifest.toml", "format = \"forrest-workspace/1\"\nname = \"W\"\nenvironment = \"Dev\"\nsecrets = \"redacted\"\n\n[[document]]\npath = \"a.frs\"\nlocation = \"/requests/a\"\nname = \"A\"\n"),
            ("a.frs", "name \"A\"\nurl \"https://a.example.com\""),
            ("extra.frs", "name \"Extra\"\nurl \"https://extra.example.com\""));

        PortableWorkspace imported = service.ImportArchive(archiveBytes);

        Assert.AreEqual("W", imported.Name);
        Assert.AreEqual("Dev", imported.SelectedEnvironment);
        Assert.AreEqual(2, imported.Documents.Count);
        Assert.AreEqual("A", imported.Documents[0].Name);
        Assert.AreEqual("/requests/a", imported.Documents[0].Location);
        Assert.AreEqual("extra", imported.Documents[1].Name);
    }

    [TestMethod]
    public void Duplicate_locations_get_unique_entry_paths()
    {
        PortableWorkspace workspace = new(
            "W",
            "Local",
            [
                new("A", "/requests/same", "name \"A\""),
                new("B", "/requests/same", "name \"B\""),
            ]);

        PortableWorkspace imported = service.ImportArchive(service.ExportArchive(workspace));

        Assert.AreEqual(2, imported.Documents.Count);
        Assert.AreEqual(2, imported.Documents.Select(static item => item.Name).Distinct().Count());
    }

    private static byte[] BuildZip(params (string Path, string Content)[] entries)
    {
        using MemoryStream output = new();
        using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, string content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(path);
                using StreamWriter writer = new(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        return output.ToArray();
    }

    private static string ReadEntry(byte[] archiveBytes, string path)
    {
        using ZipArchive archive = new(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
        ZipArchiveEntry? entry = archive.GetEntry(path);
        Assert.IsNotNull(entry, $"Expected archive entry '{path}'.");
        using StreamReader reader = new(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

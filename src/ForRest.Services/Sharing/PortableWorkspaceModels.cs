namespace ForRest.Services.Sharing;

/// <summary>A single .frs document inside a portable workspace archive.</summary>
public sealed record PortableWorkspaceDocument(string Name, string Location, string Source);

/// <summary>A workspace snapshot that can be exported to, or imported from, a shareable archive.</summary>
public sealed record PortableWorkspace(string Name, string SelectedEnvironment, IReadOnlyList<PortableWorkspaceDocument> Documents);

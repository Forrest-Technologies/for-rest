namespace ForRest.Services.AI;

public interface IAiToolCatalog
{
    IReadOnlyList<AiToolDescriptor> GetTools(AiSettings settings, IAiActiveDocumentHost? activeDocumentHost = null);
}

public sealed class AiLocalToolCatalog : IAiToolCatalog
{
    public IReadOnlyList<AiToolDescriptor> GetTools(AiSettings settings, IAiActiveDocumentHost? activeDocumentHost = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled)
        {
            return [];
        }

        List<AiToolDescriptor> tools = [];
        if (settings.Tools.EnableDocsSearch)
        {
            tools.Add(new(
                "search_docs",
                "Search the local For-Rest docs and language reference for relevant facts.",
                "Provide a concise query string and a bounded result count.",
                MutatesDocument: false));
        }

        if (settings.Tools.EnableDocumentPatch && activeDocumentHost is null)
        {
            tools.Add(new(
                "patch_document",
                "Apply bounded text edits to the active document.",
                "Provide the document id plus a small set of non-overlapping text edits.",
                MutatesDocument: true));
        }

        return tools;
    }
}

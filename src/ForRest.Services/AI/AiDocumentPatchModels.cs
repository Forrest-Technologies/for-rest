namespace ForRest.Services.AI;

public sealed record AiTextEdit(
    int StartIndex,
    int Length,
    string Replacement);

public sealed record AiDocumentPatchRequest(
    string DocumentId,
    string SourceText,
    IReadOnlyList<AiTextEdit> Edits);

public sealed record AiDocumentPatchResult(
    bool Succeeded,
    string PatchedText,
    IReadOnlyList<string> Errors)
{
    public static AiDocumentPatchResult Success(string text)
    {
        return new(true, text, []);
    }

    public static AiDocumentPatchResult Failure(string originalText, params string[] errors)
    {
        return new(false, originalText, errors);
    }
}

public interface IAiDocumentPatchService
{
    AiDocumentPatchResult Apply(AiDocumentPatchRequest request);
}

public sealed class AiDocumentPatchService : IAiDocumentPatchService
{
    public AiDocumentPatchResult Apply(AiDocumentPatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.DocumentId))
        {
            return AiDocumentPatchResult.Failure(request.SourceText, "Document id is required.");
        }

        if (request.Edits is null || request.Edits.Count == 0)
        {
            return AiDocumentPatchResult.Success(request.SourceText);
        }

        List<string> errors = [];
        List<AiTextEdit> edits = [.. request.Edits];
        for (int index = 0; index < edits.Count; index++)
        {
            AiTextEdit edit = edits[index];
            if (edit.StartIndex < 0)
            {
                errors.Add($"Edit {index + 1} has a negative start index.");
            }

            if (edit.Length < 0)
            {
                errors.Add($"Edit {index + 1} has a negative length.");
            }

            if (edit.StartIndex > request.SourceText.Length)
            {
                errors.Add($"Edit {index + 1} starts beyond the end of the source text.");
            }

            if (edit.StartIndex + edit.Length > request.SourceText.Length)
            {
                errors.Add($"Edit {index + 1} extends beyond the end of the source text.");
            }
        }

        List<(int Start, int End)> ranges = edits
            .Select(static edit => (Start: edit.StartIndex, End: edit.StartIndex + edit.Length))
            .OrderBy(static range => range.Start)
            .ToList();

        for (int index = 1; index < ranges.Count; index++)
        {
            if (ranges[index].Start < ranges[index - 1].End)
            {
                errors.Add("Edits must not overlap.");
                break;
            }
        }

        if (errors.Count > 0)
        {
            return AiDocumentPatchResult.Failure(request.SourceText, [.. errors]);
        }

        StringBuilder builder = new(request.SourceText);
        foreach (AiTextEdit edit in edits.OrderByDescending(static edit => edit.StartIndex))
        {
            builder.Remove(edit.StartIndex, edit.Length);
            builder.Insert(edit.StartIndex, edit.Replacement ?? string.Empty);
        }

        return AiDocumentPatchResult.Success(builder.ToString());
    }
}

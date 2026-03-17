using ForRest.Models;
using ForRest.Shared;

namespace ForRest.Domain;

public sealed class JsonEditorService
{
    #region Public Methods

    public OperationResult<string> Format(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return OperationResult<string>.Success(JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
            }));
        }
        catch (JsonException exception)
        {
            return OperationResult<string>.Fail(exception.Message);
        }
    }

    public OperationResult<string> Minify(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return OperationResult<string>.Success(JsonSerializer.Serialize(document.RootElement));
        }
        catch (JsonException exception)
        {
            return OperationResult<string>.Fail(exception.Message);
        }
    }

    public OperationResult Validate(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return OperationResult.Success();
        }
        catch (JsonException exception)
        {
            return OperationResult.Fail(exception.Message);
        }
    }

    #endregion
}

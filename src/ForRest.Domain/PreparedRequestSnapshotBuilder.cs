using System.Text;
using ForRest.Models;

namespace ForRest.Domain;

public static class PreparedRequestSnapshotBuilder
{
    public static RequestSnapshot Build(PreparedRequest preparedRequest, DateTimeOffset? sentUtc = null)
    {
        ArgumentNullException.ThrowIfNull(preparedRequest);

        string body = BuildBody(preparedRequest.Body);
        string contentType = ResolveContentType(preparedRequest.Body);

        return new()
        {
            Method = preparedRequest.Method.ToString().ToUpperInvariant(),
            Url = preparedRequest.Uri.ToString(),
            ContentType = contentType,
            SizeBytes = Encoding.UTF8.GetByteCount(body),
            Body = body,
            RawRequest = preparedRequest.RawRequest,
            Headers = [.. preparedRequest.Headers],
            SentUtc = sentUtc ?? DateTimeOffset.UtcNow,
        };
    }

    private static string ResolveContentType(RequestBodyDefinition body)
    {
        if (!string.IsNullOrWhiteSpace(body.ContentType))
        {
            return body.ContentType;
        }

        return body.Mode switch
        {
            RequestBodyMode.Json => "application/json",
            RequestBodyMode.FormUrlEncoded => "application/x-www-form-urlencoded",
            RequestBodyMode.MultipartFormData => "multipart/form-data",
            _ => string.Empty,
        };
    }

    private static string BuildBody(RequestBodyDefinition body)
    {
        if (body.Mode == RequestBodyMode.None)
        {
            return string.Empty;
        }

        if (body.Mode is RequestBodyMode.FormUrlEncoded or RequestBodyMode.MultipartFormData)
        {
            return string.Join(
                "&",
                body.FormValues
                    .Where(static item => item.IsEnabled)
                    .Select(static item => $"{item.Key}={item.Value}"));
        }

        return body.RawContent ?? string.Empty;
    }
}

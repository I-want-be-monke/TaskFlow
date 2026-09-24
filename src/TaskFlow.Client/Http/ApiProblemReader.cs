using System.Net;
using System.Text.Json;

namespace TaskFlow.Client.Http;

public sealed class ApiProblemReader
{
    public async Task<ApiProblem> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        string title = response.ReasonPhrase ?? "Request failed";
        string? detail = null;
        string code = $"http.{(int)response.StatusCode}";
        string? traceId = null;

        if (response.Content is not null)
        {
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(json);
                    JsonElement root = document.RootElement;
                    title = ReadString(root, "title") ?? title;
                    detail = ReadString(root, "detail");
                    code = ReadString(root, "code") ?? code;
                    traceId = ReadString(root, "traceId");
                }
                catch (JsonException)
                {
                    detail = null;
                }
            }
        }

        return new ApiProblem(
            response.StatusCode,
            code,
            title,
            detail,
            traceId);
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }
}

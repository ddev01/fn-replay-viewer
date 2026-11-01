using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReplayViewer;

internal static class HttpJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task WriteJsonAsync(
        HttpListenerResponse response,
        object value,
        int status = 200,
        string? fileName = null
    )
    {
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            response.AddHeader(
                "Content-Disposition",
                $"attachment; filename=\"{fileName}\""
            );
        }

        await JsonSerializer.SerializeAsync(response.OutputStream, value, Options)
            .ConfigureAwait(false);
        response.OutputStream.Close();
    }

    public static async Task WriteErrorAsync(HttpListenerResponse response, int status, string error)
    {
        await WriteJsonAsync(response, new ErrorBody(error), status).ConfigureAwait(false);
    }

    private sealed record ErrorBody(string Error);
}

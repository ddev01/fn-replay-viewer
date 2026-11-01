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

    public static async Task WriteBytesAsync(
        HttpListenerResponse response,
        byte[] body,
        string contentType,
        int status = 200,
        string? cacheControl = null
    )
    {
        response.StatusCode = status;
        response.ContentType = contentType;
        if (!string.IsNullOrWhiteSpace(cacheControl))
        {
            response.AddHeader("Cache-Control", cacheControl);
        }

        response.ContentLength64 = body.Length;
        await response.OutputStream.WriteAsync(body).ConfigureAwait(false);
        response.OutputStream.Close();
    }

    private sealed record ErrorBody(string Error);
}

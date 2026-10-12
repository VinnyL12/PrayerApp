using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PrayerApp.Client.Services;

/// <summary>
/// Outcome of an API call. Pages branch on Succeeded instead of catching exceptions,
/// similar to returning { data, error } from a fetch wrapper in TypeScript.
/// </summary>
public record ApiResult(bool Succeeded, string? Error, HttpStatusCode? Status)
{
    public static readonly ApiResult Success = new(true, null, null);

    public static ApiResult NetworkFailure() =>
        new(false, "Can't reach the server. Check that the API is running and try again.", null);

    public static async Task<ApiResult> FailureFromAsync(HttpResponseMessage response) =>
        new(false, await ReadErrorAsync(response), response.StatusCode);

    /// <summary>Turns an RFC 7807 problem response into one readable sentence.</summary>
    protected static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(ApiJson.Options);
            if (problem?.Errors is { Count: > 0 } errors)
            {
                return string.Join(" ", errors.Values.SelectMany(messages => messages));
            }

            if (!string.IsNullOrWhiteSpace(problem?.Detail))
            {
                return problem.Detail;
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Empty or non-JSON body; fall through to a generic message.
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Your session has ended. Please sign in again.",
            HttpStatusCode.Forbidden => "You don't have access to that.",
            HttpStatusCode.NotFound => "That couldn't be found.",
            _ => $"Something went wrong ({(int)response.StatusCode}). Please try again.",
        };
    }

    private sealed record ProblemBody(string? Title, string? Detail, Dictionary<string, string[]>? Errors);
}

public sealed record ApiResult<T>(T? Value, bool Succeeded, string? Error, HttpStatusCode? Status)
    : ApiResult(Succeeded, Error, Status)
{
    public static ApiResult<T> Ok(T value) => new(value, true, null, null);

    public static new ApiResult<T> NetworkFailure() =>
        new(default, false, ApiResult.NetworkFailure().Error, null);

    public static new async Task<ApiResult<T>> FailureFromAsync(HttpResponseMessage response) =>
        new(default, false, await ReadErrorAsync(response), response.StatusCode);
}

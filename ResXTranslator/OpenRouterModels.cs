using System.Globalization;
using System.Net;

namespace ResXTranslator;

static class OpenRouterSettings
{
    public const string ApiKeyStorageKey = "openrouter_api_key";
    public const string ModelPreferenceKey = "openrouter_model_id";
}

enum OpenRouterConnectionState
{
    NotConnected,
    Checking,
    Connected,
    Unverified,
    NeedsAttention
}

sealed record OpenRouterModel(
    string Id,
    string Name,
    decimal? PromptPricePerToken,
    decimal? CompletionPricePerToken)
{
    public string Provider => Id.Contains('/') ? Id[..Id.IndexOf('/')] : Id;

    public string PriceDescription
    {
        get
        {
            if (PromptPricePerToken is < 0 || CompletionPricePerToken is < 0 ||
                PromptPricePerToken is null || CompletionPricePerToken is null)
            {
                return "Variable pricing";
            }

            if (PromptPricePerToken == 0 && CompletionPricePerToken == 0)
            {
                return "Free";
            }

            var input = FormatPerMillion(PromptPricePerToken.Value);
            var output = FormatPerMillion(CompletionPricePerToken.Value);
            return $"Input {input} · Output {output} / 1M tokens";
        }
    }

    static string FormatPerMillion(decimal pricePerToken)
    {
        var perMillion = pricePerToken * 1_000_000m;
        return perMillion.ToString("$0.####", CultureInfo.InvariantCulture);
    }
}

sealed record OpenRouterTranslationInput(int Id, string Text);

readonly record struct OpenRouterTokenUsage(int PromptTokens, int CompletionTokens, int TotalTokens)
{
    public static OpenRouterTokenUsage operator +(OpenRouterTokenUsage left, OpenRouterTokenUsage right) =>
        new(
            left.PromptTokens + right.PromptTokens,
            left.CompletionTokens + right.CompletionTokens,
            left.TotalTokens + right.TotalTokens);
}

sealed record OpenRouterTranslationBatch(
    IReadOnlyDictionary<int, string> Translations,
    OpenRouterTokenUsage Usage);

sealed class OpenRouterApiException : Exception
{
    public OpenRouterApiException(HttpStatusCode statusCode, string? apiMessage = null)
        : base(ToUserMessage(statusCode, apiMessage))
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }

    static string ToUserMessage(HttpStatusCode statusCode, string? apiMessage) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "OpenRouter rejected the API key. Open Account and replace it.",
        HttpStatusCode.PaymentRequired => "This OpenRouter account does not have enough credit for the request.",
        HttpStatusCode.NotFound => "The selected OpenRouter model is no longer available. Choose another model.",
        HttpStatusCode.RequestTimeout => "OpenRouter timed out before completing the request. Please try again.",
        HttpStatusCode.TooManyRequests => "OpenRouter's rate limit was reached. Wait a moment before trying again.",
        HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout =>
            "OpenRouter is temporarily unavailable. Please try again later.",
        _ when !string.IsNullOrWhiteSpace(apiMessage) => $"OpenRouter could not complete the request: {apiMessage}",
        _ => $"OpenRouter could not complete the request ({(int)statusCode})."
    };
}

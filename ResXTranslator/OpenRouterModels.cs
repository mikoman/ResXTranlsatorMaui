using System.Globalization;
using System.Net;

namespace ResXTranslator;

static class OpenRouterSettings
{
    public const string ApiKeyStorageKey = "openrouter_api_key";
    public const string ModelPreferenceKey = "openrouter_model_id";
    public const string DomainInstructionsPreferenceKey = "translation_domain_instructions";
    public const string PaidConcurrencyPreferenceKey = "paid_model_concurrency";
    public const int DefaultPaidConcurrency = 4;
    public const int MinimumPaidConcurrency = 1;
    public const int MaximumPaidConcurrency = 10;

    public const string DefaultDomainInstructions = """
        Translate English product UI strings for a sports fan engagement and ticketing application using natural, concise terminology appropriate for sports audiences, teams, scheduled competitions and events, venues, rewards, ticket purchasing, ticket management, and attendance.
        """;

    public static string LoadDomainInstructions()
    {
        var saved = Preferences.Default.Get(DomainInstructionsPreferenceKey, string.Empty).Trim();
        return string.IsNullOrWhiteSpace(saved) ? DefaultDomainInstructions : saved;
    }

    public static int LoadPaidConcurrency()
    {
        var saved = Preferences.Default.Get(PaidConcurrencyPreferenceKey, DefaultPaidConcurrency);
        return saved is >= MinimumPaidConcurrency and <= MaximumPaidConcurrency
            ? saved
            : DefaultPaidConcurrency;
    }

    public static void Save(string domainInstructions, int paidConcurrency)
    {
        var normalizedInstructions = domainInstructions.Trim();

        if (string.Equals(normalizedInstructions, DefaultDomainInstructions, StringComparison.Ordinal))
        {
            Preferences.Default.Remove(DomainInstructionsPreferenceKey);
        }
        else
        {
            Preferences.Default.Set(DomainInstructionsPreferenceKey, normalizedInstructions);
        }

        var normalizedConcurrency = Math.Clamp(
            paidConcurrency,
            MinimumPaidConcurrency,
            MaximumPaidConcurrency);

        if (normalizedConcurrency == DefaultPaidConcurrency)
        {
            Preferences.Default.Remove(PaidConcurrencyPreferenceKey);
        }
        else
        {
            Preferences.Default.Set(PaidConcurrencyPreferenceKey, normalizedConcurrency);
        }
    }

    public static int GetParallelRequestLimit(OpenRouterModel model, int paidConcurrency) =>
        model.IsDefinitelyPaid
            ? Math.Clamp(paidConcurrency, MinimumPaidConcurrency, MaximumPaidConcurrency)
            : 2;
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
    decimal? CompletionPricePerToken,
    bool SupportsReasoning = false,
    bool RequiresReasoning = false)
{
    public string Provider => Id.Contains('/') ? Id[..Id.IndexOf('/')] : Id;

    public bool IsDefinitelyFree => PromptPricePerToken == 0 && CompletionPricePerToken == 0;

    public bool IsDefinitelyPaid => PromptPricePerToken is > 0 || CompletionPricePerToken is > 0;

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

sealed record TranslationSettingsResult(string DomainInstructions, int PaidModelConcurrency);

readonly record struct TranslationExecutionSettings(string DomainInstructions, int ParallelRequestLimit);

sealed record OpenRouterTranslationInput(int Id, string Text);

readonly record struct OpenRouterTokenUsage(int PromptTokens, int CompletionTokens, int TotalTokens)
{
    public int ReasoningTokens { get; init; }

    public static OpenRouterTokenUsage operator +(OpenRouterTokenUsage left, OpenRouterTokenUsage right) =>
        new(
            left.PromptTokens + right.PromptTokens,
            left.CompletionTokens + right.CompletionTokens,
            left.TotalTokens + right.TotalTokens)
        {
            ReasoningTokens = left.ReasoningTokens + right.ReasoningTokens
        };
}

enum OpenRouterTranslationStage
{
    Sending,
    WaitingForResponse,
    ProviderConnected,
    ReceivingResponse,
    ValidatingResponse,
    Retrying,
    Completed,
    Failed
}

readonly record struct OpenRouterTranslationProgress(
    string RequestId,
    OpenRouterTranslationStage Stage,
    TimeSpan Elapsed,
    long ResponseBytes,
    int ResponseCharacters,
    int AttemptNumber = 1,
    int MaximumAttempts = 5);

sealed record OpenRouterTranslationBatch(
    IReadOnlyDictionary<int, string> Translations,
    OpenRouterTokenUsage Usage);

sealed class OpenRouterProviderException : Exception
{
    public OpenRouterProviderException(
        string requestId,
        string providerName,
        string message,
        Exception innerException,
        OpenRouterTokenUsage usage)
        : base(message, innerException)
    {
        RequestId = requestId;
        ProviderName = providerName;
        Usage = usage;
    }

    public string RequestId { get; }

    public string ProviderName { get; }

    public OpenRouterTokenUsage Usage { get; }
}

sealed class OpenRouterApiException : Exception
{
    public OpenRouterApiException(HttpStatusCode statusCode, string? apiMessage = null)
        : base(ToUserMessage(statusCode, apiMessage))
    {
        StatusCode = statusCode;
        ApiMessage = apiMessage;
    }

    public HttpStatusCode StatusCode { get; }

    public string? ApiMessage { get; }

    static string ToUserMessage(HttpStatusCode statusCode, string? apiMessage) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "OpenRouter rejected the API key. Open Account and replace it.",
        HttpStatusCode.PaymentRequired => "This OpenRouter account does not have enough credit for the request.",
        HttpStatusCode.NotFound when !string.IsNullOrWhiteSpace(apiMessage) =>
            $"OpenRouter could not route this request: {apiMessage}",
        HttpStatusCode.NotFound => "OpenRouter could not find a compatible provider for this request.",
        HttpStatusCode.RequestTimeout => "OpenRouter timed out before completing the request. Please try again.",
        HttpStatusCode.TooManyRequests => "OpenRouter's rate limit was reached. Wait a moment before trying again.",
        HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout =>
            "OpenRouter is temporarily unavailable. Please try again later.",
        _ when !string.IsNullOrWhiteSpace(apiMessage) => $"OpenRouter could not complete the request: {apiMessage}",
        _ => $"OpenRouter could not complete the request ({(int)statusCode})."
    };
}

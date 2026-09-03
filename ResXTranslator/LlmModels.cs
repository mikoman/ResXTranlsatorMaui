using System.Globalization;
using System.Net;

namespace ResXTranslator;

enum LlmProviderId
{
    OpenRouter,
    OpenAI,
    Anthropic,
    Gemini,
    DeepSeek,
    Ollama,
    LmStudio,
    Custom
}

enum LlmApiDialect
{
    OpenAiChatCompletions,
    AnthropicMessages,
    GeminiGenerateContent,
    DeepSeekResponses
}

[Flags]
enum LlmProviderCapabilities
{
    None = 0,
    ModelCatalog = 1,
    StrictJsonSchema = 2,
    Streaming = 4,
    TokenUsage = 8,
    MultiRouteFailover = 16
}

sealed record LlmProviderDescriptor(
    LlmProviderId Id,
    string Name,
    string DefaultEndpoint,
    LlmApiDialect Dialect,
    bool RequiresApiKey,
    bool IsLocal,
    LlmProviderCapabilities Capabilities)
{
    public bool IsAvailable =>
        !IsLocal || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsWindows();
}

sealed record LlmConnectionProfile(
    LlmProviderId ProviderId,
    Uri Endpoint,
    string? ApiKey,
    int Concurrency);

static class LlmSettings
{
    const string CompatibilityContractVersion = "translation-map-v2";
    const string ActiveProviderPreferenceKey = "llm_active_provider";
    const string DomainInstructionsPreferenceKey = "translation_domain_instructions";
    const string LegacyModelPreferenceKey = "openrouter_model_id";
    const string LegacyConcurrencyPreferenceKey = "paid_model_concurrency";
    const int DefaultCloudConcurrency = 4;
    const int DefaultLocalConcurrency = 1;

    public const int MinimumConcurrency = 1;
    public const int MaximumConcurrency = 10;

    public const string DefaultDomainInstructions = """
        Translate English product UI strings for a sports fan engagement and ticketing application using natural, concise terminology appropriate for sports audiences, teams, scheduled competitions and events, venues, rewards, ticket purchasing, ticket management, and attendance.
        """;

    public static LlmProviderId ActiveProvider
    {
        get
        {
            var saved = Preferences.Default.Get(ActiveProviderPreferenceKey, nameof(LlmProviderId.OpenRouter));
            return Enum.TryParse<LlmProviderId>(saved, out var providerId)
                ? providerId
                : LlmProviderId.OpenRouter;
        }
        set => Preferences.Default.Set(ActiveProviderPreferenceKey, value.ToString());
    }

    public static string LoadDomainInstructions()
    {
        var saved = Preferences.Default.Get(DomainInstructionsPreferenceKey, string.Empty).Trim();
        return string.IsNullOrWhiteSpace(saved) ? DefaultDomainInstructions : saved;
    }

    public static void SaveDomainInstructions(string value)
    {
        var normalized = value.Trim();
        if (string.Equals(normalized, DefaultDomainInstructions, StringComparison.Ordinal))
        {
            Preferences.Default.Remove(DomainInstructionsPreferenceKey);
        }
        else
        {
            Preferences.Default.Set(DomainInstructionsPreferenceKey, normalized);
        }
    }

    public static string LoadEndpoint(LlmProviderDescriptor provider) =>
        Preferences.Default.Get(EndpointKey(provider.Id), provider.DefaultEndpoint).Trim();

    public static void SaveEndpoint(LlmProviderId providerId, string endpoint)
    {
        var normalized = endpoint.Trim();
        if (!string.Equals(
            Preferences.Default.Get(EndpointKey(providerId), string.Empty),
            normalized,
            StringComparison.Ordinal))
        {
            Preferences.Default.Set(EndpointKey(providerId), normalized);
            InvalidateModelCompatibility(providerId);
        }
    }

    public static string LoadModelId(LlmProviderId providerId)
    {
        var value = Preferences.Default.Get(ModelKey(providerId), string.Empty).Trim();
        if (providerId == LlmProviderId.OpenRouter && string.IsNullOrWhiteSpace(value))
        {
            value = Preferences.Default.Get(LegacyModelPreferenceKey, string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                Preferences.Default.Set(ModelKey(providerId), value);
            }
        }

        return value;
    }

    public static void SaveModelId(LlmProviderId providerId, string modelId)
    {
        var normalized = modelId.Trim();
        if (!string.Equals(
            Preferences.Default.Get(ModelKey(providerId), string.Empty),
            normalized,
            StringComparison.Ordinal))
        {
            Preferences.Default.Set(ModelKey(providerId), normalized);
            InvalidateModelCompatibility(providerId);
        }
    }

    public static int LoadConcurrency(LlmProviderDescriptor provider)
    {
        var defaultValue = provider.IsLocal ? DefaultLocalConcurrency : DefaultCloudConcurrency;
        var key = ConcurrencyKey(provider.Id);
        var value = Preferences.Default.Get(key, int.MinValue);

        if (value == int.MinValue && provider.Id == LlmProviderId.OpenRouter)
        {
            value = Preferences.Default.Get(LegacyConcurrencyPreferenceKey, defaultValue);
            Preferences.Default.Set(key, value);
        }

        return value is >= MinimumConcurrency and <= MaximumConcurrency
            ? value
            : defaultValue;
    }

    public static void SaveConcurrency(LlmProviderDescriptor provider, int value) =>
        Preferences.Default.Set(
            ConcurrencyKey(provider.Id),
            Math.Clamp(value, MinimumConcurrency, MaximumConcurrency));

    public static int GetParallelRequestLimit(
        LlmProviderDescriptor provider,
        LlmModel model,
        int configuredConcurrency)
    {
        if (provider.Id == LlmProviderId.OpenRouter && !model.IsDefinitelyPaid)
        {
            return 2;
        }

        return Math.Clamp(configuredConcurrency, MinimumConcurrency, MaximumConcurrency);
    }

    public static bool IsModelCompatibilityVerified(
        LlmProviderId providerId,
        Uri endpoint,
        string modelId) =>
        string.Equals(
            Preferences.Default.Get(CompatibilityKey(providerId), string.Empty),
            CompatibilityFingerprint(endpoint, modelId),
            StringComparison.Ordinal);

    public static void SaveModelCompatibility(LlmProviderId providerId, Uri endpoint, string modelId) =>
        Preferences.Default.Set(
            CompatibilityKey(providerId),
            CompatibilityFingerprint(endpoint, modelId));

    public static void InvalidateModelCompatibility(LlmProviderId providerId) =>
        Preferences.Default.Remove(CompatibilityKey(providerId));

    static string CompatibilityFingerprint(Uri endpoint, string modelId) =>
        $"{CompatibilityContractVersion}|{endpoint.AbsoluteUri.TrimEnd('/')}|{modelId.Trim()}";

    static string EndpointKey(LlmProviderId providerId) => $"llm_{providerId.ToString().ToLowerInvariant()}_endpoint";
    static string ModelKey(LlmProviderId providerId) => $"llm_{providerId.ToString().ToLowerInvariant()}_model";
    static string ConcurrencyKey(LlmProviderId providerId) => $"llm_{providerId.ToString().ToLowerInvariant()}_concurrency";
    static string CompatibilityKey(LlmProviderId providerId) => $"llm_{providerId.ToString().ToLowerInvariant()}_compatibility";
}

enum LlmConnectionState
{
    NotConnected,
    Checking,
    Connected,
    Unverified,
    NeedsAttention
}

sealed record LlmModel(
    string Id,
    string Name,
    decimal? PromptPricePerToken,
    decimal? CompletionPricePerToken,
    bool SupportsReasoning = false,
    bool RequiresReasoning = false)
{
    public string Provider { get; init; } = Id.Contains('/') ? Id[..Id.IndexOf('/')] : string.Empty;
    public bool IsDefinitelyFree => PromptPricePerToken == 0 && CompletionPricePerToken == 0;
    public bool IsDefinitelyPaid => PromptPricePerToken is > 0 || CompletionPricePerToken is > 0;

    public string PriceDescription
    {
        get
        {
            if (PromptPricePerToken is < 0 || CompletionPricePerToken is < 0 ||
                PromptPricePerToken is null || CompletionPricePerToken is null)
            {
                return "Pricing managed by provider";
            }

            if (PromptPricePerToken == 0 && CompletionPricePerToken == 0)
            {
                return "Free";
            }

            return $"Input {FormatPerMillion(PromptPricePerToken.Value)} · " +
                $"Output {FormatPerMillion(CompletionPricePerToken.Value)} / 1M tokens";
        }
    }

    static string FormatPerMillion(decimal pricePerToken) =>
        (pricePerToken * 1_000_000m).ToString("$0.####", CultureInfo.InvariantCulture);
}

sealed record TranslationSettingsResult(string DomainInstructions, int Concurrency);
readonly record struct LlmTranslationExecutionSettings(string DomainInstructions, int ParallelRequestLimit);
sealed record LlmTranslationInput(int Id, string Text);

readonly record struct LlmTokenUsage(int PromptTokens, int CompletionTokens, int TotalTokens)
{
    public int ReasoningTokens { get; init; }

    public static LlmTokenUsage operator +(LlmTokenUsage left, LlmTokenUsage right) =>
        new(
            left.PromptTokens + right.PromptTokens,
            left.CompletionTokens + right.CompletionTokens,
            left.TotalTokens + right.TotalTokens)
        {
            ReasoningTokens = left.ReasoningTokens + right.ReasoningTokens
        };
}

enum LlmTranslationStage
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

readonly record struct LlmTranslationProgress(
    string RequestId,
    LlmTranslationStage Stage,
    TimeSpan Elapsed,
    long ResponseBytes,
    int ResponseCharacters,
    int AttemptNumber = 1,
    int MaximumAttempts = 5);

sealed record LlmTranslationBatch(
    IReadOnlyDictionary<int, string> Translations,
    LlmTokenUsage Usage);

sealed class OpenRouterRouteException : Exception
{
    public OpenRouterRouteException(
        string requestId,
        string providerName,
        string message,
        Exception innerException,
        LlmTokenUsage usage)
        : base(message, innerException)
    {
        RequestId = requestId;
        ProviderName = providerName;
        Usage = usage;
    }

    public string RequestId { get; }
    public string ProviderName { get; }
    public LlmTokenUsage Usage { get; }
}

sealed class LlmApiException : Exception
{
    public LlmApiException(
        LlmProviderId providerId,
        HttpStatusCode statusCode,
        string? apiMessage = null,
        TimeSpan? retryAfter = null)
        : base(ToUserMessage(providerId, statusCode, apiMessage))
    {
        ProviderId = providerId;
        StatusCode = statusCode;
        ApiMessage = apiMessage;
        RetryAfter = retryAfter;
    }

    public LlmApiException(HttpStatusCode statusCode, string? apiMessage = null)
        : this(LlmProviderId.OpenRouter, statusCode, apiMessage)
    {
    }

    public LlmProviderId ProviderId { get; }
    public HttpStatusCode StatusCode { get; }
    public string? ApiMessage { get; }
    public TimeSpan? RetryAfter { get; }
    public bool IsTransient =>
        StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or
        HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout ||
        (int)StatusCode >= 500;

    static string ToUserMessage(LlmProviderId providerId, HttpStatusCode statusCode, string? apiMessage)
    {
        var provider = LlmProviderRegistry.GetDescriptor(providerId).Name;
        return statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                $"{provider} rejected the credential. Open Connection and replace it.",
            HttpStatusCode.PaymentRequired => $"The {provider} account does not have enough credit for the request.",
            HttpStatusCode.NotFound when !string.IsNullOrWhiteSpace(apiMessage) =>
                $"{provider} could not use this model or endpoint: {apiMessage}",
            HttpStatusCode.NotFound => $"{provider} could not find the selected model or endpoint.",
            HttpStatusCode.RequestTimeout => $"{provider} timed out before completing the request. Please try again.",
            HttpStatusCode.TooManyRequests => $"{provider}'s rate limit was reached. Wait a moment before trying again.",
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout =>
                $"{provider} is temporarily unavailable. Please try again later.",
            _ when !string.IsNullOrWhiteSpace(apiMessage) => $"{provider} could not complete the request: {apiMessage}",
            _ => $"{provider} could not complete the request ({(int)statusCode})."
        };
    }
}

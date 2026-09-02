using Microsoft.Extensions.AI;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ResXTranslator;

interface ILlmProvider
{
    LlmProviderDescriptor Descriptor { get; }
    Task ValidateConnectionAsync(LlmConnectionProfile profile, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LlmModel>> GetModelsAsync(LlmConnectionProfile profile, CancellationToken cancellationToken = default);
    IChatClient CreateChatClient(LlmConnectionProfile profile);
    LlmApiException MapError(HttpResponseMessage response, string responseText);
}

sealed class LlmProviderRegistry
{
    static readonly LlmProviderDescriptor[] ProviderDescriptors =
    [
        new(
            LlmProviderId.OpenRouter,
            "OpenRouter",
            "https://openrouter.ai/api/v1/",
            LlmApiDialect.OpenAiChatCompletions,
            true,
            false,
            LlmProviderCapabilities.ModelCatalog | LlmProviderCapabilities.StrictJsonSchema |
            LlmProviderCapabilities.Streaming | LlmProviderCapabilities.TokenUsage |
            LlmProviderCapabilities.MultiRouteFailover),
        new(
            LlmProviderId.OpenAI,
            "OpenAI",
            "https://api.openai.com/v1/",
            LlmApiDialect.OpenAiChatCompletions,
            true,
            false,
            StandardCapabilities),
        new(
            LlmProviderId.Anthropic,
            "Anthropic",
            "https://api.anthropic.com/v1/",
            LlmApiDialect.AnthropicMessages,
            true,
            false,
            StandardCapabilities),
        new(
            LlmProviderId.Gemini,
            "Google Gemini",
            "https://generativelanguage.googleapis.com/v1beta/",
            LlmApiDialect.GeminiGenerateContent,
            true,
            false,
            StandardCapabilities),
        new(
            LlmProviderId.DeepSeek,
            "DeepSeek",
            "https://api.deepseek.com/",
            LlmApiDialect.DeepSeekResponses,
            true,
            false,
            StandardCapabilities),
        new(
            LlmProviderId.Ollama,
            "Ollama",
            "http://localhost:11434/v1/",
            LlmApiDialect.OpenAiChatCompletions,
            false,
            true,
            StandardCapabilities),
        new(
            LlmProviderId.LmStudio,
            "LM Studio",
            "http://localhost:1234/v1/",
            LlmApiDialect.OpenAiChatCompletions,
            false,
            true,
            StandardCapabilities),
        new(
            LlmProviderId.Custom,
            "Custom OpenAI-compatible",
            "https://localhost:8443/v1/",
            LlmApiDialect.OpenAiChatCompletions,
            false,
            false,
            StandardCapabilities)
    ];

    const LlmProviderCapabilities StandardCapabilities =
        LlmProviderCapabilities.ModelCatalog | LlmProviderCapabilities.StrictJsonSchema |
        LlmProviderCapabilities.Streaming | LlmProviderCapabilities.TokenUsage;

    readonly IReadOnlyDictionary<LlmProviderId, ILlmProvider> _providers;

    public LlmProviderRegistry(OpenRouterClient openRouterClient)
    {
        _providers = ProviderDescriptors.ToDictionary(
            descriptor => descriptor.Id,
            descriptor => (ILlmProvider)new HttpLlmProvider(descriptor, openRouterClient));
    }

    public IReadOnlyList<LlmProviderDescriptor> Providers => ProviderDescriptors;

    public ILlmProvider Get(LlmProviderId providerId) =>
        _providers.TryGetValue(providerId, out var provider)
            ? provider
            : throw new ArgumentOutOfRangeException(nameof(providerId));

    public static LlmProviderDescriptor GetDescriptor(LlmProviderId providerId) =>
        ProviderDescriptors.First(descriptor => descriptor.Id == providerId);

    public static bool TryCreateEndpoint(
        LlmProviderDescriptor provider,
        string value,
        out Uri? endpoint,
        out string? error)
    {
        endpoint = null;
        error = null;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            error = "Enter an absolute HTTP or HTTPS endpoint.";
            return false;
        }

        if (parsed.Scheme == Uri.UriSchemeHttp && !IsLocalHost(parsed.Host))
        {
            error = "Unencrypted HTTP is allowed only for localhost or private-network endpoints. Use HTTPS for remote services.";
            return false;
        }

        if (provider.IsLocal && !IsLocalHost(parsed.Host))
        {
            error = $"{provider.Name} must use localhost or a private-network host.";
            return false;
        }

        endpoint = EnsureTrailingSlash(parsed);
        return true;
    }

    static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);

    static bool IsLocalHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var address))
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] == 10 ||
                bytes[0] == 127 ||
                (bytes[0] == 169 && bytes[1] == 254) ||
                (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168);
        }

        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal ||
            (bytes.Length == 16 && (bytes[0] & 0xfe) == 0xfc);
    }
}

sealed class HttpLlmProvider(
    LlmProviderDescriptor descriptor,
    OpenRouterClient openRouterClient) : ILlmProvider
{
    static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(1)
    };

    public LlmProviderDescriptor Descriptor { get; } = descriptor;

    public async Task ValidateConnectionAsync(
        LlmConnectionProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (Descriptor.Id == LlmProviderId.OpenRouter)
        {
            await openRouterClient.ValidateApiKeyAsync(
                profile.ApiKey ?? throw new InvalidOperationException("Enter an API key."),
                cancellationToken);
            return;
        }

        try
        {
            _ = await GetModelsAsync(profile, cancellationToken);
        }
        catch (LlmApiException ex) when (
            Descriptor.Id == LlmProviderId.Custom &&
            ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
        {
            // A custom compatible endpoint is allowed to omit model discovery.
            // Reaching it is sufficient here; the required model probe performs
            // the authoritative strict-schema and authentication check.
        }
    }

    public async Task<IReadOnlyList<LlmModel>> GetModelsAsync(
        LlmConnectionProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (Descriptor.Id == LlmProviderId.OpenRouter)
        {
            return await openRouterClient.GetModelsAsync(
                profile.ApiKey ?? throw new InvalidOperationException("Enter an API key."),
                cancellationToken);
        }

        var requestUri = Descriptor.Dialect == LlmApiDialect.GeminiGenerateContent
            ? BuildGeminiUri(profile, "models")
            : new Uri(profile.Endpoint, "models");
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        AddAuthentication(request, profile);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw MapError(response, responseText);
        }

        using var document = JsonDocument.Parse(responseText);
        var models = Descriptor.Dialect switch
        {
            LlmApiDialect.GeminiGenerateContent => ParseGeminiModels(document.RootElement),
            LlmApiDialect.AnthropicMessages => ParseDataModels(document.RootElement),
            _ => ParseDataModels(document.RootElement)
        };

        return models
            .OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IChatClient CreateChatClient(LlmConnectionProfile profile) =>
        new RetryingLlmChatClient(new ProtocolLlmChatClient(Descriptor, profile));

    public LlmApiException MapError(HttpResponseMessage response, string responseText) =>
        CreateApiException(Descriptor.Id, response, responseText);

    static IReadOnlyList<LlmModel> ParseDataModels(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("The provider returned an invalid model catalog.");
        }

        return data.EnumerateArray()
            .Select(item =>
            {
                var id = GetString(item, "id");
                var name = GetString(item, "display_name") ?? GetString(item, "name") ?? id;
                var owner = GetString(item, "owned_by") ?? string.Empty;
                return string.IsNullOrWhiteSpace(id)
                    ? null
                    : new LlmModel(id, name!, null, null) { Provider = owner };
            })
            .Where(model => model is not null)
            .Cast<LlmModel>()
            .ToArray();
    }

    static IReadOnlyList<LlmModel> ParseGeminiModels(JsonElement root)
    {
        if (!root.TryGetProperty("models", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Google Gemini returned an invalid model catalog.");
        }

        var models = new List<LlmModel>();
        foreach (var item in data.EnumerateArray())
        {
            var supported = item.TryGetProperty("supportedGenerationMethods", out var methods) &&
                methods.ValueKind == JsonValueKind.Array &&
                methods.EnumerateArray().Any(method => method.GetString() == "generateContent");
            var fullName = GetString(item, "name");
            if (!supported || string.IsNullOrWhiteSpace(fullName))
            {
                continue;
            }

            var id = fullName.StartsWith("models/", StringComparison.Ordinal)
                ? fullName["models/".Length..]
                : fullName;
            models.Add(new LlmModel(
                id,
                GetString(item, "displayName") ?? id,
                null,
                null)
            {
                Provider = "Google"
            });
        }

        return models;
    }

    internal static void AddAuthentication(HttpRequestMessage request, LlmConnectionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            return;
        }

        switch (profile.ProviderId)
        {
            case LlmProviderId.Anthropic:
                request.Headers.TryAddWithoutValidation("x-api-key", profile.ApiKey);
                request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                break;
            case LlmProviderId.Gemini:
                break;
            default:
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.ApiKey);
                break;
        }
    }

    internal static Uri BuildGeminiUri(LlmConnectionProfile profile, string relativePath)
    {
        var builder = new UriBuilder(new Uri(profile.Endpoint, relativePath));
        builder.Query = $"key={Uri.EscapeDataString(profile.ApiKey ?? string.Empty)}";
        return builder.Uri;
    }

    internal static LlmApiException CreateApiException(
        LlmProviderId providerId,
        HttpResponseMessage response,
        string responseText)
    {
        var message = TryReadApiMessage(responseText);
        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } retryDate)
        {
            retryAfter = retryDate - DateTimeOffset.UtcNow;
        }

        return new LlmApiException(providerId, response.StatusCode, message, retryAfter);
    }

    static string? TryReadApiMessage(string responseText)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString();
                }

                return GetString(error, "message") ?? GetString(error, "type");
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

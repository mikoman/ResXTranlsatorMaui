using Microsoft.Extensions.AI;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace ResXTranslator;

sealed class ProtocolLlmChatClient : IChatClient
{
    static readonly HttpClient HttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    readonly LlmProviderDescriptor _provider;
    readonly LlmConnectionProfile _profile;

    public ProtocolLlmChatClient(LlmProviderDescriptor provider, LlmConnectionProfile profile)
    {
        _provider = provider;
        _profile = profile;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await GetStreamingResponseAsync(messages, options, cancellationToken)
            .ToChatResponseAsync(cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messageList = messages.ToArray();
        var requestId = Guid.NewGuid().ToString("N")[..8];
        using var request = CreateRequest(messageList, options ?? new ChatOptions(), requestId);
        using var response = await HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw HttpLlmProvider.CreateApiException(_provider.Id, response, errorText);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(responseStream, Encoding.UTF8);
        var eventName = string.Empty;
        var responseId = response.Headers.TryGetValues("x-request-id", out var requestIds)
            ? requestIds.FirstOrDefault() ?? requestId
            : requestId;
        var modelId = options?.ModelId;
        var usage = new UsageDetails();

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line["event:".Length..].Trim();
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line["data:".Length..].Trim();
            if (data.Length == 0 || data == "[DONE]")
            {
                continue;
            }

            using var document = JsonDocument.Parse(data);
            foreach (var parsed in ParseStreamEvent(
                document.RootElement,
                eventName,
                responseId,
                modelId,
                usage))
            {
                if (!string.IsNullOrWhiteSpace(parsed.ResponseId))
                {
                    responseId = parsed.ResponseId;
                }

                if (!string.IsNullOrWhiteSpace(parsed.ModelId))
                {
                    modelId = parsed.ModelId;
                }

                yield return parsed;
            }

            eventName = string.Empty;
        }

        if (usage.InputTokenCount is not null || usage.OutputTokenCount is not null ||
            usage.TotalTokenCount is not null || usage.ReasoningTokenCount is not null)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(usage)])
            {
                ResponseId = responseId,
                MessageId = responseId,
                ModelId = modelId
            };
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(ProtocolLlmChatClient) || serviceType == typeof(IChatClient)
            ? this
            : serviceType == typeof(ChatClientMetadata)
                ? new ChatClientMetadata(_provider.Name, _profile.Endpoint, null)
                : null;

    public void Dispose()
    {
    }

    HttpRequestMessage CreateRequest(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        string requestId)
    {
        var (uri, payload) = _provider.Dialect switch
        {
            LlmApiDialect.AnthropicMessages => CreateAnthropicRequest(messages, options),
            LlmApiDialect.GeminiGenerateContent => CreateGeminiRequest(messages, options),
            LlmApiDialect.DeepSeekResponses => CreateDeepSeekRequest(messages, options),
            _ => CreateOpenAiRequest(messages, options)
        };

        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        request.Headers.TryAddWithoutValidation("x-client-request-id", requestId);
        HttpLlmProvider.AddAuthentication(request, _profile);
        return request;
    }

    (Uri Uri, object Payload) CreateOpenAiRequest(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = RequireModel(options),
            ["messages"] = messages.Select(ToOpenAiMessage).ToArray(),
            ["temperature"] = options.Temperature ?? 0f,
            ["stream"] = true,
            ["stream_options"] = new { include_usage = true },
            ["response_format"] = CreateOpenAiResponseFormat(options)
        };
        payload[_provider.Id == LlmProviderId.OpenAI ? "max_completion_tokens" : "max_tokens"] =
            options.MaxOutputTokens ?? 8_192;
        if (_provider.Id == LlmProviderId.OpenRouter)
        {
            payload["provider"] = new { require_parameters = true, data_collection = "deny" };
            payload["reasoning"] = new { exclude = true };
        }
        return (new Uri(_profile.Endpoint, "chat/completions"), payload);
    }

    (Uri Uri, object Payload) CreateAnthropicRequest(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options)
    {
        var system = string.Join(
            Environment.NewLine,
            messages.Where(message => message.Role == ChatRole.System).Select(message => message.Text));
        var payload = new Dictionary<string, object?>
        {
            ["model"] = RequireModel(options),
            ["system"] = system,
            ["messages"] = messages
                .Where(message => message.Role != ChatRole.System)
                .Select(message => new
                {
                    role = message.Role == ChatRole.Assistant ? "assistant" : "user",
                    content = message.Text
                })
                .ToArray(),
            ["temperature"] = options.Temperature ?? 0f,
            ["max_tokens"] = options.MaxOutputTokens ?? 8_192,
            ["stream"] = true,
            ["output_config"] = new
            {
                format = CreateAnthropicResponseFormat(options)
            }
        };
        return (new Uri(_profile.Endpoint, "messages"), payload);
    }

    (Uri Uri, object Payload) CreateGeminiRequest(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options)
    {
        var system = string.Join(
            Environment.NewLine,
            messages.Where(message => message.Role == ChatRole.System).Select(message => message.Text));
        var model = RequireModel(options);
        var endpoint = new Uri(
            _profile.Endpoint,
            $"models/{Uri.EscapeDataString(model)}:streamGenerateContent");
        var builder = new UriBuilder(endpoint)
        {
            Query = $"alt=sse&key={Uri.EscapeDataString(_profile.ApiKey ?? string.Empty)}"
        };
        var responseFormat = RequireJsonFormat(options);
        var payload = new
        {
            systemInstruction = new { parts = new[] { new { text = system } } },
            contents = messages
                .Where(message => message.Role != ChatRole.System)
                .Select(message => new
                {
                    role = message.Role == ChatRole.Assistant ? "model" : "user",
                    parts = new[] { new { text = message.Text } }
                })
                .ToArray(),
            generationConfig = new
            {
                temperature = options.Temperature ?? 0f,
                maxOutputTokens = options.MaxOutputTokens ?? 8_192,
                responseMimeType = "application/json",
                responseJsonSchema = responseFormat.Schema
            }
        };
        return (builder.Uri, payload);
    }

    (Uri Uri, object Payload) CreateDeepSeekRequest(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options)
    {
        var responseFormat = RequireJsonFormat(options);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = RequireModel(options),
            ["input"] = messages.Select(message => new
            {
                role = ToRole(message.Role),
                content = message.Text
            }).ToArray(),
            ["temperature"] = options.Temperature ?? 0f,
            ["max_output_tokens"] = options.MaxOutputTokens ?? 8_192,
            ["stream"] = true,
            ["text"] = new
            {
                format = new
                {
                    type = "json_schema",
                    name = responseFormat.SchemaName ?? "response",
                    schema = responseFormat.Schema
                }
            }
        };
        return (new Uri(_profile.Endpoint, "responses"), payload);
    }

    IEnumerable<ChatResponseUpdate> ParseStreamEvent(
        JsonElement root,
        string eventName,
        string responseId,
        string? modelId,
        UsageDetails usage)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                foreach (var update in ParseStreamEvent(item, eventName, responseId, modelId, usage))
                {
                    yield return update;
                }
            }

            yield break;
        }

        string? text = null;
        switch (_provider.Dialect)
        {
            case LlmApiDialect.AnthropicMessages:
                responseId = GetNestedString(root, "message", "id") ?? responseId;
                modelId = GetNestedString(root, "message", "model") ?? modelId;
                if (eventName == "content_block_delta" && root.TryGetProperty("delta", out var delta))
                {
                    text = GetString(delta, "text");
                }

                ReadAnthropicUsage(root, usage);
                break;

            case LlmApiDialect.GeminiGenerateContent:
                text = ReadGeminiText(root);
                ReadGeminiUsage(root, usage);
                break;

            case LlmApiDialect.DeepSeekResponses:
                var type = GetString(root, "type") ?? eventName;
                if (type == "response.output_text.delta")
                {
                    text = GetString(root, "delta");
                }

                if (root.TryGetProperty("response", out var responseObject))
                {
                    responseId = GetString(responseObject, "id") ?? responseId;
                    modelId = GetString(responseObject, "model") ?? modelId;
                    ReadOpenAiUsage(responseObject, usage, responsesShape: true);
                }
                break;

            default:
                responseId = GetString(root, "id") ?? responseId;
                modelId = GetString(root, "model") ?? modelId;
                if (root.TryGetProperty("choices", out var choices) &&
                    choices.ValueKind == JsonValueKind.Array &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("delta", out var openAiDelta))
                {
                    text = ReadTextContent(openAiDelta);
                }

                ReadOpenAiUsage(root, usage, responsesShape: false);
                break;
        }

        if (!string.IsNullOrEmpty(text))
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, text)
            {
                ResponseId = responseId,
                MessageId = responseId,
                ModelId = modelId
            };
        }
    }

    static object ToOpenAiMessage(ChatMessage message) => new
    {
        role = ToRole(message.Role),
        content = message.Text
    };

    static string ToRole(ChatRole role) => role switch
    {
        var value when value == ChatRole.System => "system",
        var value when value == ChatRole.Assistant => "assistant",
        var value when value == ChatRole.Tool => "tool",
        _ => "user"
    };

    static object CreateOpenAiResponseFormat(ChatOptions options)
    {
        var format = RequireJsonFormat(options);
        return new
        {
            type = "json_schema",
            json_schema = new
            {
                name = format.SchemaName ?? "response",
                strict = true,
                schema = format.Schema
            }
        };
    }

    static object CreateAnthropicResponseFormat(ChatOptions options)
    {
        var format = RequireJsonFormat(options);
        return new
        {
            type = "json_schema",
            schema = format.Schema
        };
    }

    static ChatResponseFormatJson RequireJsonFormat(ChatOptions options) =>
        options.ResponseFormat as ChatResponseFormatJson
        ?? throw new InvalidOperationException("This translator requires a strict JSON Schema response format.");

    static string RequireModel(ChatOptions options) =>
        !string.IsNullOrWhiteSpace(options.ModelId)
            ? options.ModelId
            : throw new InvalidOperationException("Choose a model before sending a request.");

    static void ReadOpenAiUsage(JsonElement root, UsageDetails usage, bool responsesShape)
    {
        if (!root.TryGetProperty("usage", out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        usage.InputTokenCount = GetInt(value, responsesShape ? "input_tokens" : "prompt_tokens") ?? usage.InputTokenCount;
        usage.OutputTokenCount = GetInt(value, responsesShape ? "output_tokens" : "completion_tokens") ?? usage.OutputTokenCount;
        usage.TotalTokenCount = GetInt(value, "total_tokens") ?? usage.TotalTokenCount;

        var detailName = responsesShape ? "output_tokens_details" : "completion_tokens_details";
        if (value.TryGetProperty(detailName, out var details))
        {
            usage.ReasoningTokenCount = GetInt(details, "reasoning_tokens") ?? usage.ReasoningTokenCount;
        }
    }

    static void ReadAnthropicUsage(JsonElement root, UsageDetails usage)
    {
        if (root.TryGetProperty("message", out var message) &&
            message.TryGetProperty("usage", out var startUsage))
        {
            usage.InputTokenCount = GetInt(startUsage, "input_tokens") ?? usage.InputTokenCount;
            usage.OutputTokenCount = GetInt(startUsage, "output_tokens") ?? usage.OutputTokenCount;
        }

        if (root.TryGetProperty("usage", out var deltaUsage))
        {
            usage.OutputTokenCount = GetInt(deltaUsage, "output_tokens") ?? usage.OutputTokenCount;
        }

        usage.TotalTokenCount = (usage.InputTokenCount ?? 0) + (usage.OutputTokenCount ?? 0);
    }

    static void ReadGeminiUsage(JsonElement root, UsageDetails usage)
    {
        if (!root.TryGetProperty("usageMetadata", out var value))
        {
            return;
        }

        usage.InputTokenCount = GetInt(value, "promptTokenCount") ?? usage.InputTokenCount;
        usage.OutputTokenCount = GetInt(value, "candidatesTokenCount") ?? usage.OutputTokenCount;
        usage.TotalTokenCount = GetInt(value, "totalTokenCount") ?? usage.TotalTokenCount;
        usage.ReasoningTokenCount = GetInt(value, "thoughtsTokenCount") ?? usage.ReasoningTokenCount;
    }

    static string? ReadGeminiText(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0 ||
            !candidates[0].TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return string.Concat(parts.EnumerateArray().Select(part => GetString(part, "text")));
    }

    static string? ReadTextContent(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
        {
            return null;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        return content.ValueKind == JsonValueKind.Array
            ? string.Concat(content.EnumerateArray().Select(item => GetString(item, "text")))
            : null;
    }

    static string? GetNestedString(JsonElement element, string objectName, string propertyName) =>
        element.TryGetProperty(objectName, out var nested) ? GetString(nested, propertyName) : null;

    static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    static int? GetInt(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result)
            ? result
            : null;
}

sealed class RetryingLlmChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    const int MaximumAttempts = 3;

    public event Action<int, TimeSpan>? Retrying;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messageList = messages.ToArray();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await base.GetResponseAsync(messageList, options?.Clone(), cancellationToken);
            }
            catch (Exception ex) when (attempt < MaximumAttempts && IsRetryable(ex, cancellationToken))
            {
                await DelayAsync(attempt, ex, cancellationToken);
            }
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messageList = messages.ToArray();
        for (var attempt = 1; ; attempt++)
        {
            var yieldedContent = false;
            IAsyncEnumerator<ChatResponseUpdate>? enumerator = null;
            var shouldRetry = false;
            Exception? retryException = null;

            enumerator = base.GetStreamingResponseAsync(
                messageList,
                options?.Clone(),
                cancellationToken).GetAsyncEnumerator(cancellationToken);

            while (true)
            {
                ChatResponseUpdate? update = null;
                var hasNext = false;

                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                    if (hasNext)
                    {
                        update = enumerator.Current;
                    }
                }
                catch (Exception ex) when (
                    !yieldedContent && attempt < MaximumAttempts && IsRetryable(ex, cancellationToken))
                {
                    shouldRetry = true;
                    retryException = ex;
                    break;
                }

                if (!hasNext)
                {
                    await enumerator.DisposeAsync();
                    yield break;
                }

                yieldedContent |= !string.IsNullOrEmpty(update!.Text);
                yield return update;
            }

            await enumerator.DisposeAsync();
            if (!shouldRetry || retryException is null)
            {
                yield break;
            }

            await DelayAsync(attempt, retryException, cancellationToken);
        }
    }

    static bool IsRetryable(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && exception switch
        {
            LlmApiException apiException => apiException.IsTransient,
            HttpRequestException => true,
            IOException => true,
            TimeoutException => true,
            _ => false
        };

    Task DelayAsync(int attempt, Exception exception, CancellationToken cancellationToken)
    {
        var retryAfter = (exception as LlmApiException)?.RetryAfter;
        var baseDelay = retryAfter is { } supplied && supplied > TimeSpan.Zero
            ? supplied
            : TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
        var delay = baseDelay + TimeSpan.FromMilliseconds(Random.Shared.Next(100, 450));
        Retrying?.Invoke(attempt + 1, delay);
        return Task.Delay(delay, cancellationToken);
    }
}

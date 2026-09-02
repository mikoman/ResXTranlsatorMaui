using System.Globalization;
using System.Text.Json;

namespace ResXTranslator;

static class LlmTranslationContract
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static JsonElement CreateSchema(IReadOnlyList<LlmTranslationInput> inputs)
    {
        var properties = new Dictionary<string, object>(inputs.Count, StringComparer.Ordinal);

        foreach (var input in inputs)
        {
            var id = input.Id.ToString(CultureInfo.InvariantCulture);
            if (!properties.TryAdd(id, new { type = "string" }))
            {
                throw new InvalidOperationException($"The translation batch contains id {input.Id} more than once.");
            }
        }

        return JsonSerializer.SerializeToElement(
            new
            {
                type = "object",
                properties = new
                {
                    translations = new
                    {
                        type = "object",
                        properties,
                        required = properties.Keys.ToArray(),
                        additionalProperties = false
                    }
                },
                required = new[] { "translations" },
                additionalProperties = false
            },
            JsonOptions);
    }

    public static LlmTranslationBatch Parse(
        string structuredText,
        IReadOnlyList<LlmTranslationInput> inputs,
        LlmTokenUsage usage)
    {
        using var document = JsonDocument.Parse(structuredText);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("translations", out var translations) ||
            translations.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "The response root must be an object containing a translations object.");
        }

        var requestedIds = new Dictionary<string, int>(inputs.Count, StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            var id = input.Id.ToString(CultureInfo.InvariantCulture);
            if (!requestedIds.TryAdd(id, input.Id))
            {
                throw new InvalidOperationException("The translation batch contains duplicate IDs.");
            }
        }

        var translated = new Dictionary<int, string>(inputs.Count);
        foreach (var property in translations.EnumerateObject())
        {
            if (!requestedIds.TryGetValue(property.Name, out var id))
            {
                throw new InvalidOperationException($"The provider returned an unexpected translation id ({property.Name}).");
            }

            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException(
                    $"The provider returned a non-string translation for id {id}.");
            }

            if (!translated.TryAdd(id, property.Value.GetString() ?? string.Empty))
            {
                throw new InvalidOperationException($"The provider returned translation id {id} more than once.");
            }
        }

        if (translated.Count != requestedIds.Count)
        {
            var missing = requestedIds.Values.Except(translated.Keys).Order().First();
            throw new InvalidOperationException($"The provider did not return a translation for id {missing}.");
        }

        return new LlmTranslationBatch(translated, usage);
    }
}

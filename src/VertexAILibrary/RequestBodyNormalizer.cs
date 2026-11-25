using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace VertexAILibrary;
/// <summary>
/// Normalizes an OpenAI-style chat completions request body so that <c>messages[].content</c> is always a
/// list of content parts and <c>tools</c> / <c>tool_choice</c> are JSON strings. Single streaming pass over
/// the original JSON, so every other key keeps its name and value.
/// </summary>
internal static class RequestBodyNormalizer {
    internal readonly record struct Result(string NormalizedText, bool IsValid, string ErrorMessage);

    private static readonly string[] _stringifiedKeys = { "tools", "tool_choice" };

    private static readonly JsonDocumentOptions _documentOptions = new() { MaxDepth = 256 };

    private static readonly JsonWriterOptions _writerOptions = new() {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private enum ContentShape { None, String, Array }

    public static Result Normalize(string? requestText) {
        if (string.IsNullOrWhiteSpace(requestText)) {
            return Invalid("RequestText is empty.");
        }

        JsonDocument document;
        try {
            document = JsonDocument.Parse(requestText, _documentOptions);
        } catch (JsonException ex) {
            return Invalid(ex.Message);
        }

        using (document) {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
                return Invalid("The request body must be a JSON object.");
            }

            if (!TryDetectShape(root, out var shape, out var error)) {
                return Invalid(error);
            }

            var wrapContent = shape == ContentShape.String;
            if (!wrapContent && !HasStructuredValue(root)) {
                return new Result(requestText, true, string.Empty);
            }

            return new Result(Rewrite(root, wrapContent, requestText.Length), true, string.Empty);
        }
    }

    private static Result Invalid(string message) => new(string.Empty, false, message);

    /// <summary>
    /// A body is in the "string" shape when at least one message has a string content and none has an array.
    /// Mixed or non-string/non-array content is invalid.
    /// </summary>
    private static bool TryDetectShape(JsonElement root, out ContentShape shape, out string error) {
        shape = ContentShape.None;
        error = string.Empty;

        if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind == JsonValueKind.Null) {
            return true;
        }

        if (messages.ValueKind != JsonValueKind.Array) {
            error = "messages must be a JSON array.";
            return false;
        }

        var hasString = false;
        var hasArray = false;
        var index = 0;
        foreach (var message in messages.EnumerateArray()) {
            if (message.ValueKind != JsonValueKind.Object) {
                error = $"messages[{index}] must be a JSON object.";
                return false;
            }

            if (message.TryGetProperty("content", out var content)) {
                switch (content.ValueKind) {
                    case JsonValueKind.String:
                        hasString = true;
                        break;
                    case JsonValueKind.Array:
                        hasArray = true;
                        break;
                    case JsonValueKind.Null:
                        break;
                    default:
                        error = $"messages[{index}].content must be a string or an array of content parts.";
                        return false;
                }
            }

            index++;
        }

        if (hasString && hasArray) {
            error = "messages[].content must be either all strings or all arrays of content parts.";
            return false;
        }

        shape = hasString ? ContentShape.String : hasArray ? ContentShape.Array : ContentShape.None;
        return true;
    }

    private static bool HasStructuredValue(JsonElement root) {
        foreach (var key in _stringifiedKeys) {
            if (root.TryGetProperty(key, out var value)
                && value.ValueKind is JsonValueKind.Array or JsonValueKind.Object) {
                return true;
            }
        }
        return false;
    }

    private static string Rewrite(JsonElement root, bool wrapContent, int sizeHint) {
        using var stream = new MemoryStream(sizeHint + 256);
        using (var writer = new Utf8JsonWriter(stream, _writerOptions)) {
            var stringified = new HashSet<string>(StringComparer.Ordinal);
            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject()) {
                if (IsFirstStructuredMatch(property, stringified)) {
                    writer.WriteString(property.Name, property.Value.GetRawText());
                } else if (wrapContent && property.NameEquals("messages")
                           && property.Value.ValueKind == JsonValueKind.Array) {
                    writer.WritePropertyName("messages");
                    WriteWrappedMessages(writer, property.Value);
                } else {
                    property.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    private static bool IsFirstStructuredMatch(JsonProperty property, HashSet<string> done) {
        if (property.Value.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object)) {
            return false;
        }

        foreach (var key in _stringifiedKeys) {
            if (property.NameEquals(key)) {
                return done.Add(key);
            }
        }
        return false;
    }

    private static void WriteWrappedMessages(Utf8JsonWriter writer, JsonElement messages) {
        writer.WriteStartArray();
        foreach (var message in messages.EnumerateArray()) {
            writer.WriteStartObject();
            var wroteContent = false;
            foreach (var property in message.EnumerateObject()) {
                if (!wroteContent && property.NameEquals("content")) {
                    writer.WritePropertyName("content");
                    WriteTextPart(writer, property.Value);
                    wroteContent = true;
                } else if (!property.NameEquals("content")) {
                    property.WriteTo(writer);
                }
            }

            if (!wroteContent) {
                writer.WritePropertyName("content");
                WriteTextPart(writer, default);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteTextPart(Utf8JsonWriter writer, JsonElement content) {
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteString("type", "text");
        writer.WriteString("text", content.ValueKind == JsonValueKind.String ? content.GetString() : string.Empty);
        writer.WriteEndObject();
        writer.WriteEndArray();
    }
}

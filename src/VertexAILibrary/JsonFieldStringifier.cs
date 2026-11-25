using System.Text.Encodings.Web;
using System.Text.Json;

namespace VertexAILibrary;
/// <summary>
/// Replaces a top-level JSON array/object value with its JSON string representation.
/// Single streaming pass, so it stays linear on large request bodies.
/// </summary>
internal static class JsonFieldStringifier {
    private static readonly JsonWriterOptions _writerOptions = new() {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        SkipValidation = true
    };

    private static readonly JsonDocumentOptions _documentOptions = new() { MaxDepth = 256 };

    /// <summary>
    /// Returns <paramref name="json"/> with the top-level <paramref name="key"/> value replaced by its
    /// JSON string form when that value is an array or object. Strings, other scalars, absent keys and
    /// non-object roots leave the input untouched.
    /// </summary>
    public static string Stringify(string key, string json) {
        if (string.IsNullOrEmpty(key)) {
            throw new ArgumentException("Key is required.");
        }

        if (string.IsNullOrWhiteSpace(json)) {
            return json ?? string.Empty;
        }

        using var document = JsonDocument.Parse(json, _documentOptions);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !HasStructuredValue(root, key)) {
            return json;
        }

        using var stream = new MemoryStream(json.Length + 64);
        using (var writer = new Utf8JsonWriter(stream, _writerOptions)) {
            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject()) {
                if (IsStructuredMatch(property, key)) {
                    writer.WriteString(property.Name, property.Value.GetRawText());
                } else {
                    property.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    private static bool HasStructuredValue(JsonElement root, string key) {
        foreach (var property in root.EnumerateObject()) {
            if (IsStructuredMatch(property, key)) {
                return true;
            }
        }
        return false;
    }

    private static bool IsStructuredMatch(JsonProperty property, string key)
        => property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object
           && property.NameEquals(key);
}

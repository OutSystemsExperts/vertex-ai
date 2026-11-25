using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace VertexAILibrary.UnitTests;

public class RequestBodyNormalizerTests {
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Normalize_StringContent_IsWrappedAsTextPart() {
        var result = RequestBodyNormalizer.Normalize(
            """{"model":"m","messages":[{"role":"user","content":"hi"},{"role":"assistant","content":"yo"}]}""");

        Assert.True(result.IsValid);
        Assert.Equal(string.Empty, result.ErrorMessage);
        var root = Parse(result.NormalizedText);
        Assert.Equal("m", root.GetProperty("model").GetString());
        var first = root.GetProperty("messages")[0];
        Assert.Equal("user", first.GetProperty("role").GetString());
        Assert.Equal("text", first.GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("hi", first.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("yo", root.GetProperty("messages")[1].GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void Normalize_NullOrAbsentContentInStringShape_GetsEmptyTextPart() {
        var result = RequestBodyNormalizer.Normalize(
            """{"messages":[{"role":"user","content":"hi"},{"role":"assistant","content":null,"tool_calls":[{"id":"1"}]},{"role":"tool"}]}""");

        Assert.True(result.IsValid);
        var messages = Parse(result.NormalizedText).GetProperty("messages");
        Assert.Equal(string.Empty, messages[1].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("1", messages[1].GetProperty("tool_calls")[0].GetProperty("id").GetString());
        Assert.Equal(string.Empty, messages[2].GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void Normalize_ArrayContent_IsReturnedUnchanged() {
        const string body = """{"messages":[{"role":"user","content":[{"type":"text","text":"hi"}]}]}""";
        var result = RequestBodyNormalizer.Normalize(body);

        Assert.True(result.IsValid);
        Assert.Equal(body, result.NormalizedText);
    }

    [Fact]
    public void Normalize_ToolsAndToolChoice_AreStringified() {
        var result = RequestBodyNormalizer.Normalize(
            """{"tools":[{"type":"function"}],"tool_choice":{"type":"auto"},"messages":[{"role":"user","content":[{"type":"text","text":"x"}]}]}""");

        Assert.True(result.IsValid);
        var root = Parse(result.NormalizedText);
        Assert.Equal("""[{"type":"function"}]""", root.GetProperty("tools").GetString());
        Assert.Equal("""{"type":"auto"}""", root.GetProperty("tool_choice").GetString());
        Assert.Equal("x", root.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void Normalize_ToolsAlreadyString_IsLeftAlone() {
        const string body = """{"tools":"[]","tool_choice":"auto","messages":[]}""";
        var result = RequestBodyNormalizer.Normalize(body);

        Assert.True(result.IsValid);
        Assert.Equal(body, result.NormalizedText);
    }

    [Fact]
    public void Normalize_StringShapeWithTools_DoesBoth_AndKeepsOtherKeys() {
        var result = RequestBodyNormalizer.Normalize(
            """{"stream":true,"tools":[{"a":1}],"messages":[{"role":"user","content":"hi"}],"max_tokens":5}""");

        Assert.True(result.IsValid);
        var root = Parse(result.NormalizedText);
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.Equal(5, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal("""[{"a":1}]""", root.GetProperty("tools").GetString());
        Assert.Equal("hi", root.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void Normalize_NestedToolsKey_IsNotTouched() {
        const string body = """{"extra":{"tools":[1]},"messages":[]}""";
        Assert.Equal(body, RequestBodyNormalizer.Normalize(body).NormalizedText);
    }

    [Fact]
    public void Normalize_MixedStringAndArrayContent_IsInvalid() {
        var result = RequestBodyNormalizer.Normalize(
            """{"messages":[{"role":"user","content":"a"},{"role":"user","content":[{"type":"text","text":"b"}]}]}""");

        Assert.False(result.IsValid);
        Assert.Equal(string.Empty, result.NormalizedText);
        Assert.NotEmpty(result.ErrorMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"messages":"nope"}""")]
    [InlineData("""{"messages":[1]}""")]
    [InlineData("""{"messages":[{"content":5}]}""")]
    [InlineData("""{"messages":[{"content":{"a":1}}]}""")]
    public void Normalize_InvalidBodies_AreRejectedWithMessage(string body) {
        var result = RequestBodyNormalizer.Normalize(body);

        Assert.False(result.IsValid);
        Assert.Equal(string.Empty, result.NormalizedText);
        Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
    }

    [Fact]
    public void Normalize_NullInput_IsRejected() {
        Assert.False(RequestBodyNormalizer.Normalize(null).IsValid);
    }

    [Fact]
    public void Normalize_NoMessages_IsValid() {
        Assert.True(RequestBodyNormalizer.Normalize("""{"model":"m"}""").IsValid);
    }

    [Fact]
    public void Normalize_NonAsciiAndEscapes_RoundTrip() {
        var result = RequestBodyNormalizer.Normalize("{\"messages\":[{\"role\":\"user\",\"content\":\"ação \\\"q\\\" \\n 😀\"}]}");

        Assert.True(result.IsValid);
        var text = Parse(result.NormalizedText).GetProperty("messages")[0].GetProperty("content")[0].GetProperty("text").GetString();
        Assert.Equal("ação \"q\" \n 😀", text);
    }

    [Fact]
    public void NormalizeBody_ActionSurface_MapsResultToOutParameters() {
        var sut = new VertexAILibrary(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var text = sut.NormalizeBody("""{"messages":[{"role":"user","content":"hi"}]}""", out var isValid, out var error);

        Assert.True(isValid);
        Assert.Equal(string.Empty, error);
        Assert.Contains("\"type\":\"text\"", text);

        sut.NormalizeBody("{bad", out isValid, out error);
        Assert.False(isValid);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Normalize_LargeBody_IsFast() {
        var big = new string('A', 900_000);
        var body = new StringBuilder()
            .Append("{\"tools\":[{\"type\":\"function\"}],\"tool_choice\":{\"type\":\"auto\"},\"messages\":[{\"role\":\"user\",\"content\":\"")
            .Append(big).Append("\"}]}").ToString();

        var watch = Stopwatch.StartNew();
        var result = RequestBodyNormalizer.Normalize(body);
        watch.Stop();

        Assert.True(result.IsValid);
        Assert.True(watch.ElapsedMilliseconds < 2_000, $"took {watch.ElapsedMilliseconds} ms");
    }
}

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace VertexAILibrary.UnitTests;

public class JsonFieldStringifierTests {
    [Fact]
    public void Stringify_ArrayValue_BecomesJsonString() {
        var result = JsonFieldStringifier.Stringify("messages", """{"model":"m","messages":[{"role":"user","content":"hi"}],"n":1}""");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("m", doc.RootElement.GetProperty("model").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("n").GetInt32());
        Assert.Equal("""[{"role":"user","content":"hi"}]""", doc.RootElement.GetProperty("messages").GetString());
    }

    [Fact]
    public void Stringify_ObjectValue_BecomesJsonString() {
        var result = JsonFieldStringifier.Stringify("tool_choice", """{"tool_choice":{"type":"auto"}}""");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("""{"type":"auto"}""", doc.RootElement.GetProperty("tool_choice").GetString());
    }

    [Theory]
    [InlineData("""{"a":"already a string"}""")]
    [InlineData("""{"a":5}""")]
    [InlineData("""{"b":[1]}""")]
    [InlineData("""[1,2]""")]
    public void Stringify_NothingToConvert_ReturnsInputUnchanged(string json) {
        Assert.Equal(json, JsonFieldStringifier.Stringify("a", json));
    }

    [Fact]
    public void Stringify_KeyIsCaseSensitiveAndTopLevelOnly() {
        const string json = """{"Outer":{"a":[1]},"A":[2]}""";
        Assert.Equal(json, JsonFieldStringifier.Stringify("a", json));
    }

    [Fact]
    public void Stringify_NonAsciiAndEscapes_RoundTrip() {
        var result = JsonFieldStringifier.Stringify("a", "{\"a\":[\"ação \\\"q\\\" \\n 😀\"]}");

        using var doc = JsonDocument.Parse(result);
        using var inner = JsonDocument.Parse(doc.RootElement.GetProperty("a").GetString()!);
        Assert.Equal("ação \"q\" \n 😀", inner.RootElement[0].GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Stringify_MissingKey_Throws(string? key) {
        Assert.Throws<ArgumentException>(() => JsonFieldStringifier.Stringify(key!, "{}"));
    }

    [Fact]
    public void Stringify_EmptyJson_ReturnsEmpty() {
        Assert.Equal(string.Empty, JsonFieldStringifier.Stringify("a", ""));
    }

    [Fact]
    public void Stringify_InvalidJson_Throws() {
        Assert.ThrowsAny<JsonException>(() => JsonFieldStringifier.Stringify("a", "{not json"));
    }

    [Fact]
    public void Stringify_LargeBody_IsFast() {
        var payload = new string('A', 900_000);
        var json = new StringBuilder().Append("{\"messages\":[{\"role\":\"user\",\"content\":[{\"data\":\"")
            .Append(payload).Append("\"}]}],\"tools\":[]}").ToString();

        var watch = Stopwatch.StartNew();
        var result = JsonFieldStringifier.Stringify("messages", json);
        watch.Stop();

        Assert.True(result.Length > json.Length - 10);
        Assert.True(watch.ElapsedMilliseconds < 2_000, $"took {watch.ElapsedMilliseconds} ms");
    }
}

using System.Text.Json.Nodes;
using Xunit;
using static VertexAILibrary.UnitTests.TestHelpers;
using Sut = VertexAILibrary.VertexAILibrary;

namespace VertexAILibrary.UnitTests;

public class ToolCallingTests {
    private const string Tools = """
        [{"type":"function","function":{"name":"get_weather","description":"Weather","parameters":{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}}}]
        """;

    private const string Calls = """
        [{"id":"toolu_1","type":"function","function":{"name":"get_weather","arguments":"{\"city\":\"Lisbon\"}"}}]
        """;

    private static JsonNode Body(IEnumerable<ChatMessage> messages, string? tools = null, string? toolChoice = null) {
        var (system, turns) = Sut.SplitMessages(messages);
        return JsonNode.Parse(Sut.BuildAnthropicRequestBody(system, turns, 0, tools, toolChoice))!;
    }

    [Fact]
    public void Tools_AreConvertedToAnthropicFormat() {
        var body = Body(new[] { new ChatMessage { Role = "user", Content = "hi" } }, Tools);

        var tool = body["tools"]![0]!;
        Assert.Equal("get_weather", (string?)tool["name"]);
        Assert.Equal("Weather", (string?)tool["description"]);
        Assert.Equal("object", (string?)tool["input_schema"]!["type"]);
        Assert.Equal("city", (string?)tool["input_schema"]!["required"]![0]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[]")]
    public void NoTools_OmitsToolsField(string? tools) {
        var body = Body(new[] { new ChatMessage { Role = "user", Content = "hi" } }, tools);

        Assert.Null(body["tools"]);
    }

    [Fact]
    public void ToolWithoutParameters_GetsEmptyObjectSchema() {
        var tools = Sut.ConvertTools("""[{"type":"function","function":{"name":"ping"}}]""");

        Assert.Equal("object", (string?)tools![0]!["input_schema"]!["type"]);
    }

    [Theory]
    [InlineData("""["x"]""", "ToolsJson[0]")]
    [InlineData("""[{"function":"x"}]""", "ToolsJson[0].function")]
    [InlineData("""[{"name":42}]""", "ToolsJson[0].name")]
    [InlineData("""[{"name":"a","description":1}]""", "ToolsJson[0].description")]
    [InlineData("""[{"name":""}]""", "ToolsJson[0].name")]
    [InlineData("[{\"name\":\"a\"", "ToolsJson is not valid JSON")]
    public void MalformedTools_ThrowArgumentException(string tools, string expected) {
        var ex = Assert.Throws<ArgumentException>(() => Sut.ConvertTools(tools));

        Assert.Contains(expected, ex.Message);
    }

    [Theory]
    [InlineData("""["x"]""", "ToolCallsJson[0]")]
    [InlineData("""[{"id":"a","function":"x"}]""", "ToolCallsJson[0].function")]
    [InlineData("""[{"id":7,"function":{"name":"n"}}]""", "ToolCallsJson[0].id")]
    [InlineData("""[{"id":"a","function":{"name":5}}]""", "ToolCallsJson[0].function.name")]
    [InlineData("""[{"id":"a","function":{"name":"n","arguments":"{\"city\":"}}]""", "ToolCallsJson[0].function.arguments is not valid JSON")]
    [InlineData("[{\"id\":\"a\"", "ToolCallsJson is not valid JSON")]
    public void MalformedToolCalls_ThrowArgumentException(string calls, string expected) {
        var messages = new[]
        {
            new ChatMessage { Role = "user", Content = "hi" },
            new ChatMessage { Role = "assistant", Content = "", ToolCallsJson = calls }
        };

        var ex = Assert.Throws<ArgumentException>(() => Body(messages));

        Assert.Contains(expected, ex.Message);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData(" [ ] ")]
    public void AssistantEmptyToolCalls_AreTreatedAsPlainAssistantMessage_Anthropic(string calls) {
        var body = Body(new[]
        {
            new ChatMessage { Role = "user", Content = "hi" },
            new ChatMessage { Role = "assistant", Content = "hello", ToolCallsJson = calls }
        });

        var assistant = body["messages"]![1]!;
        Assert.Equal("hello", (string?)assistant["content"]);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData(" [ ] ")]
    public void AssistantEmptyToolCalls_AreNotToolCalling_Google(string calls) {
        var (_, split) = Sut.SplitMessages(new[]
        {
            new ChatMessage { Role = "assistant", Content = "hello", ToolCallsJson = calls }
        });

        Assert.Null(split[0].ToolCallsJson);
        Assert.Null(Sut.FindToolCallingRequest("", "", split));
    }

    [Fact]
    public void AssistantToolCalls_BecomeToolUseBlocks_WithParsedInput() {
        var body = Body(new[]
        {
            new ChatMessage { Role = "user", Content = "weather?" },
            new ChatMessage { Role = "assistant", Content = "", ToolCallsJson = Calls }
        });

        var assistant = body["messages"]![1]!;
        var block = assistant["content"]![0]!;
        Assert.Equal("assistant", (string?)assistant["role"]);
        Assert.Equal("tool_use", (string?)block["type"]);
        Assert.Equal("toolu_1", (string?)block["id"]);
        Assert.Equal("Lisbon", (string?)block["input"]!["city"]);
        Assert.Single(assistant["content"]!.AsArray());
    }

    [Fact]
    public void AssistantToolCalls_KeepTextBlock_WhenContentPresent() {
        var body = Body(new[]
        {
            new ChatMessage { Role = "user", Content = "weather?" },
            new ChatMessage { Role = "assistant", Content = "Checking.", ToolCallsJson = Calls }
        });

        var content = body["messages"]![1]!["content"]!.AsArray();
        Assert.Equal("text", (string?)content[0]!["type"]);
        Assert.Equal("tool_use", (string?)content[1]!["type"]);
    }

    [Fact]
    public void ToolMessage_WithoutToolCallId_Throws() {
        var ex = Assert.Throws<ArgumentException>(() => Body(new[]
        {
            Msg("user", "q"),
            Msg("tool", "x")
        }));

        Assert.Equal("A 'tool' message requires ToolCallId.", ex.Message);
    }

    [Fact]
    public void PlainConversation_IsUnchanged() {
        var body = Body(new[]
        {
            new ChatMessage { Role = "system", Content = "be brief" },
            new ChatMessage { Role = "user", Content = "hi" }
        });

        Assert.Equal("be brief", (string?)body["system"]);
        Assert.Equal("hi", (string?)body["messages"]![0]!["content"]);
    }

    private const string ToolUseResponse = """
        {"content":[{"type":"tool_use","id":"toolu_9","name":"get_weather","input":{"city":"Porto"}}],"stop_reason":"tool_use","usage":{"input_tokens":5,"output_tokens":7}}
        """;

    [Fact]
    public void ToolUseResponse_YieldsOpenAiToolCalls() {
        var calls = JsonNode.Parse(Sut.ExtractAnthropicToolCalls(ToolUseResponse))!.AsArray();

        Assert.Single(calls);
        Assert.Equal("toolu_9", (string?)calls[0]!["id"]);
        Assert.Equal("function", (string?)calls[0]!["type"]);
        Assert.Equal("get_weather", (string?)calls[0]!["function"]!["name"]);
        // arguments is a JSON *string*, as OpenAI clients expect
        Assert.Equal("Porto", (string?)JsonNode.Parse((string)calls[0]!["function"]!["arguments"]!)!["city"]);
    }

    [Fact]
    public void ToolUseOnlyResponse_HasEmptyTextAndToolUseStopReason() {
        Assert.Equal(string.Empty, Sut.ExtractAnthropicText(ToolUseResponse));
        Assert.Equal("tool_use", Sut.ExtractAnthropicStopReason(ToolUseResponse));
    }

    [Fact]
    public void TextResponse_HasNoToolCalls() {
        const string body = """{"content":[{"type":"text","text":"hi"}],"stop_reason":"end_turn"}""";

        Assert.Equal(string.Empty, Sut.ExtractAnthropicToolCalls(body));
        Assert.Equal("hi", Sut.ExtractAnthropicText(body));
    }

    [Theory]
    [InlineData("\"stop_reason\":\"max_tokens\"", "max_tokens")]
    [InlineData("\"stop_reason\":\"end_turn\"", "end_turn")]
    [InlineData("\"stop_reason\":\"stop_sequence\"", "stop_sequence")]
    [InlineData("\"stop_reason\":null", "")]
    [InlineData("\"stop_reason\":7", "")]
    [InlineData("\"other\":1", "")]
    public void ToolUseBlocks_AreIgnored_UnlessStopReasonIsToolUse(string stopField, string expectedStopReason) {
        var body = "{\"content\":[{\"type\":\"text\",\"text\":\"let me check\"},"
                   + "{\"type\":\"tool_use\",\"id\":\"toolu_1\",\"name\":\"get_weather\",\"input\":{}}]," + stopField + "}";

        Assert.Equal(string.Empty, Sut.ExtractAnthropicToolCalls(body));
        Assert.Equal(expectedStopReason, Sut.ExtractAnthropicStopReason(body));
    }

    [Fact]
    public void ToolUseStopReason_WithoutContent_YieldsNoCallsAndEmptyText() {
        const string body = """{"content":[],"stop_reason":"tool_use"}""";

        Assert.Equal(string.Empty, Sut.ExtractAnthropicToolCalls(body));
        Assert.Equal(string.Empty, Sut.ExtractAnthropicText(body));
    }

    [Fact]
    public void TruncatedToolUse_WithoutText_ThrowsNeutralMessage() {
        const string body = """{"content":[{"type":"tool_use","id":"toolu_1","name":"get_weather","input":{}}],"stop_reason":"max_tokens"}""";

        Assert.Equal(string.Empty, Sut.ExtractAnthropicToolCalls(body));
        var ex = Assert.Throws<InvalidOperationException>(() => Sut.ExtractAnthropicText(body));
        Assert.Contains("thinking or a tool call", ex.Message);
    }

    [Fact]
    public void EmptyResponse_WithoutToolUse_StillThrows() {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Sut.ExtractAnthropicText("""{"content":[],"stop_reason":"max_tokens"}"""));

        Assert.StartsWith("Vertex AI (anthropic) returned no text content (stop_reason: max_tokens).", ex.Message);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("[]", "")]
    [InlineData("", "auto")]
    [InlineData("[]", " AUTO ")]
    public void FindToolCallingRequest_DefaultsMeanNoToolCalling(string tools, string choice) {
        Assert.Null(Sut.FindToolCallingRequest(tools, choice, new List<Sut.Turn> { new("user", "hi") }));
    }

    [Fact]
    public void FindToolCallingRequest_NamesTheTrigger() {
        var user = new Sut.Turn("user", "hi");

        Assert.Equal("ToolsJson with at least one tool", Sut.FindToolCallingRequest(Tools, "", new() { user }));
        Assert.Equal("ToolChoice 'required'", Sut.FindToolCallingRequest("", "required", new() { user }));
        Assert.Equal("a 'tool' message", Sut.FindToolCallingRequest("", "", new() { new("tool", "r", "id1") }));
        Assert.Equal("an assistant message with ToolCallsJson", Sut.FindToolCallingRequest("", "", new() { new("assistant", "", null, "[{}]") }));
    }

    [Theory]
    [InlineData("[]", "auto")]
    [InlineData("", "")]
    public void Chat_Google_WithNormalizedDefaults_IsNotRejectedAsToolCalling(string tools, string choice) {
        var lib = new Sut();

        // Passes the tool guard; fails later on the invalid credentials, not with NotSupportedException.
        var ex = Record.Exception(() => lib.Chat("test-project", DummyAccount, "us-central1", "google", "m",
            new List<ChatMessage>(Hi), 0, -1m, out _, out _, out _, ToolsJson: tools, ToolChoice: choice));

        Assert.IsNotType<NotSupportedException>(ex);
    }

    [Fact]
    public void Chat_Google_WithTools_IsRejected() {
        var lib = new Sut();

        var ex = Assert.Throws<NotSupportedException>(() => lib.Chat("test-project", DummyAccount, "us-central1", "google", "m",
            new List<ChatMessage> { new() { Role = "user", Content = "hi" } }, 0, -1m,
            out _, out _, out _, ToolsJson: Tools));

        Assert.Equal("Tool calling is only supported for the 'anthropic' publisher " +
                     "(the request has ToolsJson with at least one tool).", ex.Message);
    }

    private static readonly ChatMessage[] Hi = { new() { Role = "user", Content = "hi" } };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("auto")]
    public void ToolChoice_DefaultIsOmitted(string? choice) {
        Assert.Null(Body(Hi, Tools, choice)["tool_choice"]);
    }

    [Theory]
    [InlineData("none", "none")]
    [InlineData("required", "any")]
    [InlineData("any", "any")]
    public void ToolChoice_Keywords_MapToAnthropicTypes(string choice, string expectedType) {
        var toolChoice = Body(Hi, Tools, choice)["tool_choice"]!;
        Assert.Equal(expectedType, (string?)toolChoice["type"]);
        Assert.Null(toolChoice["name"]);
    }

    [Fact]
    public void ToolChoice_ToolName_ForcesThatTool() {
        var toolChoice = Body(Hi, Tools, "get_weather")["tool_choice"]!;
        Assert.Equal("tool", (string?)toolChoice["type"]);
        Assert.Equal("get_weather", (string?)toolChoice["name"]);
    }

    [Fact]
    public void ToolChoice_WithoutTools_Throws() {
        var ex = Assert.Throws<ArgumentException>(() => Body(Hi, null, "required"));

        Assert.Equal("ToolChoice requires ToolsJson to define at least one tool.", ex.Message);
    }

    [Fact]
    public void Chat_Google_WithToolChoice_IsRejected() {
        var lib = new Sut();

        var ex = Assert.Throws<NotSupportedException>(() => lib.Chat("test-project", DummyAccount, "us-central1", "google", "m",
            new List<ChatMessage>(Hi), 0, -1m,
            out _, out _, out _, ToolChoice: "required"));

        Assert.Equal("Tool calling is only supported for the 'anthropic' publisher " +
                     "(the request has ToolChoice 'required').", ex.Message);
    }
}

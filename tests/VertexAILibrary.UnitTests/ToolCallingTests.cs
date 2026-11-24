using System.Text.Json.Nodes;
using Xunit;
using Lib = VertexAILibrary.VertexAILibrary;

namespace VertexAILibrary.UnitTests
{
    public class ToolCallingTests
    {
        private const string Tools = """
            [{"type":"function","function":{"name":"get_weather","description":"Weather","parameters":{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}}}]
            """;

        private const string Calls = """
            [{"id":"toolu_1","type":"function","function":{"name":"get_weather","arguments":"{\"city\":\"Lisbon\"}"}}]
            """;

        private const string TwoCalls = """
            [{"id":"toolu_1","type":"function","function":{"name":"get_weather","arguments":"{\"city\":\"Lisbon\"}"}},
             {"id":"toolu_2","type":"function","function":{"name":"get_weather","arguments":"{\"city\":\"Porto\"}"}}]
            """;

        private static JsonNode Body(IEnumerable<ChatMessage> messages, string? tools = null, string? toolChoice = null)
        {
            var (system, turns) = Lib.SplitMessages(messages);
            return JsonNode.Parse(Lib.BuildAnthropicRequestBody(system, turns, 0, tools, toolChoice))!;
        }

        [Fact]
        public void Tools_AreConvertedToAnthropicFormat()
        {
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
        public void NoTools_OmitsToolsField(string? tools)
        {
            var body = Body(new[] { new ChatMessage { Role = "user", Content = "hi" } }, tools);

            Assert.Null(body["tools"]);
        }

        [Fact]
        public void ToolWithoutParameters_GetsEmptyObjectSchema()
        {
            var tools = Lib.ConvertTools("""[{"type":"function","function":{"name":"ping"}}]""");

            Assert.Equal("object", (string?)tools![0]!["input_schema"]!["type"]);
        }

        [Theory]
        [InlineData("""["x"]""", "ToolsJson[0]")]
        [InlineData("""[{"function":"x"}]""", "ToolsJson[0].function")]
        [InlineData("""[{"name":42}]""", "ToolsJson[0].name")]
        [InlineData("""[{"name":"a","description":1}]""", "ToolsJson[0].description")]
        [InlineData("""[{"name":""}]""", "ToolsJson[0].name")]
        [InlineData("[{\"name\":\"a\"", "ToolsJson is not valid JSON")]
        public void MalformedTools_ThrowArgumentException(string tools, string expected)
        {
            var ex = Assert.Throws<ArgumentException>(() => Lib.ConvertTools(tools));

            Assert.Contains(expected, ex.Message);
        }

        [Theory]
        [InlineData("""["x"]""", "ToolCallsJson[0]")]
        [InlineData("""[{"id":"a","function":"x"}]""", "ToolCallsJson[0].function")]
        [InlineData("""[{"id":7,"function":{"name":"n"}}]""", "ToolCallsJson[0].id")]
        [InlineData("""[{"id":"a","function":{"name":5}}]""", "ToolCallsJson[0].function.name")]
        [InlineData("""[{"id":"a","function":{"name":"n","arguments":"{\"city\":"}}]""", "ToolCallsJson[0].function.arguments is not valid JSON")]
        [InlineData("[{\"id\":\"a\"", "ToolCallsJson is not valid JSON")]
        public void MalformedToolCalls_ThrowArgumentException(string calls, string expected)
        {
            var messages = new[]
            {
                new ChatMessage { Role = "user", Content = "hi" },
                new ChatMessage { Role = "assistant", Content = "", ToolCallsJson = calls }
            };

            var ex = Assert.Throws<ArgumentException>(() => Body(messages));

            Assert.Contains(expected, ex.Message);
        }

        [Fact]
        public void AssistantToolCalls_BecomeToolUseBlocks_WithParsedInput()
        {
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
        public void AssistantToolCalls_KeepTextBlock_WhenContentPresent()
        {
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
        public void ConsecutiveToolResults_ShareOneUserMessage()
        {
            var body = Body(new[]
            {
                new ChatMessage { Role = "user", Content = "q" },
                new ChatMessage { Role = "assistant", Content = "", ToolCallsJson = TwoCalls },
                new ChatMessage { Role = "tool", ToolCallId = "toolu_1", Content = "sunny" },
                new ChatMessage { Role = "tool", ToolCallId = "toolu_2", Content = "rainy" }
            });

            var messages = body["messages"]!.AsArray();
            Assert.Equal(3, messages.Count);
            Assert.Equal(2, messages[1]!["content"]!.AsArray().Count);
            var results = messages[2]!["content"]!.AsArray();
            Assert.Equal("user", (string?)messages[2]!["role"]);
            Assert.Equal(2, results.Count);
            Assert.Equal("tool_result", (string?)results[0]!["type"]);
            Assert.Equal("toolu_1", (string?)results[0]!["tool_use_id"]);
            Assert.Equal("sunny", (string?)results[0]!["content"]);
            Assert.Equal("tool_result", (string?)results[1]!["type"]);
            Assert.Equal("toolu_2", (string?)results[1]!["tool_use_id"]);
            Assert.Equal("rainy", (string?)results[1]!["content"]);
        }

        [Fact]
        public void ToolMessage_WithoutToolCallId_Throws()
        {
            Assert.Throws<ArgumentException>(() => Body(new[]
            {
                new ChatMessage { Role = "user", Content = "q" },
                new ChatMessage { Role = "tool", Content = "x" }
            }));
        }

        [Fact]
        public void PlainConversation_IsUnchanged()
        {
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
        public void ToolUseResponse_YieldsOpenAiToolCalls()
        {
            var calls = JsonNode.Parse(Lib.ExtractAnthropicToolCalls(ToolUseResponse))!.AsArray();

            Assert.Single(calls);
            Assert.Equal("toolu_9", (string?)calls[0]!["id"]);
            Assert.Equal("function", (string?)calls[0]!["type"]);
            Assert.Equal("get_weather", (string?)calls[0]!["function"]!["name"]);
            // arguments is a JSON *string*, as OpenAI clients expect
            Assert.Equal("Porto", (string?)JsonNode.Parse((string)calls[0]!["function"]!["arguments"]!)!["city"]);
        }

        [Fact]
        public void ToolUseOnlyResponse_HasEmptyTextAndToolUseStopReason()
        {
            Assert.Equal(string.Empty, Lib.ExtractAnthropicText(ToolUseResponse));
            Assert.Equal("tool_use", Lib.ExtractAnthropicStopReason(ToolUseResponse));
        }

        [Fact]
        public void TextResponse_HasNoToolCalls()
        {
            const string body = """{"content":[{"type":"text","text":"hi"}],"stop_reason":"end_turn"}""";

            Assert.Equal(string.Empty, Lib.ExtractAnthropicToolCalls(body));
            Assert.Equal("hi", Lib.ExtractAnthropicText(body));
        }

        [Fact]
        public void TruncatedToolUse_WithText_ReturnsNoToolCalls()
        {
            const string body = """{"content":[{"type":"text","text":"let me check"},{"type":"tool_use","id":"toolu_1","name":"get_weather","input":{}}],"stop_reason":"max_tokens"}""";

            Assert.Equal(string.Empty, Lib.ExtractAnthropicToolCalls(body));
            Assert.Equal("max_tokens", Lib.ExtractAnthropicStopReason(body));
        }

        [Fact]
        public void TruncatedToolUse_WithoutText_ThrowsNeutralMessage()
        {
            const string body = """{"content":[{"type":"tool_use","id":"toolu_1","name":"get_weather","input":{}}],"stop_reason":"max_tokens"}""";

            Assert.Equal(string.Empty, Lib.ExtractAnthropicToolCalls(body));
            var ex = Assert.Throws<InvalidOperationException>(() => Lib.ExtractAnthropicText(body));
            Assert.Contains("thinking or a tool call", ex.Message);
        }

        [Fact]
        public void EmptyResponse_WithoutToolUse_StillThrows()
        {
            Assert.Throws<InvalidOperationException>(() =>
                Lib.ExtractAnthropicText("""{"content":[],"stop_reason":"max_tokens"}"""));
        }

        [Theory]
        [InlineData("", "")]
        [InlineData("[]", "")]
        [InlineData("", "auto")]
        [InlineData("[]", " AUTO ")]
        public void FindToolCallingRequest_DefaultsMeanNoToolCalling(string tools, string choice)
        {
            Assert.Null(Lib.FindToolCallingRequest(tools, choice, new List<Lib.Turn> { new("user", "hi") }));
        }

        [Fact]
        public void FindToolCallingRequest_NamesTheTrigger()
        {
            var user = new Lib.Turn("user", "hi");

            Assert.Contains("ToolsJson", Lib.FindToolCallingRequest(Tools, "", new() { user }));
            Assert.Contains("'required'", Lib.FindToolCallingRequest("", "required", new() { user }));
            Assert.Contains("'tool' message", Lib.FindToolCallingRequest("", "", new() { new("tool", "r", "id1") }));
            Assert.Contains("ToolCallsJson", Lib.FindToolCallingRequest("", "", new() { new("assistant", "", null, "[{}]") }));
        }

        [Theory]
        [InlineData("[]", "auto")]
        [InlineData("", "")]
        public void Chat_Google_WithNormalizedDefaults_IsNotRejectedAsToolCalling(string tools, string choice)
        {
            var lib = new Lib();
            var account = new GCPServiceAccount { ClientEmail = "a@b", PrivateKey = "k" };

            // Passes the tool guard; fails later on the invalid credentials, not with NotSupportedException.
            var ex = Record.Exception(() => lib.Chat("p", account, "us-central1", "google", "m",
                new List<ChatMessage>(Hi), 0, -1m, out _, out _, out _, ToolsJson: tools, ToolChoice: choice));

            Assert.IsNotType<NotSupportedException>(ex);
        }

        [Fact]
        public void Chat_Google_WithTools_IsRejected()
        {
            var lib = new Lib();
            var account = new GCPServiceAccount { ClientEmail = "a@b", PrivateKey = "k" };

            Assert.Throws<NotSupportedException>(() => lib.Chat("p", account, "us-central1", "google", "m",
                new List<ChatMessage> { new() { Role = "user", Content = "hi" } }, 0, -1m,
                out _, out _, out _, ToolsJson: Tools));
        }

        private static readonly ChatMessage[] Hi = { new() { Role = "user", Content = "hi" } };

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("auto")]
        public void ToolChoice_DefaultIsOmitted(string? choice)
        {
            Assert.Null(Body(Hi, Tools, choice)["tool_choice"]);
        }

        [Theory]
        [InlineData("none", "none")]
        [InlineData("required", "any")]
        [InlineData("any", "any")]
        public void ToolChoice_Keywords_MapToAnthropicTypes(string choice, string expectedType)
        {
            var toolChoice = Body(Hi, Tools, choice)["tool_choice"]!;
            Assert.Equal(expectedType, (string?)toolChoice["type"]);
            Assert.Null(toolChoice["name"]);
        }

        [Fact]
        public void ToolChoice_ToolName_ForcesThatTool()
        {
            var toolChoice = Body(Hi, Tools, "get_weather")["tool_choice"]!;
            Assert.Equal("tool", (string?)toolChoice["type"]);
            Assert.Equal("get_weather", (string?)toolChoice["name"]);
        }

        [Fact]
        public void ToolChoice_WithoutTools_Throws()
        {
            Assert.Throws<ArgumentException>(() => Body(Hi, null, "required"));
        }

        [Fact]
        public void Chat_Google_WithToolChoice_IsRejected()
        {
            var lib = new Lib();
            var account = new GCPServiceAccount { ClientEmail = "a@b", PrivateKey = "k" };

            Assert.Throws<NotSupportedException>(() => lib.Chat("p", account, "us-central1", "google", "m",
                new List<ChatMessage>(Hi), 0, -1m,
                out _, out _, out _, ToolChoice: "required"));
        }
    }
}

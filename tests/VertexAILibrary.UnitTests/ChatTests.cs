using System.Text.Json.Nodes;
using Xunit;
using Sut = VertexAILibrary.VertexAILibrary;

namespace VertexAILibrary.UnitTests
{
    /// <summary>
    /// Chat validation, system-message merging, multi-turn bodies and the tool-call round trip.
    /// Validation cases must fail before credentials are built, so a dummy account is enough
    /// and no network call is made.
    /// </summary>
    public class ChatTests
    {
        private const string ValidRegion = "europe-west4";

        private static readonly string[] UserAssistant = { "user", "assistant" };
        private static readonly string[] ToolIds = { "toolu_1", "toolu_2" };
        private static readonly string[] ToolResults = { "sunny", "10:00" };

        private static readonly GCPServiceAccount DummyAccount = new()
        {
            ClientEmail = "dummy@example.iam.gserviceaccount.com",
            PrivateKey = "not-a-key",
            PrivateKeyId = "dummy"
        };

        private static ChatMessage Msg(string role, string? content = "x", string? toolCallId = null, string? toolCallsJson = null)
            => new() { Role = role, Content = content!, ToolCallId = toolCallId!, ToolCallsJson = toolCallsJson! };

        private static void CallChat(string region, string publisher, List<ChatMessage>? messages)
            => new Sut().Chat("p", DummyAccount, region, publisher, "m", messages!, 0, -1m, "", "",
                out _, out _, out _);

        private static JsonNode Body(IEnumerable<ChatMessage> messages)
        {
            var (system, turns) = Sut.SplitMessages(messages);
            return JsonNode.Parse(Sut.BuildAnthropicRequestBody(system, turns, 0))!;
        }

        // ─────────────────────────────────────────────────────────────
        // Validation
        // ─────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("google")]
        [InlineData("anthropic")]
        public void NullMessages_Throw(string publisher)
        {
            var ex = Assert.Throws<ArgumentException>(() => CallChat(ValidRegion, publisher, null));

            Assert.Equal("Messages must contain at least one message.", ex.Message);
        }

        [Fact]
        public void EmptyMessages_Throw()
        {
            var ex = Assert.Throws<ArgumentException>(() => CallChat(ValidRegion, "google", new List<ChatMessage>()));

            Assert.Equal("Messages must contain at least one message.", ex.Message);
        }

        [Fact]
        public void SystemOnlyMessages_Throw()
        {
            var ex = Assert.Throws<ArgumentException>(
                () => CallChat(ValidRegion, "anthropic", new List<ChatMessage> { Msg("system"), Msg("SYSTEM") }));

            Assert.Contains("at least one non-system message", ex.Message);
        }

        [Theory]
        [InlineData("model")]
        [InlineData("")]
        [InlineData(null)]
        public void InvalidRole_Throws(string? role)
        {
            var ex = Assert.Throws<ArgumentException>(
                () => CallChat(ValidRegion, "google", new List<ChatMessage> { Msg("user"), Msg(role!) }));

            Assert.Contains($"Chat role '{role}' is not supported.", ex.Message);
        }

        [Fact]
        public void InvalidRegion_FailsBeforeMessageValidation()
        {
            var ex = Assert.Throws<ArgumentException>(() => CallChat("mars-north1", "google", null));

            Assert.StartsWith("Region 'mars-north1' is not supported.", ex.Message);
        }

        [Fact]
        public void InvalidPublisher_FailsBeforeMessageValidation()
        {
            var ex = Assert.Throws<ArgumentException>(() => CallChat(ValidRegion, "openai", null));

            Assert.StartsWith("Publisher 'openai' is not supported.", ex.Message);
        }

        [Fact]
        public void NormalizedRole_IsLowerCaseTrimmed()
        {
            var (_, turns) = Sut.SplitMessages(new[] { Msg(" USER "), Msg("Assistant") });

            Assert.Equal(UserAssistant, turns.Select(t => t.Role));
        }

        // ─────────────────────────────────────────────────────────────
        // System message merging
        // ─────────────────────────────────────────────────────────────

        [Fact]
        public void SeveralSystemMessages_AreJoinedWithBlankLine_InOrder()
        {
            var (system, turns) = Sut.SplitMessages(new[]
            {
                Msg("system", "first"), Msg("user", "hi"), Msg("system", "mid"), Msg("system", "last")
            });

            Assert.Equal("first\n\nmid\n\nlast", system);
            Assert.Single(turns);
        }

        [Fact]
        public void EmptyAndNullSystemMessages_AreIgnored()
        {
            var (system, _) = Sut.SplitMessages(new[]
            {
                Msg("system", ""), Msg("system", null), Msg("system", "kept"), Msg("user", "hi")
            });

            Assert.Equal("kept", system);
        }

        [Fact]
        public void OnlyEmptySystemMessages_GiveNoSystem()
        {
            var (system, _) = Sut.SplitMessages(new[] { Msg("system", ""), Msg("system", null), Msg("user", "hi") });

            Assert.Null(system);
        }

        [Fact]
        public void MidConversationSystem_GoesToTopLevelSystemField()
        {
            var body = Body(new[] { Msg("user", "a"), Msg("system", "rules"), Msg("assistant", "b") });

            Assert.Equal("rules", (string?)body["system"]);
            Assert.Equal(2, body["messages"]!.AsArray().Count);
        }

        [Fact]
        public void NullContentOnUserTurn_BecomesEmptyString()
        {
            var (_, turns) = Sut.SplitMessages(new[] { Msg("user", null) });

            Assert.Equal(string.Empty, turns[0].Content);
        }

        // ─────────────────────────────────────────────────────────────
        // Multi-turn bodies
        // ─────────────────────────────────────────────────────────────

        [Fact]
        public void MultiTurn_IsByteIdenticalToExpected()
        {
            var (system, turns) = Sut.SplitMessages(new[]
            {
                Msg("system", "s"), Msg("user", "q1"), Msg("assistant", "a1"), Msg("user", "q2")
            });

            Assert.Equal(
                "{\"anthropic_version\":\"vertex-2023-10-16\",\"max_tokens\":4096,\"messages\":[" +
                "{\"role\":\"user\",\"content\":\"q1\"},{\"role\":\"assistant\",\"content\":\"a1\"}," +
                "{\"role\":\"user\",\"content\":\"q2\"}],\"system\":\"s\"}",
                Sut.BuildAnthropicRequestBody(system, turns, 0));
        }

        // ─────────────────────────────────────────────────────────────
        // Tool round trip
        // ─────────────────────────────────────────────────────────────

        private const string TwoCallsResponse = """
            {"content":[{"type":"text","text":"Checking both."},
                        {"type":"tool_use","id":"toolu_1","name":"get_weather","input":{"city":"Lisbon"}},
                        {"type":"tool_use","id":"toolu_2","name":"get_time","input":{"zone":"WET"}}],
             "stop_reason":"tool_use"}
            """;

        [Fact]
        public void MixedResponse_YieldsTextAndEveryToolCall()
        {
            var calls = JsonNode.Parse(Sut.ExtractAnthropicToolCalls(TwoCallsResponse))!.AsArray();

            Assert.Equal("Checking both.", Sut.ExtractAnthropicText(TwoCallsResponse));
            Assert.Equal(2, calls.Count);
            Assert.Equal("toolu_1", (string?)calls[0]!["id"]);
            Assert.Equal("get_time", (string?)calls[1]!["function"]!["name"]);
        }

        [Fact]
        public void ExtractedToolCalls_RoundTripThroughParseToolCalls()
        {
            var toolCallsJson = Sut.ExtractAnthropicToolCalls(TwoCallsResponse);

            var body = Body(new[]
            {
                Msg("user", "q"),
                Msg("assistant", Sut.ExtractAnthropicText(TwoCallsResponse), toolCallsJson: toolCallsJson),
                Msg("tool", "sunny", "toolu_1"),
                Msg("tool", "10:00", "toolu_2")
            });

            var messages = body["messages"]!.AsArray();
            var assistant = messages[1]!["content"]!.AsArray();
            Assert.Equal(3, assistant.Count);
            Assert.Equal("text", (string?)assistant[0]!["type"]);
            Assert.Equal("toolu_1", (string?)assistant[1]!["id"]);
            Assert.Equal("Lisbon", (string?)assistant[1]!["input"]!["city"]);
            Assert.Equal("toolu_2", (string?)assistant[2]!["id"]);
            Assert.Equal("WET", (string?)assistant[2]!["input"]!["zone"]);

            var results = messages[2]!["content"]!.AsArray();
            Assert.Equal(ToolIds, results.Select(r => (string?)r!["tool_use_id"]));
            Assert.Equal(ToolResults, results.Select(r => (string?)r!["content"]));
        }

        [Fact]
        public void UserMessageAfterToolResults_StaysSeparateUserMessage()
        {
            var body = Body(new[]
            {
                Msg("user", "q"),
                Msg("assistant", "", toolCallsJson: """[{"id":"toolu_1","type":"function","function":{"name":"f","arguments":"{}"}}]"""),
                Msg("tool", "r", "toolu_1"),
                Msg("user", "next")
            });

            var messages = body["messages"]!.AsArray();
            Assert.Equal(4, messages.Count);
            Assert.Equal("tool_result", (string?)messages[2]!["content"]![0]!["type"]);
            Assert.Equal("user", (string?)messages[3]!["role"]);
            Assert.Equal("next", (string?)messages[3]!["content"]);
        }
    }
}

namespace VertexAILibrary.UnitTests;

/// <summary>
/// Fixtures shared by the Chat and tool-calling tests. Validation cases must fail before
/// credentials are built, so a dummy account is enough and no network call is made.
/// </summary>
internal static class TestHelpers {
    internal static readonly GCPServiceAccount DummyAccount = new() {
        ClientEmail = "dummy@example.iam.gserviceaccount.com",
        PrivateKey = "not-a-key",
        PrivateKeyId = "dummy"
    };

    internal static ChatMessage Msg(string role, string? content = "x", string? toolCallId = null, string? toolCallsJson = null)
        => new() { Role = role, Content = content!, ToolCallId = toolCallId!, ToolCallsJson = toolCallsJson! };
}

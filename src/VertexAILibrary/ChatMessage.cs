using OutSystems.ExternalLibraries.SDK;

namespace VertexAILibrary; 
/// <summary>
/// One message in a multi-turn conversation.
/// </summary>
[OSStructure(Description = "One message in a conversation: a role, its content and, for tool calling, the tool call fields.")]
public struct ChatMessage {
    [OSStructureField(Description = "Author of the message: 'system', 'user', 'assistant' or 'tool' (a tool result).")]
    public string Role { get; set; }

    [OSStructureField(Description = "Text of the message. For role 'tool': the tool result. May be empty on an 'assistant' message that only calls tools.")]
    public string Content { get; set; }

    [OSStructureField(Description = "For role 'tool': the id of the tool call this message answers (the 'id' of the call returned in ToolCallsJson). Empty otherwise.")]
    public string ToolCallId { get; set; }

    [OSStructureField(Description = "For role 'assistant': the tool calls the model made in that turn, as an OpenAI 'tool_calls' JSON array " +
                                     "(as returned by Chat in ToolCallsJson). Empty otherwise.")]
    public string ToolCallsJson { get; set; }
}

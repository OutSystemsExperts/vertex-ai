using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.AIPlatform.V1;
using Google.Protobuf;
using Grpc.Auth;

namespace VertexAILibrary;
/// <summary>
/// Implementation of the IVertexAI OSInterface.
/// ODC instantiates this class to execute each Server Action.
/// </summary>
public class VertexAILibrary : IVertexAI {
    // ─────────────────────────────────────────────────────────────
    // Supported regions
    // Add or remove entries here to control what ODC devs can pick.
    // ─────────────────────────────────────────────────────────────
    private static readonly string[] _supportedRegions =
    {
        "eu",                   // EU multi-region — guarantees EU data residency (aiplatform.eu.rep.googleapis.com)
        "us",                   // US multi-region (aiplatform.us.rep.googleapis.com)
        "global",               // Global endpoint (aiplatform.googleapis.com) — NO data-residency guarantee
        "us-central1",          // Iowa — default, lowest latency for US
        "us-east4",             // N. Virginia
        "us-west1",             // Oregon
        "us-west4",             // Las Vegas
        "northamerica-northeast1", // Montréal
        "southamerica-east1",   // São Paulo
        "europe-west1",         // Belgium
        "europe-west2",         // London
        "europe-west3",         // Frankfurt
        "europe-west4",         // Netherlands
        "europe-west9",         // Paris
        "asia-east1",           // Taiwan
        "asia-east2",           // Hong Kong
        "asia-northeast1",      // Tokyo
        "asia-northeast3",      // Seoul
        "asia-southeast1",      // Singapore
        "australia-southeast1"  // Sydney
    };

    // ─────────────────────────────────────────────────────────────
    // Supported publishers
    // ─────────────────────────────────────────────────────────────
    internal const string GooglePublisher = "google";
    internal const string AnthropicPublisher = "anthropic";

    private static readonly string[] _supportedPublishers = { GooglePublisher, AnthropicPublisher };

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(90) };

    private static readonly JsonSerializerOptions _relaxedJsonOptions =
        new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // ─────────────────────────────────────────────────────────────
    // Default output token budget, applied when the caller passes 0.
    // Current Google and Anthropic models think by default and thinking
    // tokens count against this budget, so it must leave room for both
    // the thinking pass and the visible answer (Anthropic's minimum
    // thinking budget alone is 1024 tokens).
    // ─────────────────────────────────────────────────────────────
    private const int DefaultMaxOutputTokens = 4096;
    private const string AnthropicVersion = "vertex-2023-10-16";


    // ─────────────────────────────────────────────────────────────
    // GetSupportedPublishers
    // ─────────────────────────────────────────────────────────────
    public List<string> GetSupportedPublishers() {
        return new List<string>(_supportedPublishers);
    }

    // ─────────────────────────────────────────────────────────────
    // GetSupportedRegions
    // ─────────────────────────────────────────────────────────────
    public List<string> GetSupportedRegions() {
        return new List<string>(_supportedRegions);
    }

    // ─────────────────────────────────────────────────────────────
    // GenerateContent
    // ─────────────────────────────────────────────────────────────
    public string GenerateContent(
        string ProjectId,
        GCPServiceAccount ServiceAccount,
        string Region,
        string Publisher,
        string ModelId,
        string Prompt,
        int MaxOutputTokens,
        decimal Temperature,
        out TokenUsage Usage,
        out List<OutputFile> OutputFiles) {
        var (text, usage, outputFiles) = Generate(ProjectId, ServiceAccount, Region, Publisher, ModelId,
            null, Prompt, MaxOutputTokens, Temperature);

        Usage = usage;
        OutputFiles = outputFiles;
        return text;
    }

    // ─────────────────────────────────────────────────────────────
    // GenerateContentWithSystemPrompt
    // ─────────────────────────────────────────────────────────────
    public string GenerateContentWithSystemPrompt(
        string ProjectId,
        GCPServiceAccount ServiceAccount,
        string Region,
        string Publisher,
        string ModelId,
        string SystemInstruction,
        string UserPrompt,
        int MaxOutputTokens,
        out TokenUsage Usage,
        out List<OutputFile> OutputFiles) {
        var (text, usage, outputFiles) = Generate(ProjectId, ServiceAccount, Region, Publisher, ModelId,
            SystemInstruction, UserPrompt, MaxOutputTokens, -1m);

        Usage = usage;
        OutputFiles = outputFiles;
        return text;
    }

    // ─────────────────────────────────────────────────────────────
    // GenerateContentWithFiles
    // ─────────────────────────────────────────────────────────────
    public string GenerateContentWithFiles(
        string ProjectId,
        GCPServiceAccount ServiceAccount,
        string Region,
        string Publisher,
        string ModelId,
        string Prompt,
        List<InputFile> Files,
        int MaxOutputTokens,
        decimal Temperature,
        out TokenUsage Usage,
        out List<OutputFile> OutputFiles) {
        var (text, usage, outputFiles) = Generate(ProjectId, ServiceAccount, Region, Publisher, ModelId,
            null, Prompt, MaxOutputTokens, Temperature, Files ?? new List<InputFile>());

        Usage = usage;
        OutputFiles = outputFiles;
        return text;
    }

    // ─────────────────────────────────────────────────────────────
    // GenerateContentWithSystemPromptAndFiles
    // ─────────────────────────────────────────────────────────────
    public string GenerateContentWithSystemPromptAndFiles(
        string ProjectId,
        GCPServiceAccount ServiceAccount,
        string Region,
        string Publisher,
        string ModelId,
        string SystemInstruction,
        string UserPrompt,
        List<InputFile> Files,
        int MaxOutputTokens,
        out TokenUsage Usage,
        out List<OutputFile> OutputFiles) {
        var (text, usage, outputFiles) = Generate(ProjectId, ServiceAccount, Region, Publisher, ModelId,
            SystemInstruction, UserPrompt, MaxOutputTokens, -1m, Files ?? new List<InputFile>());

        Usage = usage;
        OutputFiles = outputFiles;
        return text;
    }

    // ─────────────────────────────────────────────────────────────
    // Chat
    // ─────────────────────────────────────────────────────────────
    public string Chat(
        string ProjectId,
        GCPServiceAccount ServiceAccount,
        string Region,
        string Publisher,
        string ModelId,
        List<ChatMessage> Messages,
        int MaxOutputTokens,
        decimal Temperature,
        out TokenUsage Usage,
        out string FinishReason,
        out string ToolCallsJson,
        string ToolsJson = "",
        string ToolChoice = "") {
        ValidateRegion(Region);
        ValidatePublisher(Publisher);
        ValidateProjectId(ProjectId);
        ValidateModelId(ModelId);
        ValidateMessages(Messages);

        var result = Publisher switch {
            AnthropicPublisher => GenerateChatAnthropic(ProjectId, ServiceAccount, Region, ModelId, Messages, MaxOutputTokens, ToolsJson, ToolChoice),
            GooglePublisher => GenerateChatGoogle(ProjectId, ServiceAccount, Region, ModelId, Messages, MaxOutputTokens, Temperature, ToolsJson, ToolChoice),
            _ => throw UnhandledPublisher(Publisher)
        };

        Usage = result.Usage;
        FinishReason = result.FinishReason;
        ToolCallsJson = result.ToolCallsJson;
        return result.Text;
    }

    // ─────────────────────────────────────────────────────────────
    // TestConnection
    // ─────────────────────────────────────────────────────────────
    public string TestConnection(
        string ProjectId,
        GCPServiceAccount ServiceAccount,
        string Region,
        string Publisher,
        string ModelId) {
        try {
            Generate(ProjectId, ServiceAccount, Region, Publisher, ModelId,
                null, "Reply with the single word: OK", 16, -1m);

            return "OK";
        } catch (Exception ex) {
            // Return the error message so ODC devs can read it in the action output
            return $"ERROR: {ex.Message}";
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates the inputs and sends the prompt through the transport required by the publisher:
    /// gRPC GenerateContent for google, rawPredict (Anthropic Messages format) for anthropic.
    /// When files are given, the prompt and files are validated (region → publisher → prompt →
    /// files) before any credential or network call.
    /// </summary>
    private (string Text, TokenUsage Usage, List<OutputFile> Files) Generate(
        string projectId,
        GCPServiceAccount account,
        string region,
        string publisher,
        string modelId,
        string? systemInstruction,
        string prompt,
        int maxOutputTokens,
        decimal temperature,
        List<InputFile>? files = null) {
        ValidateRegion(region);
        ValidatePublisher(publisher);
        ValidateProjectId(projectId);
        ValidateModelId(modelId);

        IReadOnlyList<ResolvedFile>? resolved = null;
        if (files != null) {
            InputFiles.ValidatePrompt(prompt);
            resolved = InputFiles.Resolve(files, publisher);
        }

        return publisher switch {
            AnthropicPublisher => GenerateAnthropic(projectId, account, region, modelId, systemInstruction, prompt, maxOutputTokens, resolved),
            GooglePublisher => GenerateGoogle(projectId, account, region, modelId, systemInstruction, prompt, maxOutputTokens, temperature, resolved),
            _ => throw UnhandledPublisher(publisher)
        };
    }

    private static (string Text, TokenUsage Usage, List<OutputFile> Files) GenerateGoogle(
        string projectId,
        GCPServiceAccount account,
        string region,
        string modelId,
        string? systemInstruction,
        string prompt,
        int maxOutputTokens,
        decimal temperature,
        IReadOnlyList<ResolvedFile>? files) {
        var client = BuildClient(account, region);

        var request = BuildGoogleRequest(projectId, region, modelId, systemInstruction, prompt,
            maxOutputTokens, temperature, files);

        var response = client.GenerateContent(request);
        var (text, outputFiles) = GoogleOutput.Extract(response);

        return (text, ExtractGoogleUsage(response), outputFiles);
    }

    /// <summary>
    /// Outcome of one model call: the reply text, its token usage and the publisher's stop reason.
    /// </summary>
    private sealed record GenerationResult(string Text, TokenUsage Usage, string FinishReason, string ToolCallsJson = "");

    /// <summary>
    /// One non-system conversation turn. ToolCallId is set for role "tool"; ToolCallsJson for an assistant turn that called tools.
    /// </summary>
    internal sealed record Turn(string Role, string Content, string? ToolCallId = null, string? ToolCallsJson = null);

    /// <summary>
    /// Describes what in a request asks for tool calling, or returns null when nothing does.
    /// Inputs are normalized first: empty ToolsJson, "[]" and ToolChoice "" / "auto" mean "no tool calling".
    /// </summary>
    internal static string? FindToolCallingRequest(string? toolsJson, string? toolChoice, List<Turn> turns) {
        if (ConvertTools(toolsJson) != null) {
            return "ToolsJson with at least one tool";
        }

        var choice = toolChoice?.Trim();
        if (!string.IsNullOrEmpty(choice) && !choice.Equals("auto", StringComparison.OrdinalIgnoreCase)) {
            return $"ToolChoice '{choice}'";
        }

        if (turns.Exists(t => t.Role == "tool")) {
            return "a 'tool' message";
        }

        if (turns.Exists(t => !string.IsNullOrEmpty(t.ToolCallsJson))) {
            return "an assistant message with ToolCallsJson";
        }

        return null;
    }

    private static GenerationResult GenerateChatGoogle(
        string projectId,
        GCPServiceAccount account,
        string region,
        string modelId,
        IEnumerable<ChatMessage> messages,
        int maxOutputTokens,
        decimal temperature,
        string? toolsJson,
        string? toolChoice) {
        var (system, turns) = SplitMessages(messages);

        var trigger = FindToolCallingRequest(toolsJson, toolChoice, turns);
        if (trigger != null) {
            throw new NotSupportedException(
                $"Tool calling is only supported for the 'anthropic' publisher (the request has {trigger}).");
        }

        var client = BuildClient(account, region);

        var request = new GenerateContentRequest {
            Model = BuildModelPath(projectId, region, GooglePublisher, modelId),
            GenerationConfig = BuildGenerationConfig(maxOutputTokens, temperature)
        };

        foreach (var turn in turns) {
            request.Contents.Add(new Content {
                // Gemini calls the assistant role "model".
                Role = turn.Role == "assistant" ? "MODEL" : "USER",
                Parts = { new Part { Text = turn.Content } }
            });
        }

        if (system != null) {
            request.SystemInstruction = new Content { Parts = { new Part { Text = system } } };
        }

        var response = client.GenerateContent(request);
        var (text, _) = GoogleOutput.Extract(response);

        return new GenerationResult(text, ExtractGoogleUsage(response), ExtractGoogleFinishReason(response));
    }

    private static GenerationResult GenerateChatAnthropic(
        string projectId,
        GCPServiceAccount account,
        string region,
        string modelId,
        IEnumerable<ChatMessage> messages,
        int maxOutputTokens,
        string? toolsJson,
        string? toolChoice) {
        var (system, turns) = SplitMessages(messages);

        var body = PostAnthropic(projectId, account, region, modelId,
            BuildAnthropicRequestBody(system, turns, maxOutputTokens, toolsJson, toolChoice));

        return new GenerationResult(
            ExtractAnthropicText(body),
            ExtractAnthropicUsage(body),
            ExtractAnthropicStopReason(body),
            ExtractAnthropicToolCalls(body));
    }

    /// <summary>
    /// Splits a conversation into the combined system instruction (null when there is none)
    /// and the user/assistant turns, in order. Empty system messages are ignored.
    /// </summary>
    internal static (string? System, List<Turn> Turns) SplitMessages(IEnumerable<ChatMessage> messages) {
        var system = new List<string>();
        var turns = new List<Turn>();

        foreach (var message in messages) {
            var role = NormalizeRole(message.Role);
            var content = message.Content ?? string.Empty;

            if (role == "system") {
                if (content.Length > 0) {
                    system.Add(content);
                }
            } else {
                turns.Add(new Turn(role, content,
                    string.IsNullOrEmpty(message.ToolCallId) ? null : message.ToolCallId,
                    NormalizeToolCallsJson(message.ToolCallsJson)));
            }
        }

        return (system.Count == 0 ? null : string.Join("\n\n", system), turns);
    }

    /// <summary>
    /// Maps "no tool calls" to null: empty, whitespace or a JSON array with no elements ("[]", as
    /// serialized by an empty list). Anything else, including invalid JSON, is kept so it is
    /// validated and reported later.
    /// </summary>
    private static string? NormalizeToolCallsJson(string? toolCallsJson) {
        if (string.IsNullOrWhiteSpace(toolCallsJson)) {
            return null;
        }

        try {
            if (JsonNode.Parse(toolCallsJson) is JsonArray { Count: 0 }) {
                return null;
            }
        } catch (JsonException) {
            // Left for ParseToolCalls to report with the input name.
        }

        return toolCallsJson;
    }

    /// <summary>
    /// Builds the Gemini GenerateContent request. Files become InlineData parts, in list order,
    /// placed before the text part; the system instruction never carries files.
    /// </summary>
    internal static GenerateContentRequest BuildGoogleRequest(
        string projectId,
        string region,
        string modelId,
        string? systemInstruction,
        string prompt,
        int maxOutputTokens,
        decimal temperature,
        IReadOnlyList<ResolvedFile>? files = null) {
        var userContent = new Content { Role = "USER" };

        foreach (var file in files ?? Array.Empty<ResolvedFile>()) {
            userContent.Parts.Add(new Part {
                InlineData = new Blob { MimeType = file.MimeType, Data = ByteString.CopyFrom(file.Content) }
            });
        }

        userContent.Parts.Add(new Part { Text = prompt });

        var request = new GenerateContentRequest {
            Model = BuildModelPath(projectId, region, GooglePublisher, modelId),
            GenerationConfig = BuildGenerationConfig(maxOutputTokens, temperature),
            Contents = { userContent }
        };

        if (!string.IsNullOrEmpty(systemInstruction)) {
            request.SystemInstruction = new Content { Parts = { new Part { Text = systemInstruction } } };
        }

        return request;
    }

    private static (string Text, TokenUsage Usage, List<OutputFile> Files) GenerateAnthropic(
        string projectId,
        GCPServiceAccount account,
        string region,
        string modelId,
        string? systemInstruction,
        string prompt,
        int maxOutputTokens,
        IReadOnlyList<ResolvedFile>? files) {
        var body = PostAnthropic(projectId, account, region, modelId,
            BuildAnthropicRequestBody(systemInstruction, prompt, maxOutputTokens, files));

        return (ExtractAnthropicText(body), ExtractAnthropicUsage(body), new List<OutputFile>());
    }

    /// <summary>
    /// Sends an Anthropic Messages request body to Vertex AI rawPredict and returns the response body.
    /// </summary>
    private static string PostAnthropic(
        string projectId, GCPServiceAccount account, string region, string modelId, string requestBody) {
        var url = $"https://{BuildEndpoint(region)}/v1/" +
                  $"{BuildModelPath(projectId, region, AnthropicPublisher, modelId)}:rawPredict";

        var token = BuildCredential(account).UnderlyingCredential
            .GetAccessTokenForRequestAsync().GetAwaiter().GetResult();

        using var request = new HttpRequestMessage(HttpMethod.Post, url) {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = _httpClient.SendAsync(request).GetAwaiter().GetResult();
        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException(
                $"Vertex AI (anthropic) returned {(int)response.StatusCode}: {body}");
        }

        return body;
    }

    /// <summary>
    /// Reads the token counts from a Gemini response; missing usage metadata counts as 0.
    /// </summary>
    internal static TokenUsage ExtractGoogleUsage(GenerateContentResponse response) {
        var meta = response.UsageMetadata;

        if (meta == null) {
            return default;
        }

        return new TokenUsage {
            InputTokens = meta.PromptTokenCount,
            OutputTokens = meta.CandidatesTokenCount,
            TotalTokens = meta.TotalTokenCount,
            // The SDK doesn't expose thoughtsTokenCount; thinking tokens are the remainder of the total.
            ThinkingTokens = Math.Max(0, meta.TotalTokenCount - meta.PromptTokenCount - meta.CandidatesTokenCount)
        };
    }

    /// <summary>
    /// Reads the token counts from an Anthropic Messages response; missing fields count as 0.
    /// </summary>
    internal static TokenUsage ExtractAnthropicUsage(string responseBody) {
        using var doc = JsonDocument.Parse(responseBody);

        if (!doc.RootElement.TryGetProperty("usage", out var usage)
            || usage.ValueKind != JsonValueKind.Object) {
            return default;
        }

        int Read(string name) =>
            usage.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

        var input = Read("input_tokens");
        var output = Read("output_tokens");
        var cacheCreation = Read("cache_creation_input_tokens");
        var cacheRead = Read("cache_read_input_tokens");

        return new TokenUsage {
            InputTokens = input,
            OutputTokens = output,
            TotalTokens = input + output + cacheCreation + cacheRead,
            CachedInputTokens = cacheRead,
            CacheCreationInputTokens = cacheCreation
        };
    }

    /// <summary>
    /// Builds the Anthropic Messages request body expected by Vertex AI rawPredict.
    /// Without files, content is the prompt string; with files, content is an array of
    /// image / document blocks (in list order) followed by a text block.
    /// </summary>
    internal static string BuildAnthropicRequestBody(
        string? systemInstruction, string prompt, int maxOutputTokens,
        IReadOnlyList<ResolvedFile>? files = null) {
        var hasFiles = files is { Count: > 0 };

        var body = BuildAnthropicEnvelope(systemInstruction, maxOutputTokens,
            new JsonArray(new JsonObject {
                ["role"] = "user",
                ["content"] = hasFiles ? BuildAnthropicContentBlocks(prompt, files!) : JsonValue.Create(prompt)
            }));

        // The default encoder escapes '+' in base64 as \u002B; only relax it when files are sent
        // so text-only bodies stay byte-identical.
        return hasFiles ? body.ToJsonString(_relaxedJsonOptions) : body.ToJsonString();
    }

    /// <summary>
    /// Builds the Anthropic Messages request body expected by Vertex AI rawPredict.
    /// The system instruction goes in the top-level "system" field; turns go in "messages";
    /// OpenAI-format tool definitions (when given) go in "tools".
    /// </summary>
    internal static string BuildAnthropicRequestBody(
        string? systemInstruction, IEnumerable<Turn> turns, int maxOutputTokens,
        string? toolsJson = null, string? toolChoice = null) {
        var messages = new JsonArray();
        JsonArray? pendingToolResults = null;

        void FlushToolResults() {
            if (pendingToolResults == null) {
                return;
            }

            messages.Add(new JsonObject { ["role"] = "user", ["content"] = pendingToolResults });
            pendingToolResults = null;
        }

        foreach (var turn in turns) {
            if (turn.Role == "tool") {
                if (string.IsNullOrEmpty(turn.ToolCallId)) {
                    throw new ArgumentException("A 'tool' message requires ToolCallId.");
                }

                // Consecutive tool results must share one user message (Anthropic requires alternating roles).
                pendingToolResults ??= new JsonArray();
                pendingToolResults.Add(new JsonObject {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = turn.ToolCallId,
                    ["content"] = turn.Content
                });
                continue;
            }

            FlushToolResults();

            if (turn.Role == "assistant" && turn.ToolCallsJson != null) {
                var blocks = new JsonArray();
                if (turn.Content.Length > 0) {
                    blocks.Add(new JsonObject { ["type"] = "text", ["text"] = turn.Content });
                }

                foreach (var call in ParseToolCalls(turn.ToolCallsJson)) {
                    blocks.Add(call);
                }

                messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = blocks });
            } else {
                messages.Add(new JsonObject {
                    ["role"] = turn.Role,
                    ["content"] = turn.Content
                });
            }
        }
        FlushToolResults();

        var body = BuildAnthropicEnvelope(systemInstruction, maxOutputTokens, messages);

        var tools = ConvertTools(toolsJson);
        if (tools != null) {
            body["tools"] = tools;
        }

        var choice = ConvertToolChoice(toolChoice, tools != null);
        if (choice != null) {
            body["tool_choice"] = choice;
        }

        return body.ToJsonString();
    }

    private static int ResolveMaxOutputTokens(int maxOutputTokens)
        => maxOutputTokens > 0 ? maxOutputTokens : DefaultMaxOutputTokens;

    /// <summary>
    /// The part of the Anthropic Messages body shared by every request: version, max_tokens,
    /// system and messages. Callers add their own extras (tools, tool_choice).
    /// </summary>
    private static JsonObject BuildAnthropicEnvelope(
        string? systemInstruction, int maxOutputTokens, JsonArray messages) {
        var body = new JsonObject {
            ["anthropic_version"] = AnthropicVersion,
            ["max_tokens"] = ResolveMaxOutputTokens(maxOutputTokens),
            ["messages"] = messages
        };

        if (!string.IsNullOrEmpty(systemInstruction)) {
            body["system"] = systemInstruction;
        }

        return body;
    }

    private static JsonArray BuildAnthropicContentBlocks(string prompt, IReadOnlyList<ResolvedFile> files) {
        var blocks = new JsonArray();

        foreach (var file in files) {
            blocks.Add(BuildAnthropicFileBlock(file));
        }

        blocks.Add(new JsonObject { ["type"] = "text", ["text"] = prompt });

        return blocks;
    }

    /// <summary>
    /// Maps a file to an Anthropic content block: image/* → image (base64), application/pdf →
    /// document (base64), text/plain → document (text source, UTF-8 decoded).
    /// </summary>
    private static JsonObject BuildAnthropicFileBlock(ResolvedFile file) {
        if (file.MimeType == "text/plain") {
            return BuildAnthropicBlock("document", "text", file, DecodeUtf8(file.Content));
        }

        var type = file.MimeType.StartsWith("image/", StringComparison.Ordinal) ? "image" : "document";

        return BuildAnthropicBlock(type, "base64", file, Convert.ToBase64String(file.Content));
    }

    private static JsonObject BuildAnthropicBlock(string type, string sourceType, ResolvedFile file, string data) {
        var block = new JsonObject {
            ["type"] = type,
            ["source"] = new JsonObject {
                ["type"] = sourceType,
                ["media_type"] = file.MimeType,
                ["data"] = data
            }
        };

        // Image blocks don't accept a title; document titles let the model refer to files by name.
        if (type == "document" && !string.IsNullOrWhiteSpace(file.FileName)) {
            block["title"] = file.FileName;
        }

        return block;
    }

    /// <summary>
    /// Decodes UTF-8 text, dropping a leading byte-order mark.
    /// </summary>
    private static string DecodeUtf8(byte[] content) {
        var text = Encoding.UTF8.GetString(content);

        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    /// <summary>
    /// Maps an OpenAI-style tool choice to Anthropic's "tool_choice": "" / "auto" -> omitted (the default),
    /// "none" -> none, "required" / "any" -> any, anything else is a tool name -> that tool.
    /// Returns null when the default applies. A choice other than auto needs tools in the request.
    /// </summary>
    internal static JsonObject? ConvertToolChoice(string? toolChoice, bool hasTools) {
        var choice = toolChoice?.Trim();
        if (string.IsNullOrEmpty(choice) || choice.Equals("auto", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        if (!hasTools) {
            throw new ArgumentException("ToolChoice requires ToolsJson to define at least one tool.");
        }

        if (choice.Equals("none", StringComparison.OrdinalIgnoreCase)) {
            return new JsonObject { ["type"] = "none" };
        }

        if (choice.Equals("required", StringComparison.OrdinalIgnoreCase) || choice.Equals("any", StringComparison.OrdinalIgnoreCase)) {
            return new JsonObject { ["type"] = "any" };
        }

        return new JsonObject { ["type"] = "tool", ["name"] = choice };
    }

    /// <summary>
    /// Converts an OpenAI "tools" array into Anthropic tool definitions. Returns null when there are none.
    /// </summary>
    internal static JsonArray? ConvertTools(string? toolsJson) {
        if (string.IsNullOrWhiteSpace(toolsJson)) {
            return null;
        }

        if (ParseJson(toolsJson, "ToolsJson") is not JsonArray openAiTools) {
            throw new ArgumentException("ToolsJson must be a JSON array.");
        }

        var tools = new JsonArray();
        for (var i = 0; i < openAiTools.Count; i++) {
            var path = $"ToolsJson[{i}]";

            // Accept {"type":"function","function":{...}} and the flat {...} form.
            if (openAiTools[i] is not JsonObject item) {
                throw new ArgumentException($"{path} must be a JSON object.");
            }

            var def = item["function"] switch {
                null => item,
                JsonObject fn => fn,
                _ => throw new ArgumentException($"{path}.function must be a JSON object.")
            };

            var name = ReadString(def, "name", path);
            if (string.IsNullOrEmpty(name)) {
                throw new ArgumentException($"{path}.name is required.");
            }

            var tool = new JsonObject { ["name"] = name };

            var description = ReadString(def, "description", path);
            if (!string.IsNullOrEmpty(description)) {
                tool["description"] = description;
            }

            var schema = def["parameters"] ?? def["input_schema"];
            tool["input_schema"] = schema?.DeepClone()
                ?? new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() };

            tools.Add(tool);
        }

        return tools.Count == 0 ? null : tools;
    }

    /// <summary>
    /// Converts an OpenAI "tool_calls" array into Anthropic tool_use blocks.
    /// </summary>
    private static IEnumerable<JsonObject> ParseToolCalls(string toolCallsJson) {
        if (ParseJson(toolCallsJson, "ToolCallsJson") is not JsonArray calls) {
            throw new ArgumentException("ToolCallsJson must be a JSON array.");
        }

        for (var i = 0; i < calls.Count; i++) {
            var path = $"ToolCallsJson[{i}]";

            if (calls[i] is not JsonObject call) {
                throw new ArgumentException($"{path} must be a JSON object.");
            }

            if (call["function"] is not JsonObject fn) {
                throw new ArgumentException($"{path}.function must be a JSON object.");
            }

            var name = ReadString(fn, "name", $"{path}.function");
            var id = ReadString(call, "id", path);
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id)) {
                throw new ArgumentException($"{path} must have an id and a function name.");
            }

            // OpenAI sends arguments as a JSON string; Anthropic wants the parsed object.
            var args = fn["arguments"];
            JsonNode input = args is JsonValue v && v.TryGetValue<string>(out var text)
                ? (string.IsNullOrWhiteSpace(text)
                    ? new JsonObject()
                    : ParseJson(text, $"{path}.function.arguments") ?? new JsonObject())
                : args?.DeepClone() ?? new JsonObject();

            yield return new JsonObject {
                ["type"] = "tool_use",
                ["id"] = id,
                ["name"] = name,
                ["input"] = input
            };
        }
    }

    /// <summary>
    /// Parses JSON input, reporting malformed JSON as an ArgumentException that names the input.
    /// </summary>
    private static JsonNode? ParseJson(string json, string inputName) {
        try {
            return JsonNode.Parse(json);
        } catch (JsonException ex) {
            throw new ArgumentException($"{inputName} is not valid JSON: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads an optional string property; throws ArgumentException when present with another type.
    /// </summary>
    private static string? ReadString(JsonObject obj, string property, string path) {
        if (obj[property] is not JsonNode node) {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text)) {
            return text;
        }

        throw new ArgumentException($"{path}.{property} must be a string.");
    }

    /// <summary>
    /// Collects the tool_use blocks of an Anthropic response as an OpenAI "tool_calls" JSON array.
    /// Returns an empty string when the response has none, or when it did not stop with
    /// stop_reason "tool_use" (for example a max_tokens cut-off, where the calls may be incomplete).
    /// </summary>
    internal static string ExtractAnthropicToolCalls(string responseBody) {
        using var doc = JsonDocument.Parse(responseBody);

        if (!doc.RootElement.TryGetProperty("stop_reason", out var stop)
            || stop.ValueKind != JsonValueKind.String
            || stop.GetString() != "tool_use") {
            return string.Empty;
        }

        if (!doc.RootElement.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array) {
            return string.Empty;
        }

        var calls = new JsonArray();
        foreach (var block in content.EnumerateArray()) {
            if (block.ValueKind != JsonValueKind.Object
                || !block.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || type.GetString() != "tool_use") {
                continue;
            }

            var id = ReadToolUseString(block, "id");
            var name = ReadToolUseString(block, "name");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) {
                throw new InvalidOperationException(
                    "Vertex AI (anthropic) returned a tool_use block without id/name.");
            }

            var input = block.TryGetProperty("input", out var i) ? i.GetRawText() : "{}";
            calls.Add(new JsonObject {
                ["id"] = id,
                ["type"] = "function",
                ["function"] = new JsonObject {
                    ["name"] = name,
                    ["arguments"] = input
                }
            });
        }

        return calls.Count == 0 ? string.Empty : calls.ToJsonString();
    }

    private static string? ReadToolUseString(JsonElement block, string property)
        => block.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Pulls the first text block from an Anthropic Messages response (empty when the reply only calls tools).
    /// </summary>
    internal static string ExtractAnthropicText(string responseBody) {
        using var doc = JsonDocument.Parse(responseBody);

        if (doc.RootElement.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.Array) {
            foreach (var block in content.EnumerateArray()) {
                if (block.TryGetProperty("text", out var text)) {
                    return text.GetString() ?? string.Empty;
                }
            }
        }

        var stopReason = doc.RootElement.TryGetProperty("stop_reason", out var reason)
            && reason.ValueKind == JsonValueKind.String
                ? reason.GetString()
                : "unknown";

        // A reply that only calls tools has no text block; that is valid.
        if (stopReason == "tool_use") {
            return string.Empty;
        }

        throw new InvalidOperationException(
            $"Vertex AI (anthropic) returned no text content (stop_reason: {stopReason}). " +
            "If stop_reason is 'max_tokens', the output budget ran out (thinking or a tool call) " +
            "before the model could answer — increase MaxOutputTokens.");
    }

    /// <summary>
    /// Reads the stop_reason of an Anthropic Messages response (for example end_turn or max_tokens).
    /// Returns an empty string when the field is missing.
    /// </summary>
    internal static string ExtractAnthropicStopReason(string responseBody) {
        using var doc = JsonDocument.Parse(responseBody);

        return doc.RootElement.TryGetProperty("stop_reason", out var reason)
            && reason.ValueKind == JsonValueKind.String
                ? reason.GetString() ?? string.Empty
                : string.Empty;
    }

    private static GoogleCredential BuildCredential(GCPServiceAccount account) {
        var keyData = new Dictionary<string, string> {
            ["type"] = "service_account",
            ["private_key_id"] = account.PrivateKeyId ?? string.Empty,
            ["private_key"] = account.PrivateKey,
            ["client_email"] = account.ClientEmail,
            ["token_uri"] = "https://oauth2.googleapis.com/token"
        };

        return GoogleCredential
            .FromJson(JsonSerializer.Serialize(keyData))
            .CreateScoped("https://www.googleapis.com/auth/cloud-platform");
    }

    /// <summary>
    /// Builds a PredictionServiceClient authenticated with a Service Account
    /// JSON key and pointed at the correct regional endpoint.
    /// </summary>
    private static PredictionServiceClient BuildClient(GCPServiceAccount account, string region) {
        var channelCredentials = BuildCredential(account).ToChannelCredentials();

        return new PredictionServiceClientBuilder {
            Endpoint = BuildEndpoint(region),
            ChannelCredentials = channelCredentials
        }.Build();
    }

    /// <summary>
    /// Resolves the Vertex AI host for a region. Multi-regions ("eu", "us") use the
    /// regional endpoint pattern (rep), which guarantees data residency; single regions
    /// use the {region}-aiplatform host. "global" uses the bare aiplatform host and
    /// offers no data-residency guarantee.
    /// </summary>
    internal static string BuildEndpoint(string region)
        => region is "eu" or "us"
            ? $"aiplatform.{region}.rep.googleapis.com"
            : region == "global"
                ? "aiplatform.googleapis.com"
                : $"{region}-aiplatform.googleapis.com";

    /// <summary>
    /// Composes the full Vertex AI model resource path.
    /// Format: projects/{project}/locations/{region}/publishers/{publisher}/models/{model}
    /// where {region} is a single region (e.g. europe-west4) a multi-region (eu, us) or "global".
    /// </summary>
    internal static string BuildModelPath(string projectId, string region, string publisher, string modelId)
        => $"projects/{projectId}/locations/{region}/publishers/{publisher}/models/{modelId}";

    /// <summary>
    /// Builds a GenerationConfig from the supplied parameters.
    /// Defaults are applied when sentinel values (0 / -1) are passed.
    /// </summary>
    private static GenerationConfig BuildGenerationConfig(int maxOutputTokens, decimal temperature) {
        var config = new GenerationConfig {
            MaxOutputTokens = ResolveMaxOutputTokens(maxOutputTokens)
        };

        if (temperature >= 0m) {
            config.Temperature = (float)temperature;
        } else {
            config.Temperature = 0.4f; // Vertex AI default
        }

        return config;
    }

    /// <summary>
    /// Returns the finish reason of the first candidate (for example Stop or MaxTokens),
    /// or an empty string when the response has no candidate.
    /// </summary>
    private static string ExtractGoogleFinishReason(GenerateContentResponse response)
        => response?.Candidates is { Count: > 0 } candidates
            ? candidates[0].FinishReason.ToString()
            : string.Empty;

    /// <summary>
    /// Normalises a chat role to "system", "user", "assistant" or "tool" (case-insensitive).
    /// </summary>
    private static string NormalizeRole(string? role) {
        var normalized = role?.Trim().ToLowerInvariant();

        return normalized is "system" or "user" or "assistant" or "tool"
            ? normalized
            : throw new ArgumentException(
                $"Chat role '{role}' is not supported. Use 'system', 'user', 'assistant' or 'tool'.");
    }

    /// <summary>
    /// Checks that a chat has at least one non-system message and that every role is valid.
    /// </summary>
    private static void ValidateMessages(IReadOnlyList<ChatMessage>? messages) {
        if (messages == null || messages.Count == 0) {
            throw new ArgumentException("Messages must contain at least one message.");
        }

        var hasTurn = false;
        foreach (var message in messages) {
            if (NormalizeRole(message.Role) != "system") {
                hasTurn = true;
            }
        }

        if (!hasTurn) {
            throw new ArgumentException("Messages must contain at least one non-system message (user, assistant or tool).");
        }
    }

    // A publisher added to _supportedPublishers without a dispatch arm fails loudly
    // instead of silently falling into the Gemini gRPC path.
    private static NotSupportedException UnhandledPublisher(string publisher)
        => new($"Publisher '{publisher}' has no transport implemented.");

    /// <summary>
    /// Throws an ArgumentException if the publisher is not in the supported list.
    /// </summary>
    private static void ValidatePublisher(string publisher) {
        if (!Array.Exists(_supportedPublishers, p => p == publisher)) {
            throw new ArgumentException(
                $"Publisher '{publisher}' is not supported. " +
                $"Call GetSupportedPublishers() to see the full list.");
        }
    }

    // ProjectId and ModelId are interpolated into the request path that receives the bearer
    // token, so they must not be able to contain '/', '?', '#' or '..' path tricks.
    private static readonly Regex _projectIdPattern = new(
        "^[a-z][a-z0-9-]{4,28}[a-z0-9]$", RegexOptions.CultureInvariant);
    private static readonly Regex _modelIdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._@-]{0,127}$", RegexOptions.CultureInvariant);

    private static void ValidateProjectId(string projectId) {
        if (projectId is null || !_projectIdPattern.IsMatch(projectId)) {
            throw new ArgumentException("ProjectId is not a valid Google Cloud project ID.");
        }
    }

    private static void ValidateModelId(string modelId) {
        if (modelId is null || !_modelIdPattern.IsMatch(modelId) || modelId.Contains("..")) {
            throw new ArgumentException(
                "ModelId contains invalid characters. Use letters, digits, '.', '_', '-' and '@' only.");
        }
    }

    /// <summary>
    /// Throws an ArgumentException if the region is not in the supported list.
    /// </summary>
    private static void ValidateRegion(string region) {
        if (!Array.Exists(_supportedRegions, r => r == region)) {
            throw new ArgumentException(
                $"Region '{region}' is not supported. " +
                $"Call GetSupportedRegions() to see the full list.");
        }
    }
}

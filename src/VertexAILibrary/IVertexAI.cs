using OutSystems.ExternalLibraries.SDK;

namespace VertexAILibrary
{
    /// <summary>
    /// ODC External Library interface for Google Vertex AI.
    /// Each method maps to a Server Action in ODC Studio.
    /// </summary>
    [OSInterface(
        Name = "VertexAI",
        Description = "Integrates OutSystems ODC apps with Google Cloud Vertex AI (Gemini and Anthropic Claude models). " +
                      "Supports region selection, prompt generation with optional file (image / PDF / text) inputs, and multi-turn chat.",
        IconResourceName = "VertexAILibrary.Resources.icon.png"
    )]
    public interface IVertexAI
    {
        // ─────────────────────────────────────────────────────────────
        // Core: Generate content (single prompt → text response)
        // ─────────────────────────────────────────────────────────────

        [OSAction(
            Description = "Sends a prompt to a Vertex AI model (google or anthropic publisher) and returns the generated text and any files the model returns. " +
                          "Use GetSupportedRegions to see valid region values.",
            ReturnName = "ResponseText"
        )]
        string GenerateContent(
            [OSParameter(Description = "Google Cloud Project ID (e.g. my-gcp-project).")]
            string ProjectId,

            [OSParameter(Description = "Google Cloud service account credentials. " +
                                       "Store each field in an ODC Secret – never hard-code private keys.")]
            GCPServiceAccount ServiceAccount,

            [OSParameter(Description = "GCP region that will handle the request " +
                                       "(e.g. us-central1, europe-west4, asia-northeast1), or the " +
                                       "multi-region 'eu' / 'us' (use 'eu' for mandatory EU data residency), or " +
                                       "'global' (no data-residency guarantee). " +
                                       "Call GetSupportedRegions for the full list.")]
            string Region,

            [OSParameter(Description = "Model publisher: 'google' (Gemini) or 'anthropic' (Claude). " +
                                       "Call GetSupportedPublishers for the full list.")]
            string Publisher,

            [OSParameter(Description = "Vertex AI model ID (e.g. gemini-2.0-flash-001 for google, claude-sonnet-4@20250514 for anthropic).")]
            string ModelId,

            [OSParameter(Description = "The prompt text to send to the model.")]
            string Prompt,

            [OSParameter(Description = "Maximum number of output tokens. Pass 0 to use the default (4096).")]
            int MaxOutputTokens,

            [OSParameter(Description = "Sampling temperature between 0 and 1. " +
                                       "Lower values are more deterministic. Pass -1 to use the default (0.4 for Google models; " +
                                       "not sent for Anthropic models, since newer Claude models reject it).")]
            decimal Temperature,

            [OSParameter(Description = "Token counts for the call (input, output, total, cache and thinking tokens).")]
            out TokenUsage Usage,

            [OSParameter(Description = "Files returned by the model (e.g. generated images). Empty when the model returns none; " +
                                       "only google models can return files.")]
            out List<OutputFile> OutputFiles
        );

        // ─────────────────────────────────────────────────────────────
        // Generate with system prompt (instruction + user prompt)
        // ─────────────────────────────────────────────────────────────

        [OSAction(
            Description = "Sends a system instruction together with a user prompt to Vertex AI. " +
                          "Useful for giving the model a persona or strict behavior rules.",
            ReturnName = "ResponseText"
        )]
        string GenerateContentWithSystemPrompt(
            [OSParameter(Description = "Google Cloud Project ID.")]
            string ProjectId,

            [OSParameter(Description = "Google Cloud service account credentials.")]
            GCPServiceAccount ServiceAccount,

            [OSParameter(Description = "GCP region that will handle the request (single region, multi-region 'eu' / 'us', or 'global' — which has no data-residency guarantee).")]
            string Region,

            [OSParameter(Description = "Model publisher: 'google' (Gemini) or 'anthropic' (Claude). " +
                                       "Call GetSupportedPublishers for the full list.")]
            string Publisher,

            [OSParameter(Description = "Vertex AI model ID.")]
            string ModelId,

            [OSParameter(Description = "System instruction that shapes the model's behavior " +
                                       "(e.g. 'You are a helpful assistant that replies only in Portuguese.').")]
            string SystemInstruction,

            [OSParameter(Description = "The user prompt text to send to the model.")]
            string UserPrompt,

            [OSParameter(Description = "Maximum number of output tokens. Pass 0 to use the default (4096).")]
            int MaxOutputTokens,

            [OSParameter(Description = "Token counts for the call (input, output, total, cache and thinking tokens).")]
            out TokenUsage Usage,

            [OSParameter(Description = "Files returned by the model (e.g. generated images). Empty when the model returns none; " +
                                       "only google models can return files.")]
            out List<OutputFile> OutputFiles
        );

        // ─────────────────────────────────────────────────────────────
        // Generate with files (prompt + binary files → text response)
        // ─────────────────────────────────────────────────────────────

        [OSAction(
            Description = "Sends a prompt plus one or more files (images, PDF, text; audio/video for google) " +
                          "to a Vertex AI model and returns the generated text and any files the model returns. " +
                          "The whole request (files + prompt) must stay under the ODC 5.5 MB payload limit.",
            ReturnName = "ResponseText"
        )]
        string GenerateContentWithFiles(
            [OSParameter(Description = "Google Cloud Project ID (e.g. my-gcp-project).")]
            string ProjectId,

            [OSParameter(Description = "Google Cloud service account credentials. " +
                                       "Store each field in an ODC Secret – never hard-code private keys.")]
            GCPServiceAccount ServiceAccount,

            [OSParameter(Description = "GCP region that will handle the request " +
                                       "(e.g. us-central1, europe-west4, asia-northeast1), or the " +
                                       "multi-region 'eu' / 'us' (use 'eu' for mandatory EU data residency), or " +
                                       "'global' (no data-residency guarantee). " +
                                       "Call GetSupportedRegions for the full list.")]
            string Region,

            [OSParameter(Description = "Model publisher: 'google' (Gemini) or 'anthropic' (Claude). " +
                                       "Call GetSupportedPublishers for the full list.")]
            string Publisher,

            [OSParameter(Description = "Vertex AI model ID (e.g. gemini-2.0-flash-001 for google, claude-sonnet-4@20250514 for anthropic).")]
            string ModelId,

            [OSParameter(Description = "The prompt text to send to the model. Required when sending files.")]
            string Prompt,

            [OSParameter(Description = "One or more files sent before the prompt, in list order. " +
                                       "Set MimeType or use a file name with a known extension. " +
                                       "Supported types depend on the publisher (see README).")]
            List<InputFile> Files,

            [OSParameter(Description = "Maximum number of output tokens. Pass 0 to use the default (4096).")]
            int MaxOutputTokens,

            [OSParameter(Description = "Sampling temperature between 0 and 1. " +
                                       "Lower values are more deterministic. Pass -1 to use the default (0.4 for Google models; " +
                                       "not sent for Anthropic models, since newer Claude models reject it).")]
            decimal Temperature,

            [OSParameter(Description = "Token counts for the call (input, output, total, cache and thinking tokens).")]
            out TokenUsage Usage,

            [OSParameter(Description = "Files returned by the model (e.g. generated images). Empty when the model returns none; " +
                                       "only google models can return files.")]
            out List<OutputFile> OutputFiles
        );

        [OSAction(
            Description = "Sends a system instruction, a user prompt and one or more files (images, PDF, text; " +
                          "audio/video for google) to Vertex AI and returns the generated text and any files the model returns. " +
                          "The whole request (files + prompts) must stay under the ODC 5.5 MB payload limit.",
            ReturnName = "ResponseText"
        )]
        string GenerateContentWithSystemPromptAndFiles(
            [OSParameter(Description = "Google Cloud Project ID.")]
            string ProjectId,

            [OSParameter(Description = "Google Cloud service account credentials.")]
            GCPServiceAccount ServiceAccount,

            [OSParameter(Description = "GCP region that will handle the request (single region, multi-region 'eu' / 'us', or 'global' — which has no data-residency guarantee).")]
            string Region,

            [OSParameter(Description = "Model publisher: 'google' (Gemini) or 'anthropic' (Claude). " +
                                       "Call GetSupportedPublishers for the full list.")]
            string Publisher,

            [OSParameter(Description = "Vertex AI model ID.")]
            string ModelId,

            [OSParameter(Description = "System instruction that shapes the model's behavior " +
                                       "(e.g. 'You are a helpful assistant that replies only in Portuguese.').")]
            string SystemInstruction,

            [OSParameter(Description = "The user prompt text to send to the model. Required when sending files.")]
            string UserPrompt,

            [OSParameter(Description = "One or more files sent before the prompt, in list order. " +
                                       "Set MimeType or use a file name with a known extension. " +
                                       "Supported types depend on the publisher (see README).")]
            List<InputFile> Files,

            [OSParameter(Description = "Maximum number of output tokens. Pass 0 to use the default (4096).")]
            int MaxOutputTokens,

            [OSParameter(Description = "Token counts for the call (input, output, total, cache and thinking tokens).")]
            out TokenUsage Usage,

            [OSParameter(Description = "Files returned by the model (e.g. generated images). Empty when the model returns none; " +
                                       "only google models can return files.")]
            out List<OutputFile> OutputFiles
        );

        // ─────────────────────────────────────────────────────────────
        // Chat (multi-turn conversation)
        // ─────────────────────────────────────────────────────────────

        [OSAction(
            Description = "Sends a multi-turn conversation to a Vertex AI model and returns the next assistant reply. " +
                          "Use 'system' messages for instructions, then alternating 'user' and 'assistant' messages. " +
                          "FinishReason is publisher-specific (e.g. Stop, MaxTokens for google; end_turn, max_tokens, tool_use for anthropic). " +
                          "Tool calling (anthropic only): pass the tool definitions in ToolsJson; when the model wants to call tools, " +
                          "FinishReason is 'tool_use' and ToolCallsJson holds the calls (it is empty for any other FinishReason, e.g. max_tokens: raise MaxOutputTokens). Run them, then call Chat again with the assistant " +
                          "message (ToolCallsJson) followed by one 'tool' message per call (ToolCallId + Content).",
            ReturnName = "ResponseText"
        )]
        string Chat(
            [OSParameter(Description = "Google Cloud Project ID.")]
            string ProjectId,

            [OSParameter(Description = "Google Cloud service account credentials.")]
            GCPServiceAccount ServiceAccount,

            [OSParameter(Description = "GCP region that will handle the request (single region, multi-region 'eu' / 'us', or 'global' — which has no data-residency guarantee).")]
            string Region,

            [OSParameter(Description = "Model publisher: 'google' (Gemini) or 'anthropic' (Claude). " +
                                       "Call GetSupportedPublishers for the full list.")]
            string Publisher,

            [OSParameter(Description = "Vertex AI model ID.")]
            string ModelId,

            [OSParameter(Description = "The conversation so far, oldest first. Each item has a Role ('system', 'user', 'assistant' or 'tool') and Content. " +
                                       "For tool calling (anthropic only), an 'assistant' item carries the calls it made in ToolCallsJson (its Content may be empty), " +
                                       "and each 'tool' item carries the result in Content and the id of the call it answers in ToolCallId.")]
            List<ChatMessage> Messages,

            [OSParameter(Description = "Maximum number of output tokens. Pass 0 to use the default (4096).")]
            int MaxOutputTokens,

            [OSParameter(Description = "Sampling temperature between 0 and 1 for Google models; pass -1 to use the default (0.4). " +
                                       "Ignored for Anthropic models (never sent, since newer Claude models reject it).")]
            decimal Temperature,

            [OSParameter(Description = "Token counts for the call (input, output, total, cache and thinking tokens).")]
            out TokenUsage Usage,

            [OSParameter(Description = "Why generation stopped, as reported by the publisher (e.g. Stop, MaxTokens, end_turn, max_tokens, tool_use).")]
            out string FinishReason,

            [OSParameter(Description = "Tool calls requested by the model, as an OpenAI 'tool_calls' JSON array " +
                                       "([{\"id\",\"type\":\"function\",\"function\":{\"name\",\"arguments\"}}]). Empty string when the model made no tool call.")]
            out string ToolCallsJson,

            [OSParameter(Description = "Tools the model may call, as an OpenAI 'tools' JSON array " +
                                       "([{\"type\":\"function\",\"function\":{\"name\",\"description\",\"parameters\"}}]). " +
                                       "Pass an empty string or [] for no tools. Tool calling is Anthropic publisher only.")]
            string ToolsJson = "",

            [OSParameter(Description = "Which tool the model may call: empty or 'auto' (model decides), 'none' (no tool calls), " +
                                       "'required' (must call some tool) or a tool name (must call that tool). " +
                                       "Anything other than empty/'auto' needs ToolsJson. Tool calling is Anthropic publisher only.")]
            string ToolChoice = ""
        );

        // ─────────────────────────────────────────────────────────────
        // Validate connectivity / credentials
        // ─────────────────────────────────────────────────────────────

        [OSAction(
            Description = "Tests the connection to Vertex AI with the supplied credentials and region. " +
                          "Returns 'OK' if successful or an error message if not.",
            ReturnName = "Status"
        )]
        string TestConnection(
            [OSParameter(Description = "Google Cloud Project ID.")]
            string ProjectId,

            [OSParameter(Description = "Google Cloud service account credentials.")]
            GCPServiceAccount ServiceAccount,

            [OSParameter(Description = "GCP region to test (e.g. us-central1, multi-region 'eu' / 'us', or 'global').")]
            string Region,

            [OSParameter(Description = "Model publisher: 'google' (Gemini) or 'anthropic' (Claude). " +
                                       "Call GetSupportedPublishers for the full list.")]
            string Publisher,

            [OSParameter(Description = "Vertex AI model ID to use in the test call.")]
            string ModelId
        );

        // ─────────────────────────────────────────────────────────────
        // Utility: list available regions
        // ─────────────────────────────────────────────────────────────

        [OSAction(
            Description = "Returns the list of GCP regions supported by this library. " +
                          "Use this to populate a region-selection dropdown in your ODC app. " +
                          "Note: 'global' does not guarantee data residency; use 'eu' when EU residency is required.",
            ReturnName = "Regions"
        )]
        List<string> GetSupportedRegions();

        [OSAction(
            Description = "Returns the list of model publishers supported by this library (e.g. google, anthropic).",
            ReturnName = "Publishers"
        )]
        List<string> GetSupportedPublishers();
    }
}

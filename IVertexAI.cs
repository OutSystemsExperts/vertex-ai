using OutSystems.ExternalLibraries.SDK;

namespace VertexAILibrary
{
    /// <summary>
    /// ODC External Library interface for Google Vertex AI.
    /// Each method maps to a Server Action in ODC Studio.
    /// </summary>
    [OSInterface(
        Name = "VertexAI",
        Description = "Integrates OutSystems ODC apps with Google Cloud Vertex AI (Gemini models). " +
                      "Supports region selection, prompt generation, and multi-turn chat.",
        IconResourceName = "VertexAILibrary.Resources.icon.png"
    )]
    public interface IVertexAI
    {
        // ─────────────────────────────────────────────────────────────
        // Core: Generate content (single prompt → text response)
        // ─────────────────────────────────────────────────────────────

        [OSAction(
            Description = "Sends a prompt to a Vertex AI Gemini model and returns the generated text. " +
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
                                       "(e.g. us-central1, europe-west4, asia-northeast1). " +
                                       "Call GetSupportedRegions for the full list.")]
            string Region,

            [OSParameter(Description = "Vertex AI model ID (e.g. gemini-2.0-flash-001, gemini-1.5-pro-002).")]
            string ModelId,

            [OSParameter(Description = "The prompt text to send to the model.")]
            string Prompt,

            [OSParameter(Description = "Maximum number of output tokens. Pass 0 to use the default (1024).")]
            int MaxOutputTokens,

            [OSParameter(Description = "Sampling temperature between 0 and 1. " +
                                       "Lower values are more deterministic. Pass -1 to use the default (0.4).")]
            decimal Temperature
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

            [OSParameter(Description = "GCP region that will handle the request.")]
            string Region,

            [OSParameter(Description = "Vertex AI model ID.")]
            string ModelId,

            [OSParameter(Description = "System instruction that shapes the model's behavior " +
                                       "(e.g. 'You are a helpful assistant that replies only in Portuguese.').")]
            string SystemInstruction,

            [OSParameter(Description = "The user prompt text to send to the model.")]
            string UserPrompt,

            [OSParameter(Description = "Maximum number of output tokens. Pass 0 to use the default (1024).")]
            int MaxOutputTokens
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

            [OSParameter(Description = "GCP region to test (e.g. us-central1).")]
            string Region,

            [OSParameter(Description = "Vertex AI model ID to use in the test call.")]
            string ModelId
        );

        // ─────────────────────────────────────────────────────────────
        // Utility: list available regions
        // ─────────────────────────────────────────────────────────────

        [OSAction(
            Description = "Returns the list of GCP regions supported by this library. " +
                          "Use this to populate a region-selection dropdown in your ODC app.",
            ReturnName = "Regions"
        )]
        List<string> GetSupportedRegions();
    }
}

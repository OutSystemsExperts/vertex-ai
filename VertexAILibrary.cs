using Google.Apis.Auth.OAuth2;
using Google.Cloud.AIPlatform.V1;
using Grpc.Auth;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VertexAILibrary
{
    /// <summary>
    /// Implementation of the IVertexAI OSInterface.
    /// ODC instantiates this class to execute each Server Action.
    /// </summary>
    public class VertexAILibrary : IVertexAI
    {
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
        private const string GooglePublisher = "google";
        private const string AnthropicPublisher = "anthropic";

        private static readonly string[] _supportedPublishers = { GooglePublisher, AnthropicPublisher };

        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(90) };

        // ─────────────────────────────────────────────────────────────
        // GetSupportedPublishers
        // ─────────────────────────────────────────────────────────────
        public List<string> GetSupportedPublishers()
        {
            return new List<string>(_supportedPublishers);
        }

        // ─────────────────────────────────────────────────────────────
        // GetSupportedRegions
        // ─────────────────────────────────────────────────────────────
        public List<string> GetSupportedRegions()
        {
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
            int    MaxOutputTokens,
            decimal Temperature,
            out TokenUsage Usage)
        {
            var (text, usage) = Generate(ProjectId, ServiceAccount, Region, Publisher, ModelId,
                null, Prompt, MaxOutputTokens, Temperature);

            Usage = usage;
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
            int    MaxOutputTokens,
            out TokenUsage Usage)
        {
            var (text, usage) = Generate(ProjectId, ServiceAccount, Region, Publisher, ModelId,
                SystemInstruction, UserPrompt, MaxOutputTokens, -1m);

            Usage = usage;
            return text;
        }

        // ─────────────────────────────────────────────────────────────
        // TestConnection
        // ─────────────────────────────────────────────────────────────
        public string TestConnection(
            string ProjectId,
            GCPServiceAccount ServiceAccount,
            string Region,
            string Publisher,
            string ModelId)
        {
            try
            {
                Generate(ProjectId, ServiceAccount, Region, Publisher, ModelId,
                    null, "Reply with the single word: OK", 16, -1m);

                return "OK";
            }
            catch (Exception ex)
            {
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
        /// </summary>
        private (string Text, TokenUsage Usage) Generate(
            string projectId,
            GCPServiceAccount account,
            string region,
            string publisher,
            string modelId,
            string? systemInstruction,
            string prompt,
            int maxOutputTokens,
            decimal temperature)
        {
            ValidateRegion(region);
            ValidatePublisher(publisher);

            return publisher == AnthropicPublisher
                ? GenerateAnthropic(projectId, account, region, modelId, systemInstruction, prompt, maxOutputTokens)
                : GenerateGoogle(projectId, account, region, modelId, systemInstruction, prompt, maxOutputTokens, temperature);
        }

        private static (string Text, TokenUsage Usage) GenerateGoogle(
            string projectId,
            GCPServiceAccount account,
            string region,
            string modelId,
            string? systemInstruction,
            string prompt,
            int maxOutputTokens,
            decimal temperature)
        {
            var client = BuildClient(account, region);

            var request = new GenerateContentRequest
            {
                Model            = BuildModelPath(projectId, region, GooglePublisher, modelId),
                GenerationConfig = BuildGenerationConfig(maxOutputTokens, temperature),
                Contents =
                {
                    new Content
                    {
                        Role  = "USER",
                        Parts = { new Part { Text = prompt } }
                    }
                }
            };

            if (!string.IsNullOrEmpty(systemInstruction))
                request.SystemInstruction = new Content { Parts = { new Part { Text = systemInstruction } } };

            var response = client.GenerateContent(request);
            var meta = response.UsageMetadata;
            var usage = meta == null
                ? default
                : new TokenUsage
                {
                    InputTokens  = meta.PromptTokenCount,
                    OutputTokens = meta.CandidatesTokenCount,
                    TotalTokens  = meta.TotalTokenCount,
                    // The SDK doesn't expose thoughtsTokenCount; thinking tokens are the remainder of the total.
                    ThinkingTokens = Math.Max(0, meta.TotalTokenCount - meta.PromptTokenCount - meta.CandidatesTokenCount)
                };

            return (ExtractText(response), usage);
        }

        private static (string Text, TokenUsage Usage) GenerateAnthropic(
            string projectId,
            GCPServiceAccount account,
            string region,
            string modelId,
            string? systemInstruction,
            string prompt,
            int maxOutputTokens)
        {
            var url = $"https://{BuildEndpoint(region)}/v1/" +
                      $"{BuildModelPath(projectId, region, AnthropicPublisher, modelId)}:rawPredict";

            var token = BuildCredential(account).UnderlyingCredential
                .GetAccessTokenForRequestAsync().GetAwaiter().GetResult();

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    BuildAnthropicRequestBody(systemInstruction, prompt, maxOutputTokens),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = _httpClient.SendAsync(request).GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Vertex AI (anthropic) returned {(int)response.StatusCode}: {body}");

            return (ExtractAnthropicText(body), ExtractAnthropicUsage(body));
        }

        /// <summary>
        /// Reads the token counts from an Anthropic Messages response; missing fields count as 0.
        /// </summary>
        internal static TokenUsage ExtractAnthropicUsage(string responseBody)
        {
            using var doc = JsonDocument.Parse(responseBody);

            if (!doc.RootElement.TryGetProperty("usage", out var usage)
                || usage.ValueKind != JsonValueKind.Object)
                return default;

            int Read(string name) =>
                usage.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

            var input         = Read("input_tokens");
            var output        = Read("output_tokens");
            var cacheCreation = Read("cache_creation_input_tokens");
            var cacheRead     = Read("cache_read_input_tokens");

            return new TokenUsage
            {
                InputTokens              = input,
                OutputTokens             = output,
                TotalTokens              = input + output + cacheCreation + cacheRead,
                CachedInputTokens        = cacheRead,
                CacheCreationInputTokens = cacheCreation
            };
        }

        /// <summary>
        /// Builds the Anthropic Messages request body expected by Vertex AI rawPredict.
        /// </summary>
        internal static string BuildAnthropicRequestBody(
            string? systemInstruction, string prompt, int maxOutputTokens)
        {
            var body = new JsonObject
            {
                ["anthropic_version"] = "vertex-2023-10-16",
                ["max_tokens"]        = maxOutputTokens > 0 ? maxOutputTokens : 1024,
                ["messages"]          = new JsonArray(new JsonObject
                {
                    ["role"]    = "user",
                    ["content"] = prompt
                })
            };

            if (!string.IsNullOrEmpty(systemInstruction))
                body["system"] = systemInstruction;

            return body.ToJsonString();
        }

        /// <summary>
        /// Pulls the first text block from an Anthropic Messages response.
        /// </summary>
        internal static string ExtractAnthropicText(string responseBody)
        {
            using var doc = JsonDocument.Parse(responseBody);

            if (doc.RootElement.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    if (block.TryGetProperty("text", out var text))
                        return text.GetString() ?? string.Empty;
                }
            }

            throw new InvalidOperationException("Vertex AI (anthropic) returned no text content.");
        }

        private static GoogleCredential BuildCredential(GCPServiceAccount account)
        {
            var keyData = new Dictionary<string, string>
            {
                ["type"]           = "service_account",
                ["private_key_id"] = account.PrivateKeyId ?? string.Empty,
                ["private_key"]    = account.PrivateKey,
                ["client_email"]   = account.ClientEmail,
                ["token_uri"]      = "https://oauth2.googleapis.com/token"
            };

            return GoogleCredential
                .FromJson(JsonSerializer.Serialize(keyData))
                .CreateScoped("https://www.googleapis.com/auth/cloud-platform");
        }

        /// <summary>
        /// Builds a PredictionServiceClient authenticated with a Service Account
        /// JSON key and pointed at the correct regional endpoint.
        /// </summary>
        private static PredictionServiceClient BuildClient(GCPServiceAccount account, string region)
        {
            var channelCredentials = BuildCredential(account).ToChannelCredentials();

            return new PredictionServiceClientBuilder
            {
                Endpoint           = BuildEndpoint(region),
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
        private static GenerationConfig BuildGenerationConfig(int maxOutputTokens, decimal temperature)
        {
            var config = new GenerationConfig
            {
                MaxOutputTokens = maxOutputTokens > 0 ? maxOutputTokens : 1024
            };

            if (temperature >= 0m)
                config.Temperature = (float)temperature;
            else
                config.Temperature = 0.4f; // Vertex AI default

            return config;
        }

        /// <summary>
        /// Pulls the first text part from the first candidate in a GenerateContentResponse.
        /// </summary>
        private static string ExtractText(GenerateContentResponse response)
        {
            if (response?.Candidates == null || response.Candidates.Count == 0)
                throw new InvalidOperationException("Vertex AI returned no candidates.");

            var candidate = response.Candidates[0];

            if (candidate.Content?.Parts == null || candidate.Content.Parts.Count == 0)
                throw new InvalidOperationException("Vertex AI candidate has no content parts.");

            return candidate.Content.Parts[0].Text ?? string.Empty;
        }

        /// <summary>
        /// Throws an ArgumentException if the publisher is not in the supported list.
        /// </summary>
        private static void ValidatePublisher(string publisher)
        {
            if (!Array.Exists(_supportedPublishers, p => p == publisher))
                throw new ArgumentException(
                    $"Publisher '{publisher}' is not supported. " +
                    $"Call GetSupportedPublishers() to see the full list.");
        }

        /// <summary>
        /// Throws an ArgumentException if the region is not in the supported list.
        /// </summary>
        private static void ValidateRegion(string region)
        {
            if (!Array.Exists(_supportedRegions, r => r == region))
                throw new ArgumentException(
                    $"Region '{region}' is not supported. " +
                    $"Call GetSupportedRegions() to see the full list.");
        }
    }
}

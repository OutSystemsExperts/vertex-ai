using Google.Apis.Auth.OAuth2;
using Google.Cloud.AIPlatform.V1;
using Google.Protobuf;
using Grpc.Auth;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
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
        internal const string GooglePublisher = "google";
        internal const string AnthropicPublisher = "anthropic";

        private static readonly string[] _supportedPublishers = { GooglePublisher, AnthropicPublisher };

        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(90) };

        private static readonly JsonSerializerOptions _relaxedJsonOptions =
            new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

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
            int MaxOutputTokens,
            decimal Temperature,
            out TokenUsage Usage,
            out List<OutputFile> OutputFiles)
        {
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
            out List<OutputFile> OutputFiles)
        {
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
            out List<OutputFile> OutputFiles)
        {
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
            out List<OutputFile> OutputFiles)
        {
            var (text, usage, outputFiles) = Generate(ProjectId, ServiceAccount, Region, Publisher, ModelId,
                SystemInstruction, UserPrompt, MaxOutputTokens, -1m, Files ?? new List<InputFile>());

            Usage = usage;
            OutputFiles = outputFiles;
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
            List<InputFile>? files = null)
        {
            ValidateRegion(region);
            ValidatePublisher(publisher);

            IReadOnlyList<ResolvedFile>? resolved = null;
            if (files != null)
            {
                InputFiles.ValidatePrompt(prompt);
                resolved = InputFiles.Resolve(files, publisher);
            }

            return publisher == AnthropicPublisher
                ? GenerateAnthropic(projectId, account, region, modelId, systemInstruction, prompt, maxOutputTokens, resolved)
                : GenerateGoogle(projectId, account, region, modelId, systemInstruction, prompt, maxOutputTokens, temperature, resolved);
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
            IReadOnlyList<ResolvedFile>? files)
        {
            var client = BuildClient(account, region);

            var request = BuildGoogleRequest(projectId, region, modelId, systemInstruction, prompt,
                maxOutputTokens, temperature, files);

            var response = client.GenerateContent(request);
            var meta = response.UsageMetadata;
            var usage = meta == null
                ? default
                : new TokenUsage
                {
                    InputTokens = meta.PromptTokenCount,
                    OutputTokens = meta.CandidatesTokenCount,
                    TotalTokens = meta.TotalTokenCount,
                    // The SDK doesn't expose thoughtsTokenCount; thinking tokens are the remainder of the total.
                    ThinkingTokens = Math.Max(0, meta.TotalTokenCount - meta.PromptTokenCount - meta.CandidatesTokenCount)
                };

            var (text, outputFiles) = GoogleOutput.Extract(response);

            return (text, usage, outputFiles);
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
            IReadOnlyList<ResolvedFile>? files = null)
        {
            var userContent = new Content { Role = "USER" };

            foreach (var file in files ?? Array.Empty<ResolvedFile>())
            {
                userContent.Parts.Add(new Part
                {
                    InlineData = new Blob { MimeType = file.MimeType, Data = ByteString.CopyFrom(file.Content) }
                });
            }

            userContent.Parts.Add(new Part { Text = prompt });

            var request = new GenerateContentRequest
            {
                Model = BuildModelPath(projectId, region, GooglePublisher, modelId),
                GenerationConfig = BuildGenerationConfig(maxOutputTokens, temperature),
                Contents = { userContent }
            };

            if (!string.IsNullOrEmpty(systemInstruction))
                request.SystemInstruction = new Content { Parts = { new Part { Text = systemInstruction } } };

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
            IReadOnlyList<ResolvedFile>? files)
        {
            var url = $"https://{BuildEndpoint(region)}/v1/" +
                      $"{BuildModelPath(projectId, region, AnthropicPublisher, modelId)}:rawPredict";

            var token = BuildCredential(account).UnderlyingCredential
                .GetAccessTokenForRequestAsync().GetAwaiter().GetResult();

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    BuildAnthropicRequestBody(systemInstruction, prompt, maxOutputTokens, files),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = _httpClient.SendAsync(request).GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Vertex AI (anthropic) returned {(int)response.StatusCode}: {body}");

            return (ExtractAnthropicText(body), ExtractAnthropicUsage(body), new List<OutputFile>());
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

            var input = Read("input_tokens");
            var output = Read("output_tokens");
            var cacheCreation = Read("cache_creation_input_tokens");
            var cacheRead = Read("cache_read_input_tokens");

            return new TokenUsage
            {
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
            IReadOnlyList<ResolvedFile>? files = null)
        {
            var hasFiles = files is { Count: > 0 };

            var body = new JsonObject
            {
                ["anthropic_version"] = "vertex-2023-10-16",
                ["max_tokens"] = maxOutputTokens > 0 ? maxOutputTokens : 1024,
                ["messages"] = new JsonArray(new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = hasFiles ? BuildAnthropicContentBlocks(prompt, files!) : JsonValue.Create(prompt)
                })
            };

            if (!string.IsNullOrEmpty(systemInstruction))
                body["system"] = systemInstruction;

            // The default encoder escapes '+' in base64 as \u002B; only relax it when files are sent
            // so text-only bodies stay byte-identical.
            return hasFiles ? body.ToJsonString(_relaxedJsonOptions) : body.ToJsonString();
        }

        private static JsonArray BuildAnthropicContentBlocks(string prompt, IReadOnlyList<ResolvedFile> files)
        {
            var blocks = new JsonArray();

            foreach (var file in files)
                blocks.Add(BuildAnthropicFileBlock(file));

            blocks.Add(new JsonObject { ["type"] = "text", ["text"] = prompt });

            return blocks;
        }

        /// <summary>
        /// Maps a file to an Anthropic content block: image/* → image (base64), application/pdf →
        /// document (base64), text/plain → document (text source, UTF-8 decoded).
        /// </summary>
        private static JsonObject BuildAnthropicFileBlock(ResolvedFile file)
        {
            if (file.MimeType == "text/plain")
                return BuildAnthropicBlock("document", "text", file, DecodeUtf8(file.Content));

            var type = file.MimeType.StartsWith("image/", StringComparison.Ordinal) ? "image" : "document";

            return BuildAnthropicBlock(type, "base64", file, Convert.ToBase64String(file.Content));
        }

        private static JsonObject BuildAnthropicBlock(string type, string sourceType, ResolvedFile file, string data)
        {
            var block = new JsonObject
            {
                ["type"] = type,
                ["source"] = new JsonObject
                {
                    ["type"] = sourceType,
                    ["media_type"] = file.MimeType,
                    ["data"] = data
                }
            };

            // Image blocks don't accept a title; document titles let the model refer to files by name.
            if (type == "document" && !string.IsNullOrWhiteSpace(file.FileName))
                block["title"] = file.FileName;

            return block;
        }

        /// <summary>
        /// Decodes UTF-8 text, dropping a leading byte-order mark.
        /// </summary>
        private static string DecodeUtf8(byte[] content)
        {
            var text = Encoding.UTF8.GetString(content);

            return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
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
        private static PredictionServiceClient BuildClient(GCPServiceAccount account, string region)
        {
            var channelCredentials = BuildCredential(account).ToChannelCredentials();

            return new PredictionServiceClientBuilder
            {
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

using Google.Apis.Auth.OAuth2;
using Google.Cloud.AIPlatform.V1;
using Grpc.Auth;
using System.Text.Json;

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
            string ModelId,
            string Prompt,
            int    MaxOutputTokens,
            decimal Temperature)
        {
            ValidateRegion(Region);

            var client = BuildClient(ServiceAccount, Region);

            var request = new GenerateContentRequest
            {
                Model            = BuildModelPath(ProjectId, Region, ModelId),
                GenerationConfig = BuildGenerationConfig(MaxOutputTokens, Temperature),
                Contents =
                {
                    new Content
                    {
                        Role  = "USER",
                        Parts = { new Part { Text = Prompt } }
                    }
                }
            };

            var response = client.GenerateContent(request);
            return ExtractText(response);
        }

        // ─────────────────────────────────────────────────────────────
        // GenerateContentWithSystemPrompt
        // ─────────────────────────────────────────────────────────────
        public string GenerateContentWithSystemPrompt(
            string ProjectId,
            GCPServiceAccount ServiceAccount,
            string Region,
            string ModelId,
            string SystemInstruction,
            string UserPrompt,
            int    MaxOutputTokens)
        {
            ValidateRegion(Region);

            var client = BuildClient(ServiceAccount, Region);

            var request = new GenerateContentRequest
            {
                Model            = BuildModelPath(ProjectId, Region, ModelId),
                GenerationConfig = BuildGenerationConfig(MaxOutputTokens, -1m),
                SystemInstruction = new Content
                {
                    Parts = { new Part { Text = SystemInstruction } }
                },
                Contents =
                {
                    new Content
                    {
                        Role  = "USER",
                        Parts = { new Part { Text = UserPrompt } }
                    }
                }
            };

            var response = client.GenerateContent(request);
            return ExtractText(response);
        }

        // ─────────────────────────────────────────────────────────────
        // TestConnection
        // ─────────────────────────────────────────────────────────────
        public string TestConnection(
            string ProjectId,
            GCPServiceAccount ServiceAccount,
            string Region,
            string ModelId)
        {
            try
            {
                ValidateRegion(Region);

                var client = BuildClient(ServiceAccount, Region);

                var request = new GenerateContentRequest
                {
                    Model            = BuildModelPath(ProjectId, Region, ModelId),
                    GenerationConfig = BuildGenerationConfig(16, -1m),
                    Contents =
                    {
                        new Content
                        {
                            Role  = "USER",
                            Parts = { new Part { Text = "Reply with the single word: OK" } }
                        }
                    }
                };

                client.GenerateContent(request);

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
        /// Builds a PredictionServiceClient authenticated with a Service Account
        /// JSON key and pointed at the correct regional endpoint.
        /// </summary>
        private static PredictionServiceClient BuildClient(GCPServiceAccount account, string region)
        {
            var keyData = new Dictionary<string, string>
            {
                ["type"]           = "service_account",
                ["private_key_id"] = account.PrivateKeyId ?? string.Empty,
                ["private_key"]    = account.PrivateKey,
                ["client_email"]   = account.ClientEmail,
                ["token_uri"]      = "https://oauth2.googleapis.com/token"
            };

            var credential = GoogleCredential
                .FromJson(JsonSerializer.Serialize(keyData))
                .CreateScoped("https://www.googleapis.com/auth/cloud-platform");

            var channelCredentials = credential.ToChannelCredentials();

            // Each region has its own Vertex AI endpoint
            var endpoint = $"{region}-aiplatform.googleapis.com";

            return new PredictionServiceClientBuilder
            {
                Endpoint           = endpoint,
                ChannelCredentials = channelCredentials
            }.Build();
        }

        /// <summary>
        /// Composes the full Vertex AI model resource path.
        /// Format: projects/{project}/locations/{region}/publishers/google/models/{model}
        /// </summary>
        private static string BuildModelPath(string projectId, string region, string modelId)
            => $"projects/{projectId}/locations/{region}/publishers/google/models/{modelId}";

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
        /// Throws an ArgumentException if the region is not in the supported list.
        /// </summary>
        private static void ValidateRegion(string region)
        {            if (!Array.Exists(_supportedRegions, r => r == region))
                throw new ArgumentException(
                    $"Region '{region}' is not supported. " +
                    $"Call GetSupportedRegions() to see the full list.");
        }
    }
}

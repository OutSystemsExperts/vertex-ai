using Google.Cloud.AIPlatform.V1;

namespace VertexAILibrary
{
    /// <summary>
    /// Extracts the text and the files from a Gemini response.
    /// </summary>
    internal static class GoogleOutput
    {
        // Keeps the response under the ODC 5.5 MB payload limit (inputs + outputs combined).
        internal const int MaxTotalBytes = 4 * 1024 * 1024;

        private static readonly Dictionary<string, string> _extensionsByMimeType = new(StringComparer.Ordinal)
        {
            ["image/png"] = ".png",
            ["image/jpeg"] = ".jpg",
            ["image/webp"] = ".webp",
            ["image/gif"] = ".gif",
            ["image/heic"] = ".heic",
            ["image/heif"] = ".heif",
            ["application/pdf"] = ".pdf",
            ["text/plain"] = ".txt",
            ["audio/wav"] = ".wav",
            ["audio/mpeg"] = ".mp3",
            ["audio/mp3"] = ".mp3",
            ["audio/ogg"] = ".ogg",
            ["audio/flac"] = ".flac",
            ["audio/aac"] = ".aac",
            ["video/mp4"] = ".mp4",
            ["video/webm"] = ".webm"
        };

        internal static string FileNameFor(int index, string mimeType)
        {
            var normalized = (mimeType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
            var extension = _extensionsByMimeType.TryGetValue(normalized, out var ext) ? ext : ".bin";

            return $"output-{index}{extension}";
        }

        /// <summary>
        /// Concatenates the text parts of the first candidate and collects its inline
        /// data parts as files. Throws when the response has neither text nor files.
        /// </summary>
        internal static (string Text, List<OutputFile> Files) Extract(GenerateContentResponse response)
        {
            if (response?.Candidates == null || response.Candidates.Count == 0)
                throw new InvalidOperationException("Vertex AI returned no candidates.");

            var candidate = response.Candidates[0];

            if (candidate.Content?.Parts == null || candidate.Content.Parts.Count == 0)
                throw new InvalidOperationException(
                    $"Vertex AI candidate has no content parts (finish reason: {candidate.FinishReason}). " +
                    "If the finish reason is MaxTokens, the model's thinking consumed the output budget " +
                    "before it could answer — increase MaxOutputTokens.");

            var text = new System.Text.StringBuilder();
            var files = new List<OutputFile>();
            long totalBytes = 0;

            foreach (var part in candidate.Content.Parts)
            {
                if (part.InlineData != null && part.InlineData.Data.Length > 0)
                {
                    totalBytes += part.InlineData.Data.Length;
                    if (totalBytes > MaxTotalBytes)
                        throw new InvalidOperationException(
                            "The files returned by the model exceed the 4 MB limit that fits the ODC 5.5 MB payload limit.");

                    files.Add(new OutputFile
                    {
                        FileName = FileNameFor(files.Count + 1, part.InlineData.MimeType),
                        MimeType = part.InlineData.MimeType,
                        Content = part.InlineData.Data.ToByteArray()
                    });
                }
                else if (!string.IsNullOrEmpty(part.Text))
                {
                    text.Append(part.Text);
                }
            }

            return (text.ToString(), files);
        }
    }
}

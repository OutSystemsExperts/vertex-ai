namespace VertexAILibrary;

/// <summary>
/// An input file whose content was validated and whose MIME type was resolved and normalised.
/// </summary>
internal readonly record struct ResolvedFile(string FileName, string MimeType, byte[] Content);

/// <summary>
/// Validates the files sent with a prompt and resolves their MIME types per publisher.
/// </summary>
internal static class InputFiles {
    // ─────────────────────────────────────────────────────────────
    // MIME types accepted by each publisher (normalised, lower-case)
    // ─────────────────────────────────────────────────────────────
    private static readonly HashSet<string> _googleMimeTypes = new(StringComparer.Ordinal)
    {
        "image/png", "image/jpeg", "image/webp", "image/heic", "image/heif",
        "application/pdf",
        "text/plain",
        "audio/wav", "audio/mp3", "audio/mpeg", "audio/aiff", "audio/aac", "audio/ogg", "audio/flac",
        "video/mp4", "video/mpeg", "video/mpg", "video/mpegps", "video/mov", "video/quicktime",
        "video/avi", "video/x-flv", "video/flv", "video/webm", "video/wmv", "video/3gpp"
    };

    private static readonly HashSet<string> _anthropicMimeTypes = new(StringComparer.Ordinal)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp",
        "application/pdf",
        "text/plain"
    };

    // ─────────────────────────────────────────────────────────────
    // File extension → MIME type (publisher-independent)
    // ─────────────────────────────────────────────────────────────
    private static readonly Dictionary<string, string> _mimeTypesByExtension = new(StringComparer.OrdinalIgnoreCase) {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".heic"] = "image/heic",
        [".heif"] = "image/heif",
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".aiff"] = "audio/aiff",
        [".aac"] = "audio/aac",
        [".ogg"] = "audio/ogg",
        [".flac"] = "audio/flac",
        [".mp4"] = "video/mp4",
        [".mpeg"] = "video/mpeg",
        [".mpg"] = "video/mpeg",
        [".mov"] = "video/mov",
        [".avi"] = "video/avi",
        [".flv"] = "video/flv",
        [".webm"] = "video/webm",
        [".wmv"] = "video/wmv",
        [".3gp"] = "video/3gpp"
    };

    /// <summary>
    /// Validates every file and resolves its MIME type against the publisher's allow-list,
    /// preserving the list order. Throws an ArgumentException on the first invalid file.
    /// </summary>
    internal static IReadOnlyList<ResolvedFile> Resolve(IReadOnlyList<InputFile>? files, string publisher) {
        if (files == null || files.Count == 0) {
            throw new ArgumentException("At least one file is required.");
        }

        var resolved = new List<ResolvedFile>(files.Count);
        for (var i = 0; i < files.Count; i++) {
            resolved.Add(ResolveOne(files[i], i, publisher));
        }

        return resolved;
    }

    /// <summary>
    /// Throws an ArgumentException if the prompt is null, empty or whitespace-only.
    /// Applies only when files are sent: the text part must always be present.
    /// </summary>
    internal static void ValidatePrompt(string? prompt) {
        if (string.IsNullOrWhiteSpace(prompt)) {
            throw new ArgumentException("Prompt is required when sending files.");
        }
    }

    /// <summary>
    /// Trims the MIME type, drops any parameters (";charset=…"), lower-cases it and maps the
    /// non-standard alias image/jpg to image/jpeg. Returns "" for null or blank input.
    /// </summary>
    internal static string NormalizeMimeType(string? mimeType) {
        if (string.IsNullOrWhiteSpace(mimeType)) {
            return string.Empty;
        }

        var separator = mimeType.IndexOf(';');
        var normalized = (separator >= 0 ? mimeType[..separator] : mimeType).Trim().ToLowerInvariant();

        return normalized == "image/jpg" ? "image/jpeg" : normalized;
    }

    /// <summary>
    /// Infers the MIME type from the file name extension; null when missing or unknown.
    /// </summary>
    internal static string? InferMimeType(string? fileName) {
        if (string.IsNullOrWhiteSpace(fileName)) {
            return null;
        }

        var extension = Path.GetExtension(fileName.Trim());

        return _mimeTypesByExtension.TryGetValue(extension, out var mimeType) ? mimeType : null;
    }

    private static ResolvedFile ResolveOne(InputFile file, int index, string publisher) {
        var label = Label(file, index);

        if (file.Content == null || file.Content.Length == 0) {
            throw new ArgumentException($"File {label} has no content.");
        }

        var mimeType = NormalizeMimeType(file.MimeType);
        if (mimeType.Length == 0) {
            mimeType = InferMimeType(file.FileName) ?? string.Empty;
        }

        if (mimeType.Length == 0) {
            throw new ArgumentException(
                $"Could not determine the MIME type of file {label}. " +
                $"Set MimeType or use a file name with a known extension.");
        }

        if (!AllowedMimeTypes(publisher).Contains(mimeType)) {
            throw new ArgumentException(
                $"MIME type {mimeType} is not supported by publisher '{publisher}' (file {label}).");
        }

        return new ResolvedFile(file.FileName ?? string.Empty, mimeType, file.Content);
    }

    private static HashSet<string> AllowedMimeTypes(string publisher)
        => publisher == VertexAILibrary.AnthropicPublisher ? _anthropicMimeTypes : _googleMimeTypes;

    /// <summary>
    /// Identifies a file in error messages by its name, or by its 0-based list index when the
    /// name is empty. Never includes file content.
    /// </summary>
    private static string Label(InputFile file, int index)
        => string.IsNullOrWhiteSpace(file.FileName) ? $"#{index}" : $"'{file.FileName}'";
}

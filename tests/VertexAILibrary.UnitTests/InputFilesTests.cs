using Xunit;

namespace VertexAILibrary.UnitTests; 
public class InputFilesTests {
    private const string Google = "google";
    private const string Anthropic = "anthropic";

    private static readonly byte[] SomeBytes = { 1, 2, 3 };

    private static InputFile File(string fileName, string mimeType = "", byte[]? content = null)
        => new() { FileName = fileName, MimeType = mimeType, Content = content ?? SomeBytes };

    // ─────────────────────────────────────────────────────────────
    // List and content validation (AC3, AC4)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_NullList_Throws() {
        var ex = Assert.Throws<ArgumentException>(() => InputFiles.Resolve(null, Google));
        Assert.Equal("At least one file is required.", ex.Message);
    }

    [Fact]
    public void Resolve_EmptyList_Throws() {
        var ex = Assert.Throws<ArgumentException>(() => InputFiles.Resolve(new List<InputFile>(), Google));
        Assert.Equal("At least one file is required.", ex.Message);
    }

    [Fact]
    public void Resolve_NullContent_ThrowsNamingFile() {
        var file = new InputFile { FileName = "report.pdf", MimeType = "", Content = null! };

        var ex = Assert.Throws<ArgumentException>(() => InputFiles.Resolve(new[] { file }, Google));
        Assert.Equal("File 'report.pdf' has no content.", ex.Message);
    }

    [Fact]
    public void Resolve_ZeroLengthContent_ThrowsNamingFile() {
        var files = new[] { File("report.pdf", content: Array.Empty<byte>()) };

        var ex = Assert.Throws<ArgumentException>(() => InputFiles.Resolve(files, Google));
        Assert.Contains("'report.pdf'", ex.Message);
    }

    [Fact]
    public void Resolve_EmptyContentWithoutFileName_ThrowsNamingIndex() {
        var files = new[] { File("a.png"), File("", "image/png", Array.Empty<byte>()) };

        var ex = Assert.Throws<ArgumentException>(() => InputFiles.Resolve(files, Google));
        Assert.Equal("File #1 has no content.", ex.Message);
    }

    // ─────────────────────────────────────────────────────────────
    // Prompt validation (D5)
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidatePrompt_MissingPrompt_Throws(string? prompt) {
        var ex = Assert.Throws<ArgumentException>(() => InputFiles.ValidatePrompt(prompt));
        Assert.Equal("Prompt is required when sending files.", ex.Message);
    }

    [Fact]
    public void ValidatePrompt_NonEmptyPrompt_DoesNotThrow() {
        InputFiles.ValidatePrompt("x");
    }

    // ─────────────────────────────────────────────────────────────
    // MIME inference (AC5)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_EmptyMimeTypeWithKnownExtension_InfersMimeType() {
        var resolved = InputFiles.Resolve(new[] { File("report.pdf") }, Anthropic);

        Assert.Equal("application/pdf", resolved[0].MimeType);
    }

    [Theory]
    [InlineData("notes")]
    [InlineData("data.xyz")]
    [InlineData("")]
    public void Resolve_EmptyMimeTypeWithUnknownExtension_Throws(string fileName) {
        var ex = Assert.Throws<ArgumentException>(() => InputFiles.Resolve(new[] { File(fileName) }, Google));
        Assert.StartsWith("Could not determine the MIME type of file", ex.Message);
    }

    [Theory]
    [InlineData("photo.JPG", "image/jpeg")]
    [InlineData("clip.mpg", "video/mpeg")]
    [InlineData("song.mp3", "audio/mpeg")]
    [InlineData("notes", null)]
    [InlineData(null, null)]
    public void InferMimeType_MapsExtensions(string? fileName, string? expected) {
        Assert.Equal(expected, InputFiles.InferMimeType(fileName));
    }

    [Fact]
    public void Resolve_ExplicitMimeType_WinsOverExtension() {
        var resolved = InputFiles.Resolve(new[] { File("scan.pdf", "image/png") }, Google);

        Assert.Equal("image/png", resolved[0].MimeType);
    }

    // ─────────────────────────────────────────────────────────────
    // MIME normalisation (AC6)
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("image/PNG; x=y", "image/png")]
    [InlineData(" Application/PDF ", "application/pdf")]
    [InlineData("image/jpg", "image/jpeg")]
    [InlineData("text/plain;charset=utf-8", "text/plain")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void NormalizeMimeType_NormalisesValue(string? mimeType, string expected) {
        Assert.Equal(expected, InputFiles.NormalizeMimeType(mimeType));
    }

    [Fact]
    public void Resolve_MixedCaseMimeTypeWithParameters_IsAccepted() {
        var resolved = InputFiles.Resolve(new[] { File("x", "image/PNG; x=y") }, Anthropic);

        Assert.Equal("image/png", resolved[0].MimeType);
    }

    // ─────────────────────────────────────────────────────────────
    // Per-publisher allow-lists (AC9)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_AudioForAnthropic_ThrowsNamingPublisherAndMimeType() {
        var ex = Assert.Throws<ArgumentException>(
            () => InputFiles.Resolve(new[] { File("song.mp3", "audio/mpeg") }, Anthropic));

        Assert.Contains("audio/mpeg", ex.Message);
        Assert.Contains("'anthropic'", ex.Message);
        Assert.Contains("'song.mp3'", ex.Message);
    }

    [Fact]
    public void Resolve_Gif_AcceptedByAnthropic() {
        var resolved = InputFiles.Resolve(new[] { File("anim.gif") }, Anthropic);

        Assert.Equal("image/gif", resolved[0].MimeType);
    }

    [Fact]
    public void Resolve_Gif_RejectedByGoogle() {
        var ex = Assert.Throws<ArgumentException>(() => InputFiles.Resolve(new[] { File("anim.gif") }, Google));

        Assert.Equal("MIME type image/gif is not supported by publisher 'google' (file 'anim.gif').", ex.Message);
    }

    [Fact]
    public void Resolve_Mp3_AcceptedByGoogle() {
        var resolved = InputFiles.Resolve(new[] { File("song.mp3") }, Google);

        Assert.Equal("audio/mpeg", resolved[0].MimeType);
    }

    [Fact]
    public void Resolve_ErrorMessages_NeverContainFileContent() {
        var content = System.Text.Encoding.UTF8.GetBytes("SECRET-CONTENT");

        var ex = Assert.Throws<ArgumentException>(
            () => InputFiles.Resolve(new[] { File("song.mp3", "audio/mpeg", content) }, Anthropic));

        Assert.DoesNotContain("SECRET-CONTENT", ex.Message);
    }

    // ─────────────────────────────────────────────────────────────
    // Ordering and pass-through
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_PreservesOrderNameAndContent() {
        var png = new byte[] { 0x89, 0x50 };
        var pdf = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var files = new[] { File("b.pdf", content: pdf), File("a.png", content: png) };

        var resolved = InputFiles.Resolve(files, Google);

        Assert.Equal(2, resolved.Count);
        Assert.Equal(new ResolvedFile("b.pdf", "application/pdf", pdf), resolved[0]);
        Assert.Equal(new ResolvedFile("a.png", "image/png", png), resolved[1]);
    }
}

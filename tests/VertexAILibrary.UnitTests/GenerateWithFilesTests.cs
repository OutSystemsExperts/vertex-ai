using Xunit;
using Sut = VertexAILibrary.VertexAILibrary;

namespace VertexAILibrary.UnitTests; 
/// <summary>
/// Exercises the public file actions. Every case must fail validation before credentials are
/// built, so a dummy service account is enough and no network call is made.
/// </summary>
public class GenerateWithFilesTests {
    private const string ValidRegion = "europe-west4";

    private static readonly GCPServiceAccount DummyAccount = new() {
        ClientEmail = "dummy@example.iam.gserviceaccount.com",
        PrivateKey = "not-a-key",
        PrivateKeyId = "dummy"
    };

    private static readonly List<InputFile> OnePdf = new()
    {
        new InputFile { FileName = "invoice.pdf", MimeType = "", Content = new byte[] { 0x25, 0x50 } }
    };

    private delegate void FileAction(string region, string publisher, string prompt, List<InputFile> files);

    public static TheoryData<string> BothActions => new() { "WithFiles", "WithSystemPromptAndFiles" };

    private static FileAction ActionFor(string name)
        => name == "WithFiles"
            ? (region, publisher, prompt, files) => new Sut().GenerateContentWithFiles(
                "test-project", DummyAccount, region, publisher, "m", prompt, files, 0, -1m, out _, out _)
            : (region, publisher, prompt, files) => new Sut().GenerateContentWithSystemPromptAndFiles(
                "test-project", DummyAccount, region, publisher, "m", "sys", prompt, files, 0, out _, out _);

    [Theory]
    [MemberData(nameof(BothActions))]
    public void InvalidRegion_FailsBeforeFileValidation(string action) {
        var ex = Assert.Throws<ArgumentException>(
            () => ActionFor(action)("mars-north1", "google", "", new List<InputFile>()));

        Assert.StartsWith("Region 'mars-north1' is not supported.", ex.Message);
    }

    [Theory]
    [MemberData(nameof(BothActions))]
    public void InvalidPublisher_FailsBeforeFileValidation(string action) {
        var ex = Assert.Throws<ArgumentException>(
            () => ActionFor(action)(ValidRegion, "openai", "", new List<InputFile>()));

        Assert.StartsWith("Publisher 'openai' is not supported.", ex.Message);
    }

    [Theory]
    [MemberData(nameof(BothActions))]
    public void EmptyFileList_Throws(string action) {
        var ex = Assert.Throws<ArgumentException>(
            () => ActionFor(action)(ValidRegion, "anthropic", "Summarise", new List<InputFile>()));

        Assert.Equal("At least one file is required.", ex.Message);
    }

    [Theory]
    [MemberData(nameof(BothActions))]
    public void NullFileList_Throws(string action) {
        var ex = Assert.Throws<ArgumentException>(
            () => ActionFor(action)(ValidRegion, "google", "Summarise", null!));

        Assert.Equal("At least one file is required.", ex.Message);
    }

    [Theory]
    [InlineData("WithFiles", "")]
    [InlineData("WithFiles", "   ")]
    [InlineData("WithFiles", null)]
    [InlineData("WithSystemPromptAndFiles", "")]
    [InlineData("WithSystemPromptAndFiles", "   ")]
    [InlineData("WithSystemPromptAndFiles", null)]
    public void MissingPrompt_Throws(string action, string? prompt) {
        var ex = Assert.Throws<ArgumentException>(
            () => ActionFor(action)(ValidRegion, "google", prompt!, OnePdf));

        Assert.Equal("Prompt is required when sending files.", ex.Message);
    }

    [Theory]
    [MemberData(nameof(BothActions))]
    public void UnsupportedMimeTypeForPublisher_Throws(string action) {
        var files = new List<InputFile>
        {
            new() { FileName = "song.mp3", MimeType = "audio/mpeg", Content = new byte[] { 1 } }
        };

        var ex = Assert.Throws<ArgumentException>(
            () => ActionFor(action)(ValidRegion, "anthropic", "Transcribe", files));

        Assert.Contains("MIME type audio/mpeg is not supported by publisher 'anthropic'", ex.Message);
    }
}

using System.Text;
using System.Text.Json;
using Xunit;
using Sut = VertexAILibrary.VertexAILibrary;

namespace VertexAILibrary.UnitTests; 
public class AnthropicRequestBodyTests {
    // Bytes whose base64 contains '+' and '/' (0xFB 0xEF 0xBE → "++++").
    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0xFB, 0xEF, 0xBE, 0x47 };
    private static readonly byte[] PdfBytes = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray();

    private static readonly ResolvedFile[] ThreeFiles =
    {
        new("photo.png", "image/png", PngBytes),
        new("invoice.pdf", "application/pdf", PdfBytes),
        new("notes.txt", "text/plain", Encoding.UTF8.GetBytes("Olá mundo"))
    };

    // ─────────────────────────────────────────────────────────────
    // Text-only regression (FR10)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TextOnly_IsByteIdenticalToToday() {
        Assert.Equal(
            "{\"anthropic_version\":\"vertex-2023-10-16\",\"max_tokens\":4096,\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}",
            Sut.BuildAnthropicRequestBody(null, "hi", 0));
    }

    [Fact]
    public void TextOnlyWithSystem_IsByteIdenticalToToday() {
        Assert.Equal(
            "{\"anthropic_version\":\"vertex-2023-10-16\",\"max_tokens\":4096,\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}],\"system\":\"s\"}",
            Sut.BuildAnthropicRequestBody("s", "hi", 0));
    }

    [Fact]
    public void EmptyFileList_IsByteIdenticalToTextOnly() {
        Assert.Equal(
            Sut.BuildAnthropicRequestBody(null, "a+b", 0),
            Sut.BuildAnthropicRequestBody(null, "a+b", 0, Array.Empty<ResolvedFile>()));
    }

    // ─────────────────────────────────────────────────────────────
    // Content blocks (AC8)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void WithFiles_EmitsBlocksInOrderThenText() {
        var blocks = ContentBlocks(Sut.BuildAnthropicRequestBody(null, "Summarise", 0, ThreeFiles));

        Assert.Equal(4, blocks.Length);
        AssertBlock(blocks[0], "image", "base64", "image/png");
        AssertBlock(blocks[1], "document", "base64", "application/pdf");
        AssertBlock(blocks[2], "document", "text", "text/plain");
        Assert.Equal("text", blocks[3].GetProperty("type").GetString());
        Assert.Equal("Summarise", blocks[3].GetProperty("text").GetString());
    }

    [Fact]
    public void WithFiles_DocumentBlocksHaveTitleAndImageBlocksDoNot() {
        var blocks = ContentBlocks(Sut.BuildAnthropicRequestBody(null, "x", 0, ThreeFiles));

        Assert.False(blocks[0].TryGetProperty("title", out _));
        Assert.Equal("invoice.pdf", blocks[1].GetProperty("title").GetString());
        Assert.Equal("notes.txt", blocks[2].GetProperty("title").GetString());
    }

    [Fact]
    public void WithFiles_Base64RoundTripsWithoutLineBreaksOrEscapes() {
        var json = Sut.BuildAnthropicRequestBody(null, "x", 0, ThreeFiles);
        var blocks = ContentBlocks(json);

        var pngData = blocks[0].GetProperty("source").GetProperty("data").GetString()!;
        var pdfData = blocks[1].GetProperty("source").GetProperty("data").GetString()!;

        Assert.Equal(PngBytes, Convert.FromBase64String(pngData));
        Assert.Equal(PdfBytes, Convert.FromBase64String(pdfData));
        Assert.Contains("+", pngData);
        Assert.DoesNotContain("\\u002B", json);

        foreach (var data in new[] { pngData, pdfData }) {
            Assert.DoesNotContain("\n", data);
            Assert.DoesNotContain("\r", data);
        }
    }

    [Fact]
    public void WithFiles_TextFileIsDecodedAsUtf8() {
        var blocks = ContentBlocks(Sut.BuildAnthropicRequestBody(null, "x", 0, ThreeFiles));

        Assert.Equal("Olá mundo", blocks[2].GetProperty("source").GetProperty("data").GetString());
    }

    [Fact]
    public void WithFiles_TextFileBomIsStripped() {
        var bomText = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("hello")).ToArray();
        var files = new[] { new ResolvedFile("bom.txt", "text/plain", bomText) };

        var blocks = ContentBlocks(Sut.BuildAnthropicRequestBody(null, "x", 0, files));

        Assert.Equal("hello", blocks[0].GetProperty("source").GetProperty("data").GetString());
    }

    [Fact]
    public void WithFiles_PromptIsCarriedVerbatim() {
        const string prompt = "Extract \"total\" <as JSON> & check a+b";

        var blocks = ContentBlocks(Sut.BuildAnthropicRequestBody(null, prompt, 0, ThreeFiles));

        Assert.Equal(prompt, blocks[^1].GetProperty("text").GetString());
    }

    [Fact]
    public void WithFiles_SystemAndMaxTokensHandledAsToday() {
        using var doc = JsonDocument.Parse(Sut.BuildAnthropicRequestBody("be brief", "x", 200, ThreeFiles));
        var root = doc.RootElement;

        Assert.Equal("vertex-2023-10-16", root.GetProperty("anthropic_version").GetString());
        Assert.Equal(200, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal("be brief", root.GetProperty("system").GetString());
        Assert.Equal("user", root.GetProperty("messages")[0].GetProperty("role").GetString());
    }

    [Fact]
    public void WithFilesAndNoSystem_OmitsSystem() {
        using var doc = JsonDocument.Parse(Sut.BuildAnthropicRequestBody("", "x", 0, ThreeFiles));

        Assert.False(doc.RootElement.TryGetProperty("system", out _));
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    private static JsonElement[] ContentBlocks(string json) {
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");

        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        return content.EnumerateArray().Select(e => e.Clone()).ToArray();
    }

    private static void AssertBlock(JsonElement block, string type, string sourceType, string mediaType) {
        Assert.Equal(type, block.GetProperty("type").GetString());

        var source = block.GetProperty("source");
        Assert.Equal(sourceType, source.GetProperty("type").GetString());
        Assert.Equal(mediaType, source.GetProperty("media_type").GetString());
    }
}

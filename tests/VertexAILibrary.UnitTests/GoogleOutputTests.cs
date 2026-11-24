using Google.Cloud.AIPlatform.V1;
using Google.Protobuf;
using Xunit;

namespace VertexAILibrary.UnitTests; 
public class GoogleOutputTests {
    private static readonly string[] _expectedFileNames = { "output-1.png", "output-2.pdf" };

    private static Part Text(string text) => new() { Text = text };

    private static Part Blob(string mimeType, params byte[] data)
        => new() { InlineData = new Blob { MimeType = mimeType, Data = ByteString.CopyFrom(data) } };

    private static GenerateContentResponse Response(params Part[] parts) {
        var content = new Content { Role = "MODEL" };
        content.Parts.AddRange(parts);

        return new GenerateContentResponse { Candidates = { new Candidate { Content = content } } };
    }

    [Fact]
    public void TextOnly_ReturnsTextAndNoFiles() {
        var (text, files) = GoogleOutput.Extract(Response(Text("hello")));

        Assert.Equal("hello", text);
        Assert.Empty(files);
    }

    [Fact]
    public void TextAndInlineData_ReturnsBoth() {
        var (text, files) = GoogleOutput.Extract(Response(Text("here"), Blob("image/png", 1, 2, 3)));

        Assert.Equal("here", text);
        var file = Assert.Single(files);
        Assert.Equal("image/png", file.MimeType);
        Assert.Equal("output-1.png", file.FileName);
        Assert.Equal(new byte[] { 1, 2, 3 }, file.Content);
    }

    [Fact]
    public void InlineDataOnly_ReturnsEmptyTextAndFile() {
        var (text, files) = GoogleOutput.Extract(Response(Blob("image/jpeg", 9)));

        Assert.Equal(string.Empty, text);
        Assert.Equal("output-1.jpg", Assert.Single(files).FileName);
    }

    [Fact]
    public void MultipleFiles_KeepOrderAndAreNumbered() {
        var (_, files) = GoogleOutput.Extract(
            Response(Blob("image/png", 1), Text("x"), Blob("application/pdf", 2)));

        Assert.Equal(_expectedFileNames, files.Select(f => f.FileName));
        Assert.Equal(new byte[] { 2 }, files[1].Content);
    }

    [Fact]
    public void MultipleTextParts_AreConcatenated() {
        var (text, _) = GoogleOutput.Extract(Response(Text("a"), Text("b")));

        Assert.Equal("ab", text);
    }

    [Theory]
    [InlineData("image/png", "output-3.png")]
    [InlineData("IMAGE/PNG; charset=x", "output-3.png")]
    [InlineData("application/x-unknown", "output-3.bin")]
    [InlineData("", "output-3.bin")]
    public void FileNameFor_UsesMimeTypeExtension(string mimeType, string expected)
        => Assert.Equal(expected, GoogleOutput.FileNameFor(3, mimeType));

    [Fact]
    public void NoCandidates_Throws()
        => Assert.Throws<InvalidOperationException>(() => GoogleOutput.Extract(new GenerateContentResponse()));

    [Fact]
    public void NoParts_Throws()
        => Assert.Throws<InvalidOperationException>(() => GoogleOutput.Extract(Response()));

    [Fact]
    public void FilesOverLimit_Throw() {
        var big = new byte[GoogleOutput.MaxTotalBytes + 1];

        Assert.Throws<InvalidOperationException>(() => GoogleOutput.Extract(Response(Blob("image/png", big))));
    }
}

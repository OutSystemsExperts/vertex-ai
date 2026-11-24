using Google.Cloud.AIPlatform.V1;
using Xunit;
using Sut = VertexAILibrary.VertexAILibrary;

namespace VertexAILibrary.UnitTests
{
    public class GoogleRequestTests
    {
        private static GenerateContentRequest Build(
            string? systemInstruction = null,
            string prompt = "Describe the files",
            IReadOnlyList<ResolvedFile>? files = null)
            => Sut.BuildGoogleRequest("my-project", "europe-west4", "gemini-2.0-flash-001",
                systemInstruction, prompt, 0, -1m, files);

        [Fact]
        public void TextOnly_KeepsTodaysShape()
        {
            var request = Build(prompt: "hi");

            Assert.Equal("projects/my-project/locations/europe-west4/publishers/google/models/gemini-2.0-flash-001",
                request.Model);
            Assert.Equal(4096, request.GenerationConfig.MaxOutputTokens);
            Assert.Equal(0.4f, request.GenerationConfig.Temperature);

            var content = Assert.Single(request.Contents);
            Assert.Equal("USER", content.Role);
            var part = Assert.Single(content.Parts);
            Assert.Equal(Part.DataOneofCase.Text, part.DataCase);
            Assert.Equal("hi", part.Text);
            Assert.Null(request.SystemInstruction);
        }

        [Fact]
        public void WithFiles_AddsInlineDataPartsInOrderBeforeText()
        {
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
            var pdf = new byte[] { 0x25, 0x50, 0x44, 0x46 };
            var files = new[]
            {
                new ResolvedFile("a.png", "image/png", png),
                new ResolvedFile("b.pdf", "application/pdf", pdf)
            };

            var request = Build(prompt: "Compare them", files: files);

            var parts = Assert.Single(request.Contents).Parts;
            Assert.Equal(3, parts.Count);

            Assert.Equal("image/png", parts[0].InlineData.MimeType);
            Assert.Equal(png, parts[0].InlineData.Data.ToByteArray());

            Assert.Equal("application/pdf", parts[1].InlineData.MimeType);
            Assert.Equal(pdf, parts[1].InlineData.Data.ToByteArray());

            Assert.Equal(Part.DataOneofCase.Text, parts[2].DataCase);
            Assert.Equal("Compare them", parts[2].Text);
        }

        [Fact]
        public void WithFiles_SystemInstructionHasOnlyText()
        {
            var files = new[] { new ResolvedFile("a.png", "image/png", new byte[] { 1 }) };

            var request = Build(systemInstruction: "You are a catalog writer", files: files);

            var part = Assert.Single(request.SystemInstruction.Parts);
            Assert.Equal("You are a catalog writer", part.Text);
        }

        [Fact]
        public void EmptySystemInstruction_IsNotSent()
        {
            var files = new[] { new ResolvedFile("a.png", "image/png", new byte[] { 1 }) };

            Assert.Null(Build(systemInstruction: "", files: files).SystemInstruction);
        }
    }
}

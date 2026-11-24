using OutSystems.ExternalLibraries.SDK;

namespace VertexAILibrary; 
/// <summary>
/// A binary file returned by the model together with its text response.
/// </summary>
[OSStructure(Description = "A file returned by the model (e.g. a generated image).")]
public struct OutputFile {
    [OSStructureField(Description = "Generated file name (output-1.png, output-2.pdf, ...), derived from the MIME type.")]
    public string FileName { get; set; }

    [OSStructureField(Description = "MIME type of the file (e.g. image/png).")]
    public string MimeType { get; set; }

    [OSStructureField(Description = "File content.")]
    public byte[] Content { get; set; }
}

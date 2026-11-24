using OutSystems.ExternalLibraries.SDK;

namespace VertexAILibrary;

/// <summary>
/// A binary file sent to the model together with the prompt.
/// </summary>
[OSStructure(Description = "A file sent to the model together with the prompt.")]
public struct InputFile {
    [OSStructureField(Description = "File name, used to infer the MIME type when MimeType is empty.")]
    public string FileName { get; set; }

    [OSStructureField(Description = "MIME type (e.g. application/pdf, image/png). Optional if FileName has a known extension.")]
    public string MimeType { get; set; }

    [OSStructureField(Description = "File content.", IsMandatory = true)]
    public byte[] Content { get; set; }
}

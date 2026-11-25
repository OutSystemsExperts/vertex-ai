using System.Reflection;
using OutSystems.ExternalLibraries.SDK;
using Xunit;

namespace VertexAILibrary.UnitTests;
/// <summary>
/// Guards the ODC action surface: existing actions must not change and new elements must follow
/// the External Libraries SDK naming rules.
/// </summary>
public class InterfaceContractTests {
    private static readonly string[] _outputFileFields = { "FileName", "MimeType", "Content" };
    private static readonly string[] _chatMessageFields = { "Role", "Content", "ToolCallId", "ToolCallsJson" };
    private static readonly Type[] _outputFileFieldTypes = { typeof(string), typeof(string), typeof(byte[]) };

    private static readonly Dictionary<string, string[]> ExpectedSignatures = new() {
        ["GenerateContent"] = new[]
        {
            "ProjectId:String", "ServiceAccount:GCPServiceAccount", "Region:String", "Publisher:String",
            "ModelId:String", "Prompt:String", "MaxOutputTokens:Int32", "Temperature:Decimal",
            "Usage:out TokenUsage", "OutputFiles:out List<OutputFile>"
        },
        ["GenerateContentWithSystemPrompt"] = new[]
        {
            "ProjectId:String", "ServiceAccount:GCPServiceAccount", "Region:String", "Publisher:String",
            "ModelId:String", "SystemInstruction:String", "UserPrompt:String", "MaxOutputTokens:Int32",
            "Usage:out TokenUsage", "OutputFiles:out List<OutputFile>"
        },
        ["Chat"] = new[]
        {
            "ProjectId:String", "ServiceAccount:GCPServiceAccount", "Region:String", "Publisher:String",
            "ModelId:String", "Messages:List<ChatMessage>", "MaxOutputTokens:Int32", "Temperature:Decimal",
            "Usage:out TokenUsage", "FinishReason:out String", "ToolCallsJson:out String",
            "ToolsJson:String", "ToolChoice:String"
        },
        ["TestConnection"] = new[]
        {
            "ProjectId:String", "ServiceAccount:GCPServiceAccount", "Region:String", "Publisher:String",
            "ModelId:String"
        },
        ["GetSupportedRegions"] = Array.Empty<string>(),
        ["GetSupportedPublishers"] = Array.Empty<string>(),
        ["StringifyJsonField"] = new[] { "Key:String", "Json:String" },
        ["NormalizeBody"] = new[] { "RequestText:String", "IsValid:out Boolean", "ErrorMessage:out String" },
        ["GenerateContentWithFiles"] = new[]
        {
            "ProjectId:String", "ServiceAccount:GCPServiceAccount", "Region:String", "Publisher:String",
            "ModelId:String", "Prompt:String", "Files:List<InputFile>", "MaxOutputTokens:Int32",
            "Temperature:Decimal", "Usage:out TokenUsage", "OutputFiles:out List<OutputFile>"
        },
        ["GenerateContentWithSystemPromptAndFiles"] = new[]
        {
            "ProjectId:String", "ServiceAccount:GCPServiceAccount", "Region:String", "Publisher:String",
            "ModelId:String", "SystemInstruction:String", "UserPrompt:String", "Files:List<InputFile>",
            "MaxOutputTokens:Int32", "Usage:out TokenUsage", "OutputFiles:out List<OutputFile>"
        }
    };

    private static readonly MethodInfo[] Actions = typeof(IVertexAI).GetMethods();

    [Fact]
    public void Interface_ExposesExactlyTheExpectedActions() {
        Assert.Equal(
            ExpectedSignatures.Keys.OrderBy(n => n),
            Actions.Select(m => m.Name).OrderBy(n => n));
    }

    [Theory]
    [InlineData("GenerateContent")]
    [InlineData("GenerateContentWithSystemPrompt")]
    [InlineData("Chat")]
    [InlineData("TestConnection")]
    [InlineData("GetSupportedRegions")]
    [InlineData("GetSupportedPublishers")]
    [InlineData("GenerateContentWithFiles")]
    [InlineData("GenerateContentWithSystemPromptAndFiles")]
    public void Action_HasExpectedParameters(string actionName) {
        var method = Assert.Single(Actions, m => m.Name == actionName);

        Assert.Equal(ExpectedSignatures[actionName], method.GetParameters().Select(Describe));
    }

    [Theory]
    [InlineData("GenerateContentWithFiles")]
    [InlineData("GenerateContentWithSystemPromptAndFiles")]
    public void NewAction_IsDocumented(string actionName) {
        var method = Assert.Single(Actions, m => m.Name == actionName);

        var action = method.GetCustomAttribute<OSActionAttribute>();
        Assert.NotNull(action);
        Assert.Contains("5.5 MB", action!.Description);
        Assert.Equal("ResponseText", action.ReturnName);

        foreach (var parameter in method.GetParameters()) {
            Assert.False(string.IsNullOrWhiteSpace(parameter.GetCustomAttribute<OSParameterAttribute>()?.Description),
                $"{actionName}.{parameter.Name} has no description");
        }
    }

    [Fact]
    public void Names_FollowOdcRules() {
        var actionNames = Actions.Select(m => m.Name).ToList();
        AssertValidNames(actionNames);

        foreach (var method in Actions) {
            AssertValidNames(method.GetParameters().Select(p => p.Name!).ToList());
        }
    }

    [Fact]
    public void InputFile_IsPublicNonNestedOdcStructure() {
        var type = typeof(InputFile);

        Assert.True(type.IsValueType);
        Assert.True(type.IsPublic);
        Assert.False(type.IsNested);
        Assert.NotNull(type.GetCustomAttribute<OSStructureAttribute>());

        var fields = type.GetProperties()
            .Select(p => (p.Name, p.PropertyType, Attr: p.GetCustomAttribute<OSStructureFieldAttribute>()))
            .ToList();

        Assert.Equal(_outputFileFields, fields.Select(f => f.Name));
        Assert.All(fields, f => Assert.NotNull(f.Attr));
        Assert.Equal(_outputFileFieldTypes, fields.Select(f => f.PropertyType));
        Assert.True(fields.Single(f => f.Name == "Content").Attr!.IsMandatory);
    }

    [Fact]
    public void ChatMessage_IsPublicNonNestedOdcStructure() {
        var type = typeof(ChatMessage);

        Assert.True(type.IsValueType);
        Assert.True(type.IsPublic);
        Assert.False(type.IsNested);
        Assert.NotNull(type.GetCustomAttribute<OSStructureAttribute>());

        var fields = type.GetProperties()
            .Select(p => (p.Name, p.PropertyType, Attr: p.GetCustomAttribute<OSStructureFieldAttribute>()))
            .ToList();

        Assert.Equal(_chatMessageFields, fields.Select(f => f.Name));
        Assert.All(fields, f => Assert.NotNull(f.Attr));
        Assert.All(fields, f => Assert.Equal(typeof(string), f.PropertyType));
        Assert.All(fields, f => Assert.False(string.IsNullOrWhiteSpace(f.Attr!.Description), $"{f.Name} has no description"));
    }

    [Fact]
    public void Chat_ToolInputs_AreOptionalWithEmptyDefault() {
        var parameters = Assert.Single(Actions, m => m.Name == "Chat").GetParameters();

        foreach (var name in new[] { "ToolsJson", "ToolChoice" }) {
            var parameter = parameters.Single(p => p.Name == name);
            Assert.True(parameter.IsOptional, $"{name} must be optional");
            Assert.Equal(string.Empty, parameter.DefaultValue);
        }
    }

    [Fact]
    public void Chat_IsDocumented() {
        var method = Assert.Single(Actions, m => m.Name == "Chat");

        var action = method.GetCustomAttribute<OSActionAttribute>();
        Assert.NotNull(action);
        Assert.Equal("ResponseText", action!.ReturnName);
        Assert.Contains("tool_use", action.Description);
        Assert.Contains("ToolCallsJson", action.Description);

        foreach (var parameter in method.GetParameters()) {
            Assert.False(string.IsNullOrWhiteSpace(parameter.GetCustomAttribute<OSParameterAttribute>()?.Description),
                $"Chat.{parameter.Name} has no description");
        }

        var messages = method.GetParameters().Single(p => p.Name == "Messages")
            .GetCustomAttribute<OSParameterAttribute>()!.Description;
        Assert.Contains("'tool'", messages);
        Assert.Contains("ToolCallId", messages);
        Assert.Contains("ToolCallsJson", messages);

        var temperature = method.GetParameters().Single(p => p.Name == "Temperature")
            .GetCustomAttribute<OSParameterAttribute>()!.Description;
        Assert.Contains("Ignored for Anthropic", temperature);
    }

    [Fact]
    public void OutputFile_IsPublicNonNestedOdcStructure() {
        var type = typeof(OutputFile);

        Assert.True(type.IsValueType);
        Assert.True(type.IsPublic);
        Assert.False(type.IsNested);
        Assert.NotNull(type.GetCustomAttribute<OSStructureAttribute>());

        var fields = type.GetProperties()
            .Select(p => (p.Name, p.PropertyType, Attr: p.GetCustomAttribute<OSStructureFieldAttribute>()))
            .ToList();

        Assert.Equal(_outputFileFields, fields.Select(f => f.Name));
        Assert.All(fields, f => Assert.NotNull(f.Attr));
        Assert.Equal(_outputFileFieldTypes, fields.Select(f => f.PropertyType));
    }

    private static void AssertValidNames(IReadOnlyCollection<string> names) {
        Assert.All(names, n => Assert.True(n.Length <= 50, $"'{n}' is longer than 50 characters"));
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private static string Describe(ParameterInfo parameter) {
        var type = parameter.ParameterType;
        var prefix = parameter.IsOut ? "out " : string.Empty;

        if (type.IsByRef) {
            type = type.GetElementType()!;
        }

        var typeName = type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(",", type.GetGenericArguments().Select(a => a.Name))}>"
            : type.Name;

        return $"{parameter.Name}:{prefix}{typeName}";
    }
}

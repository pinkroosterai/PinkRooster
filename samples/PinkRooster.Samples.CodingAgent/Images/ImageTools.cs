using Microsoft.Extensions.AI;
using PinkRooster.Samples.CodingAgent.ModelSettings;
using PinkRooster.ToolCollections.BuiltIn;
using PinkRooster.ToolCollections.BuiltIn.Images;

namespace PinkRooster.Samples.CodingAgent.Images;

/// <summary>
/// The image tool: the assistant asks a model that accepts images about a screenshot, a diagram or a mock-up in the workspace,
/// and gets text back. The assistant's own model does not have to see, and no image enters its conversation.
/// </summary>
public static class ImageTools
{
    /// <summary>How long one look at an image may take before the assistant is told it failed.</summary>
    public static readonly TimeSpan CallLimit = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The model that looks at images: the assistant's own when the settings mark it <c>"vision": true</c>, otherwise the first
    /// model that is marked. Null when none is, and then the assistant gets no image tool.
    /// </summary>
    public static ModelEntry? PickModel(IReadOnlyList<ModelEntry> models, ModelEntry assistantModel) =>
        assistantModel.Vision ? assistantModel : models.FirstOrDefault(model => model.Vision);

    /// <param name="workspace">The assistant's workspace; image paths are held to it as file paths are.</param>
    /// <param name="visionClient">The client of the model that looks at the images.</param>
    public static ImageToolCollection Create(Workspace workspace, IChatClient visionClient) =>
        new ImageToolCollectionBuilder()
            .InWorkspace(workspace)
            .WithVisionClient(visionClient)
            .WithTimeout(CallLimit)
            .Build();
}

using Microsoft.Extensions.AI;

namespace PinkRooster.ToolCollections.BuiltIn.Images;

/// <summary>Fluent builder for an <see cref="ImageToolCollection"/>: the workspace, the vision client and the limits, then <see cref="Build"/>.</summary>
/// <remarks>The builder is mutable: every method changes it and returns it. The workspace and the vision client are required.</remarks>
/// <example>
/// <code>
/// ImageToolCollection images = new ImageToolCollectionBuilder()
///     .InWorkspace(workspace)
///     .WithVisionClient(visionClient)
///     .WithMaxImagesPerCall(2)
///     .WithTimeout(TimeSpan.FromSeconds(60))
///     .Build();
/// </code>
/// </example>
public sealed class ImageToolCollectionBuilder
{
    private Workspace? workspace;
    private IChatClient? visionClient;
    private int maxImageBytes = ImageToolCollection.DefaultMaxImageBytes;
    private int maxImagesPerCall = ImageToolCollection.DefaultMaxImagesPerCall;
    private TimeSpan? timeout;

    /// <summary>Sets the workspace whose image files the tool may reach.</summary>
    public ImageToolCollectionBuilder InWorkspace(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        this.workspace = workspace;
        return this;
    }

    /// <summary>Sets the chat client of a model that accepts images. Set its model, answer length and other options on the client itself.</summary>
    public ImageToolCollectionBuilder WithVisionClient(IChatClient visionClient)
    {
        ArgumentNullException.ThrowIfNull(visionClient);
        this.visionClient = visionClient;
        return this;
    }

    /// <summary>
    /// Sets the largest image file, in bytes, that is sent. An image goes into the request as it is, about a third larger once
    /// encoded, so set this to what your provider takes. Without it the limit is <see cref="ImageToolCollection.DefaultMaxImageBytes"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxImageBytes"/> is below 1.</exception>
    public ImageToolCollectionBuilder WithMaxImageBytes(int maxImageBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxImageBytes, 1);
        this.maxImageBytes = maxImageBytes;
        return this;
    }

    /// <summary>Sets the most images one <c>QueryImage</c> call carries. Without it the limit is <see cref="ImageToolCollection.DefaultMaxImagesPerCall"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxImagesPerCall"/> is below 1.</exception>
    public ImageToolCollectionBuilder WithMaxImagesPerCall(int maxImagesPerCall)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxImagesPerCall, 1);
        this.maxImagesPerCall = maxImagesPerCall;
        return this;
    }

    /// <summary>
    /// Sets how long one vision call may take before the tool gives up and tells the model so. Without it there is no limit
    /// beyond the run's own cancellation and the client's.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is zero or negative.</exception>
    public ImageToolCollectionBuilder WithTimeout(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        this.timeout = timeout;
        return this;
    }

    /// <summary>Builds a new collection from the builder's current settings. Can be called more than once; each collection is independent.</summary>
    /// <exception cref="InvalidOperationException">No workspace or no vision client was set; the message names the call.</exception>
    public ImageToolCollection Build() =>
        new(
            workspace ?? throw new InvalidOperationException("An image collection needs a workspace; call InWorkspace(new Workspace(rootDirectory)) before Build()."),
            visionClient ?? throw new InvalidOperationException("An image collection needs a vision client; call WithVisionClient(chatClient) with the client of a model that accepts images before Build()."),
            maxImageBytes,
            maxImagesPerCall,
            timeout);
}

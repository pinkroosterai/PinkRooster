# PinkRooster.Samples.Imaging

An image tool: an agent asks a vision model about image files, and gets text back.

It shows `ImageToolCollection` from `PinkRooster.ToolCollections.BuiltIn` on a `Workspace`, built with `ImageToolCollectionBuilder` (`WithMaxImagesPerCall`, `WithTimeout`). Its one tool, `QueryImage`, sends one or more image files and a question to the vision client and returns its answer as text. The vision client is the host's own object, so the sample wraps it in a `DelegatingChatClient` that counts the calls and their tokens.

## Try it

Run it and the model is asked about the two images in the sample's `images` folder: to quote the error in `build-error.png`, a screenshot of a failed build, and to say what `sales-chart.png` shows.

- `-> QueryImage` lines show each call. Every one is one request to the vision model.
- The last line counts the vision calls and their tokens. The images went there and never into the agent's own conversation: what came back is text.
- A model may ask about both images in one call; they are then labelled `Image 1` and `Image 2` for the vision model.

Try it: lower `WithMaxImagesPerCall` to 1 and ask it to compare the two images in one call, to see the error the model gets and how it reacts; point the question at a file that is not an image and read what the tool answers.

## Run

From the repository root, with a model configured in `samples/PinkRooster.Samples/models.json` (the first run creates it for a local
Ollama; `models.example.json` next to it shows Groq, LM Studio and OpenAI entries):

```text
dotnet run --project samples/PinkRooster.Samples.Imaging
```

**Needs:** a model that supports tool calls and accepts images. With a model that cannot see, `QueryImage` returns the provider's error to the model

**Guide:** [The built-in tools guide: ImageToolCollection](../../docs/PinkRooster.ToolCollections.BuiltIn.md#imagetoolcollection)

The tests run the same `Program.RunAsync` against a scripted model, with no key and no network:
`dotnet test --project tests/PinkRooster.Samples.Tests`.

Previous sample: [BackgroundTools](../PinkRooster.Samples.BackgroundTools/README.md).

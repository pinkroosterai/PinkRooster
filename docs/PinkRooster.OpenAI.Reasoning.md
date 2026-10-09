# PinkRooster.OpenAI.Reasoning

Brings back the model's reasoning when you talk to an OpenAI-compatible server through `Microsoft.Extensions.AI.OpenAI`.

```text
dotnet add package PinkRooster.OpenAI.Reasoning --prerelease
```

Needs the .NET 10 SDK and an OpenAI-compatible server, such as Ollama with the model pulled (`ollama pull gpt-oss:20b` for the example below). The package brings `Microsoft.Extensions.AI.OpenAI` with it.

Ollama and Groq send a reasoning model's thoughts in a `reasoning` field. The OpenAI adapter reads only `reasoning_content`, so
the thoughts are dropped. `ReasoningFieldChatClient` wraps any `IChatClient` and adds the field back as `TextReasoningContent`, in
both full and streamed responses. It adds nothing to a response that already carries reasoning, so an adapter that reads the
field itself is not doubled.

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using PinkRooster.OpenAI.Reasoning;

IChatClient chatClient = new ReasoningFieldChatClient(
    new OpenAIClient(new ApiKeyCredential("ollama"), new OpenAIClientOptions { Endpoint = new Uri("http://localhost:11434/v1") })
        .GetChatClient("gpt-oss:20b")
        .AsIChatClient());
```

Read the reasoning from a response (`chatClient` is the client above):

```csharp
using Microsoft.Extensions.AI;

ChatResponse response = await chatClient.GetResponseAsync("A bat and a ball cost $1.10 together. The bat costs $1 more than the ball. What does the ball cost?");
foreach (TextReasoningContent thought in response.Messages.SelectMany(message => message.Contents).OfType<TextReasoningContent>())
{
    Console.WriteLine(thought.Text);
}
```

With [`PinkRooster.Agents`](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.Agents.md), the reasoning arrives as
`ReasoningDelta` and `ReasoningCompleted` events. The package depends only on `Microsoft.Extensions.AI.OpenAI`.
It reads the SDK's experimental `JsonPatch` (SCME0001), which is the SDK's only way to read a field it has no property for, so a
new SDK version may need a new release of this package.

## Feedback and license

Report a bug or ask a question on [GitHub Issues](https://github.com/pinkroosterai/PinkRooster/issues). The package is released under the [MIT license](https://github.com/pinkroosterai/PinkRooster/blob/main/LICENSE).

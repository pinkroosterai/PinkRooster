# PinkRooster.Samples

The library every sample in [`samples/`](../README.md) shares. It holds what is not the feature of any one sample: which model to talk to,
how a terminal draws what an agent does, and the small ticket domain some samples use. A sample's own `Program.cs` holds its feature.

## Choosing a model: `models.json`

The models a sample can use are listed in `models.json`, next to this README. It is git-ignored, so keys in it stay on your machine.
The first run creates it with one model, Ollama's `qwen3.5:9b`; `models.example.json` is the tracked example, with Ollama, Groq, LM Studio
and OpenAI entries to copy from.

```json
{
  "models": [
    { "model": "qwen3.5:9b", "endpoint": "http://localhost:11434/v1", "apiKey": null },
    { "model": "openai/gpt-oss-120b", "endpoint": "https://api.groq.com/openai/v1", "apiKey": "<your Groq key>" }
  ]
}
```

| Field | Required | Meaning |
| --- | --- | --- |
| `model` | yes | Model ID. Most samples need a model that supports tool calls. |
| `endpoint` | yes | Absolute HTTP(S) base URL of an OpenAI-compatible endpoint. |
| `apiKey` | no | Credential for the endpoint. Leave it out or `null` for Ollama. Never printed; every key in the file is redacted from error output. |
| `reasoningEffort` | no | `none`, `low`, `medium` (default), `high` or `extraHigh`, sent as `reasoning_effort` unless the sample sets its own. |
| `api` | no | `chatCompletions` (default, calls `/v1/chat/completions`) or `responses` (calls `/v1/responses`). Some OpenAI reasoning models reject `reasoning_effort` alongside function tools on `chatCompletions`; switch those to `responses`, or set `reasoningEffort` to `none`. |

With more than one model, a sample asks which one to use at start. The file allows comments and trailing commas, and is found through the
project folder, whichever folder a sample starts in. When the model cannot be reached, a sample says so, names `models.json`, and exits 1;
Ctrl+C exits 130. Samples that ask you something (approvals, questions) need an interactive terminal.

## What is in it

- `SampleHost` — reads `models.json`, builds the model client, handles Ctrl+C and failures, and returns the exit code. A sample's `Main`
  calls it with the sample's own `RunAsync`.
- `Models/` — `ModelSettings`, `ModelsFile` and `ModelClient`.
- `Terminal/` — `ISampleConsole`, which extends `IAgentConsole` from `PinkRooster.SpectreConsole`, and its Spectre.Console implementation:
  an `AgentConsole` draws streamed text and reasoning, tool calls with their results, approvals and questions, with every configured key
  redacted; the sample adds error text and model selection.
- `Tickets/` — a three-ticket in-memory store and its tool collection, used by `Events`, `Approvals`, `MafPassthrough` and `OtherAgents`.

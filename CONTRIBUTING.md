# Contributing

Bug reports and focused pull requests are welcome. Search existing issues before opening a new one. For a behavior change, describe the expected behavior and how it differs from the current one. Do not include API keys, private state, or live responses in issues or fixtures.

This is a small .NET 10 repository of packages (`PinkRooster.Agents*`, `PinkRooster.ToolCollections*`, `PinkRooster.OpenAI.Reasoning`, `PinkRooster.SpectreConsole`). Keep changes close to the existing code. Add tests when changing behavior; they are xunit v3 tests, one project per package under `tests/`. The `src` projects stay free of model-provider and UI packages, and only `PinkRooster.ToolCollections.Mcp` may reference MCP, only `PinkRooster.OpenAI.Reasoning` may reference OpenAI and only `PinkRooster.SpectreConsole` may reference Spectre.Console; `NeutralityTests` checks all of it. Run these commands from the repository root before a pull request:

```text
dotnet build PinkRooster.slnx -c Release
dotnet test --solution PinkRooster.slnx -c Release
```

Warnings are errors; in `src` that includes a public member without an XML doc, `var`, and an `if` without braces. `tests/PinkRooster.Repository.Tests/PublicApi/` lists each package's public API and a test compares: after a change to the public API, run the tests once with `PINKROOSTER_UPDATE_PUBLIC_API=1` and commit the changed files. Use a Conventional Commit subject such as `fix(agents): handle an empty step`. In a pull request, explain the behavior change, list the checks run, and link a related issue when there is one. Security issues belong in the private reporting channel described in [SECURITY.md](SECURITY.md).

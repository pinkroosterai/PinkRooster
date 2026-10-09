# Security policy

Security fixes are considered for the current release line. The current release line is `0.4.x`.

Do not disclose a suspected vulnerability in a public issue. Use **Security → Report a vulnerability** to send a private report to the maintainers. Include the affected package and version or commit, impact, and reproduction steps without sharing API keys or private response data. If private vulnerability reporting is unavailable, email info@pinkrooster.nl without including secrets.

Note for users of `PinkRooster.ToolCollections.BuiltIn`: the shell collection runs commands with the host's permissions and environment, and its prefix lists are a guardrail, not a sandbox. That is documented behavior, not a vulnerability; see [the package guide](docs/PinkRooster.ToolCollections.BuiltIn.md).

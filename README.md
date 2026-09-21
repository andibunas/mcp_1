# mcp_1

An MCP (Model Context Protocol) server in .NET providing granular, folder-scoped Google Drive access. Runs locally (stdio for Claude Desktop/Code, or HTTP) and is deployable to AWS (Docker on EC2/ECS, or Lambda).

See [docs/PLAN.md](docs/PLAN.md) for the full architecture and build plan.

## Repo structure

```
src/
  McpGoogleDrive.Core/            # Drive access, MCP tools, config — all real logic
  McpGoogleDrive.Host.Stdio/      # local stdio host (Claude Desktop/Code)
  McpGoogleDrive.Host.Web/        # HTTP host — also the base for Docker/EC2/Fargate
  McpGoogleDrive.Host.Lambda/     # AWS Lambda host
test/
  McpGoogleDrive.Core.Tests/
docs/
  PLAN.md                         # architecture, build order, environment notes
  steps.md                        # detailed log of steps done + what's left
  setup.md                        # Google Cloud project + folder access setup
```

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download) (LTS). Check with `dotnet --version`.
- A Google Cloud project with the Drive API enabled and OAuth credentials — see [docs/setup.md](docs/setup.md).

## Status

- Solution scaffolding: done (`McpGoogleDrive.slnx`, all four projects + tests, builds clean).
- `McpGoogleDrive.Core`: Drive client wrapper with folder allow-list enforcement, pluggable
  `ITokenStore` (local file implementation), config model, and the interactive Picker-based
  folder-grant flow are implemented and unit tested (`dotnet test`).
- MCP tool definitions and host wiring (stdio, HTTP, Docker, Lambda) are not yet implemented —
  see [docs/PLAN.md](docs/PLAN.md) for the current step.

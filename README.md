# mcp_1

An MCP (Model Context Protocol) server in .NET providing granular, folder-scoped Google Drive access. Runs locally (stdio for Claude Desktop/Code, or HTTP) and is deployable to AWS (Docker on EC2/ECS, or Lambda).

See [docs/PLAN.md](docs/PLAN.md) for the full architecture and build plan, and [docs/steps.md](docs/steps.md) for exactly what's been built and verified vs. what's documented-but-untested.

## Repo structure

```
src/
  McpGoogleDrive.Core/            # Drive access, auth, MCP tools, config — all real logic
  McpGoogleDrive.Host.Stdio/      # local stdio host (Claude Desktop/Code)
  McpGoogleDrive.Host.Web/        # HTTP host — also the base for Docker/EC2/Fargate
  McpGoogleDrive.Host.Lambda/     # AWS Lambda host (Function URL, response streaming)
test/
  McpGoogleDrive.Core.Tests/
docs/
  PLAN.md                         # architecture, build order, environment notes
  steps.md                        # detailed log of steps done + what's left
  setup.md                        # Google Cloud project + folder access setup
  deploy-aws.md                   # EC2/ECS and Lambda deployment (documented, not yet deployed)
Dockerfile                        # builds/runs Host.Web; also the EC2/ECS/App Runner artifact
template.yaml                     # SAM template for Host.Lambda
```

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download) (LTS). Check with `dotnet --version`.
- A Google Cloud project with the Drive API enabled and OAuth credentials — see [docs/setup.md](docs/setup.md).
- For AWS deployment: an AWS account and the AWS SAM CLI — see [docs/deploy-aws.md](docs/deploy-aws.md).

## Status

Every step of the plan in [docs/PLAN.md](docs/PLAN.md) is implemented:

- **`McpGoogleDrive.Core`**: Drive client wrapper with folder allow-list enforcement, pluggable
  `ITokenStore` (`FileTokenStore` for local disk, `SecretsManagerTokenStore` for AWS), config
  model, the interactive Picker-based folder-grant flow, and MCP tool definitions
  (`drive_list_files`/`drive_read_file`/`drive_write_file`/`drive_move_file`) — all unit tested
  (`dotnet test`, 20/20).
- **All three hosts are wired up**: `Host.Stdio` (stdio transport + a `setup` verb for the
  interactive folder grant), `Host.Web` (HTTP transport), and `Host.Lambda` (HTTP transport via
  `Amazon.Lambda.AspNetCoreServer.Hosting`, runs through Kestrel locally or the Lambda runtime when
  deployed).
- **Docker** (`Dockerfile`) and **AWS** (`docs/deploy-aws.md`, `template.yaml`,
  `src/McpGoogleDrive.Host.Lambda/Dockerfile`) deployment paths are written and documented.

**What's genuinely unverified**, flagged rather than assumed working:
- An actual OAuth sign-in / Drive tool call against a real Google account — needs your own Google
  Cloud credentials (`docker build`/`docker run` and Lambda equally need real credentials/token
  seeding — see `docs/deploy-aws.md`).
- `docker build`/`docker run` themselves — Docker isn't installed in this dev environment; only the
  underlying `dotnet publish` step was verified.
- Everything AWS-specific — the Lambda container base image tag, `sam deploy`, and Function URL
  response-streaming behavior — no AWS credentials were used or available while building this.

See [docs/steps.md](docs/steps.md) for the full per-step breakdown of what was actually verified.

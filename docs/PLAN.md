# Plan: .NET MCP Server for Granular Google Drive Access

> Working plan for this repo. Update this file as the design changes — it's the
> source of truth for "why is this structured this way," not a one-time snapshot.
> For a step-by-step log of what's done vs. left, see [steps.md](steps.md).

## Context

The repo (`mcp_1`) started empty except for a README stating the goal: an MCP server in .NET giving granular Google Drive access, runnable locally and optionally on AWS. The actual logic (Drive access + MCP tool implementations) is built as a single shared **library**, with thin, separate **host** projects for each way of running it: stdio (local MCP client), local HTTP, Docker, EC2, and Lambda.

## Architecture

```
mcp_1/
  McpGoogleDrive.slnx
  src/
    McpGoogleDrive.Core/            # all real logic — no hosting concerns
    McpGoogleDrive.Host.Stdio/      # console app, stdio transport (Claude Desktop/Code)
    McpGoogleDrive.Host.Web/        # ASP.NET Core, HTTP transport — also the base for Docker/EC2/Fargate
    McpGoogleDrive.Host.Lambda/     # wraps Host.Web for AWS Lambda
  test/
    McpGoogleDrive.Core.Tests/
  Dockerfile                        # builds Host.Web image; reused by EC2 docker run, ECS/Fargate, App Runner
```

**Why this split:** every host project is just wiring (pick a transport, pick a token-store implementation, call into Core). None of them contain Drive or MCP tool logic, so adding a new way to run it later (e.g. Azure, a different serverless platform) means writing one more small host project, not touching Core.

### McpGoogleDrive.Core (class library, .NET 10)
- **Google Drive client wrapper** using `Google.Apis.Drive.v3` — list/search/read/create/update/move, all scoped to a configured allow-list of folder IDs (the "granular" part — nothing outside those folders is reachable, enforced in Core, not left to the caller).
- **MCP tools** using the official `ModelContextProtocol` C# SDK, defined as plain C# methods with `[McpServerTool]` attributes (e.g. `drive_list_files`, `drive_read_file`, `drive_write_file`) that call the Drive wrapper. This SDK is transport-agnostic — the same tool classes work whether the host later attaches stdio or HTTP.
- **`ITokenStore` abstraction** for OAuth2 refresh-token persistence, so credential storage is pluggable per host:
  - `FileTokenStore` — local JSON file, used by Stdio/local Web hosts.
  - `SecretsManagerTokenStore` — AWS Secrets Manager, used by Lambda/EC2/Docker-on-AWS hosts.
- **Config model** — allowed folder IDs, Drive scopes, which token store to use — bound from `appsettings.json`/environment variables so each host supplies its own config without Core knowing who's hosting it.
- **Interactive folder-grant flow** — instead of only accepting manually-pasted folder IDs, a small setup command in Core (`dotnet run -- setup` or similar) runs the OAuth flow and then uses the **Google Picker API** to open a browser folder picker, so the user visually selects which Drive folders to grant the server access to. The picker returns folder IDs directly, which get written into the local config/token store. Manual folder-ID entry (via the Drive URL) stays as a documented fallback for headless/AWS setups where no browser is available.

### McpGoogleDrive.Host.Stdio (console app)
Thinnest possible host: builds a Generic Host, calls `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`, references Core. This is what you'd point Claude Desktop/Claude Code at directly as a local process — no network involved.

### McpGoogleDrive.Host.Web (ASP.NET Core minimal API)
Calls `AddMcpServer().WithHttpTransport()`, maps the MCP endpoint. Runs with `dotnet run` for local HTTP testing (`localhost:5000`), and is also the image the Dockerfile builds — so this one project covers local HTTP, Docker, EC2 (`docker run` or as a systemd service), and ECS/Fargate/App Runner, all from the same code.

### McpGoogleDrive.Host.Lambda
Wraps `Host.Web`'s app via `Amazon.Lambda.AspNetCoreServer.Hosting`, deployed behind a Lambda Function URL. **Caveat:** MCP's Streamable HTTP transport wants long-lived/streaming responses; Lambda only supports that via Function URLs in `RESPONSE_STREAM` invoke mode. Confirm in the AWS build phase that the MCP SDK's streaming behavior works cleanly through that path — if it doesn't, fall back to the SDK's stateless HTTP mode for the Lambda host specifically.

### AWS options at a glance
- **Docker image → EC2**: simplest mental model, closest to "just run the local server on a box you control." `docker run` the same image on an EC2 instance.
- **Docker image → ECS/Fargate or App Runner**: same image, no server to patch/manage; scales/restarts for you. More moving parts to learn (task definitions, ALB) than EC2.
- **Lambda**: pay-per-request, zero idle cost, but MCP's streaming needs the Function URL response-streaming mode (newer capability) — most fiddly of the three.

Build Docker/EC2 first since it's the most direct extension of "local," then Lambda once the core hosting story works.

## README / docs

`README.md` should stay a short overview + pointer here. A `docs/setup.md` (to be written alongside step 2, once the real flow exists) will cover folder access setup, since "granular access" hinges on getting the right folder IDs:
- **Interactive route (recommended, local only):** run the setup command, sign in via OAuth, pick folders in the Picker UI — IDs are captured automatically.
- **Manual route (needed for headless/AWS setups):** how to find a folder ID from its Drive URL (`https://drive.google.com/drive/folders/<FOLDER_ID>`), and how to paste it into config.
- How to grant the server's Google account/service account access to a folder in the first place (share the folder with the OAuth user, or with the service account's email for AWS deployments) — without this step the folder ID alone isn't enough.

## Build order (staged)

1. **Scaffold** the solution and all four projects, plus the test project. ✅ Done.
2. **Core**: Drive client wrapper + folder allow-list enforcement + `ITokenStore`/`FileTokenStore` + config model + the interactive Picker-based setup command. Unit-testable without any MCP or hosting code. Write `docs/setup.md` alongside this step. ✅ Done.
3. **Core**: MCP tool definitions on top of the Drive wrapper (list/read/write/create/move), using the `ModelContextProtocol` SDK. ✅ Done.
4. **Host.Stdio**: wire it up, verify end-to-end against a real Google Drive account with Claude Desktop or the MCP inspector CLI. ✅ Wired and build/config-verified; real-account end-to-end run still pending (needs a user's own Google Cloud credentials — see docs/steps.md).
5. **Host.Web**: wire up HTTP transport, verify locally with the MCP inspector or `curl`. ✅ Wired (DI wiring shared with Host.Stdio via `Core.AddDriveCoreServices()`) and build/config-verified; real-account end-to-end run still pending — see docs/steps.md.
6. **Dockerfile** for Host.Web; verify `docker run` locally. ✅ Dockerfile written and the underlying `dotnet publish` step verified; `docker build`/`run` themselves not verified — Docker isn't installed in this dev environment. See docs/steps.md.
7. **AWS**: `SecretsManagerTokenStore`, then EC2/Docker deployment, then Lambda host + Function URL streaming.

Steps 1–6 get a fully working local (stdio + HTTP + Docker) server. Step 7 is the AWS layer, tackled after that foundation is solid.

## Verification
- Unit tests in `McpGoogleDrive.Core.Tests` for the folder allow-list enforcement and Drive wrapper (mocking the Drive API client).
- Manual end-to-end: run `Host.Stdio` and connect it as an MCP server in Claude Desktop/Code; run `Host.Web` locally and hit it with the MCP inspector; `docker build` + `docker run` the Web host and repeat.
- For AWS steps: deploy to a real EC2 instance/Lambda function once the local story works, and re-run the same MCP inspector checks against the public endpoint.

## Environment notes
- Requires .NET SDK 10 (an LTS release). This repo targets `net10.0` across all projects.
- On this dev machine, the SDK is installed at `/usr/local/share/dotnet` but is **not** on PATH inside Claude Code's sandboxed shell (each Bash call gets a fresh, minimal PATH that doesn't read `/private/etc/paths.d/`). Either prefix commands with `export PATH="/usr/local/share/dotnet:$PATH"` or call `/usr/local/share/dotnet/dotnet` directly when running `dotnet` from an agent session. A normal interactive Terminal.app/iTerm shell should already have it on PATH.
- `dotnet new sln` on this SDK version generates the newer `McpGoogleDrive.slnx` format, not a classic `.sln` file — same purpose, just a different file format/extension.

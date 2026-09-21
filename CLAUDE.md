# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

An MCP (Model Context Protocol) server in .NET providing granular, folder-scoped Google Drive access. All real logic lives in a shared library, `McpGoogleDrive.Core`; separate thin host projects wire it up for each way of running it (stdio, local HTTP, Docker, Lambda). See [docs/PLAN.md](docs/PLAN.md) for the full architecture and rationale, and [docs/steps.md](docs/steps.md) for a step-by-step log of what's built vs. what's left.

## Commands

The .NET SDK on this machine may not be on PATH in a fresh shell — if `dotnet` isn't found, use the full path `/usr/local/share/dotnet/dotnet` or `export PATH="/usr/local/share/dotnet:$PATH"` first.

```
dotnet build                                    # build the whole solution
dotnet test test/McpGoogleDrive.Core.Tests      # run Core's unit tests
dotnet test test/McpGoogleDrive.Core.Tests --filter FullyQualifiedName~DriveFileServiceTests  # run one test class
```

```
dotnet run --project src/McpGoogleDrive.Host.Stdio            # start the MCP server over stdio
dotnet run --project src/McpGoogleDrive.Host.Stdio -- setup   # interactive Drive folder-grant flow
```

Both require real Google OAuth credentials configured via user-secrets or env vars (never edit them into the committed `appsettings.json`) — see `docs/setup.md`. Without them, both commands fail fast with a clear `GoogleAuth:ClientId and GoogleAuth:ClientSecret are not configured` error rather than hanging. `Host.Web`/`Host.Lambda` build but aren't wired to the MCP SDK yet.

## Architecture

- **`src/McpGoogleDrive.Core`** — all real logic, no hosting concerns:
  - `Drive/` — `DriveFileService` is the single entry point for Drive operations, and the only place folder-scoped access is enforced: every call walks the target's parent chain and throws `FolderAccessDeniedException` unless an ancestor is in `DriveAccessOptions.AllowedFolderIds`. `IGoogleDriveApi`/`GoogleDriveApi` are the seam over `Google.Apis.Drive.v3` (the interface exists purely so tests can fake it).
  - `Auth/` — `ITokenStore` is pluggable per host (`FileTokenStore` for local disk today; an AWS Secrets Manager implementation is planned for step 7). `GoogleAuthService` runs the OAuth "installed app" flow through whichever store is configured.
  - `Setup/` — `InteractiveSetupService` + `DriveFolderPickerService` implement a browser-based folder-grant flow (serves a local page embedding the Google Picker widget over a loopback `HttpListener`) as an alternative to manually pasting folder IDs. Wired up as `Host.Stdio`'s `setup` verb.
  - `Tools/` — `DriveTools` exposes `DriveFileService` as MCP tools (`[McpServerToolType]`/`[McpServerTool]` from the `ModelContextProtocol` SDK). Kept deliberately transport-agnostic; hosts attach stdio or HTTP transport around the same tool classes.
  - `Configuration/` — options classes plus `ConfigurationBuilderExtensions.AddAllowedFoldersFile()`, which layers folder IDs saved by the interactive setup flow into `DriveAccess:AllowedFolderIds`.
- **`src/McpGoogleDrive.Host.*`** — thin wiring only (pick a transport, pick a token store, call into Core). None should contain Drive or MCP tool logic; a new way to run the server means a new small host project, not changes to Core.
- **`test/McpGoogleDrive.Core.Tests`** — unit tests use `FakeGoogleDriveApi` (an in-memory `IGoogleDriveApi`) to exercise allow-list enforcement and tool behavior without hitting the real Drive API.

## Status

See [docs/steps.md](docs/steps.md) for the current step and what's done. As of the last update: Core's Drive access, auth, config, interactive setup, and MCP tool definitions are implemented and unit tested; `Host.Stdio` is wired to the MCP SDK and build/config-verified (a real end-to-end run needs a user's own Google Cloud credentials — not yet done). `Host.Web`/`Host.Lambda` are still unwired.

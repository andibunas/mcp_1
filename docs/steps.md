# Steps

A running log of the build order from [PLAN.md](PLAN.md): what's been done (with what it actually
produced) and what's left. Update this file as each step completes — check items off, don't
rewrite history.

## ✅ Step 1 — Scaffold the solution

Commit: `f6aa03c`

- Created `McpGoogleDrive.slnx` and five projects: `McpGoogleDrive.Core` (classlib),
  `McpGoogleDrive.Host.Stdio` / `.Host.Web` / `.Host.Lambda` (console/web/console), and
  `McpGoogleDrive.Core.Tests` (xunit). All target `net10.0`.
- Wired project references: all three hosts + the test project reference `Core`.
- Verified `dotnet build` succeeds clean (0 warnings/errors).
- Saved the architecture plan to `docs/PLAN.md`, rewrote `README.md` as a short pointer + status.
- Noted an environment quirk: the .NET SDK lives at `/usr/local/share/dotnet` but isn't on PATH
  in Claude Code's sandboxed shell — commands need `export PATH="/usr/local/share/dotnet:$PATH"`
  or the full binary path.

## ✅ Step 2 — Core: Drive access, auth, config, interactive setup

Commit: `fccf73c`

- **Drive/**: `IGoogleDriveApi` (seam for testing) + `GoogleDriveApi` (real `Google.Apis.Drive.v3`
  client) + `DriveFileService`, which enforces the folder allow-list on every call by walking the
  parent chain up to `MaxAncestorDepth` and throwing `FolderAccessDeniedException` if no ancestor
  is in `DriveAccessOptions.AllowedFolderIds`.
- **Auth/**: `ITokenStore` + `FileTokenStore` (local disk, `~/.mcp-google-drive/tokens`),
  `GoogleTokenDataStore` (bridges it into the Google client library's `IDataStore`), and
  `GoogleAuthService` (runs the OAuth "installed app" flow via `GoogleWebAuthorizationBroker`).
- **Configuration/**: `GoogleAuthOptions`, `DriveAccessOptions`, and
  `ConfigurationBuilderExtensions.AddAllowedFoldersFile()` for loading picker output back in.
- **Setup/**: `DriveFolderPickerService` (serves a one-off local HTML page embedding the Google
  Picker JS widget over a loopback `HttpListener`, captures selected folder IDs) and
  `InteractiveSetupService` (orchestrates OAuth → picker → writes
  `~/.mcp-google-drive/allowed-folders.json`).
- **docs/setup.md**: Google Cloud project setup (Drive API, OAuth client, Picker API key) and both
  the interactive and manual (paste-a-folder-ID) grant routes.
- Unit tests: `DriveFileServiceTests` (allow-list enforcement, incl. nested-folder walk-up, via a
  `FakeGoogleDriveApi`) and `FileTokenStoreTests` (save/load/delete round-trip). 9/9 passing.
- **Known gap, by design**: nothing calls `InteractiveSetupService` yet — no CLI verb exists. That
  lands in step 4 when `Host.Stdio` gets wired up.

## ⬜ Step 3 — Core: MCP tool definitions

- Add the `ModelContextProtocol` C# SDK package to `Core`.
- Define MCP tools as `[McpServerTool]`-attributed methods on top of `DriveFileService`:
  `drive_list_files`, `drive_read_file`, `drive_write_file` (create/update), `drive_move_file`.
  Tool descriptions should make the folder scoping visible to the calling model (e.g. mention that
  only allowed folders are reachable).
- Decide how `FolderAccessDeniedException` surfaces through the MCP tool-call error path (should
  read as a clear, actionable error to the calling model, not a stack trace).
- These tool classes must stay transport-agnostic — no stdio/HTTP-specific code here.

## ⬜ Step 4 — Host.Stdio

- Build a Generic Host in `Program.cs`: bind `GoogleAuthOptions`/`DriveAccessOptions` from
  `appsettings.json` + env vars, call `AddAllowedFoldersFile()`, register `Core` services,
  `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`.
- Add a `setup` verb (`dotnet run -- setup`) that calls `InteractiveSetupService.RunAsync()` —
  this is where step 2's setup flow actually becomes runnable.
- Verify end-to-end: connect it as an MCP server in Claude Desktop or Claude Code, or drive it with
  the MCP inspector CLI, against a real Google Drive account.

## ⬜ Step 5 — Host.Web

- Same DI/config wiring as Host.Stdio, but `AddMcpServer().WithHttpTransport()` mapped on an
  ASP.NET Core minimal API.
- Verify locally with the MCP inspector or `curl` against `localhost`.

## ⬜ Step 6 — Docker

- Write a `Dockerfile` (multi-stage: SDK build → runtime image) that builds and runs `Host.Web`.
- Verify `docker build` + `docker run` locally, hitting the containerized server the same way as
  step 5's local verification.
- This image is the artifact reused for EC2, ECS/Fargate, and App Runner in step 7 — no separate
  Dockerfiles per AWS target.

## ⬜ Step 7 — AWS

- Implement `SecretsManagerTokenStore` (`ITokenStore` backed by AWS Secrets Manager) for
  headless/AWS-hosted token persistence.
- Document/script an EC2 deployment: `docker run` the step 6 image on an instance, or run it as a
  systemd service.
- Build `Host.Lambda`: wrap `Host.Web`'s app via `Amazon.Lambda.AspNetCoreServer.Hosting`, deploy
  behind a Lambda Function URL in `RESPONSE_STREAM` invoke mode (needed for MCP's Streamable HTTP).
  Confirm the SDK's streaming behavior actually works through that path; fall back to the SDK's
  stateless HTTP mode for this host specifically if it doesn't.
- Re-run the MCP inspector checks from steps 4/5 against the deployed public endpoint(s).

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

## ✅ Step 3 — Core: MCP tool definitions

Commit: `8969c1b`

- Added the `ModelContextProtocol` SDK (v2.2.0) package to `Core`.
- **Tools/DriveTools.cs**: static `[McpServerToolType]` class with four `[McpServerTool]`-attributed
  methods on top of `DriveFileService`: `drive_list_files`, `drive_read_file`, `drive_write_file`
  (single tool that creates when given `parentFolderId`/`name`/`mimeType`, or overwrites when given
  `fileId`), `drive_move_file`. Each has a `[Description]` naming the folder-scoping behavior so the
  calling model understands why an operation might be denied.
- `DriveFileService` is taken as a method parameter, injected by the MCP SDK's DI support — the
  tool methods don't construct or own it, keeping them transport-agnostic (no stdio/HTTP code here).
- Exception surfacing: relied on the SDK's default behavior (confirmed via the `McpException`/
  `IsError` types in `ModelContextProtocol.Core.dll`) — exceptions thrown from a tool method are
  caught by the framework and returned as an error tool result using the exception's `Message`, not
  a stack trace. `FolderAccessDeniedException`'s message ("Access to '{id}' is denied: it is not
  inside any allowed folder.") is already written to read as a clear model-facing error, so no
  extra try/catch was added.
- Unit tests: `DriveToolsTests` exercises all four tools (including the create-vs-update branch in
  `drive_write_file` and the argument-validation error when neither path is satisfied) against
  `FakeGoogleDriveApi`. 15/15 tests passing overall.
- **Known gap, by design**: no host registers `DriveTools`/`DriveFileService`/`IGoogleDriveApi` with
  a DI container or an `AddMcpServer()` call yet — that's step 4.

## ✅ Step 4 — Host.Stdio (build + config wiring verified; real-account run still needed)

Commit: `c0e0a3c`

- Added `Microsoft.Extensions.Hosting` and `ModelContextProtocol` packages to `Host.Stdio`.
- **Program.cs**: two entry points off `args`:
  - Default: `Host.CreateApplicationBuilder()` → bind `GoogleAuthOptions`/`DriveAccessOptions` from
    `appsettings.json` + env vars → `builder.Configuration.AddAllowedFoldersFile()` → register Core
    services (`ITokenStore`/`GoogleAuthService`/`DriveFileService`/`IGoogleDriveApi` factory) →
    `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly(typeof(DriveTools).Assembly)`.
  - `setup`: same DI wiring minus the MCP server, calls `InteractiveSetupService.RunAsync()` and
    prints the granted folders.
- **Stdout discipline**: `builder.Logging.ClearProviders()` + `AddConsole(... LogToStandardErrorThreshold = LogLevel.Trace)`
  so nothing but MCP protocol messages ever hits stdout — the default console logger would
  otherwise corrupt the stdio transport.
- **Eager auth at startup**: `host.Services.GetRequiredService<DriveFileService>()` is resolved
  right after `builder.Build()`, before `RunAsync()`, so the OAuth browser flow (or a config error)
  happens during startup instead of stalling the first tool call mid-request.
- Added `appsettings.json` with empty `GoogleAuth`/`DriveAccess` placeholders (real secrets go in
  user-secrets or env vars, documented in `docs/setup.md` — never edit real values into this
  committed file), wired to copy to the build output.
- **Verified**: `dotnet build` (whole solution) and `dotnet test` (15/15) both pass. Ran
  `dotnet run --project src/McpGoogleDrive.Host.Stdio` and the `setup` variant with no credentials
  configured — both fail fast with the clear `GoogleAuth:ClientId and GoogleAuth:ClientSecret are
  not configured...` message (from `GoogleAuthService`) rather than hanging or crashing opaquely,
  confirming config binding, `AddAllowedFoldersFile()`, and the eager-auth wiring all execute
  correctly end to end.
- **Not verified (needs the user's own Google Cloud credentials + a browser)**: the actual OAuth
  sign-in, Picker folder selection, and a real MCP tool call round-trip (e.g. via Claude
  Desktop/Code or the MCP inspector CLI) against a live Drive account. That's a manual step for
  whoever has real credentials to run once `docs/setup.md`'s Google Cloud project setup is done.

## ✅ Step 5 — Host.Web (build + config wiring verified; real-account run still needed)

Commit: `ba38ea3`

- Added `ModelContextProtocol.AspNetCore` to `Host.Web`.
- **Refactored the DI wiring introduced in step 4 into `Core/ServiceCollectionExtensions.cs`**
  (`AddDriveCoreServices(IConfiguration)`) — options binding, token store, auth, `DriveFileService`,
  the `IGoogleDriveApi` factory. Both `Host.Stdio` and `Host.Web` now call this one method instead
  of duplicating the same ~15 lines; `Host.Stdio`'s `Program.cs` was updated to use it too, so the
  two hosts stay in sync. Required adding `Microsoft.Extensions.DependencyInjection.Abstractions`
  and `Microsoft.Extensions.Options.ConfigurationExtensions` to `Core`.
- **Program.cs**: `WebApplication.CreateBuilder()` → `AddAllowedFoldersFile()` →
  `AddDriveCoreServices()` → `AddMcpServer().WithHttpTransport().WithToolsFromAssembly(...)` →
  eager `DriveFileService` resolution before `app.Run()` (same startup-not-mid-request rationale as
  step 4) → `app.MapMcp()`.
- Added `GoogleAuth`/`DriveAccess` placeholder sections to `Host.Web/appsettings.json` (already
  present from the ASP.NET Core template; extended, not created).
- **Verified**: `dotnet build` (whole solution) and `dotnet test` (15/15) pass. Ran both
  `Host.Stdio` and `Host.Web` with no credentials configured after the refactor — both still fail
  fast with the same clear config error, confirming the extracted `AddDriveCoreServices()` behaves
  identically to the pre-refactor inline wiring for both hosts.
- **Not verified (needs the user's own Google Cloud credentials + a browser)**: an actual HTTP MCP
  session (`curl`/MCP inspector against `localhost`) or a real Drive tool call — same limitation as
  step 4, since the eager-auth step blocks server startup without real credentials in this
  environment.

## ✅ Step 6 — Docker (Dockerfile written; `docker build`/`run` not runnable in this environment)

Commit: (pending)

- Added a multi-stage `Dockerfile` at the repo root: `mcr.microsoft.com/dotnet/sdk:10.0` build
  stage (restore on `.csproj` files first for layer caching, then `dotnet publish` `Host.Web`) →
  `mcr.microsoft.com/dotnet/aspnet:10.0` runtime stage, listening on `:8080`
  (`ASPNETCORE_URLS=http://+:8080`), `ENTRYPOINT ["dotnet", "McpGoogleDrive.Host.Web.dll"]`.
- Added `.dockerignore` (bin/obj/.git/docs/markdown excluded from the build context).
- **Headless/container OAuth caveat, documented in `docs/setup.md`**: the container has no browser
  to complete the OAuth "installed app" flow, so `FileTokenStore`'s default
  `~/.mcp-google-drive` directory needs to be mounted in from a host that already ran
  `Host.Stdio -- setup` (`docker run -v ~/.mcp-google-drive:/root/.mcp-google-drive ...`) — a
  proper `SecretsManagerTokenStore` for deployments where that isn't practical is still step 7.
- **Verified**: `dotnet publish src/McpGoogleDrive.Host.Web/McpGoogleDrive.Host.Web.csproj -c
  Release -o <dir>` (the exact command the Dockerfile's build stage runs) succeeds and produces
  the expected output (`McpGoogleDrive.Host.Web.dll`, its dependencies, `appsettings.json`).
- **Not verified**: `docker build`/`docker run` themselves — **Docker is not installed in this
  environment** (`which docker` finds nothing), so the Dockerfile has not actually been built or
  run. This needs to happen on a machine with Docker before step 6 can be considered fully done;
  flagging honestly rather than claiming it works.

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

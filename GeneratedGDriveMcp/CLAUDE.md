# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

An MCP (Model Context Protocol) server in .NET providing granular, folder-scoped Google Drive access. All real logic lives in a shared library, `McpGoogleDrive.Core`; separate thin host projects wire it up for each way of running it (stdio, local HTTP, Docker, Lambda). See [docs/PLAN.md](docs/PLAN.md) for the full architecture and rationale, and [docs/steps.md](docs/steps.md) for a step-by-step log of what's built vs. what's left — every step in the original plan is now implemented, though several deployment paths (Docker, AWS) are unverified beyond `dotnet build`/`publish` since Docker and AWS credentials aren't available in this dev environment.

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
dotnet run --project src/McpGoogleDrive.Host.Web              # start the MCP server over HTTP
dotnet run --project src/McpGoogleDrive.Host.Lambda           # runs the same app locally via Kestrel (not the Lambda runtime)
```

All require real Google OAuth credentials configured via user-secrets or env vars (never edit them into the committed `appsettings.json` files) — see `docs/setup.md`. Without them, each command fails fast with a clear `GoogleAuth:ClientId and GoogleAuth:ClientSecret are not configured` error rather than hanging. `Host.Lambda` additionally needs AWS credentials/region resolvable (it constructs a Secrets Manager client at startup) — locally that fails fast too, with `AmazonClientException: No RegionEndpoint or ServiceURL configured`.

```
docker build -t mcp-google-drive .   # build the Host.Web image (untested here — no Docker in this dev environment)
```

Note: containers have no browser to complete OAuth sign-in — see "Running in Docker / headless" in `docs/setup.md`, and `docs/deploy-aws.md` for the AWS deployment paths (also unverified against a real AWS account).

## Architecture

- **`src/McpGoogleDrive.Core`** — all real logic, no hosting concerns:
  - `Drive/` — `DriveFileService` is the single entry point for Drive operations, and the only place folder-scoped access is enforced: every call walks the target's parent chain and throws `FolderAccessDeniedException` unless an ancestor is in `DriveAccessOptions.AllowedFolderIds`. `IGoogleDriveApi`/`GoogleDriveApi` are the seam over `Google.Apis.Drive.v3` (the interface exists purely so tests can fake it).
  - `Auth/` — `ITokenStore` is pluggable per host: `FileTokenStore` (local disk, the default) or `SecretsManagerTokenStore` (AWS Secrets Manager, used by `Host.Lambda` and documented for EC2/ECS in `docs/deploy-aws.md`). `GoogleAuthService` runs the OAuth "installed app" flow through whichever store is configured — it refreshes silently if a cached credential exists and only opens a browser when one doesn't, which is what makes headless (Lambda/EC2) deployments work once a token has been seeded into their store.
  - `Setup/` — `InteractiveSetupService` + `DriveFolderPickerService` implement a browser-based folder-grant flow (serves a local page embedding the Google Picker widget over a loopback `HttpListener`) as an alternative to manually pasting folder IDs. Wired up as `Host.Stdio`'s `setup` verb.
  - `Tools/` — `DriveTools` exposes `DriveFileService` as MCP tools (`[McpServerToolType]`/`[McpServerTool]` from the `ModelContextProtocol` SDK). Kept deliberately transport-agnostic; hosts attach stdio or HTTP transport around the same tool classes.
  - `Configuration/` — options classes plus `ConfigurationBuilderExtensions.AddAllowedFoldersFile()`, which layers folder IDs saved by the interactive setup flow into `DriveAccess:AllowedFolderIds`.
  - `ServiceCollectionExtensions.AddDriveCoreServices(IConfiguration)` — the one place that registers options binding, a token store (`TryAddSingleton`, so a host can register its own first — see `Host.Lambda`), `GoogleAuthService`, `DriveFileService`, and the `IGoogleDriveApi` factory. Every host calls this instead of each re-declaring the same DI wiring; add any new host the same way rather than copying the registrations inline.
- **`src/McpGoogleDrive.Host.*`** — thin wiring only (pick a transport, optionally override the token store, call `AddDriveCoreServices()`, start the MCP server). None should contain Drive or MCP tool logic; a new way to run the server means a new small host project, not changes to Core. `Host.Lambda` wraps the same wiring as `Host.Web` via `Amazon.Lambda.AspNetCoreServer.Hosting`'s `AddAWSLambdaHosting`, which runs the app through Kestrel locally or the Lambda runtime when deployed, based on environment variables — same `Program.cs` either way.
- **`test/McpGoogleDrive.Core.Tests`** — unit tests use `FakeGoogleDriveApi` (hand-written in-memory `IGoogleDriveApi`) for Drive/allow-list tests, and `Moq` for `SecretsManagerTokenStore` (its dependency, `IAmazonSecretsManager`, is too large an interface to hand-write a fake for).
- **`Dockerfile`** (repo root) builds/runs `Host.Web` — the artifact reused for EC2/ECS/App Runner. `src/McpGoogleDrive.Host.Lambda/Dockerfile` is a separate, Lambda-specific container image (different base image, different entry point contract) — don't conflate the two.
- **`template.yaml`** — a SAM template for deploying `Host.Lambda` behind a Function URL in `RESPONSE_STREAM` invoke mode (required for MCP's Streamable HTTP transport over Lambda).

## Status

See [docs/steps.md](docs/steps.md) for the full log. Every step of the original plan (scaffold → Core Drive/auth/setup → MCP tools → Host.Stdio → Host.Web → Dockerfile → AWS) is implemented and builds/tests pass (`dotnet build`, `dotnet test` — 20/20). What's genuinely unverified, and documented as such rather than assumed working: an actual OAuth sign-in / Drive tool call against a real Google account (needs a user's own Google Cloud credentials); `docker build`/`docker run` (Docker isn't installed in this dev environment); and everything AWS-specific — the Lambda container base image tag, `sam deploy`, and Function URL response-streaming behavior (no AWS credentials were used or available here).

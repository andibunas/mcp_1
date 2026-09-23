# Deploying to AWS

Two paths, covering the plan's AWS options (see [PLAN.md](PLAN.md)'s "AWS options at a glance").
**Neither has been deployed or tested against a real AWS account from this environment** — no AWS
credentials or account access were used to write this. Treat both as a documented starting point,
not a verified recipe; validate each step against current AWS documentation as you go.

## Shared prerequisite: get a refresh token into Secrets Manager

Both paths use `SecretsManagerTokenStore` (`src/McpGoogleDrive.Core/Auth/SecretsManagerTokenStore.cs`)
instead of `FileTokenStore`, because neither a Lambda execution environment nor (typically) an EC2
instance has the kind of durable, browser-reachable local disk `FileTokenStore` assumes. Neither
can complete the OAuth "installed app" browser sign-in itself, so:

1. Run `dotnet run --project src/McpGoogleDrive.Host.Stdio -- setup` locally (with a browser) to
   sign in and grant folder access, same as any local setup — see [setup.md](setup.md).
2. Copy the resulting token out of `~/.mcp-google-drive/tokens/default-user.json` into a Secrets
   Manager secret named `mcp-google-drive/default-user` (matching `SecretsManagerTokenStore`'s
   default `secretPrefix`), in the same AWS account/region the server will run in:

   ```
   aws secretsmanager create-secret \
     --name mcp-google-drive/default-user \
     --secret-string file://~/.mcp-google-drive/tokens/default-user.json
   ```

   `GoogleWebAuthorizationBroker` (used by `GoogleAuthService`) refreshes silently when it finds a
   cached credential and only opens a browser when it doesn't — so once this secret exists, the
   deployed server authorizes without any interaction.
3. Grant the server's execution role (Lambda function role, or EC2 instance role) permission to
   read/write `mcp-google-drive/*` secrets — `template.yaml` shows the IAM policy shape for Lambda.

`GoogleAuth:ClientId`/`ClientSecret`/`PickerApiKey` and `DriveAccess:AllowedFolderIds` still come
from config (env vars, e.g. `GoogleAuth__ClientId`) — they're OAuth client credentials and folder
IDs, not per-user secrets, so they don't need Secrets Manager.

## Option A: EC2 (or ECS/Fargate/App Runner) — the Docker image

Reuses the `Dockerfile` from step 6 — no separate image for this path.

1. Build and push the image to a registry the target can pull from (e.g. ECR):
   ```
   docker build -t <your-ecr-repo>:latest .
   docker push <your-ecr-repo>:latest
   ```
2. On EC2: launch an instance with an IAM instance role granted the Secrets Manager policy above,
   install Docker, and run:
   ```
   docker run -p 8080:8080 \
     -e GoogleAuth__ClientId=... -e GoogleAuth__ClientSecret=... \
     -e DriveAccess__AllowedFolderIds__0=<folder-id> \
     <your-ecr-repo>:latest
   ```
   Open port 8080 in the instance's security group to whatever should reach it.
3. On ECS/Fargate or App Runner: point the service at the same image, with the same env vars and
   an execution/task role granted the same Secrets Manager policy. These manage restarts/scaling
   for you; EC2 is the simpler mental model but you own the instance.

## Option B: Lambda — `Host.Lambda` behind a streaming Function URL

`src/McpGoogleDrive.Host.Lambda` wraps the same MCP wiring as `Host.Web`, but via
`Amazon.Lambda.AspNetCoreServer.Hosting`'s `AddAWSLambdaHosting`, which runs the app through the
Lambda runtime when Lambda's environment variables are present and via Kestrel otherwise — same
`Program.cs` either way.

**Why streaming matters**: MCP's Streamable HTTP transport needs a long-lived/streaming response.
Lambda Function URLs only support that in `RESPONSE_STREAM` invoke mode. `Program.cs` sets
`options.EnableResponseStreaming = true` to match — if the Function URL isn't also configured for
`RESPONSE_STREAM` (see `template.yaml`), or if the SDK's streaming behavior doesn't hold up
end-to-end through this specific path once actually tested, fall back to
`EnableResponseStreaming = false` (buffered responses) rather than leaving a mismatch between the
app and the Function URL config.

`template.yaml` is a SAM template covering:
- A container-image Lambda function (`src/McpGoogleDrive.Host.Lambda/Dockerfile` — **check that
  Dockerfile's caution comment about the base image tag before building**).
- A Function URL with `InvokeMode: RESPONSE_STREAM`.
- An IAM policy for the `mcp-google-drive/*` Secrets Manager prefix.

Rough deploy flow (needs the AWS SAM CLI and a configured AWS account):
```
sam build
sam deploy --guided
```

The Function URL in this template uses `AuthType: AWS_IAM` (callers must sign requests with AWS
credentials) rather than public/unauthenticated access — appropriate for a server with write
access to your Drive folders. Adjust the MCP client configuration accordingly.

## Known gaps

- Neither path has actually been built/deployed/tested here — Docker isn't installed in this dev
  environment, and no AWS credentials were available or used.
- The Lambda container base image tag (`public.ecr.aws/lambda/dotnet:10`) is unverified.
- The streaming behavior through a Function URL in `RESPONSE_STREAM` mode is unverified against
  the actual `ModelContextProtocol` HTTP transport.

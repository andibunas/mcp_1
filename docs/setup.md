# Setup: Google Cloud project + folder access

This server needs its own Google Cloud project (OAuth credentials and, optionally, a Picker
API key) plus a list of Drive folder IDs it's allowed to touch. This doc covers both.

## 1. Google Cloud project

1. Create a project (or reuse one) at [console.cloud.google.com](https://console.cloud.google.com).
2. Enable **Google Drive API** (APIs & Services → Library).
3. Create OAuth credentials (APIs & Services → Credentials → Create Credentials → OAuth client ID):
   - Application type: **Desktop app**.
   - Note the generated **Client ID** and **Client secret**.
4. If you want the interactive folder picker (recommended for local setup, see below), also:
   - Enable **Google Picker API** (APIs & Services → Library).
   - Create an **API key** (Credentials → Create Credentials → API key). Restrict it to the
     Picker API.
5. Put these values into your host's config (`appsettings.json`, environment variables, or
   .NET user-secrets — never commit real values):

   ```jsonc
   {
     "GoogleAuth": {
       "ClientId": "...",
       "ClientSecret": "...",
       "PickerApiKey": "..." // omit if you're only using manual folder-ID entry
     }
   }
   ```

## 2. Granting folder access

The server enforces a folder allow-list (`DriveAccess:AllowedFolderIds`) in `McpGoogleDrive.Core`
— nothing outside those folders (or their subfolders) is reachable, regardless of what the OAuth
scope otherwise permits. There are two ways to populate that list:

### Interactive route (recommended, local machine with a browser)

Not yet wired into a host command (that's step 4 of [docs/PLAN.md](PLAN.md)) — for now this is
invoked directly by calling `InteractiveSetupService.RunAsync()` from `McpGoogleDrive.Core`.
It will:
1. Open your browser for Google sign-in (standard OAuth "installed app" flow).
2. Open the Google Picker so you can visually select one or more folders.
3. Save the selected folder IDs to `~/.mcp-google-drive/allowed-folders.json`.

Once a host calls `ConfigurationBuilderExtensions.AddAllowedFoldersFile()` during startup (step 4),
that file is loaded automatically into `DriveAccess:AllowedFolderIds` — nothing further to configure.

### Manual route (needed for headless/AWS setups, or if you'd rather not use the Picker)

1. Open the folder in Drive and copy its ID out of the URL:
   `https://drive.google.com/drive/folders/<FOLDER_ID>` — the part after `/folders/` is the ID.
2. Make sure the account (or, for AWS deployments, the service account) the server signs in as
   actually has access to that folder — share the folder with that account's email in Drive if
   it isn't already the owner. A folder ID alone doesn't grant access; Drive sharing still applies.
3. Add the ID to config:

   ```jsonc
   {
     "DriveAccess": {
       "AllowedFolderIds": ["<FOLDER_ID>"]
     }
   }
   ```

## Where tokens are stored

Refresh tokens are persisted via `ITokenStore` (see `McpGoogleDrive.Core/Auth/`):
- Local hosts (stdio, local Web) use `FileTokenStore`, defaulting to `~/.mcp-google-drive/tokens`.
- AWS-hosted deployments will use a `SecretsManagerTokenStore` (AWS Secrets Manager) — planned
  for the AWS build phase, not implemented yet.

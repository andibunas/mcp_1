# TestServiceAccount

A console app to test Google Drive API usage with a service account.

## Setup

1. **Obtain a service account key file** from Google Cloud Console:
   - Go to [Google Cloud Console](https://console.cloud.google.com/)
   - Create a new service account or use an existing one
   - Download the JSON key file
   - Save it as `service-account-key.json` in this directory (or pass the path as an argument)

2. **Share Google Drive folders** with the service account:
   - Copy the service account email from the JSON key file (looks like `xxx@xxx.iam.gserviceaccount.com`)
   - Share the folders you want to access with this email address

## Usage

```bash
# Using default path (service-account-key.json in current directory)
dotnet run

# Using custom path to key file
dotnet run /path/to/service-account-key.json
```

## What it does

The app uses the `GoogleDriveFolderLister` class to:
- Authenticate with Google Drive API using a service account
- List all folders accessible to the service account
- Display folder details including name, ID, creation date, and owner information

## Project Structure

- `Program.cs` - Entry point
- `GoogleDriveFolderLister.cs` - Class that handles Google Drive API calls
- `TestServiceAccount.csproj` - Project configuration with Google API dependencies

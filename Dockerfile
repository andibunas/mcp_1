# Builds and runs McpGoogleDrive.Host.Web. This same image is the deployment artifact for
# EC2 (`docker run` on an instance), ECS/Fargate, and App Runner — see docs/steps.md step 7.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, on just the .csproj files, so dependency downloads are cached across builds
# that only change source code.
COPY McpGoogleDrive.slnx ./
COPY src/McpGoogleDrive.Core/McpGoogleDrive.Core.csproj src/McpGoogleDrive.Core/
COPY src/McpGoogleDrive.Host.Web/McpGoogleDrive.Host.Web.csproj src/McpGoogleDrive.Host.Web/
RUN dotnet restore src/McpGoogleDrive.Host.Web/McpGoogleDrive.Host.Web.csproj

COPY src/McpGoogleDrive.Core/ src/McpGoogleDrive.Core/
COPY src/McpGoogleDrive.Host.Web/ src/McpGoogleDrive.Host.Web/
RUN dotnet publish src/McpGoogleDrive.Host.Web/McpGoogleDrive.Host.Web.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# FileTokenStore defaults to ~/.mcp-google-drive; mount a volume there (or at $HOME_OVERRIDE
# if you change HOME) with a token obtained from a local `Host.Stdio -- setup` run, since this
# container has no browser to complete the OAuth "installed app" flow itself. See docs/setup.md.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "McpGoogleDrive.Host.Web.dll"]

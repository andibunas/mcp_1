# Container image for McpGoogleDrive.Host.Lambda, deployed via a Lambda Function URL.
#
# CAUTION, unverified: this has not been built or deployed against a real AWS account. In
# particular, check that the `public.ecr.aws/lambda/dotnet:10` base image tag below actually
# exists before using it — AWS's Lambda .NET base images (gallery.ecr.aws/lambda/dotnet) have
# historically only tracked actively-supported LTS releases, and .NET 10 support may lag. If the
# tag doesn't exist yet, either check https://gallery.ecr.aws/lambda/dotnet for the current tags,
# or retarget this project to net8.0 (change <TargetFramework> in the .csproj) until it does.
# See docs/deploy-aws.md.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY McpGoogleDrive.slnx ./
COPY src/McpGoogleDrive.Core/McpGoogleDrive.Core.csproj src/McpGoogleDrive.Core/
COPY src/McpGoogleDrive.Host.Lambda/McpGoogleDrive.Host.Lambda.csproj src/McpGoogleDrive.Host.Lambda/
RUN dotnet restore src/McpGoogleDrive.Host.Lambda/McpGoogleDrive.Host.Lambda.csproj

COPY src/McpGoogleDrive.Core/ src/McpGoogleDrive.Core/
COPY src/McpGoogleDrive.Host.Lambda/ src/McpGoogleDrive.Host.Lambda/
RUN dotnet publish src/McpGoogleDrive.Host.Lambda/McpGoogleDrive.Host.Lambda.csproj \
    -c Release -o /var/task --no-restore

FROM public.ecr.aws/lambda/dotnet:10 AS runtime
COPY --from=build /var/task /var/task
CMD ["McpGoogleDrive.Host.Lambda"]

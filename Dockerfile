# Web app image. Build from the repository root:  docker build -t three-way-match-web .
# Multi-stage: the SDK image builds, the small ASP.NET runtime image runs. The "extra" chiseled variant has no shell or package
# manager, runs as a non-root user, and includes ICU, which the Estonian number and date handling needs.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files only, so this layer is cached until a package changes.
COPY .editorconfig Directory.Build.props Directory.Packages.props ./
COPY src/ThreeWayMatch.Domain/ThreeWayMatch.Domain.csproj src/ThreeWayMatch.Domain/
COPY src/ThreeWayMatch.Infrastructure/ThreeWayMatch.Infrastructure.csproj src/ThreeWayMatch.Infrastructure/
COPY src/ThreeWayMatch.Web/ThreeWayMatch.Web.csproj src/ThreeWayMatch.Web/
RUN dotnet restore src/ThreeWayMatch.Web/ThreeWayMatch.Web.csproj

COPY src/ src/
RUN dotnet publish src/ThreeWayMatch.Web/ThreeWayMatch.Web.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app .

# The chiseled images run as the built-in non-root user "app" and listen on 8080.
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "ThreeWayMatch.Web.dll"]

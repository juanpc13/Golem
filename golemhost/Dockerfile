# GolemHost: the golem's Puppeteer host, running on its own Ubuntu.
# Build context is the repo root (the host references the domain project).
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY golemhost/nuget.config golemhost/GolemHost.csproj golemhost/
COPY golemhost/localfeed/ golemhost/localfeed/
COPY golemdomain/Golem.Domain.csproj golemdomain/
RUN dotnet restore golemhost/GolemHost.csproj
COPY golemdomain/ golemdomain/
COPY golemhost/ golemhost/
RUN dotnet publish golemhost/GolemHost.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "GolemHost.dll"]

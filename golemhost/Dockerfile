# GolemHost: the golem's Puppeteer brain, running on its own Ubuntu.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY nuget.config GolemHost.csproj ./
COPY localfeed/ localfeed/
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "GolemHost.dll"]

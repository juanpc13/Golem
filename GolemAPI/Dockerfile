# GolemAPI: the golem's Puppeteer host, running on its own Ubuntu.
# Build context is the repo root (the API references the domain project).
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY GolemAPI/nuget.config GolemAPI/GolemAPI.csproj GolemAPI/
COPY GolemAPI/localfeed/ GolemAPI/localfeed/
COPY GolemDomain/GolemDomain.csproj GolemDomain/
RUN dotnet restore GolemAPI/GolemAPI.csproj
COPY GolemDomain/ GolemDomain/
COPY GolemAPI/ GolemAPI/
RUN dotnet publish GolemAPI/GolemAPI.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "GolemAPI.dll"]

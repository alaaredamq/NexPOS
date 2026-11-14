# Coduo POS Cloud. Build from the REPOSITORY ROOT (the project references Sync, Infrastructure, Core, Common):
#   docker build -f Cloud/Dockerfile -t nexpos-cloud .
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Common/Common.csproj Common/
COPY Core/Core.csproj Core/
COPY Infrastructure/Infrastructure.csproj Infrastructure/
COPY Sync/Sync.csproj Sync/
COPY Cloud/Cloud.csproj Cloud/
RUN dotnet restore Cloud/Cloud.csproj
COPY Common/ Common/
COPY Core/ Core/
COPY Infrastructure/ Infrastructure/
COPY Sync/ Sync/
COPY Cloud/ Cloud/
RUN dotnet publish Cloud/Cloud.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
# Control database, per-client mirror files and encryption keys all live here: mount a persistent volume and back it up.
ENV Cloud__DataRoot=/data
ENV ASPNETCORE_URLS=http://+:8080
RUN mkdir -p /data && chown -R $APP_UID /data
USER $APP_UID
EXPOSE 8080
VOLUME /data
ENTRYPOINT ["dotnet", "Cloud.dll"]

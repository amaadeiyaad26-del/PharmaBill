FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY PharmaBill.Core/PharmaBill.Core.csproj PharmaBill.Core/
COPY PharmaBill.CloudSync/PharmaBill.CloudSync.csproj PharmaBill.CloudSync/
RUN dotnet restore PharmaBill.CloudSync/PharmaBill.CloudSync.csproj
COPY PharmaBill.Core/ PharmaBill.Core/
COPY PharmaBill.CloudSync/ PharmaBill.CloudSync/
RUN dotnet publish PharmaBill.CloudSync/PharmaBill.CloudSync.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /data/blobs && chown -R $APP_UID /data
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080 \
    Cloud__BlobRoot=/data/blobs
EXPOSE 8080
ENTRYPOINT ["dotnet", "PharmaBill.CloudSync.dll"]

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Project root is the Docker build context
COPY NuGet.config ./
COPY LiveAuthCore.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "LiveAuthCore.dll"]

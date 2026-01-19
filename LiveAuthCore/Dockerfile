FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy NuGet config FIRST (critical)
COPY NuGet.config ./

# Copy csproj and restore
COPY LiveAuthCore.csproj ./
RUN dotnet restore

# Copy the rest and publish
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "LiveAuthCore.dll"]

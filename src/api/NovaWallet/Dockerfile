# Build context is this directory (src/api/NovaWallet), containing all three projects.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["NovaWallet.Api/NovaWallet.Api.csproj", "NovaWallet.Api/"]
COPY ["NovaWallet.Core/NovaWallet.Core.csproj", "NovaWallet.Core/"]
COPY ["NovaWallet.Infrastructure/NovaWallet.Infrastructure.csproj", "NovaWallet.Infrastructure/"]
RUN dotnet restore "NovaWallet.Api/NovaWallet.Api.csproj"
COPY . .
RUN dotnet build "NovaWallet.Api/NovaWallet.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "NovaWallet.Api/NovaWallet.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "NovaWallet.Api.dll"]

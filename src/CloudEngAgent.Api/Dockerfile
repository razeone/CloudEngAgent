# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Packages.props", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["nuget.config", "./"]
COPY ["src/CloudEngAgent.Api/CloudEngAgent.Api.csproj", "src/CloudEngAgent.Api/"]
COPY ["src/CloudEngAgent.Application/CloudEngAgent.Application.csproj", "src/CloudEngAgent.Application/"]
COPY ["src/CloudEngAgent.Domain/CloudEngAgent.Domain.csproj", "src/CloudEngAgent.Domain/"]
COPY ["src/CloudEngAgent.Infrastructure/CloudEngAgent.Infrastructure.csproj", "src/CloudEngAgent.Infrastructure/"]

RUN dotnet restore "src/CloudEngAgent.Api/CloudEngAgent.Api.csproj"

COPY src/ src/
RUN dotnet publish "src/CloudEngAgent.Api/CloudEngAgent.Api.csproj" \
        -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/nightly/aspnet:10.0 AS runtime
WORKDIR /app

# Run as non-root
RUN groupadd -r app && useradd -r -g app -u 10001 app
USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish .

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD wget -qO- http://127.0.0.1:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "CloudEngAgent.Api.dll"]

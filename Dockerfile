# syntax=docker/dockerfile:1
ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Restore against the project files alone so the layer caches across source edits.
COPY BudgetAssistant.slnx ./
COPY src/BudgetAssistant.Core/BudgetAssistant.Core.csproj  src/BudgetAssistant.Core/
COPY src/BudgetAssistant.Web/BudgetAssistant.Web.csproj    src/BudgetAssistant.Web/
COPY tests/BudgetAssistant.Core.Tests/BudgetAssistant.Core.Tests.csproj tests/BudgetAssistant.Core.Tests/
# The embedding model is vendored, and the Web project's build points at it by path, so it
# must be present before restore/publish. This is why the build needs no network for it.
COPY models/ models/
RUN dotnet restore src/BudgetAssistant.Web/BudgetAssistant.Web.csproj

COPY . .
RUN dotnet publish src/BudgetAssistant.Web/BudgetAssistant.Web.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

# ONNX Runtime links against libgomp for its threading; it is not in the aspnet image.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgomp1 \
    && rm -rf /var/lib/apt/lists/*

# Run as a non-root user. The base image provides `app` (uid 1654).
USER app

COPY --from=build --chown=app:app /app .

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_gcServer=0
EXPOSE 8080

ENTRYPOINT ["dotnet", "BudgetAssistant.Web.dll"]

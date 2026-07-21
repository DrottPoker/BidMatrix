FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /source

COPY global.json Directory.Build.props BidMatrix.slnx ./
COPY src/backend/BidMatrix.Api/BidMatrix.Api.csproj src/backend/BidMatrix.Api/
COPY src/backend/BidMatrix.Application/BidMatrix.Application.csproj src/backend/BidMatrix.Application/
COPY src/backend/BidMatrix.Contracts/BidMatrix.Contracts.csproj src/backend/BidMatrix.Contracts/
COPY src/backend/BidMatrix.Database/BidMatrix.Database.csproj src/backend/BidMatrix.Database/
COPY src/backend/BidMatrix.Domain/BidMatrix.Domain.csproj src/backend/BidMatrix.Domain/
COPY src/backend/BidMatrix.Infrastructure/BidMatrix.Infrastructure.csproj src/backend/BidMatrix.Infrastructure/
RUN dotnet restore src/backend/BidMatrix.Api/BidMatrix.Api.csproj

COPY src/backend/ src/backend/
RUN dotnet publish src/backend/BidMatrix.Api/BidMatrix.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10 AS runtime
WORKDIR /app
COPY --from=build /app ./
COPY tests/fixtures/engineering/repository /var/lib/bidmatrix/engineering/repository

ENV ASPNETCORE_HTTP_PORTS=8080
RUN apt-get update \
    && apt-get install --yes --no-install-recommends git \
    && rm -rf /var/lib/apt/lists/* \
    && git -C /var/lib/bidmatrix/engineering/repository init --initial-branch=main \
    && git -C /var/lib/bidmatrix/engineering/repository config user.email sandbox@bidmatrix.invalid \
    && git -C /var/lib/bidmatrix/engineering/repository config user.name "BidMatrix Sandbox Fixture" \
    && git -C /var/lib/bidmatrix/engineering/repository add README.md \
    && git -C /var/lib/bidmatrix/engineering/repository commit -m "Create engineering sandbox fixture" \
    && git -C /var/lib/bidmatrix/engineering/repository tag fixture-v1 \
    && mkdir -p /var/lib/bidmatrix/data-protection /var/lib/bidmatrix/engineering/worktrees \
    && chown -R $APP_UID:$APP_UID /var/lib/bidmatrix
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "BidMatrix.Api.dll"]

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

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "BidMatrix.Api.dll"]

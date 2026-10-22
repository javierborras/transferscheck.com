FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/TransfersCheck.Core/TransfersCheck.Core.csproj src/TransfersCheck.Core/
COPY src/TransfersCheck.Infrastructure/TransfersCheck.Infrastructure.csproj src/TransfersCheck.Infrastructure/
COPY src/TransfersCheck.Api/TransfersCheck.Api.csproj src/TransfersCheck.Api/
RUN dotnet restore src/TransfersCheck.Api/TransfersCheck.Api.csproj
COPY src/ src/
RUN dotnet publish src/TransfersCheck.Api/TransfersCheck.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "TransfersCheck.Api.dll"]

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY TradingAgent.slnx ./
COPY src/TradingAgent/TradingAgent.csproj src/TradingAgent/
RUN dotnet restore src/TradingAgent/TradingAgent.csproj

COPY . .
RUN dotnet publish src/TradingAgent/TradingAgent.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish ./
RUN mkdir -p /app/data

EXPOSE 8080
ENTRYPOINT ["dotnet", "TradingAgent.dll"]

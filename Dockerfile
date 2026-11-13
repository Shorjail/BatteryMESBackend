FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["BatteryMes.sln", "./"]
COPY ["BatteryMes.Api/BatteryMes.Api.csproj", "BatteryMes.Api/"]
COPY ["BatteryMes.Core/BatteryMes.Core.csproj", "BatteryMes.Core/"]
COPY ["BatteryMes.Infrastructure/BatteryMes.Infrastructure.csproj", "BatteryMes.Infrastructure/"]

RUN dotnet restore "BatteryMes.Api/BatteryMes.Api.csproj"

# Copy full source code and publish
COPY . .
WORKDIR "/src/BatteryMes.Api"
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "BatteryMes.Api.dll"]
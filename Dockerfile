FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy everything into the container build context
COPY . .

# DEBUG: List all files in the container to verify exact paths and casing
RUN ls -la

# Restore and publish the API project using the solution
RUN dotnet restore "BatteryMes.slnx"
RUN dotnet publish "BatteryMes.Api/BatteryMes.Api.csproj" -c Release -o /app/publish

# Runtime stage for .NET 10
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

# Render injects PORT at runtime; ASP.NET Core reads it automatically.
EXPOSE 10000

ENTRYPOINT ["dotnet", "BatteryMes.Api.dll"]
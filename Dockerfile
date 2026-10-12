FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so this layer is cached until a project file changes.
COPY BookingService.Api/BookingService.Api.csproj BookingService.Api/
COPY BookingService.Application/BookingService.Application.csproj BookingService.Application/
COPY BookingService.Domain/BookingService.Domain.csproj BookingService.Domain/
COPY BookingService.Infrastructure/BookingService.Infrastructure.csproj BookingService.Infrastructure/
RUN dotnet restore BookingService.Api/BookingService.Api.csproj

COPY . .
RUN dotnet publish BookingService.Api/BookingService.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "BookingService.Api.dll"]

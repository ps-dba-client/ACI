# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/AcaOtelLab/AcaOtelLab.csproj ./AcaOtelLab/
RUN dotnet restore ./AcaOtelLab/AcaOtelLab.csproj
COPY src/AcaOtelLab/ ./AcaOtelLab/
WORKDIR /src/AcaOtelLab
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "AcaOtelLab.dll"]

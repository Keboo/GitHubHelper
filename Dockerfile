#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "GitHubHelper/GitHubHelper.csproj"
WORKDIR "/src/GitHubHelper"
RUN dotnet build "GitHubHelper.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "GitHubHelper.csproj" -c Release -o /app/publish

FROM base AS final
COPY --from=publish /app/publish /app
ENTRYPOINT ["dotnet", "/app/GitHubHelper.dll"]
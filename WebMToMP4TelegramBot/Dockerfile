FROM mcr.microsoft.com/dotnet/core/aspnet:3.1 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build
WORKDIR /src
COPY ["WebMToMP4TelegramBot/WebMToMP4TelegramBot.csproj", "WebMToMP4TelegramBot/"]
RUN dotnet restore "WebMToMP4TelegramBot/WebMToMP4TelegramBot.csproj"
COPY . .
WORKDIR "/src/WebMToMP4TelegramBot"
RUN dotnet build "WebMToMP4TelegramBot.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "WebMToMP4TelegramBot.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=mwader/static-ffmpeg:4.2.1 /ffmpeg /ffprobe /usr/local/bin/
ENTRYPOINT ["dotnet", "WebMToMP4TelegramBot.dll"]

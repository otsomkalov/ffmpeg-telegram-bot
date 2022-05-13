FROM mcr.microsoft.com/dotnet/runtime-deps:6.0-alpine AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["Bot/Bot.csproj", "Bot/"]
RUN dotnet restore "Bot/Bot.csproj" --runtime alpine-x64
COPY . .
WORKDIR "/src/Bot"
RUN dotnet build "Bot.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Bot.csproj" -c Release -o /app/publish \
    --runtime alpine-x64 \
    --self-contained true \
    /p:PublishSingleFile=true

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
RUN apk -U upgrade
RUN apk add ffmpeg
ENTRYPOINT ["dotnet", "Bot.dll"]

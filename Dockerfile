#Builder
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /source
COPY osu-collaboration-bot ./
RUN dotnet publish "./osu-collaboration-bot.csproj" -c Release -a "$TARGETARCH" -p:PublishSingleFile=true -o /app/publish

#Runner
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish /app/
CMD [ "./osu-collaboration-bot" ]

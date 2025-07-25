#Builder
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /source
COPY osu-collaboration-bot ./
#RUN dotnet restore "./osu-collaboration-bot.csproj"
RUN dotnet publish "./osu-collaboration-bot.csproj" -c Release -r linux-${TARGETARCH} -p:PublishSingleFile=true -o /app/publish

#Runner
FROM mcr.microsoft.com/dotnet/runtime:8.0
ARG TARGETARCH
WORKDIR /app
COPY --from=build /app/publish /app/
CMD [ "./osu-collaboration-bot" ]

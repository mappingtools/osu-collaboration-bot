#Builder
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
ARG TARGETARCH
WORKDIR /source
COPY osu-collaboration-bot ./
#RUN dotnet restore "./osu-collaboration-bot.csproj"
RUN dotnet publish "./osu-collaboration-bot.csproj" -c Release -r linux-${TARGETARCH} -p:PublishSingleFile=true

#Runner
FROM mcr.microsoft.com/dotnet/runtime:6.0
ARG TARGETARCH
WORKDIR /app
COPY --from=build /source/bin/Release/net6.0/linux-${TARGETARCH}/publish /app/
CMD [ "./osu-collaboration-bot" ]

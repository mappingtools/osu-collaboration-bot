# osu! Collaboration Bot
This is a Discord bot meant to help manage big osu! mapping collabs in Discord.
It automates various tedious tasks that collab organisers have to deal with, especially when you have many people working on a beatmap.

Built using [Mapping Tools Core](https://github.com/OliBomby/Mapping_Tools_Core), [Discord.Net](https://github.com/discord-net/Discord.Nethttps://github.com/discord-net/Discord.Net), and a backing PostgreSQL database.

## Features:
- Creation and deletion of mapping projects
- Adding or removing members of a project
- Configurable permissions
- Beatmap part division and claiming
- Merging submitted parts and posting the latest version of the beatmap
- Project channel and role creation
- Reminders about upcoming deadlines
- Part picking priority system
- Generation of beatmap tags and description
- CSV export of annotated beatmap parts for storyboarding
- Mapped drain time calculation
- Generation of difficulty names

For a full list of commands use `/help` in a server where the bot is present.


# Adding this bot to your server
You can add this bot to your server by clicking [here](https://discord.com/api/oauth2/authorize?client_id=863480217958612992&permissions=534992251984&scope=bot%20applications.commands).

Make sure your collab category permissions are set up correctly. The bot needs at least the `View Channels`, `Manage Channels`, `Manage Permissions`, `Send Messages and Create Posts`, `Attach Files`, and `Mention @everyone, @here and All Roles` permissions in this category.

Lastly use `/adminguide` and `/help guild` to get a list of commands that can be used to configure the bot for your server.

# To run this bot
- Create a PostgreSQL database with the schema from database.sql
- Add your discord bot token and PostgreSQL connection string to `appsettings.json` OR add them as environment variables:
```
App__ConnectionString=<YOUR CONNECTION STRING>
App__Token=<YOUR DISCORD BOT TOKEN>
```
- Compile and run the program with .NET 6.0

# Running with Docker
You can build a Docker image with the provided Docker compose file.
To do this, you first have to edit `docker-compose.yml` and add in your Discord bot token in the environment variables where it says `<BOT TOKEN HERE/>`.
You can also change the output location for the logs from `usr/share/osu-collab-bot/logs` to somewhere else.

If you want to be able to access the PostgreSQL database externally, uncomment the `ports` section of the Docker compose file and change the password of the database to something secure. Don't forget to update the password in the connection string environment variable.

If you are running on a server with ARM architecture, you'll have to change the `Dockerfile` to change `linux-x64` to `linux-arm64` in the `dotnet publish` and `COPY` commands.

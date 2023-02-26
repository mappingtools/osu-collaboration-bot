# osu! Collaboration Bot
This is a discord bot meant to help manage osu! mapping collabs in discord.
It automates various tedious tasks that collab organisers have to deal with.

Built using [Mapping Tools Core](https://github.com/OliBomby/Mapping_Tools_Core), [Discord.Net](https://github.com/discord-net/Discord.Nethttps://github.com/discord-net/Discord.Net), and a backing PostgreSQL database.

## Features:
- Creation and deletion of mapping projects
- Adding or removing members of a project
- Configurable permissions
- Beatmap part division and claiming
- Automatic project channel and role creation
- Automatic reminders about upcoming deadlines
- Part picking priority system
- Automatically merging submitted parts and posting the latest version of the beatmap
- Automatic generation of beatmap tags and description
- CSV export of annotated beatmap parts for storyboarding
- Automatic mapped drain time calculation

For a full list of commands use /help in a server where the bot is present.

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

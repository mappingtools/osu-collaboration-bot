# To run this bot
- Create a PostgreSQL database with the schema from database.sql
- Add your discord bot token and PostgreSQL connection string to `appsettings.json` OR add them as environment variables:
```
App__ConnectionString=<YOUR CONNECTION STRING>
App__Token=<YOUR DISCORD BOT TOKEN>
```
- Compile and run the program with .NET 5.0

# Running with Docker
You can build a Docker image with the provided Docker compose file.
To do this, you first have to edit `docker-compose.yml` and add in your Discord bot token in the environment variables where it says `<BOT TOKEN HERE/>`.
You can also change the output location for the logs from `usr/share/osu-collab-bot/logs` to somewhere else.

If you want to be able to access the PostgreSQL database externally, uncomment the `ports` section of the Docker compose file and change the password of the database to something secure. Don't forget to update the password in the connection string environment variable.

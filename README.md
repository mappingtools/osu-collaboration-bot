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

For a full list of commands see the [API Documentation](#api-documentation).


## Adding this bot to your server
You can add this bot to your server by clicking [here](https://discord.com/api/oauth2/authorize?client_id=863480217958612992&permissions=534992251984&scope=bot%20applications.commands).

Make sure your collab category permissions are set up correctly. The bot needs at least the `View Channels`, `Manage Channels`, `Manage Permissions`, `Send Messages and Create Posts`, `Attach Files`, and `Mention @everyone, @here and All Roles` permissions in this category.

Lastly use `/adminguide` and `/help guild` to get a list of commands that can be used to configure the bot for your server.

## To run this bot
- Create a PostgreSQL database with the schema from database.sql
- Add your discord bot token and PostgreSQL connection string to `appsettings.json` OR add them as environment variables:
```
App__ConnectionString=<YOUR CONNECTION STRING>
App__Token=<YOUR DISCORD BOT TOKEN>
```
- Compile and run the program with .NET 6.0

## Running with Docker
You can build a Docker image with the provided Docker compose file.
To do this, you first have to edit `docker-compose.yml` and add in your Discord bot token in the environment variables where it says `<BOT TOKEN HERE/>`.
You can also change the output location for the logs from `usr/share/osu-collab-bot/logs` to somewhere else.

If you want to be able to access the PostgreSQL database externally, uncomment the `ports` section of the Docker compose file and change the password of the database to something secure. Don't forget to update the password in the connection string environment variable.

If you are running on a server with ARM architecture, you'll have to change the `Dockerfile` to change `linux-x64` to `linux-arm64` in the `dotnet publish` and `COPY` commands.

# API Documentation

**Table of Contents**

- [help](#help-help)
- [adminguide](#adminguide-adminguide)
- [collabguide](#collabguide-collabguide)
- [participantguide](#participantguide-participantguide)
- [list](#list-list)
- [info](#info-info)
- [members](#members-members)
- [joinproject](#joinproject-joinproject)
- [leaveproject](#leaveproject-leaveproject)
- [alias](#alias-alias)
- [tags](#tags-tags)
- [id](#id-id)
- [submitpart](#submitpart-submitpart)
- [claim](#claim-claim)
- [unclaim](#unclaim-unclaim)
- [done](#done-done)
- [diffname](#diffname-diffname)
- [blixys](#blixys-blixys)
- [Guild Module](#guild-module-guild)
  - [init](#init-guild-init)
  - [collabcategory](#collabcategory-guild-collabcategory)
  - [maxcollabs](#maxcollabs-guild-maxcollabs)
  - [inactivitytimer](#inactivitytimer-guild-inactivitytimer)
  - [createroles](#createroles-guild-createroles)
- [Project Module](#project-module-project)
  - [setbasefile](#setbasefile-project-setbasefile)
  - [getbasefile](#getbasefile-project-getbasefile)
  - [create](#create-project-create)
  - [delete](#delete-project-delete)
  - [setup](#setup-project-setup)
  - [add](#add-project-add)
  - [remove](#remove-project-remove)
  - [promote](#promote-project-promote)
  - [demote](#demote-project-demote)
  - [setowner](#setowner-project-setowner)
  - [alias](#alias-project-alias)
  - [tags](#tags-project-tags)
  - [gettags](#gettags-project-gettags)
  - [id](#id-project-id)
  - [priority](#priority-project-priority)
  - [generatepriorities](#generatepriorities-project-generatepriorities)
  - [rename](#rename-project-rename) 
  - [Options Module](#project-options-module-project-options)
    - [options](#options-project-options-options)
    - [role](#role-project-options-role)
    - [managerrole](#managerrole-project-options-managerrole)
    - [rolecolor](#rolecolor-project-options-rolecolor)
    - [description](#description-project-options-description)
    - [status](#status-project-options-status)
    - [maxassignments](#maxassignments-project-options-maxassignments)
    - [assignmentlifetime](#assignmentlifetime-project-options-assignmentlifetime)
    - [mainchannel](#mainchannel-project-options-mainchannel)
    - [infochannel](#infochannel-project-options-infochannel)
    - [deletioncleanup](#deletioncleanup-project-options-deletioncleanup)
- [Part Module](#part-module-part)
  - [list](#list-part-list)
  - [listunclaimed](#listunclaimed-part-listunclaimed)
  - [add](#add-part-add)
  - [rename](#rename-part-rename)
  - [start](#start-part-start)
  - [end](#end-part-end)
  - [status](#status-part-status)
  - [remove](#remove-part-remove)
  - [clear](#clear-part-clear)
  - [frombookmarks](#frombookmarks-part-frombookmarks)
  - [fromcsv](#fromcsv-part-fromcsv)
  - [tocsv](#tocsv-part-tocsv)
  - [todescription](#todescription-part-todescription)
- [Assignment Module](#assignment-module-asn)
  - [list](#list-asn-list)
  - [add](#add-asn-add)
  - [remove](#remove-asn-remove)
  - [deadline](#deadline-asn-deadline)
  - [draintimes](#draintimes-asn-draintimes)
- [Auto Update Module](#auto-update-module-au)
  - [list](#list-au-list)
  - [add](#add-au-add)
  - [remove](#remove-au-remove)
  - [cooldown](#cooldown-au-cooldown)
  - [mentions](#mentions-au-mentions)
  - [trigger](#trigger-au-trigger)

---

### help `/help`

Displays command information.

**Arguments:**
- `module` (string, optional): Look for a command in a specific module. Defaults to an empty string.

### adminguide `/adminguide`

Provides a guide for server admins on how to set up the bot.

### collabguide `/collabguide`

Provides a guide for collaboration organizers on how to set up a collaboration with the bot.

### participantguide `/participantguide`

Provides a guide for collaboration participants on how to use the bot.

**Arguments:**
- `project` (string, optional): The name of the project to replace occurrences of '[PROJECT NAME]' in the guide. Defaults to `null`.

### list `/list`

Lists all the projects on the server and their status.

### info `/info`

Shows general information about a project.

**Arguments:**
- `project` (string): The project.

### members `/members`

Lists all members of a project.

**Arguments:**
- `project` (string): The project.

### joinproject `/joinproject`

Allows you to become a member of a project that is looking for members.

**Arguments:**
- `project` (string): The project.

### leaveproject `/leaveproject`

Allows you to leave a project.

**Permissions Required:** Project Member

**Arguments:**
- `project` (string): The project.

### alias `/alias`

Changes your alias in a project.

**Permissions Required:** Project Member

**Arguments:**
- `project` (string): The project.
- `alias` (string): The new alias.

### tags `/tags`

Changes your tags in a project.

**Permissions Required:** Project Member

**Arguments:**
- `project` (string): The project.
- `tags` (string): The new tags.

### id `/id`

Changes your osu! profile ID in a project.

**Permissions Required:** Project Member

**Arguments:**
- `project` (string): The project.
- `id` (string): The new ID.

### submitpart `/submitpart`

Submits a part of a beatmap to a project.

**Permissions Required:** Project Member

**Arguments:**
- `project` (string): The project.
- `attachment` (Attachment): The part to submit as a .osu file.
- `part` (string, optional): The part name to submit to. Defaults to `null`.

### claim `/claim`

Claims one or more parts and assigns them to you.

**Permissions Required:** Project Member

**Arguments:**
- `project` (string): The project.
- `parts` (string[]): The parts to claim.

### unclaim `/unclaim`

Unclaims one or more parts and unassigns them.

**Permissions Required:** Project Member

**Arguments:**
- `project` (string): The project.
- `parts` (string[]): The parts to unclaim.

### done `/done`

Marks one or more parts as done.

**Permissions Required:** Project Member

**Arguments:**
- `project` (string): The project.
- `parts` (string[]): The parts to complete.

### diffname `/diffname`

Generates a random difficulty name.

**Arguments:**
- `wordcount` (int, optional): The number of words to use in the sentence. Must be between 1 and 200. Defaults to `-1`.

### blixys `/blixys`

Generates some inspiration.

**Arguments:**
- `wordcount` (int, optional): The number of words to use in the sentence. Must be between 1 and 200. Defaults to `-1`.

## Guild Module `/guild`

### init `/guild init`

Initializes compatibility with the server.

**Permissions Required:** Administrator

### collabcategory `/guild collabcategory`

Changes the category in which project channels will be automatically generated.

**Permissions Required:** Administrator

**Arguments:**
- `category` (ICategoryChannel): The category to set.

### maxcollabs `/guild maxcollabs`

Changes the maximum number of projects a regular member can create.

**Permissions Required:** Administrator

**Arguments:**
- `count` (int): The maximum number of projects.

### inactivitytimer `/guild inactivitytimer`

Changes the duration of inactivity after which a project will be deleted. If `null`, it will never be deleted..

**Permissions Required:** Administrator

**Arguments:**
- `time` (TimeSpan?, optional): The new inactivity timer duration (dd:hh:mm:ss:fff). Defaults to `null`

### createroles `/guild createroles`

Changes whether the setup command creates new roles.

**Permissions Required:** Administrator

**Arguments:**
- `value` (bool): Whether the setup command creates new roles.

## Project Module `/project`

### setbasefile `/project setbasefile`

Replaces the current beatmap state of the project with the attached `.osu` file.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `attachment` (Attachment): The .osu file to set as the base file.

### getbasefile `/project getbasefile`

Gets the current beatmap state of the project.

**Permissions Required:** Project Member

**Arguments:**
- `project` (string): The project.

### create `/project create`

Creates a new project.

**Arguments:**
- `project` (string): The name of the new project.

### delete `/project delete`

Deletes an existing project.

**Permissions Required:** Project Owner

**Arguments:**
- `project` (string): The project.

### setup `/project setup`

Automatically sets-up the project, complete with roles, channels, and update notifications.

**Permissions Required:** Project Owner

**Arguments:**
- `project` (string): The project.

### add `/project add`

Adds a new member to the project.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `user` (string): The user to add.

### remove `/project remove`

Removes a member from the project.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `user` (string): The user to remove.

### promote `/project promote`

Promotes a member to a manager of the project.

**Permissions Required:** Project Owner

**Arguments:**
- `project` (string): The project.
- `user` (string): The user to promote.

### demote `/project demote`

Demotes a manager to a regular member of the project.

**Permissions Required:** Project Owner

**Arguments:**
- `project` (string): The project.
- `user` (string): The user to demote.

### setowner `/project setowner`

Changes the owner of the project.

**Permissions Required:** Administrator

**Arguments:**
- `project` (string): The project.
- `user` (string): The new owner.

### alias `/project alias`

Changes the alias of a member of the project.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `user` (string): The member.
- `alias` (string): The new alias.

### tags `/project tags`

Changes the tags of a member of the project.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `user` (string): The member.
- `tags` (string): The new tags.

### gettags `/project gettags`

Gets all the tags of the project including aliases.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.

### id `/project id`

Changes the osu! profile ID of a member of the project.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `user` (string): The member.
- `id` (string): The new ID.

### priority `/project priority`

Changes the priority of a member of the project.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `user` (string): The member.
- `priority` (int): The new priority.

### generatepriorities `/project generatepriorities`

Automatically generates priorities for all members based on membership age.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `timeweight` (int, optional): The priority value of one day. Defaults to `1`.
- `replace` (bool, optional): Whether to replace all the existing priority values. Defaults to `false`.

### rename `/project rename`

Renames the project.

**Permissions Required:** Project Owner

**Arguments:**
- `project` (string): The old project name.
- `newname` (string): The new project name.

## Project Options Module `/project options`

### options `/project options options`

Configures several boolean project options.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `canclaim` (bool, optional): Whether members may claim parts on their own.
- `canjoin` (bool, optional): Whether anyone may join the project.
- `prioritypicking` (bool, optional): Whether priority picking is enabled.
- `partrestrictedupload` (bool, optional): Whether to restrict part submission to just the assigned parts.
- `doreminders` (bool, optional): Whether to automatically remind members about their deadlines.

### role `/project options role`

Changes the member role of a project and optionally assigns the new role to all members.

**Permissions Required:** Administrator

**Arguments:**
- `project` (string): The project.
- `role` (IRole): The new member role.
- `update` (bool, optional): Whether to update member roles and channel permissions. Defaults to `true`.

### managerrole `/project options managerrole`

Changes the manager role of the project and optionally assigns the new role to all managers.

**Permissions Required:** Administrator

**Arguments:**
- `project` (string): The project.
- `role` (IRole): The new manager role.
- `update` (bool, optional): Whether to update manager roles and channel permissions. Defaults to `true`.

### rolecolor `/project options rolecolor`

Changes the color of the roles of the project.

**Permissions Required:** Administrator

**Arguments:**
- `project` (string): The project.
- `color` (Color): The new color as Hex code.

### description `/project options description`

Changes the description of the project.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `description` (string): The new description.

### status `/project options status`

Changes the status of the project.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `status` (ProjectStatus): The new status.

### maxassignments `/project options maxassignments`

Changes the maximum number of allowed assignments for members of the project.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `maxassignments` (int?): The new maximum number of allowed assignments (can be null).

### assignmentlifetime `/project options assignmentlifetime`

Changes the default duration of assignments of the project.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `lifetime` (TimeSpan?): The new duration of assignments (dd:hh:mm:ss:fff) (can be null).

### mainchannel `/project options mainchannel`

Changes the main channel of the project.

**Permissions Required:** Administrator

**Arguments:**
- `project` (string): The project.
- `channel` (ITextChannel): The new main channel.

### infochannel `/project options infochannel`

Changes the info channel of the project.

**Permissions Required:** Administrator

**Arguments:**
- `project` (string): The project.
- `channel` (ITextChannel): The new info channel.

### deletioncleanup `/project options deletioncleanup`

Changes whether to remove the roles and channels assigned to the project upon project deletion.

**Permissions Required:** Administrator

**Arguments:**
- `project` (string): The project.
- `cleanup` (bool): Whether to do cleanup.

## Part Module `/part`

### list `/part list`

Lists all the parts of the project.

**Arguments:**
- `project` (string): The project.

### listunclaimed `/part listunclaimed`

Lists all the unclaimed parts of the project.

**Arguments:**
- `project` (string): The project.

### add `/part add`

Adds a new part to the project.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `name` (string): The name of the part.
- `start` (TimeSpan?, optional): The start time (can be null). Defaults to `null`.
- `end` (TimeSpan?, optional): The end time (can be null). Defaults to `null`.
- `status` (PartStatus, optional): The status of the part. Defaults to `NotFinished`.

### rename `/part rename`

Changes the name of the part.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `part` (string): The part.
- `newname` (string): The new name for the part.

### start `/part start`

Changes the start time of the part.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `part` (string): The part.
- `start` (TimeSpan?, optional): The new start time (can be null). Defaults to `null`.

### end `/part end`

Changes the end time of the part.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `part` (string): The part.
- `end` (TimeSpan?, optional): The new end time (can be null). Defaults to `null`.

### status `/part status`

Changes the status of the part.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `part` (string): The part.
- `status` (PartStatus): The new status.

### remove `/part remove`

Removes one or more parts from the project.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `parts` (string[]): The parts to remove.

### clear `/part clear`

Removes all parts from the project.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.

### frombookmarks `/part frombookmarks`

Imports parts from a beatmap's bookmarks.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `beatmap` (Attachment): The beatmap .osu to import bookmarks from.
- `hasstart` (bool, optional): Whether there is a bookmark indicating the start of the first part. Defaults to `true`.
- `hasend` (bool, optional): Whether there is a bookmark indicating the end of the last part. Defaults to `false`.
- `replace` (bool, optional): Whether to clear the existing parts before importing. Defaults to `true`.

### fromcsv `/part fromcsv`

Imports parts from a CSV file.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `file` (Attachment): The .csv file to import parts from.
- `hasheaders` (bool, optional): Whether the CSV file has explicit headers. Defaults to `true`.
- `replace` (bool, optional): Whether to clear the existing parts before importing. Defaults to `true`.

### tocsv `/part tocsv`

Exports all parts of the project to a CSV file.

**Permissions Required:** Project Member

**Arguments:**
- `project` (string): The project.
- `includemappers` (bool, optional): Whether to include columns showing the mappers assigned to each part. Defaults to `false`.

### todescription `/part todescription`

Generates an element with all the parts which you can add to your beatmap description.

**Permissions Required:** Project Member

**Arguments:**
- `project` (string): The project.
- `includemappers` (bool, optional): Whether to show the mappers assigned to each part. Defaults to `true`.
- `includepartnames` (bool, optional): Whether to show the name of each part. Defaults to `false`.

## Assignment Module `/asn`

### list `/asn list`

Lists all the assignments in the project.

**Permissions Required:** Project Member

**Arguments:**
- `project` (string): The project.

### add `/asn add`

Adds one or more assignments.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `user` (IGuildUser): The member to assign to.
- `parts` (string[]): The parts to assign to the member.
- `deadline` (DateTime?, optional): The deadline for the assignment (can be null). Defaults to `null`.

### remove `/asn remove`

Removes one or more assignments.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `user` (IUser): The member to remove assignments from.
- `parts` (string[]): The parts to unassign from the member.

### deadline `/asn deadline`

Changes the deadline of the assignment.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `part` (string): The part of the assignment.
- `user` (IGuildUser): The member of the assignment.
- `deadline` (DateTime?, optional): The new deadline (can be null).

### draintimes `/asn draintimes`

Calculates the total drain time assigned to each participant.

**Permissions Required:** Project Member

**Arguments:**
- `project` (string): The project.

## Auto Update Module `/au`

### list `/au list`

Lists all the update notifications attached to the project.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.

### add `/au add`

Adds a new update notification to the project.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `channel` (ITextChannel): The channel to post the notification in.
- `cooldown` (TimeSpan?, optional): The cooldown on the notification (dd:hh:mm:ss:fff) (can be null). Defaults to `null`.
- `mentions` (bool, optional): Whether to ping members on an update notification. Defaults to `false`.

### remove `/au remove`

Removes an update notification from the project.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `channel` (ITextChannel): The channel the notification is in.

### cooldown `/au cooldown`

Changes the cooldown of the update notification.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `channel` (ITextChannel): The channel the notification is in.
- `cooldown` (TimeSpan?, optional): The new cooldown (dd:hh:mm:ss:fff) (can be null).

### mentions `/au mentions`

Changes whether the update notification pings all members.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
- `channel` (ITextChannel): The channel the notification is in.
- `mentions` (bool): Whether to ping all members in the update notification.

### trigger `/au trigger`

Triggers all update notifications of the project.

**Permissions Required:** Project Manager

**Arguments:**
- `project` (string): The project.
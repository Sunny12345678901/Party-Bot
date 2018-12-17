﻿using Discord;
using Discord.WebSocket;
using PartyBot.DataStructs;
using PartyBot.Handlers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Victoria;
using Victoria.Entities;
using Victoria.Entities.Enums;

namespace PartyBot.Services
{
    public sealed class AudioService
    {
        private Lavalink _lavalink;

        public AudioService(Lavalink lavalink)
            => _lavalink = lavalink;

        private readonly Lazy<ConcurrentDictionary<ulong, AudioOptions>> _lazyOptions
            = new Lazy<ConcurrentDictionary<ulong, AudioOptions>>();

        private ConcurrentDictionary<ulong, AudioOptions> Options
            => _lazyOptions.Value;

        /*This is ran when a user uses either the command !Join or !Play
            I decided to put these two commands as one, will probably change it in future. 
            Task Returns a string for now for use in the Command Module (ReplyAsync).
            TODO: Return Embed to make it all pretty. */
        public async Task<Embed> JoinOrPlayAsync(SocketGuildUser user, IMessageChannel textChannel, ulong guildId, string query = null)
        {
            //Check If User Is Connected To Voice Cahnnel.
            if (user.VoiceChannel == null)
                return await EmbedHandler.CreateErrorEmbed("Music, Join/Play", "You Must First Join a Voice Channel.");

            //Check if user who used !Join is a user that has already summoned the Bot.
            if (Options.TryGetValue(user.Guild.Id, out var options) && options.Summoner.Id != user.Id)
                return await EmbedHandler.CreateErrorEmbed("Music, Join/Play", $"I can't join another voice channel untill {options.Summoner} disconnects me.");

            //If The user hasn't provided a Search string from the !Play command, then they must have used the !Join command.
            //Join the voice channel the user is in.
            if (query == null)
            {
                await _lavalink.DefaultNode.ConnectAsync(user.VoiceChannel, textChannel /*This Param is Optional, Only used If we want to bind the Bot to a TextChannel For commands.*/);
                Options.TryAdd(user.Guild.Id, new AudioOptions
                {
                    Summoner = user
                });
                return await EmbedHandler.CreateBasicEmbed("Music", $"Now connected to {user.VoiceChannel.Name} and bound to {textChannel.Name}. Get Ready For Betrays...", Color.Blue);
            }
            else
            {
                try
                {
                    //Try get the player. If it returns null then the user has used the command !Play without using the command !Join.
                    var player = _lavalink.DefaultNode.GetPlayer(guildId);
                    if (player == null)
                    {
                        //User Used Command !Play before they used !Join
                        //So We Create a Connection To The Users Voice Channel.
                        await _lavalink.DefaultNode.ConnectAsync(user.VoiceChannel, textChannel);
                        Options.TryAdd(user.Guild.Id, new AudioOptions
                        {
                            Summoner = user
                        });
                        //Now we can set the player to out newly created player.
                        player = _lavalink.DefaultNode.GetPlayer(guildId);
                    }

                    //Find The Youtube Track the User requested.
                    LavaTrack track;
                    var search = await _lavalink.DefaultNode.SearchYouTubeAsync(query);

                    //If we couldn't find anything, tell the user.
                    if (search.LoadResultType == LoadResultType.NoMatches)
                        return await EmbedHandler.CreateErrorEmbed("Music", $"BAMBOOZLED! I wasn't able to find anything for {query}.");

                    //Get the first track from the search results.
                    //TODO: Add a 1-5 list for the user to pick from. (Like Fredboat)
                    track = search.Tracks.FirstOrDefault();

                    //If the Bot is already playing music, or if it is paused but still has music in the playlist, Add the requested track to the queue.
                    if(player.IsPlaying || player.IsPaused)
                    {
                        player.Queue.Enqueue(track);
                        return await EmbedHandler.CreateBasicEmbed("Music", $"{track.Title} has been added to queue.", Color.Blue);
                    }

                    //Player was not playing anything, so lets play the requested track.
                    await player.PlayAsync(track);
                    return await EmbedHandler.CreateBasicEmbed("Music", $"Now Playing: {track.Title}\nUrl: {track.Uri}", Color.Blue);
                }
                //If after all the checks we did, something still goes wrong. Tell the user about it so they can report it back to us.
                catch (Exception ex)
                {
                    return await EmbedHandler.CreateErrorEmbed("Music, Join/Play", ex.ToString());
                }
            }
            
        }

        /*This is ran when a user uses the command !Leave.
            Task Returns a string for now for use in the Command Module (ReplyAsync).
            TODO: Return Embed to make it all pretty. */
        public async Task<Embed> LeaveAsync(ulong guildId)
        {
            try
            {
                //Get The Player Via GuildID.
                var player = _lavalink.DefaultNode.GetPlayer(guildId);

                //if The Player is playing, Stop it.
                if (player.IsPlaying)
                    await player.StopAsync();

                //Leave the voice channel.
                var name = player.VoiceChannel.Name;
                await _lavalink.DefaultNode.DisconnectAsync(guildId);
                return await EmbedHandler.CreateBasicEmbed("Music", $"I've left {name}. Thank you for playing moosik.", Color.Blue);
            }
            //Tell the user about the error so they can report it back to us.
            catch (InvalidOperationException ex)
            {
                return await EmbedHandler.CreateErrorEmbed("Music, Leave", ex.ToString());
            }
        }

        public async Task<Embed> ListAsync(ulong guildId)
        {
            try
            {
                var descriptionBuilder = new StringBuilder();
                var player = _lavalink.DefaultNode.GetPlayer(guildId);
                if (player == null)
                    return await EmbedHandler.CreateErrorEmbed("Music, List", $"Could not aquire player.\nAre you using the bot right now? check{Global.Config.DefaultPrefix}Help for info on how to use the bot.");

                if (player.IsPlaying)
                {
                    if (player.Queue.Count < 1 && player.CurrentTrack != null)
                    {
                        return await EmbedHandler.CreateBasicEmbed($"Now Playing: {player.CurrentTrack.Title}", "Nothing Else Is Queued.", Color.Blue);
                    }
                    else
                    {
                        var trackNum = 2;
                        foreach (var track in player.Queue.Items)
                        {
                            descriptionBuilder.Append($"{trackNum}: [{track.Title}]({track.Uri}) - {track.Id}");
                            trackNum++;
                        }
                        return await EmbedHandler.CreateBasicEmbed("Music Playlist", $"Now Playing: [{player.CurrentTrack.Title}]({player.CurrentTrack.Uri})\n{descriptionBuilder.ToString()}", Color.Blue);
                    }
                }
                else
                {
                    return await EmbedHandler.CreateErrorEmbed("Music, List", "Player doesn't seem to be playing anything right now. If this is an error, Please Contact Draxis.");
                }
            }
            catch (Exception ex)
            {
                return await EmbedHandler.CreateErrorEmbed("Music, List", ex.Message);
            }

        }

        public async Task<Embed> SkipTrackAsync(ulong guildId)
        {
            try
            {
                var player = _lavalink.DefaultNode.GetPlayer(guildId);
                if (player == null)
                    return await EmbedHandler.CreateErrorEmbed("Music, List", $"Could not aquire player.\nAre you using the bot right now? check{Global.Config.DefaultPrefix}Help for info on how to use the bot.");
                if (player.Queue.Count < 1)
                {
                    return await EmbedHandler.CreateErrorEmbed("Music, SkipTrack", "Unable To skip a track as there is only One or No songs currently playing.");
                }
                else
                {
                    try
                    {
                        var currentTrack = player.CurrentTrack;
                        await player.SkipAsync();
                        return await EmbedHandler.CreateBasicEmbed("Music Delist", $"I have successfully skiped {currentTrack.Title}", Color.Blue);
                    }
                    catch (Exception ex)
                    {
                        return await EmbedHandler.CreateErrorEmbed("Music, DelistTrack", ex.ToString());
                    }

                }
            }
            catch (Exception ex)
            {
                return await EmbedHandler.CreateErrorEmbed("Music, DelistTrack", ex.ToString());
            }
        }

        public async Task<Embed> StopAsync(ulong guildId)
        {
            try
            {
                var player = _lavalink.DefaultNode.GetPlayer(guildId);
                if (player == null)
                    return await EmbedHandler.CreateErrorEmbed("Music, List", $"Could not aquire player.\nAre you using the bot right now? check{Global.Config.DefaultPrefix}Help for info on how to use the bot.");
                if (player.IsPlaying)
                    await player.StopAsync();
                /* Not sure if this is required as I think player.StopAsync(); clears the queue anyway. */
                foreach (var track in player.Queue.Items)
                    player.Queue.Dequeue();

                return await EmbedHandler.CreateBasicEmbed("Music Stop", "I Have stopped playback & the playlist has been cleared.", Color.Blue);
            }
            catch (Exception ex)
            {
                return await EmbedHandler.CreateErrorEmbed("Music, Stop", ex.ToString());
            }
        }
    }
}

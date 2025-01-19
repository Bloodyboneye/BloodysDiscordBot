using NetCord.Services.ApplicationCommands;
using System.Text;
using NetCord;
using NetCord.Rest;
using NetCord.Gateway.Voice;
using System.Diagnostics;

namespace BloodysDiscordBot
{
    public class MusicModule : ApplicationCommandModule<ApplicationCommandContext>
    {

        // TODO: Instead of playing directly add play file to queue and implement playing from file!
        [SlashCommand("playfile", "Plays music from File", Contexts = [InteractionContextType.Guild])]
        public async Task PlayFileCommand(string track)
        {
            // Check if owner ID is set and if then only allow owner to use command
            if (Globals.G_BotAuthor != 0 && Context.User.Id != Globals.G_BotAuthor)
            {
                await RespondAsync(InteractionCallback.Message("Only the Bot Author can use this command!"));
                return;
            }

            //Console.WriteLine($"User ID: {Context.User.Id} UserName: {Context.User.Username}");

            // Check if track is uri
            if (Uri.IsWellFormedUriString(track, UriKind.Absolute))
            {
                await RespondAsync(InteractionCallback.Message("Invalid track!"));
                return;
            }

            var guild = Context.Guild!;
            
            if (!guild.VoiceStates.TryGetValue(Context.User.Id, out var voiceState))
            {
                await RespondAsync(InteractionCallback.Message("You are not connected to any voice channel!"));
                return;
            }

            var client = Context.Client;

            //guild.GetUserVoiceStateAsync

            // TODO: Use existing VoiceClient if bot is already connected?
            var voiceClient = await client.JoinVoiceChannelAsync(
                guild.Id,
                voiceState.ChannelId.GetValueOrDefault());

            // Connect
            await voiceClient.StartAsync();

            // Enter speaking state, top be able to send voice
            await voiceClient.EnterSpeakingStateAsync(SpeakingFlags.Microphone);

            // Respond to the interaction
            await RespondAsync(InteractionCallback.Message($"Playing {Path.GetFileNameWithoutExtension(track)}!"));

            await Log.LogAsync($"Playing {track}!");

            // Create a stream that sends voice to Discord
            var outStream = voiceClient.CreateOutputStream();

            OpusEncodeStream stream = new(outStream, PcmFormat.Short, VoiceChannels.Stereo, OpusApplication.Audio);

            ProcessStartInfo startInfo = new("ffmpeg")
            {
                RedirectStandardOutput = true,
            };

            var arguments = startInfo.ArgumentList;

            // Set the logging level to quiet mode
            arguments.Add("-loglevel");
            arguments.Add("8");

            // Specify the input
            arguments.Add("-i");
            arguments.Add(track);

            // Set the number of audio channels to 2 (stereo)
            arguments.Add("-ac");
            arguments.Add("2");

            // Set the audio sampling rate to 48 kHz
            arguments.Add("-ar");
            arguments.Add("48000");

            // Set the output format to 16-bit signed little-endian
            arguments.Add("-f");
            arguments.Add("s16le");

            // Set the Volume
            arguments.Add("-filter:a");
            arguments.Add($"volume={1f}");

            // Direct the output to stdout
            arguments.Add("pipe:1");

            // Start the FFmpeg process
            var ffmpeg = Process.Start(startInfo)!;

            // Copy the FFmpeg stdout to 'stream', which encodes the voice using Opus and passes it to 'outStream'
            await ffmpeg.StandardOutput.BaseStream.CopyToAsync(stream);

            // Flush 'stream' to make sure all the data has been sent and to indicate to Discord that we have finished sending
            await stream.FlushAsync();

            //Console.WriteLine(stream.ToString());

            await voiceClient.CloseAsync();

            await client.UpdateVoiceStateAsync(new(guild.Id, null));

            await Log.LogAsync($"Finished playing {track}!");
        }

        [SlashCommand("play", "Plays the specified track", Contexts = [InteractionContextType.Guild])]
        public async Task PlayCommand([SlashCommandParameter(Description = "Music Link or Query to play")]string track)
        {
            //if (Globals.G_BotAuthor != 0 && Context.User.Id != Globals.G_BotAuthor)
            //{
            //    await RespondAsync(InteractionCallback.Message("Invalid Permissions to use this command!"));
            //    return;
            //}

            if (!Context.Guild!.VoiceStates.TryGetValue(Context.User.Id, out var voiceState))
            {
                await RespondAsync(InteractionCallback.Message("You are not connected to any voice channel!"));
                return;
            }

            // Defer Message becuase Video lookup takes to long
            await RespondAsync(InteractionCallback.DeferredMessage());

            var musicBot = Bot.GetBot<MusicBot>(Context.Guild!.Id);
            musicBot ??= new MusicBot(Context.Guild, Context.Client);

            musicBot.textChannel = Context.Channel;

            try
            {
                // Attempt to play the track
                string? message = await musicBot.PlayMusicAsync(track, voiceState, Context.Client);

                if (message == null)
                {
                    // Update the response if an error occurred
                    await ModifyResponseAsync(action: options => options.Content = "Error while trying to play Music!");
                }
                else
                {
                    // Update the response with success message
                    await ModifyResponseAsync(action: options => options.Content = message);
                }
            }
            catch (Exception ex)
            {
                await Log.LogAsync($"Error occured in {nameof(PlayCommand)}: {ex.Message}");
                await ModifyResponseAsync(action: options => options.Content = $"An error occurred: {ex.Message}");
            }
        }

        [SlashCommand("stop", "Stops the playback", Contexts = [InteractionContextType.Guild])]
        public async Task StopMusicCommand()
        {
            var musicBot = Bot.GetBot<MusicBot>(Context.Guild!.Id);
            musicBot ??= new MusicBot(Context.Guild, Context.Client);

            await musicBot.StopMusicAsync();

            await RespondAsync(InteractionCallback.Message("Stopped Music playback"));
        }

        [SlashCommand("volume", "Gets/Sets the volume of the bot", Contexts = [InteractionContextType.Guild])]
        public async Task ChangeVolumeCommand([SlashCommandParameter(Description = "New volume in percent")] int? volume = null)
        {
            var musicBot = Bot.GetBot<MusicBot>(Context.Guild!.Id);
            // No bot with this Guild Id, create it
            musicBot ??= new MusicBot(Context.Guild, Context.Client);

            if (volume == null)
            {
                await RespondAsync(InteractionCallback.Message($"Current Volume is {musicBot.musicVolume * 100}%"));
                return;
            }

            if (Globals.G_BotAuthor != 0 && Context.User.Id != Globals.G_BotAuthor)
            {
                // Only allow Bot Author full control
                if (volume < 0 || volume > Globals.G_BotMusicMaxVolume)
                {
                    await RespondAsync(InteractionCallback.Message($"The volume can only be between 0% and {Globals.G_BotMusicMaxVolume}%!\nCurrent Volume is: {musicBot.musicVolume}"));
                    return;
                }
                musicBot.musicVolume = ((float)volume) / 100;
                await RespondAsync(InteractionCallback.Message($"New Volume is {musicBot.musicVolume * 100}%"));
                await Log.LogDebugAsync($"New volume is {musicBot.musicVolume * 100}%");
                return;
            }
            // Allow Bot Author full control
            if (volume < 0)
            {
                await RespondAsync(InteractionCallback.Message($"The volume can not be below 0%!\nCurrent Volume is {musicBot.musicVolume * 100}%"));
                return;
            }
            musicBot.musicVolume = ((float)volume) / 100;
            await RespondAsync(InteractionCallback.Message($"New Volume is {musicBot.musicVolume * 100}%"));
            await Log.LogDebugAsync($"New volume is {musicBot.musicVolume * 100}%");
        }

        [SlashCommand("filter", "Applies the selected audio filter", Contexts = [InteractionContextType.Guild])]
        public async Task FilterAsyncCommand([SlashCommandParameter(Name = "filter", Description = "The filter to apply")]AudioFilter? filter = null,
                                             [SlashCommandParameter(Name = "strength", Description = "The strength of the Filter")] float? strength = null)
        {
            var musicBot = Bot.GetBot<MusicBot>(Context.Guild!.Id);
            // No bot with this Guild Id, create it
            musicBot ??= new MusicBot(Context.Guild, Context.Client);

            if (filter is null)
            {
                if (musicBot.musicFilters is null || musicBot.musicFilters.IsEmpty)
                {
                    await RespondAsync(InteractionCallback.Message("Filters:\n```-```"));
                }
                else
                {
                    // List filters instead of setting filter
                    StringBuilder filters = new("Filters:\n```");
                    foreach (var filterValue in musicBot.musicFilters)
                    {
                        filters.AppendLine($"{filterValue.Value.filterName}:{filterValue.Value.strength}");
                    }

                    filters.Append("```");

                    await RespondAsync(InteractionCallback.Message(filters.ToString()));
                }
                return;
            }

            // Add or remove filter if it alread was added
            MusicFilter? musicFilter;
            switch (filter)
            {
                case AudioFilter.BassBost:
                    musicFilter = strength.HasValue ? MusicFilter.BassFilter(strength.Value * 10) : MusicFilter.BassFilter();
                    break;
                case AudioFilter.Pitch:
                    musicFilter = strength.HasValue ? MusicFilter.PitchFilter(strength.Value * 1) : MusicFilter.PitchFilter();
                    break;
                case AudioFilter.Tempo:
                    musicFilter = strength.HasValue ? MusicFilter.TempoFilter(strength.Value * 1) : MusicFilter.TempoFilter();
                    break;
                case AudioFilter.Nightcore:
                    musicFilter = strength.HasValue ? MusicFilter.NightcoreFilter(strength.Value * 1.15f, strength.Value * 1.25f) : MusicFilter.NightcoreFilter();
                    break;
                case AudioFilter.Slowdown:
                    musicFilter = strength.HasValue ? MusicFilter.SlowdownFilter(strength.Value * 0.83f, strength.Value * 0.87f) : MusicFilter.SlowdownFilter();
                    break;
                case AudioFilter.Reverb:
                    musicFilter = strength.HasValue ? MusicFilter.ReverbFilter(strength.Value * 0.8f, strength.Value * 0.9f, strength.Value * 1000f, strength.Value * 0.3f) : MusicFilter.ReverbFilter();
                    break;
                case AudioFilter.Chorus:
                    musicFilter = strength.HasValue ? MusicFilter.ChorusFilter(strength.Value * 0.5f, strength.Value * 0.9f, strength.Value * 60f, strength.Value * 0.4f, strength.Value * 0.25f, strength.Value * 2f) : MusicFilter.ChorusFilter();
                    break;
                case AudioFilter.Distortion:
                    musicFilter = MusicFilter.DistortionFilter();
                    break;
                case AudioFilter.Flanger:
                    musicFilter = MusicFilter.FlangerFilter();
                    break;
                case AudioFilter.Tremolo:
                    musicFilter = strength.HasValue ? MusicFilter.TremoloFilter(depth: strength.Value * 0.8f) : MusicFilter.TremoloFilter();
                    break;
                case AudioFilter.Vibrato:
                    musicFilter = strength.HasValue ? MusicFilter.VibratoFilter(depth: strength.Value * 0.1f) : MusicFilter.VibratoFilter();
                    break;
                case AudioFilter.Phaser:
                    musicFilter = strength.HasValue ? MusicFilter.PhaserFilter() : MusicFilter.PhaserFilter();
                    break;
                default:
                    await Log.LogAsync($"Invalid filter: {Enum.GetName(typeof(AudioFilter), filter)}");
                    await RespondAsync(InteractionCallback.Message($"Internal Error: Filter is not valid!"));
                    return;
            }

            musicFilter.strength = strength ?? 1f;

            musicBot.musicFilters ??= []; // Create Dictionary if it for some reason doesn't exist

            // Check if Filter is already contained

            var musicFilterDict = musicBot.musicFilters;

            if (!musicFilterDict.IsEmpty && musicFilterDict.ContainsKey(filter.Value))
            {
                // Filter already set, remove it
                if (!musicFilterDict.TryRemove(filter.Value, out _))
                {
                    await RespondAsync(InteractionCallback.Message("Internal Error while trying to disable filter!"));
                    await Log.LogAsync($"Internal Error while trying to disable filter:{filter.Value}!");
                    return;
                }
                await RespondAsync(InteractionCallback.Message($"> Filter: {filter.Value} disabled"));
                await Log.LogAsync($"Filter: '{filter.Value}' disabled");
                return;
            }
            // Add filter
            if (!musicFilterDict.TryAdd(filter.Value, musicFilter))
            {
                await RespondAsync(InteractionCallback.Message("Internal Error while trying to activate filter!"));
                await Log.LogAsync($"Internal Error while trying to activate filter:{filter.Value}!");
                return;
            }
            await RespondAsync(InteractionCallback.Message("Filter successfully activated!"));
            await Log.LogAsync($"Successfully activated filter: {filter.Value}!");
        }

        [SlashCommand("skip", "Skips current song", Contexts = [InteractionContextType.Guild])]
        public async Task SkipMusicCommand()
        {
            var musicBot = Bot.GetBot<MusicBot>(Context.Guild!.Id);
            // No bot with this Guild Id, create it
            musicBot ??= new MusicBot(Context.Guild, Context.Client);

            if (!musicBot.IsPlayingMusic)
            {
                await RespondAsync(InteractionCallback.Message("No Music is currently playing!"));
                return;
            }
            musicBot.SkipCurrentMusic();

            await RespondAsync(InteractionCallback.Message("Skipped current playback"));
        }

        [SlashCommand("clear", "Clears the Queue", Contexts = [InteractionContextType.Guild])]
        public async Task ClearMusicQueueCommand()
        {
            var musicBot = Bot.GetBot<MusicBot>(Context.Guild!.Id);
            // No bot with this Guild Id, create it
            musicBot ??= new MusicBot(Context.Guild, Context.Client);

            musicBot.musicQueue.Clear();

            await RespondAsync(InteractionCallback.Message("Music queue cleared!"));
        }


        [SlashCommand("queue", "Displays the songs in the queue", Contexts = [InteractionContextType.Guild])]
        public async Task DisplayQueueMusicCommand([SlashCommandParameter(Name = "page", Description = "Page to get info for", MinValue = 1)] int page = 1)
        {
            int entriesPerPage = 10;
            var musicBot = Bot.GetBot<MusicBot>(Context.Guild!.Id);
            // No bot with this Guild Id, create it
            musicBot ??= new MusicBot(Context.Guild, Context.Client);

            if (musicBot.musicQueue.IsEmpty)
            {
                await RespondAsync(InteractionCallback.Message("Queue is Empty!"));
                return;
            }
            int pageCount = (int)Math.Clamp(musicBot.musicQueue.Count / entriesPerPage, 1, decimal.MaxValue);
            if (page > pageCount)
                page = pageCount;
            MusicQueueItem[] musicQueueArray = new MusicQueueItem[musicBot.musicQueue.Count];
            musicBot.musicQueue.CopyTo(musicQueueArray, 0);

            int startIndex = (page - 1) * entriesPerPage;
            int endIndex = Math.Min(musicQueueArray.Length, ((page - 1) * entriesPerPage) + entriesPerPage);

            StringBuilder sb = new($"Queue Page: {page}/{pageCount}\n```");

            for (int i = startIndex; i < endIndex; i++)
            {
                sb.AppendLine($"[{musicQueueArray[i].songName}]({musicQueueArray[i].fileOrURL})");
            }

            sb.Append("```");

            await RespondAsync(InteractionCallback.Message(sb.ToString().Trim()));
        }

        [SlashCommand("loop", "Changes loop setting", Contexts = [InteractionContextType.Guild])]
        public async Task LoopMusicCommand([SlashCommandParameter(Name = "type", Description = "Looping type")] LoopType? loopType = null) 
        {
            var musicBot = Bot.GetBot<MusicBot>(Context.Guild!.Id);
            // No bot with this Guild Id, create it
            musicBot ??= new MusicBot(Context.Guild, Context.Client);

            loopType ??= musicBot.loopType == LoopType.None ? LoopType.CurrentSong : LoopType.None; // Swap Loop type to Current Song or to none if none is set

            musicBot.loopType = loopType.Value;

            await Log.LogAsync($"Changed loop setting to: {Enum.GetName(typeof(LoopType), loopType.Value)}");

            await RespondAsync(InteractionCallback.Message($"> Changed loop setting to {Enum.GetName(typeof(LoopType), loopType)}"));
        }

        [SlashCommand("shuffle", "Shuffels the current queue", Contexts = [InteractionContextType.Guild])]
        public async Task ShuffleQueueCommand()
        {
            var musicBot = Bot.GetBot<MusicBot>(Context.Guild!.Id);
            musicBot ??= new MusicBot(Context.Guild, Context.Client);

            if (musicBot.musicQueue.IsEmpty)
            {
                await RespondAsync(InteractionCallback.Message("Tried to shuffle queue while queue is empty!"));
                return;
            }

            if (musicBot.musicQueue.Count == 1)
                return; // No need to shuffle queue if only one item is inside

            // Maybe Fix/TODO? Kind of not thread safe, queue Reads from for example the music Player / Queue List and others could still use wrong queue while shuffeling
            List<MusicQueueItem> items = [.. musicBot.musicQueue];

            Random random = new();

            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }

            // Maybe change? Create new Queue instead of clearing current queue and adding items
            musicBot.musicQueue.Clear();
            foreach (var item in items)
            {
                musicBot.musicQueue.Enqueue(item);
            }

            await RespondAsync(InteractionCallback.Message("> Shuffled queue!"));
            await Log.LogAsync($"Shuffled queue with {items.Count} items!");
        }

        [SlashCommand("remove", "Removes the specified Item from the queue", Contexts = [InteractionContextType.Guild])]
        public async Task RemoveFromQueueCommand([SlashCommandParameter(Name = "index", Description = "The Index of the Item to remove", MinValue = 1)] int queueItemIndex)
        {
            var musicBot = Bot.GetBot<MusicBot>(Context.Guild!.Id);
            musicBot ??= new MusicBot(Context.Guild, Context.Client);

            if (musicBot.musicQueue.Count <= queueItemIndex)
            {
                await Log.LogAsync($"Tried to remove Item with Index {queueItemIndex - 1} from queue but queue size is {musicBot.musicQueue.Count}");
                await RespondAsync(InteractionCallback.Message($"> Can't remove Item at {queueItemIndex} because max Item is {musicBot.musicQueue.Count}!"));
                return;
            }
            // Maybe Fix/TODO? Kind of not thread safe, queue Reads from for example the music Player / Queue List and others could still use wrong queue while removing item...
            List<MusicQueueItem> items = [.. musicBot.musicQueue];

            items.RemoveAt(queueItemIndex - 1);

            musicBot.musicQueue.Clear();

            foreach (var item in items)
            {
                musicBot.musicQueue.Enqueue(item);
            }

            await Log.LogAsync($"Removed Item from Queue at Location {queueItemIndex}");
            await RespondAsync(InteractionCallback.Message($"> Removed Item from Queue at Location {queueItemIndex}!"));
        }

        [SlashCommand("pause", "Pauses playback", Contexts = [InteractionContextType.Guild])]
        public async Task PauseMusicPlaybackCommand()
        {
            var musicBot = Bot.GetBot<MusicBot>(Context.Guild!.Id);
            musicBot ??= new MusicBot(Context.Guild, Context.Client);

            if (!musicBot.IsPlayingMusic)
            {
                await Log.LogAsync("Tried to pause playback while there is no song playing!");
                await RespondAsync(InteractionCallback.Message("> Can not pause playback while there is no song playing!"));
                return;
            } else if (musicBot.isPaused)
            {
                await Log.LogAsync("Tried to pause playback while Music is already paused!");
                await RespondAsync(InteractionCallback.Message("> Music playback is already paused!"));
                return;
            }
            musicBot.PausePlayback();
            await Log.LogAsync("Paused music playback");
            await RespondAsync(InteractionCallback.Message("> Music playback is now paused!"));
        }

        [SlashCommand("resume", "Resumes playback", Contexts = [InteractionContextType.Guild])]
        public async Task ResumeMusicPlaybackCommand()
        {
            var musicBot = Bot.GetBot<MusicBot>(Context.Guild!.Id);
            musicBot ??= new MusicBot(Context.Guild, Context.Client);

            if (!musicBot.IsPlayingMusic)
            {
                await Log.LogAsync("Tried to resume playback while there is no song playing!");
                await RespondAsync(InteractionCallback.Message("> Can not resume playback while there is no song playing!"));
                return;
            }
            else if (!musicBot.isPaused)
            {
                await Log.LogAsync("Tried to resume playback while Music is not paused!");
                await RespondAsync(InteractionCallback.Message("> Music playback is not paused!"));
                return;
            }
            musicBot.ResumePlayback();
            await Log.LogAsync("Resumed music playback");
            await RespondAsync(InteractionCallback.Message("> Music playback is now resumed!"));
        }

        [SlashCommand("now_playing", "Gives Info about the current music that is playing", Contexts = [InteractionContextType.Guild])]
        public async Task NowPlayingCommand()
        {
            var musicBot = Bot.GetBot<MusicBot>(Context.Guild!.Id);
            musicBot ??= new MusicBot(Context.Guild, Context.Client);

            if (!musicBot.IsPlayingMusic)
            {
                await RespondAsync(InteractionCallback.Message("> Nothing is currently playing!"));
                return;
            }
            await Log.LogAsync($"Currently playing [{musicBot.currentMusic!.songName}]({musicBot.currentMusic.fileOrURL}) Time: {musicBot.currentMusicLocation}s/{musicBot.currentMusic.duration}s | {(uint)(musicBot.currentMusicLocation / musicBot.currentMusic.duration * 100)}%");
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties().WithContent($"Currently playing [{musicBot.currentMusic!.songName}]({musicBot.currentMusic.fileOrURL}) Time: {musicBot.currentMusicLocation}s/{musicBot.currentMusic.duration}s | {(uint)(musicBot.currentMusicLocation / musicBot.currentMusic.duration * 100)}%")
                                                                                             .WithFlags(MessageFlags.SuppressEmbeds)));
        }
    }
}

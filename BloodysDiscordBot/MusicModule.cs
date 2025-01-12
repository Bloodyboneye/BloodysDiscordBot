using NetCord.Services.ApplicationCommands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NetCord.Services;
using NetCord;
using NetCord.Rest;
using NetCord.Gateway;
using NetCord.Gateway.Voice;
using System.Diagnostics;
using System.Collections.Immutable;
using System.Dynamic;
using System.Reflection;

namespace BloodysDiscordBot
{
    public class MusicModule : ApplicationCommandModule<ApplicationCommandContext>
    {

        [SlashCommand("playfile", "Plays music from File", Contexts = [InteractionContextType.Guild])]
        public async Task PlayFileAsync(string track)
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
        public async Task PlayAsync([SlashCommandParameter(Description = "Music Link or Query to play")]string track)
        {
            if (Globals.G_BotAuthor != 0 && Context.User.Id != Globals.G_BotAuthor)
            {
                await RespondAsync(InteractionCallback.Message("Invalid Permissions to use this command!"));
                return;
            }

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
                await Log.LogAsync($"Error occured in {nameof(PlayAsync)}: {ex.Message}");
                await ModifyResponseAsync(action: options => options.Content = $"An error occurred: {ex.Message}");
            }
        }

        [SlashCommand("stop", "Stops the playback", Contexts = [InteractionContextType.Guild])]
        public async Task StopMusicAsync()
        {
            var musicBot = Bot.GetBot<MusicBot>(Context.Guild!.Id);
            musicBot ??= new MusicBot(Context.Guild, Context.Client);

            await musicBot.StopMusicAsync();

            await RespondAsync(InteractionCallback.Message("Stopped Music playback"));
        }

        // TODO: Restart Bot Audio when changing volume?
        [SlashCommand("volume", "Changes the volume of the bot", Contexts = [InteractionContextType.Guild])]
        public async Task ChangeVolumeAsync(int volume)
        {
            var musicBot = Bot.GetBot<MusicBot>(Context.Guild!.Id);
            // No bot with this Guild Id, create it
            musicBot ??= new MusicBot(Context.Guild, Context.Client);
            if (Globals.G_BotAuthor != 0 && Context.User.Id != Globals.G_BotAuthor)
            {
                // Only allow Bot Author full control
                if (volume < 0 || volume > Globals.G_BotMusicMaxVolume)
                {
                    await RespondAsync(InteractionCallback.Message($"The volume can only be between 0 and {Globals.G_BotMusicMaxVolume}!\nCurrent Volume is: {musicBot.MusicVolume}"));
                    return;
                }
                musicBot.MusicVolume = ((float)volume) / 100;
                await RespondAsync(InteractionCallback.Message($"New Volume is {musicBot.MusicVolume * 100}%"));
                return;
            }
            // Allow Bot Author full control
            if (volume < 0)
            {
                await RespondAsync(InteractionCallback.Message($"The volume can not be below 0!\nCurrent Volume is {musicBot.MusicVolume * 100}%"));
                return;
            }
            musicBot.MusicVolume = ((float)volume) / 100;
            await RespondAsync(InteractionCallback.Message($"New Volume is {musicBot.MusicVolume * 100}%"));
        }
    }
}

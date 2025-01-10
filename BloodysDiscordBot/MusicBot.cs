﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NetCord;
using NetCord.Rest;
using NetCord.Gateway;
using NetCord.Gateway.Voice;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace BloodysDiscordBot
{
    public class MusicQueueItem(string fileOrURL, string songName, bool isFile, string author, uint duration)
    {
        public readonly string fileOrURL = fileOrURL;

        public readonly string songName = songName;

        public readonly bool isFile = isFile;

        public readonly string author = author;

        public readonly uint duration = duration;
    }

    public class MusicBot : Bot
    {
        private const string defaultSearchOption = "ytsearch:";

        private const string basecmdargs = "/C yt-dlp {0} | ffmpeg {1}";

        public string? ffmpegargs;

        public string? ytdlpargs;

        public Process? musicPlayerProcess;

        public Task? musicPlayerTask;

        public string? cmdargs;

        public uint currentMusicLocation;

        public float MusicVolume = 1f;

        public ConcurrentQueue<MusicQueueItem> musicQueue = [];

        public MusicQueueItem? currentMusic;

        public MusicBot(Guild guild, GatewayClient client) : base(guild, client)
        {
            
        }

        private async Task<string?> AddMusicToQueueAsync(string musicInput)
        {
            // Gather Info using yt-dlp about music
            // Check if the input is a url or not
            string musicToIndex;

            if (Helper.IsValidUrl(musicInput))
            {
                await Log.LogDebugAsync($"Adding {musicInput} to the Queue");
                musicToIndex = musicInput;
            }
            else
            {
                await Log.LogDebugAsync($"{musicInput} is not a valid url adding default search option: {defaultSearchOption}");
                musicToIndex = $"{defaultSearchOption}{musicInput}";
            }

            // Index Music using yt-dlp
            var music = Helper.GetMusicInfo(musicToIndex);

            if (music == null)
            {
                if (textChannel != null)
                    await textChannel.SendMessageAsync("Internal Error occured while trying to add Music to queue!");
                else
                    await Log.LogAsync("Error occured while trying to add Music to queue! Users have not been notified: No text Channel!", LogType.Error);
                return null;
            }

            musicQueue.Enqueue(music);
            //if (textChannel != null)
            //{
            //    await textChannel.SendMessageAsync($"[{music.songName}]({music.fileOrURL}) was added to the queue!");
            //}
            Log.LogMessage($"[{music.songName}]({music.fileOrURL}) was added to the queue!");
            return $"[{music.songName}]({music.fileOrURL}) was added to the queue!";
        }

        private void ForceStopCurrentMusic()
        {
            // Stop Current music playback if there is any currently playing
            currentMusic = null;
            if (musicPlayerProcess != null)
            {
                if (!musicPlayerProcess.HasExited)
                    musicPlayerProcess.Kill();
                musicPlayerProcess = null;
            }
        }

        private async Task StartPlayingMusic()
        {
            while (!musicQueue.IsEmpty)
            {
                OpusEncodeStream? opusStream = null;

                Stream? outStream = null;

                try
                {
                    ForceStopCurrentMusic();

                    // Check if is in voice channel
                    VoiceState? voicestate = GetVoiceState();

                    if (voicestate is null || voiceClient is null)
                    {
                        await Log.LogDebugAsync("Tried to play music while bot isn't in any voice channel");
                        musicQueue.Clear();
                        if (voiceClient != null)
                            await voiceClient.CloseAsync();
                        voiceClient = null;
                        this.voiceState = null;
                        return;
                    }

                    // Dequeue music
                    if (!musicQueue.TryDequeue(out currentMusic) || currentMusic is null)
                    {
                        if (textChannel != null)
                        {
                            await textChannel.SendMessageAsync("Tried to play Music while queue is empty!");
                        }
                        await Log.LogDebugAsync($"Tried to play Music while queue is empty!");
                    }
                    // Setup Music
                    outStream = voiceClient.CreateOutputStream();

                    opusStream = new(outStream, PcmFormat.Short, VoiceChannels.Stereo, OpusApplication.Audio);

                    string ffmpegargs = Helper.BuildFFMPEGArgs("pipe:0", "pipe:1");
                    string ytdlpargs = Helper.BuildYTDLPArgs(currentMusic!.fileOrURL);
                    string cmdargs = string.Format(basecmdargs, ytdlpargs, ffmpegargs);

                    this.ffmpegargs = ffmpegargs;
                    this.ytdlpargs = ytdlpargs;
                    this.cmdargs = cmdargs;

                    await Log.LogDebugAsync($"FFMPEG Args: {ffmpegargs}");
                    await Log.LogDebugAsync($"YTDLP Args: {ytdlpargs}");
                    await Log.LogDebugAsync($"CMD Args: {cmdargs}");

                    // Start Music Process

                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = cmdargs,
                            //Arguments = $"/C yt-dlp {ytdlpargs} | ffmpeg {ffmpegargs}",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,   // Capture standard output
                            RedirectStandardError = true,    // Capture error output
                            CreateNoWindow = true           // No window for cmd.exe
                        }
                    };

                    musicPlayerProcess = process;

                    currentMusicLocation = 0;

                    process.Start();

                    var errorTask = Task.Run(() => Helper.ReadStreamAsync(process.StandardError, "ERROR"));

                    var copyTask = Task.Run(() => process.StandardOutput.BaseStream.CopyToAsync(opusStream));

                    // Wait for the process to complete
                    await process.WaitForExitAsync();

                    // Wait for all output to be read
                    await Task.WhenAll(errorTask, copyTask);

                    // Flush 'stream' to make sure all the data has been sent and to indicate to Discord that we have finished sending
                    await opusStream.FlushAsync();

                    await Log.LogAsync($"Finished playing {currentMusic.songName}");

                    if (textChannel != null)
                    {
                        await textChannel.SendMessageAsync($"Finished playing [{currentMusic.songName}]({currentMusic.fileOrURL})");
                    }
                }
                catch (Exception ex)
                {
                    await Log.LogAsync(ex.ToString(), LogType.Error);
                }
                finally
                {
                    opusStream?.Close();
                    outStream?.Close();
                }
            }
        }

        public async Task<string?> PlayMusicAsync(string musicInput, VoiceState newVoiceState, GatewayClient client)
        {
            // Check if the bot is currently playing music and add it to the queue if it is otherwise play the music
            if (currentMusic is null || musicPlayerProcess is null || musicPlayerProcess.HasExited)
            {
                // No Music is currently playing
                // Add it to the Queue and play it directly
                // Clear queue just in case
                musicQueue.Clear();
                string? queueMessage = await AddMusicToQueueAsync(musicInput);
                if (queueMessage is null)
                {
                    await LeaveVoiceChannelAsync();
                    return null;
                }

                bool newVoiceStateSet = false;

                if (voiceState is null || voiceState.ChannelId != newVoiceState.ChannelId)
                {
                    voiceState = newVoiceState;
                    newVoiceStateSet = true;
                }

                if (voiceClient is null || newVoiceStateSet)
                {
                    if (voiceClient != null)
                    {
                        await voiceClient.CloseAsync();
                    }

                    voiceClient = await client.JoinVoiceChannelAsync(voiceState.GuildId, voiceState.ChannelId.GetValueOrDefault());
                    await voiceClient.StartAsync();
                    await voiceClient.EnterSpeakingStateAsync(SpeakingFlags.Microphone);
                }

                // Start Playing music

                //musicPlayerTask = Task.Run(StartPlayingMusic);

                musicPlayerTask = Task.Run(async () =>
                {
                    try
                    {
                        await StartPlayingMusic();
                    }
                    catch (Exception ex)
                    {
                        Log.LogDebug($"{ex} Exception occured while trying to play music!");
                    }
                });

                return queueMessage;
            }
            else
            {
                // Music is currently playing Add it to the queue instead!
                return await AddMusicToQueueAsync(musicInput);
            }
        }

        public async Task StopMusicAsync()
        {
            // Stop music Playback and clear queue
            musicQueue.Clear();
            ForceStopCurrentMusic();
            await LeaveVoiceChannelAsync();
            await Log.LogDebugAsync("Stopped music playback!");
        }
    }
}

using NetCord;
using NetCord.Rest;
using NetCord.Gateway;
using NetCord.Gateway.Voice;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace BloodysDiscordBot
{
    public class MusicBot(Guild guild, GatewayClient client) : Bot(guild, client)
    {
        private const string defaultSearchOption = "ytsearch:";

        private const int maxQueueSizeForPlaybackCopy = 10;

        //private const string basecmdargs = "/C yt-dlp {0} | ffmpeg {1}";

        public string? ffmpegargs;

        public string? ytdlpargs;

        //public Process? musicPlayerProcess;

        public Process? ytdlpProcess;

        public Process? ffmpegProcess;

        public Task? musicPlayerTask;

        //public string? cmdargs;

        public double currentMusicLocation;

        public float musicVolume = 1f;

        public float defaultMusicVolume = 1f;

        public bool isPaused;

        public LoopType loopType = LoopType.None;
    
        public ConcurrentQueue<MusicQueueItem> musicQueue = [];

        public MusicQueueItem? currentMusic;

        public ConcurrentDictionary<AudioFilter, MusicFilter> musicFilters = [];

        private CancellationTokenSource? playbackCancellationSource;

        public override async Task LeaveVoiceChannelAsync()
        {
            await base.LeaveVoiceChannelAsync();
            musicVolume = defaultMusicVolume;
            if (musicFilters is not null && !musicFilters.IsEmpty)
            {
                musicFilters.Clear();
                await Log.LogAsync("All Music Filters have been disabled!");
                if (textChannel != null)
                {
                    await textChannel.SendMessageAsync("All Filters have been disabled!");
                }
            }
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
            if (ytdlpProcess != null)
            {
                if (!ytdlpProcess.HasExited)
                    ytdlpProcess.Kill();
                ytdlpProcess.Dispose();
                ytdlpProcess = null;
            }
            if (ffmpegProcess != null)
            {
                if (!ffmpegProcess.HasExited)
                    ffmpegProcess.Kill();
                ffmpegProcess.Dispose();
                ffmpegProcess = null;
            }
            playbackCancellationSource?.Cancel();
            playbackCancellationSource?.Dispose();
            playbackCancellationSource = null;
        }

        public void SkipCurrentMusic() => ForceStopCurrentMusic();

        private async Task StartPlayingMusic()
        {
            while (!musicQueue.IsEmpty)
            {
                OpusEncodeStream? opusStream = null;

                Stream? outStream = null;

                try
                {
                    ForceStopCurrentMusic();

                    playbackCancellationSource = new CancellationTokenSource();
                    CancellationToken ct = playbackCancellationSource.Token;

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

                    LoopType oldLoopType = loopType;

                    // Dequeue music // Check if should loop current song and then don't remove it from the queue
                    if (!(loopType == LoopType.CurrentSong ? musicQueue.TryPeek(out MusicQueueItem? currentMusicCached) : musicQueue.TryDequeue(out currentMusicCached)) || currentMusicCached is null)
                    {
                        if (textChannel != null)
                        {
                            await textChannel.SendMessageAsync("Tried to play Music while queue is empty!");
                        }
                        await Log.LogAsync($"Tried to play Music while queue is empty!");
                        return;
                    }

                    if (loopType == LoopType.CurrentQueue)
                    {
                        // Readd Item to queue if should loop through queue
                        await Log.LogDebugAsync("Readding song to queue because LoopType is set to CurrentQueue!");
                        musicQueue.Enqueue(currentMusicCached);
                    }

                    currentMusic = currentMusicCached;

                    if (textChannel != null)
                        await textChannel.SendMessageAsync(new MessageProperties().WithContent($"> Starting playback of [{currentMusicCached.songName}]({currentMusicCached.fileOrURL})")
                                                                                  .WithFlags(MessageFlags.SuppressEmbeds));

                    await Log.LogAsync($"Starting playback of [{currentMusicCached.songName}]({currentMusicCached.fileOrURL})");

                    // Setup Music
                    outStream = voiceClient.CreateOutputStream();

                    opusStream = new(outStream, PcmFormat.Short, VoiceChannels.Stereo, OpusApplication.Audio);

                    string ffmpegargs = Helper.BuildFFMPEGArgs("pipe:0", "pipe:1", musicVolume, musicFilters);
                    string ytdlpargs = Helper.BuildYTDLPArgs(currentMusicCached.fileOrURL);
                    //string cmdargs = string.Format(basecmdargs, ytdlpargs, ffmpegargs);

                    this.ffmpegargs = ffmpegargs;
                    this.ytdlpargs = ytdlpargs;
                    //this.cmdargs = cmdargs;

                    await Log.LogDebugAsync($"FFMPEG Args: {ffmpegargs}");
                    await Log.LogDebugAsync($"YTDLP Args: {ytdlpargs}");

                    // Start the music processes

                    ytdlpProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "yt-dlp",
                            Arguments = ytdlpargs,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };


                    ffmpegProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "ffmpeg",
                            Arguments = ffmpegargs,
                            UseShellExecute = false,
                            RedirectStandardInput = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };

                    currentMusicLocation = 0d;

                    ytdlpProcess.Start();
                    ffmpegProcess.Start();

                    var ytdlpErrorTask = Task.Run(() => Helper.ReadErrorStreamAsync(ytdlpProcess.StandardError.BaseStream, "yt-dlp"), ct);
                    var ffmpegErrorTask = Task.Run(() => Helper.ReadErrorStreamAsync(ffmpegProcess.StandardError.BaseStream, "ffmpeg", this), ct);

                    //var ytdlpOutputTask = Task.Run(() => ytdlpProcess.StandardOutput.BaseStream.CopyToAsync(ffmpegProcess.StandardInput.BaseStream, ct), ct);
                    var ytdlpOutputTask = Task.Run(() => Helper.StreamDataAsync(ytdlpProcess.StandardOutput.BaseStream, ffmpegProcess.StandardInput.BaseStream, maxQueueSizeForPlaybackCopy, this, ct), ct);
                    var ffmpegOutputTask = Task.Run(() => ffmpegProcess.StandardOutput.BaseStream.CopyToAsync(opusStream, ct), ct);

                    await ytdlpOutputTask;

                    ffmpegProcess.StandardOutput.Close();
                    ffmpegProcess.StandardError.Close();

                    //ffmpegProcess.Kill(); // Force close ffmpeg because reading from StandardError or StandardOutput? seems to make it not close

                    //await ffmpegOutputTask;
                    //await Task.WhenAll(ytdlpOutputTask, ffmpegOutputTask);

                    await opusStream.FlushAsync();     

                    await Log.LogAsync($"Finished playing {currentMusicCached.songName}");

                    if (textChannel != null)
                    {
                        await textChannel.SendMessageAsync(new MessageProperties().WithContent($"> Finished playing [{currentMusicCached.songName}]({currentMusicCached.fileOrURL})")
                                                                                  .WithFlags(MessageFlags.SuppressEmbeds));
                    }

                    // Check old loop type and readd song if nesecary
                    if (oldLoopType != loopType)
                    {
                        // Loop Type changed while playing music!
                        switch (loopType)
                        {
                            case LoopType.None:
                                if (oldLoopType == LoopType.CurrentSong)
                                    musicQueue.TryDequeue(out _); // Remove first because we don't want to loop the current song anymore
                                else if (oldLoopType == LoopType.CurrentQueue)
                                    Helper.ConcurrentQueueTryRemoveLast(musicQueue);
                                break;
                            case LoopType.CurrentQueue:
                                musicQueue.Enqueue(currentMusicCached); // Readd current song because it was removed
                                break;
                            case LoopType.CurrentSong:
                                Helper.ConcurrentQueueAddToFront(musicQueue, currentMusicCached);
                                break;
                            default:
                                await Log.LogDebugAsync("Unknown LoopType!");
                                break;
                        }
                    }
                }
                catch (OperationCanceledException) 
                {
                    await Log.LogDebugAsync("Music Playback was canceled with OperationCanceledException");
                }
                catch (Exception ex)
                {
                    await Log.LogAsync(ex.ToString(), LogType.Error);
                }
                finally
                {
                    opusStream?.Close();
                    outStream?.Close();
                    currentMusic = null;
                    playbackCancellationSource?.Dispose();
                    playbackCancellationSource = null;
                }
            }

            await LeaveVoiceChannelAsync();
        }

        public async Task<string?> PlayMusicAsync(string musicInput, VoiceState newVoiceState, GatewayClient client)
        {
            // Check if the bot is currently playing music and add it to the queue if it is otherwise play the music
            if (!IsPlayingMusic)
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

                if (GetVoiceState()?.ChannelId != newVoiceState.ChannelId)
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

                    voiceClient = await client.JoinVoiceChannelAsync(voiceState!.GuildId, voiceState.ChannelId.GetValueOrDefault());
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
                        Log.LogMessage($"{ex} Exception occured while trying to play music!", LogType.Error);
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
            isPaused = false;
            await LeaveVoiceChannelAsync();
            await Log.LogAsync("Stopped music playback!");
        }

        public void PausePlayback()
        {
            isPaused = true;
            Log.LogMessage("Paused Playback");
        }

        public void ResumePlayback()
        {
            isPaused = false;
            Log.LogMessage("Resumed Playback");
        }

        public bool IsPlayingMusic => !(currentMusic is null || ((ytdlpProcess is null || ytdlpProcess.HasExited) && (ffmpegProcess is null || ffmpegProcess.HasExited)));
    }
}

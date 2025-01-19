using NetCord.Gateway.Voice;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace BloodysDiscordBot
{
    internal static class Helper
    {
        private const int musicInfoTimeOut = 10 * 1000;

        internal static string BuildFFMPEGArgs(string? input = null, string? output = null, float volume = 1f, ConcurrentDictionary<AudioFilter, MusicFilter>? filters = null)
        {
            StringBuilder sb = new();

            sb.AppendLine("-loglevel");
            sb.AppendLine(Globals.G_DebugMode ? "debug" : "8");

            if (input != null)
            {
                sb.AppendLine("-i");
                sb.AppendLine(input);
            }
            // Set the number of audio channels to 2 (stereo)
            sb.AppendLine("-ac");
            sb.AppendLine("2");

            // Set the audio sampling rate to 48kHz
            sb.AppendLine("-ar");
            sb.AppendLine("48000");

            // Set the output format to 16-bit signed little-endian
            sb.AppendLine("-f");
            sb.AppendLine("s16le");

            // Set the Volume
            sb.AppendLine("-filter:a");
            sb.Append($"\"volume={volume}");

            // Apply all other Filters
            if (filters != null)
            {
                foreach (var filter in filters)
                {
                    sb.Append($",{filter.Value.musicFilter}");
                }
            }
            sb.Append("\" ");

            if (output != null)
                sb.AppendLine(output);

            return sb.Replace(Environment.NewLine, " ").ToString().Trim();
        }

        internal static string BuildYTDLPArgs(string track, bool randomPlaylist = false, bool completePlaylist = true)
        {
            StringBuilder sb = new();

            sb.AppendLine(completePlaylist ? "--yes-playlist" : "--no-playlist");

            if (randomPlaylist)
                sb.AppendLine("--playlist-random");

            sb.AppendLine("-o -");

            sb.AppendLine("-q");
            sb.AppendLine("--verbose");
            sb.AppendLine("--no-progress");

            sb.AppendLine("-f bestaudio/best");

            sb.AppendLine("-x");

            sb.AppendLine(track);

            return sb.Replace(Environment.NewLine, " ").ToString().Trim();
        }

        internal static double? TimeStringToSeconds(string input)
        {
            // Find the 'time=' string inside of the ffmpeg error output and parse it
            int timeIndex = input.IndexOf("time=");  
            if (timeIndex == -1)
                return null;
            timeIndex += 5; // "time=" is 5 characters long

            int spaceIndex = input.IndexOf(' ', timeIndex); // Find the next space after "time="
            string timeString = spaceIndex == -1
                ? input[timeIndex..]
                : input[timeIndex..spaceIndex];

            string[] timeParts = timeString.Split(':');

            double hours = 0;
            double minutes;
            double seconds;
            if (timeParts.Length == 3) // HH:MM:SS.xx format
            {
                hours = double.Parse(timeParts[0]);
                minutes = double.Parse(timeParts[1]);
                seconds = double.Parse(timeParts[2]);
            }
            else if (timeParts.Length == 2) // MM:SS.xx format
            {
                minutes = double.Parse(timeParts[0]);
                seconds = double.Parse(timeParts[1]);
            }
            else
            {
                return null;
            }

            // Convert to total seconds
            return hours * 3600 + minutes * 60 + seconds;
        }

        internal static async Task ReadErrorStreamAsync(Stream errorStream, string processName, MusicBot? musicBot = null)
        {
            using StreamReader reader = new(errorStream);
            string? error;
            while ((error = await reader.ReadLineAsync()) != null)
            {
                await Log.LogDebugAsync($"{processName} ErrorStream: {error}");

                if (error.Contains("[error]", StringComparison.OrdinalIgnoreCase))
                {
                    await Log.LogAsync($"{processName} Error: {error}", LogType.Error); // Only display error if it actually is an error
                }

                if (musicBot is not null && "ffmpeg".Equals(processName))
                {
                    // Extract time
                    double? currentTime = TimeStringToSeconds(error);
                    if (currentTime.HasValue) 
                        musicBot.currentMusicLocation = currentTime.Value;
                }
            }
        }

        internal static async Task StreamDataAsync(Stream ytdlpStream, Stream ffmpegStream, int maxQueueSize, MusicBot musicBot, CancellationToken cancellationToken)
        {
            // TODO: Fix pauses... Not Sure how yet // Maybe buffer output from ffmpeg instead?
            var buffer = new byte[4096]; // 8192
            int bytesRead;
            bool wasPaused = false;

            var dataQueue = new Queue<byte[]>();

            while (!cancellationToken.IsCancellationRequested)
            {
                // Check if paused or queue is full
                if (musicBot.isPaused)
                {
                    wasPaused = true;
                    if (dataQueue.Count >= maxQueueSize)
                    {
                        // Pause Streaming data
                        await Task.Delay(100, cancellationToken);
                        continue;
                    }
                }

                // Read data from ytdlp Stream if queue has space and playback if not paused
                bytesRead = await ytdlpStream.ReadAsync(buffer, cancellationToken);

                if (cancellationToken.IsCancellationRequested || (bytesRead <= 0 && dataQueue.Count <= 0))
                    break; // End of Stream and queue is empty or cancellation requested

                if (bytesRead > 0)
                {
                    byte[] data = new byte[bytesRead];

                    Array.Copy(buffer, data, bytesRead);

                    // Add the data to the queue

                    dataQueue.Enqueue(data);
                }

                // Write to ffmpeg stream if there's data in the queue and music is not paused
                while (dataQueue.Count > 0 && !musicBot.isPaused)
                {
                    byte[] queuedData = dataQueue.Dequeue();
                    await ffmpegStream.WriteAsync(queuedData, cancellationToken);
                    if (wasPaused)
                        await Task.Delay((int)(queuedData.Length / 44100.0 * 1000), cancellationToken); // Assume 44.1kHz audio // Without will skip forward
                }
            }
        }

        internal static bool IsValidUrl(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uriResult))
            {
                // Check if the scheme is either HTTP or HTTPS
                return uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps;
            }

            return false;
        }

        internal static MusicQueueItem? GetMusicInfo(string input)
        {
            try
            {
                string ytdlpArgs = $"--print \"title,duration,uploader,webpage_url\" \"{input}\"";

                // Start the Process

                Process process = new()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "yt-dlp",
                        Arguments = ytdlpArgs,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();

                if (!process.WaitForExit(musicInfoTimeOut))
                {
                    Log.LogMessage("Timed out on getting Music Info!", LogType.Error);
                    return null;
                }
                //process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    string error = process.StandardError.ReadToEnd();
                    Log.LogMessage($"{error} occured while trying to get music info!", LogType.Error);
                    return null;
                }

                // Read output
                string output = process.StandardOutput.ReadToEnd();

                Log.LogDebug($"GetMusicInfo output from ytdlp: '{output}'");

                // Parse output
                string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                if (lines.Length >= 4)
                {
                    if (!uint.TryParse(lines[1], out var duration))
                        duration = 0;
                    return new MusicQueueItem(fileOrURL: lines[3], songName: lines[0], isFile: false, author: lines[2], duration: duration);
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.LogDebug($"{ex} occured while trying to parse Music Info");
                return null;
            }
        }

        internal static void ConcurrentQueueAddToFront<T>(ConcurrentQueue<T> queue, T item)
        {
            var stack = new ConcurrentStack<T>();

            while(queue.TryDequeue(out var queuedItem))
            {
                stack.Push(queuedItem);
            }

            queue.Enqueue(item);

            while(stack.TryPop(out var poppedItem))
            {
                queue.Enqueue(poppedItem);
            }
        }

        internal static bool ConcurrentQueueTryRemoveLast<T>(ConcurrentQueue<T> queue)
        {
            List<T> tempList = [];
            bool itemRemoved = false;

            // Dequeue all items
            while (queue.TryDequeue(out var item))
            {
                tempList.Add(item);
            }

            if (tempList.Count > 0)
            {
                // Remove the last item
                tempList.RemoveAt(tempList.Count - 1);
                itemRemoved = true;
            }

            // Re-enqueue the remaining items
            foreach (var item in tempList)
            {
                queue.Enqueue(item);
            }

            return itemRemoved; // Return true if an item was removed
        }
    }
}

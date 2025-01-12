using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;

namespace BloodysDiscordBot
{
    internal static class Helper
    {
        private const int musicInfoTimeOut = 10 * 1000;

        internal static string BuildFFMPEGArgs(string? input = null, string? output = null, float volume = 1f)
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
            sb.AppendLine($"volume={volume}");

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

        internal static async Task ReadStreamAsync(StreamReader reader, string streamName)
        {
            while (!reader.EndOfStream)
            {
                string? line = await reader.ReadLineAsync();
                //await Log.LogDebugAsync($"[{streamName}] {line}");
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
    }
}

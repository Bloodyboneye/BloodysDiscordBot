using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;

namespace BloodysDiscordBot
{
    internal static class Helper
    {
        internal static string BuildFFMPEGArgs(string? input = null, string? output = null)
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
            sb.AppendLine($"volume={Globals.G_BotMusicVolume}");

            // Direct output to stdout
            if (output != null)
                sb.AppendLine(output);

            return sb.Replace(Environment.NewLine, " ").ToString().Trim();
        }

        internal static string BuildYTDLPArgs(string track, bool randomPlaylist = false)
        {
            StringBuilder sb = new();

            sb.AppendLine(Globals.G_BotMusicDownloadPlayList ? "--yes-playlist" : "--no-playlist");

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
                await Log.LogDebugAsync($"[{streamName}] {line}");
            }
        }
    }
}

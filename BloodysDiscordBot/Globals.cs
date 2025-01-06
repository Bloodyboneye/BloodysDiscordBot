using NetCord.Gateway;
using NetCord.JsonConverters;
using NetCord.Services.ApplicationCommands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BloodysDiscordBot
{
    internal static class Globals
    {
        internal static string G_BotToken { get; set; } = string.Empty;

        internal static ApplicationCommandService<ApplicationCommandContext>? G_ApplicationCommandService { get; set; }

        internal static GatewayClient? G_GatewayClient { get; set; }

        internal static ulong G_BotAuthor { get; set; }

        internal static bool G_DebugMode { get; set; } = false;

        internal static int G_BotMusicVolume { get; set; } = 1;

        internal static int G_BotMusicMaxVolume { get; set; } = 100; 

        internal static bool G_BotMusicDownloadPlayList { get; set; } = true;
    }
}

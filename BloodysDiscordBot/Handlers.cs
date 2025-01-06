using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;

namespace BloodysDiscordBot
{
    internal static class Handlers
    {
        internal static async ValueTask HandleInteraction(Interaction interaction)
        {
            if (interaction is not ApplicationCommandInteraction applicationCommandInteraction)
                return;

            if (Globals.G_ApplicationCommandService is null)
            {
                await Log.LogDiscordClientMessageAsync(LogMessage.Info("'G_ApplicationCommandService' is null"));
                return;
            }

            if (Globals.G_GatewayClient is null)
            {
                await Log.LogDiscordClientMessageAsync(LogMessage.Info("'G_GatewayClient' is null"));
                return;
            }

            var result = await Globals.G_ApplicationCommandService.ExecuteAsync(new ApplicationCommandContext(applicationCommandInteraction, Globals.G_GatewayClient));

            if (result is not IFailResult failResult)
                return;

            try
            {
                await interaction.SendResponseAsync(InteractionCallback.Message(failResult.Message));
            }
            catch (Exception ex)
            {
                LogMessage logMessage = LogMessage.Error(ex);
                await Log.LogDiscordClientMessageAsync(logMessage);
                await Log.LogAsync(failResult.Message);
            }
        }

        internal static async ValueTask HandleVoiceStateUpdate(VoiceState voiceState)
        {
            
        }
    }
}

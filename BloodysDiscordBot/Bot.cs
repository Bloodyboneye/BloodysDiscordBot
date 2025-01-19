using NetCord;
using NetCord.Gateway;
using NetCord.Gateway.Voice;
using System.Collections.Concurrent;

namespace BloodysDiscordBot
{
    public class Bot
    {
        public static readonly ConcurrentDictionary<ulong, ConcurrentDictionary<Type, Bot>> AllBots = [];

        public readonly Guild guild;

        public readonly GatewayClient client;

        public VoiceClient? voiceClient;

        public TextChannel? textChannel;

        public VoiceState? voiceState;

        public Bot(Guild guild, GatewayClient client)
        {
            this.guild = guild ?? throw new ArgumentNullException(nameof(guild));
            this.client = client ?? throw new ArgumentNullException(nameof(client));

            var botsByType = AllBots.GetOrAdd(guild.Id, _ => new ConcurrentDictionary<Type, Bot>());

            if (!botsByType.TryAdd(GetType(), this))
            {
                throw new InvalidOperationException($"A bot of type {GetType().Name} already exists for guild {guild.Id}.");
            }
        }

        public void Delete()
        {
            if (AllBots.TryGetValue(guild.Id, out var botsByType))
            {
                botsByType.TryRemove(GetType(), out _);

                if (botsByType.IsEmpty)
                {
                    AllBots.TryRemove(guild.Id, out _);
                }
            }
        }

        public static TBot? GetBot<TBot>(ulong guildId) where TBot : Bot
        {
            if (AllBots.TryGetValue(guildId, out var botsByType) &&
                botsByType.TryGetValue(typeof(TBot), out var bot) &&
                bot is TBot typedBot)
            {
                return typedBot;
            }
            return null;
        }

        public VoiceState? GetVoiceState()
        {
            if (this.voiceState == null)
            {
                _ = guild.VoiceStates.TryGetValue(client.Id, out this.voiceState);
            }
            return this.voiceState;
        }

        public virtual async Task LeaveVoiceChannelAsync()
        {
            if (this.voiceState == null)
            {
                // Try another way
                if (guild.VoiceStates.TryGetValue(client.Id, out var voiceState) && voiceState != null)
                {
                    this.voiceState = voiceState;
                }
                else
                {
                    Log.LogDebug("Called LeaveVoiceChannel on bot but bot is not connected to any Voice Channel!");
                    return;
                }
            }
            // Diconnect Bot
            if (this.voiceClient != null)
            {
                try
                {
                    await this.voiceClient.CloseAsync();
                }
                catch { };
            }

            await this.client!.UpdateVoiceStateAsync(new(guild.Id, null));

            this.voiceState = null;
            this.voiceClient = null;
        }
    }
}

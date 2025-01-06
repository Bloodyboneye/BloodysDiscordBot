using NetCord;
using NetCord.Gateway;
using NetCord.Services.ApplicationCommands;
using System.Diagnostics.CodeAnalysis;

namespace BloodysDiscordBot
{
    internal class Program
    {
        //[RequiresUnreferencedCode("Calls NetCord.Services.ApplicationCommands.ApplicationCommandService<TContext>.AddModules(Assembly)")]
        static async Task Main(string[] args)
        {
            HandleArgs(args);

            if (string.IsNullOrEmpty(Globals.G_BotToken))
            {
                Console.WriteLine("BotToken can not be empty! Use '-bt BotToken' to set it!");
                Console.ReadLine();
                return;
            }

            GatewayClient client = new(new BotToken(Globals.G_BotToken), new GatewayClientConfiguration()
            {
                Intents = Globals.G_DebugMode ? GatewayIntents.All : GatewayIntents.AllNonPrivileged
            });

            Globals.G_GatewayClient = client;

            client.Log += Log.LogDiscordClientMessageAsync;

            client.MessageCreate += message =>
            {
                if (Globals.G_DebugMode)
                    Console.WriteLine(message.Content);
                return default;
            };

            client.VoiceStateUpdate += Handlers.HandleVoiceStateUpdate;

            if (Globals.G_DebugMode)
            {
                client.MessageReactionAdd += async args =>
                {
                    await client.Rest.SendMessageAsync(args.ChannelId, $"<@{args.UserId}> reacted with {args.Emoji.Name}!");
                };
            }

            ApplicationCommandService<ApplicationCommandContext> applicationCommandService = new();

            Globals.G_ApplicationCommandService = applicationCommandService;

            if (Globals.G_DebugMode)
            {
                applicationCommandService.AddSlashCommand("ping", "Ping!", () => "Pong!");
            }

            //applicationCommandService.AddModules(typeof(Program).Assembly);

            if (Globals.G_DebugMode)
            {
                applicationCommandService.AddModule<TestModule>();
            }

            applicationCommandService.AddModule<MusicModule>();

            client.InteractionCreate += Handlers.HandleInteraction;

            await applicationCommandService.CreateCommandsAsync(client.Rest, client.Id);

            await client.StartAsync();
            await Task.Delay(-1);
        }

        static void HandleArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "-bt":
                    case "-bottoken":
                        if (i + 1 >= args.Length)
                            break;
                        Globals.G_BotToken = args[i+1];
                        i = i + 1;
                        break;
                    case "-ba":
                    case "-botauthor":
                        if (i + 1 >= args.Length)
                            break;
                        if (!ulong.TryParse(args[i + 1], out ulong authortoken))
                        {
                            Console.WriteLine("Failed to parse Bot Author Token!");
                            i = i + 1;
                            break;
                        }
                        Globals.G_BotAuthor = authortoken;
                        i = i + 1;
                        break;
                    case "-d":
                    case "-debug":
                        Globals.G_DebugMode = true;
                        break;
                    default:
                        Console.WriteLine($"Invalid Argument: {args[i]}");
                        Console.ReadLine();
                        Environment.Exit(0);
                        break;
                }
            }
        }
    }
}

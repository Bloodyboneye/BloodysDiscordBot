using NetCord;
using NetCord.Services.ApplicationCommands;

namespace BloodysDiscordBot
{
    public class TestModule : ApplicationCommandModule<ApplicationCommandContext>
    {
        [SlashCommand("pong", "Pong!")]
        public static string Pong()
        {
            return "Ping!";
        }

        [SlashCommand("username", "Returns user's username")]
        public string Username(User? user = null)
        {
            user ??= Context.User;
            return user.Username;
        }
    }
}
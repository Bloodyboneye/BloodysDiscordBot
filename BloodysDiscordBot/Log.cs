using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NetCord;
using NetCord.Gateway;

namespace BloodysDiscordBot
{
    internal enum LogType : uint
    {
        Info,
        Error,
        Debug,
    }

    internal static class Log
    {
        private const string ErrorSeverity = "Error";

        private const string InfoSeverity = "Info";

        private const string DebugSeverity = "Debug";
        internal static async ValueTask LogDiscordClientMessageAsync(LogMessage message)
        {
            Console.WriteLine(message);

            await Task.CompletedTask;
        }

        internal static void LogMessage(string message, LogType logType = LogType.Info)
        {
            if (message == null)
                return;

            string severity = string.Empty;
            switch (logType)
            {
                case LogType.Info:
                    severity = InfoSeverity;
                    break;
                case LogType.Error:
                    severity = ErrorSeverity;
                    break;
                case LogType.Debug:
                    severity = DebugSeverity;
                    break;
                default:
                    severity = InfoSeverity;
                    break;
            }
            Console.WriteLine($"{DateTime.Now:T} [{severity}] {message}");
        }

        internal static void LogDebug(string message)
        {
            if (Globals.G_DebugMode)
                LogMessage(message, LogType.Debug);
        }

        internal static async ValueTask LogAsync(string message, LogType logType = LogType.Info)
        {
            LogMessage(message, logType);
            await Task.CompletedTask;
        }

        internal static async ValueTask LogDebugAsync(string message)
        {
            if (Globals.G_DebugMode)
                await LogAsync(message, LogType.Debug);
        }
    }
}

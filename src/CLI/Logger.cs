using System.CommandLine.Binding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace KanikoRemote.CLI
{
    internal class LoggingBinder : BinderBase<ILoggerFactory>
    {
        protected override ILoggerFactory GetBoundValue(BindingContext bindingContext)
        {
            ILoggerFactory loggerFactory = LoggerFactory.Create(
                builder => builder
                    .AddConsole(options => options.FormatterName = "kaniko-remote")
                    .AddConsoleFormatter<KanikoRemoteConsoleFormatter, ConsoleFormatterOptions>());

            return loggerFactory;
        }
    }

    internal sealed class KanikoRemoteConsoleFormatter : ConsoleFormatter
    {
        public static readonly EventId OverwritableEvent = new EventId(2002, "overwritable");

        public KanikoRemoteConsoleFormatter() : base("kaniko-remote") { }

        public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider scopeProvider, TextWriter textWriter)
        {
            string? message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);

            if (message is null)
            {
                return;
            }

            LogLevel logLevel = logEntry.LogLevel;
            var (logLevelString, foreColor, backColor) = GetLogLevel(logLevel);

            textWriter.Write("[KANIKO-REMOTE] (");
            textWriter.WriteWithColor(logLevelString, foreColor, backColor);
            textWriter.Write(") ");

            if (logEntry.EventId == OverwritableEvent)
            {
                textWriter.Write(message);
                textWriter.Write("\r");
            }
            else
            {
                textWriter.WriteLine(message);
            }
        }

        private static (string, ConsoleColor, ConsoleColor) GetLogLevel(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => ("trce", ConsoleColor.Gray, ConsoleColor.Black),
                LogLevel.Debug => ("dbug", ConsoleColor.Gray, ConsoleColor.Black),
                LogLevel.Information => ("info", ConsoleColor.DarkGreen, ConsoleColor.Black),
                LogLevel.Warning => ("warn", ConsoleColor.Yellow, ConsoleColor.Black),
                LogLevel.Error => ("fail", ConsoleColor.Black, ConsoleColor.DarkRed),
                LogLevel.Critical => ("crit", ConsoleColor.White, ConsoleColor.DarkRed),
                _ => throw new ArgumentOutOfRangeException(nameof(logLevel))
            };
        }

    }
    public static class TextWriterExtensions
    {
        const string DefaultForegroundColor = "\x1B[39m\x1B[22m";
        const string DefaultBackgroundColor = "\x1B[49m";

        public static void WriteWithColor(
            this TextWriter textWriter,
            string message,
            ConsoleColor? foreground,
            ConsoleColor? background)
        {
            // Order:
            //   1. background color
            //   2. foreground color
            //   3. message
            //   4. reset foreground color
            //   5. reset background color

            var backgroundColor = background.HasValue ? GetBackgroundColorEscapeCode(background.Value) : null;
            var foregroundColor = foreground.HasValue ? GetForegroundColorEscapeCode(foreground.Value) : null;

            if (backgroundColor != null)
            {
                textWriter.Write(backgroundColor);
            }
            if (foregroundColor != null)
            {
                textWriter.Write(foregroundColor);
            }

            textWriter.Write(message);

            if (foregroundColor != null)
            {
                textWriter.Write(DefaultForegroundColor);
            }
            if (backgroundColor != null)
            {
                textWriter.Write(DefaultBackgroundColor);
            }
        }

        static string GetForegroundColorEscapeCode(ConsoleColor color) =>
            color switch
            {
                ConsoleColor.Black => "\x1B[30m",
                ConsoleColor.DarkRed => "\x1B[31m",
                ConsoleColor.DarkGreen => "\x1B[32m",
                ConsoleColor.DarkYellow => "\x1B[33m",
                ConsoleColor.DarkBlue => "\x1B[34m",
                ConsoleColor.DarkMagenta => "\x1B[35m",
                ConsoleColor.DarkCyan => "\x1B[36m",
                ConsoleColor.Gray => "\x1B[37m",
                ConsoleColor.Red => "\x1B[1m\x1B[31m",
                ConsoleColor.Green => "\x1B[1m\x1B[32m",
                ConsoleColor.Yellow => "\x1B[1m\x1B[33m",
                ConsoleColor.Blue => "\x1B[1m\x1B[34m",
                ConsoleColor.Magenta => "\x1B[1m\x1B[35m",
                ConsoleColor.Cyan => "\x1B[1m\x1B[36m",
                ConsoleColor.White => "\x1B[1m\x1B[37m",

                _ => DefaultForegroundColor
            };

        static string GetBackgroundColorEscapeCode(ConsoleColor color) =>
            color switch
            {
                ConsoleColor.Black => "\x1B[40m",
                ConsoleColor.DarkRed => "\x1B[41m",
                ConsoleColor.DarkGreen => "\x1B[42m",
                ConsoleColor.DarkYellow => "\x1B[43m",
                ConsoleColor.DarkBlue => "\x1B[44m",
                ConsoleColor.DarkMagenta => "\x1B[45m",
                ConsoleColor.DarkCyan => "\x1B[46m",
                ConsoleColor.Gray => "\x1B[47m",

                _ => DefaultBackgroundColor
            };
    }
}
using System.Collections.Concurrent;
using System.CommandLine.Binding;
using System.Text.Json.Serialization;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KanikoRemote.CLI
{
    internal class LoggerBinder : BinderBase<ILoggerFactory>
    {
        private static readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(builder => {
            builder.AddProvider(new ColorConsoleLoggerProvider());
        });

        protected override ILoggerFactory GetBoundValue(BindingContext bindingContext) => _loggerFactory;
    }

    public class LoggerConfiguration
    {
        public int EventId { get; set; }

        public bool ColorEnabled { get; set; }

        public List<LogLevel> LogLevels { get; set; } = new()
        {
            LogLevel.Information, LogLevel.Warning
        };

        public List<string> OverwritableEvents { get; set; } = new()
        {
            "progress"
        };

        public List<string> RawEvents { get; set; } = new()
        {
            "version"
        };
    }

    internal sealed class ColorConsoleLoggerProvider : ILoggerProvider
    {
        private readonly IDisposable? _onChangeToken;
        private LoggerConfiguration _currentConfig;
        private readonly ConcurrentDictionary<string, Logger> _loggers =
            new(StringComparer.OrdinalIgnoreCase);


        public ColorConsoleLoggerProvider()
        {
            _currentConfig = new LoggerConfiguration();
            _onChangeToken = null;
        }

        public ColorConsoleLoggerProvider(
            IOptionsMonitor<LoggerConfiguration> config)
        {
            _currentConfig = config.CurrentValue;
            _onChangeToken = config.OnChange(updatedConfig => _currentConfig = updatedConfig);
        }

        public ILogger CreateLogger(string categoryName) =>
            _loggers.GetOrAdd(categoryName, name => new Logger(name, GetCurrentConfig));

        private LoggerConfiguration GetCurrentConfig() => _currentConfig;

        public void Dispose()
        {
            _loggers.Clear();
            _onChangeToken?.Dispose();
        }
    }

    internal class Logger : ILogger
    {
        private readonly string _name;
        private readonly Func<LoggerConfiguration> _getCurrentConfig;

        public Logger(string name, Func<LoggerConfiguration> getCurrentConfig) => (_name, _getCurrentConfig) = (name, getCurrentConfig);

        public IDisposable BeginScope<TState>(TState state) => default!;

        public bool IsEnabled(LogLevel logLevel) => _getCurrentConfig().LogLevels.Contains(logLevel);
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            LoggerConfiguration config = _getCurrentConfig();
            if (eventId.Name != null && config.RawEvents.Contains(eventId.Name))
            {
                Console.WriteLine(formatter(state, exception));
                return;
            }
            
            var (logLevelText, foreColor, backColor) = GetColoredLogLevel(logLevel);
            
            Console.Write("[KANIKO-REMOTE] (");
            if (config.ColorEnabled)
            {
                Console.ForegroundColor = foreColor;
                Console.BackgroundColor = backColor;
            }
            Console.Write(logLevelText);
            if (config.ColorEnabled)
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.BackgroundColor = ConsoleColor.Black;
            }
            Console.Write(") ");

            var message = formatter(state, exception);
            if (eventId.Name != null && config.OverwritableEvents.Contains(eventId.Name))
            {
                Console.Write(message);
                Console.Write("\r");
            }
            else
            {
                Console.WriteLine(message);
            }
        }

        private static (string, ConsoleColor, ConsoleColor) GetColoredLogLevel(LogLevel logLevel)
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

    [JsonSourceGenerationOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(V1ContainerState))]
    [JsonSerializable(typeof(V1ContainerStateTerminated))]
    internal partial class LoggerSerialiserContext : JsonSerializerContext { }
}
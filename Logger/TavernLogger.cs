using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using CCTavern;

namespace CCTavern.Logger {
    public class TavernLogger : ILogger<Program> {
        private static readonly object _lock = new();
        private const int CATEGORY_MAX_LENGTH = 12;


        private string CategoryName { get; }
        private LogLevel MinimumLevel { get; }
        private string TimestampFormat { get; }

        //internal DefaultLogger(Program client)
        //    : this(client.Configuration.MinimumLogLevel, client.Configuration.LogTimestampFormat) { }

        internal TavernLogger(LogLevel minLevel = LogLevel.Information, string timestampFormat = "yyyy-MM-dd HH:mm:ss zzz") {
            this.MinimumLevel = minLevel;
            this.TimestampFormat = timestampFormat;
        }

        internal TavernLogger(string categoryName, LogLevel minLevel = LogLevel.Information, string timestampFormat = "yyyy-MM-dd HH:mm:ss zzz") {
            this.CategoryName = categoryName;
            this.MinimumLevel = minLevel;
            this.TimestampFormat = timestampFormat;
        }

#pragma warning disable CS8767 // Nullability of reference types in type of parameter doesn't match implicitly implemented member (possibly because of nullability attributes).
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) {
            if (!this.IsEnabled(logLevel))
                return;

            lock (_lock) {
                var ename = eventId.Name;
                ename = ename?.Length > CATEGORY_MAX_LENGTH ? ename?.Substring(0, CATEGORY_MAX_LENGTH) : ename;

                Console.Write($"[{DateTimeOffset.Now.ToString(this.TimestampFormat)}] [{eventId.Id,-4}/{ename,-CATEGORY_MAX_LENGTH}] ");

                switch (logLevel) {
                    case LogLevel.Trace:
                        Console.ForegroundColor = ConsoleColor.Gray;
                        break;

                    case LogLevel.Debug:
                        Console.ForegroundColor = ConsoleColor.DarkMagenta;
                        break;

                    case LogLevel.Information:
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        break;

                    case LogLevel.Warning:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        break;

                    case LogLevel.Error:
                        Console.ForegroundColor = ConsoleColor.Red;
                        break;

                    case LogLevel.Critical:
                        Console.BackgroundColor = ConsoleColor.Red;
                        Console.ForegroundColor = ConsoleColor.Black;
                        break;
                }
                Console.Write(logLevel switch {
                    LogLevel.Trace => "[Trace] ",
                    LogLevel.Debug => "[Debug] ",
                    LogLevel.Information => "[Info ] ",
                    LogLevel.Warning => "[Warn ] ",
                    LogLevel.Error => "[Error] ",
                    LogLevel.Critical => "[Crit ]",
                    LogLevel.None => "[None ] ",
                    _ => "[?????] "
                });
                Console.ResetColor();

                //The foreground color is off.
                if (logLevel == LogLevel.Critical)
                    Console.Write(" ");

                var message = formatter(state, exception);
                Console.WriteLine(message);
                if (exception != null)
                    Console.WriteLine(exception);
            }
        }
#pragma warning restore CS8767 // Nullability of reference types in type of parameter doesn't match implicitly implemented member (possibly because of nullability attributes).

        public bool IsEnabled(LogLevel logLevel)
            => logLevel >= this.MinimumLevel;

#pragma warning disable CS8633 // Nullability in constraints for type parameter doesn't match the constraints for type parameter in implicitly implemented interface method'.
        public IDisposable BeginScope<TState>(TState state)
            => new DisposableStim(); //throw new NotImplementedException();

        private class DisposableStim : IDisposable {
            public void Dispose() { }
        } 
#pragma warning restore CS8633 // Nullability in constraints for type parameter doesn't match the constraints for type parameter in implicitly implemented interface method'.
    }
}

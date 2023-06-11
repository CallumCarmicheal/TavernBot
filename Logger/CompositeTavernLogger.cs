using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System;
using Microsoft.Extensions.Logging;
using CCTavern;

namespace CCTavern.Logger {
    internal class CompositeTavernLogger : ILogger<Program> {
        private IEnumerable<ILogger<Program>> Loggers { get; }

        public CompositeTavernLogger(IEnumerable<ILoggerProvider> providers) {
            this.Loggers = providers.Select(x => x.CreateLogger(typeof(Program).FullName))
                .OfType<ILogger<Program>>()
                .ToList();
        }

        public bool IsEnabled(LogLevel logLevel)
            => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) {
            foreach (var logger in this.Loggers)
                logger.Log(logLevel, eventId, state, exception, formatter);
        }

        public IDisposable BeginScope<TState>(TState state) => throw new NotImplementedException();
    }
}

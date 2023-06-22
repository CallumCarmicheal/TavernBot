using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using CCTavern;

namespace CCTavern.Logger {
    internal class CompositeTavernLogger : ILogger<Program> {
        private IEnumerable<ILogger<Program>> Loggers { get; }

        public CompositeTavernLogger(IEnumerable<ILoggerProvider> providers) {
#pragma warning disable CS8604 // Possible null reference argument.
            this.Loggers = providers.Select(x => x.CreateLogger(typeof(Program).FullName))
                .OfType<ILogger<Program>>()
                .ToList();
#pragma warning restore CS8604 // Possible null reference argument.
        }

        public bool IsEnabled(LogLevel logLevel)
            => true;

#pragma warning disable CS8767 // Nullability of reference types in type of parameter doesn't match implicitly implemented member (possibly because of nullability attributes).
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) {
            foreach (var logger in this.Loggers)
#pragma warning disable CS8620 // Argument cannot be used for parameter due to differences in the nullability of reference types.
                logger.Log(logLevel, eventId, state, exception, formatter);
#pragma warning restore CS8620 // Argument cannot be used for parameter due to differences in the nullability of reference types.
        }
#pragma warning restore CS8767 // Nullability of reference types in type of parameter doesn't match implicitly implemented member (possibly because of nullability attributes).

#pragma warning disable CS8633 // Nullability in constraints for type parameter doesn't match the constraints for type parameter in implicitly implemented interface method'.
        public IDisposable BeginScope<TState>(TState state) => throw new NotImplementedException();
#pragma warning restore CS8633 // Nullability in constraints for type parameter doesn't match the constraints for type parameter in implicitly implemented interface method'.
    }
}

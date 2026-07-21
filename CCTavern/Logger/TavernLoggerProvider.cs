using DSharpPlus;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Logger {
    internal class TavernLoggerProvider : ILoggerProvider {
        private LogLevel MinimumLevel { get; }
        private string TimestampFormat { get; }

        private bool _isDisposed = false;

        // TODO: Setup logging levels
        // internal DefaultLoggerProvider(BaseDiscordClient client)
        //     : this(client.Configuration.MinimumLogLevel, client.Configuration.LogTimestampFormat) { }

        internal TavernLoggerProvider(LogLevel minLevel = LogLevel.Information, string timestampFormat = "yyyy-MM-dd HH:mm:ss zzz") {
            this.MinimumLevel = minLevel;
            this.TimestampFormat = timestampFormat;
        }

        public ILogger CreateLogger(string categoryName) {
            if (this._isDisposed)
                throw new InvalidOperationException("This logger provider is already disposed.");

            return new TavernLogger(this.MinimumLevel, this.TimestampFormat);
        }

        public void Dispose() => this._isDisposed = true;
    }
}

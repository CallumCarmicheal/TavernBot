using System;
using System.Collections.Generic;
using System.Linq;

using CCTavern;

using DSharpPlus;

using Microsoft.Extensions.Logging;

namespace CCTavern.Logger {
    internal class TavernLoggerFactory : ILoggerFactory {
        private List<ILoggerProvider> Providers { get; } = new List<ILoggerProvider>();
        private bool _isDisposed = false;

        public void AddProvider(ILoggerProvider provider) => this.Providers.Add(provider);

        public ILogger CreateLogger(string categoryName) {
            if (this._isDisposed)
                throw new InvalidOperationException("This logger factory is already disposed.");

            return new CompositeTavernLogger(this.Providers.AsEnumerable()); ;

            return categoryName != typeof(Program).FullName
                ? throw new ArgumentException($"This factory can only provide instances of loggers for {typeof(Program).FullName}.", nameof(categoryName))
                : new CompositeTavernLogger(this.Providers.AsEnumerable());
        }

        public void Dispose() {
            if (this._isDisposed)
                return;
            this._isDisposed = true;

            foreach (var provider in this.Providers)
                provider.Dispose();

            this.Providers.Clear();
        }
    }
}

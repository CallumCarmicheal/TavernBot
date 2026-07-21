using DSharpPlus;

using Lavalink4NET;
using Lavalink4NET.Players;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CCTavern.Player {
    internal class RadioStreamPlayer : LavalinkPlayer, IDisposable {
        private bool disposedValue;

        private readonly ILogger<RadioStreamPlayer> logger;
        private readonly MusicBotHelper mbHelper;
        private readonly DiscordClient discordClient;
        private readonly IAudioService audioService;
        private readonly BotInactivityManager botInactivityManager;

        private Timer _timer;
        private CancellationTokenSource _cancellationTokenSource;

        public RadioStreamPlayer(IPlayerProperties<LavalinkPlayer, LavalinkPlayerOptions> properties) : base(properties) {
            _cancellationTokenSource = new CancellationTokenSource();
            _timer = new Timer(callback: ProgressBarTimerCallback, state: null, dueTime: Timeout.Infinite, period: Timeout.Infinite);

            mbHelper = properties.ServiceProvider!.GetRequiredService<MusicBotHelper>();
            discordClient = properties.ServiceProvider!.GetRequiredService<DiscordClient>();
            audioService = properties.ServiceProvider!.GetRequiredService<IAudioService>();
            botInactivityManager = properties.ServiceProvider!.GetRequiredService<BotInactivityManager>();

            logger = properties.ServiceProvider!.GetRequiredService<ILogger<RadioStreamPlayer>>();

            logger.LogDebug("RadioStreamPlayer <<<<<<<<< Constructor");
        }

        private void ProgressBarTimerCallback(object? state) {
            // 
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    // TODO: dispose managed state (managed objects)
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        ~RadioStreamPlayer() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);

            logger.LogDebug("RadioStreamPlayer <<<<<<<<< Destructor");
        }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}

using CCTavern.Web;

using EmbedIO;
using EmbedIO.WebApi;

using Microsoft.Extensions.DependencyInjection;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CCTavern {
    internal class WebServer {
        public IServiceProvider ServiceProvider { get; }

        public WebServer(IServiceProvider serviceProvider) {
            ServiceProvider = serviceProvider;
        }

        public async Task StartServer(CancellationToken cancellationToken) {
            using var server = new EmbedIO.WebServer(o => o
                   .WithUrlPrefix("http://*:10001/")
                   .WithMode(HttpListenerMode.EmbedIO));

            // Inject dependencies into controller
            server.WithLocalSessionManager();
            _ = server.WithWebApi("Serve status page", "/api/status",
                m => m.RegisterController( () => ServiceProvider.GetInstance<StatusController>()! )
            );
            server.WithAction("/", HttpVerbs.Any, async (ctx) => await ctx.SendStandardHtmlAsync(200, (x) => { x.WriteLine("Waiting for API requests"); }));

            // Start server
            await server.RunAsync(cancellationToken);
        }
    }
}

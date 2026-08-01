using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CCTavern.Web {
    internal static class WebServer {
        private const int Port = 10001;

        public static void Configure(WebApplicationBuilder builder) {
            builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(Port));
            builder.Services
                .AddControllers()
                .AddApplicationPart(typeof(StatusController).Assembly);
        }

    #region Placeholder endpoints

        private const string HtmlContentType = "text/html; charset=utf-8";

        private const string RootHtml = """
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>CCTavern</title>
            </head>
            <body>Waiting for API requests</body>
            </html>
            """;

        private const string LoginHtml = """
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>Login - CCTavern</title>
            </head>
            <body>
                <h1>Login</h1>
                <p>Authentication is not available yet.</p>
            </body>
            </html>
            """;

        private const string LogoutHtml = """
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>Logout - CCTavern</title>
            </head>
            <body>
                <h1>Logout</h1>
                <p>Authentication is not available yet.</p>
            </body>
            </html>
            """;

        private const string HomeHtml = """
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>Home - CCTavern</title>
            </head>
            <body>
                <h1>Welcome</h1>
            </body>
            </html>
            """;

        public static void MapEndpoints(WebApplication app) {
            app.MapControllers();
            app.MapGet("/", () => Results.Content(RootHtml, HtmlContentType));
            app.MapGet("/app/auth/login", () => Results.Content(LoginHtml, HtmlContentType));
            app.MapGet("/app/auth/logout", () => Results.Content(LogoutHtml, HtmlContentType));
            app.MapGet("/app/home", () => Results.Content(HomeHtml, HtmlContentType));
        }
        #endregion
    }
}

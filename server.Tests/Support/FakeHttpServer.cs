/*!************************************************************************
\file   FakeHttpServer.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   2nd November 2026
\brief  This file defines FakeHttpServer, a tiny real web server on a free
        local port that answers the way a test needs: it echoes the headers
        it was sent, answers 401 with and without a code, answers 500 and
        503, and can be slow. It lets the SDK tests see real network
        events, such as a timeout, a refused connection or a 5xx, instead
        of faking them.

        Used by HakutakuHttpTests and the server SDK tests. Needs no
        database.
**************************************************************************/
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace server.Tests.Support
{
    public sealed class FakeHttpServer : IAsyncDisposable
    {
        readonly WebApplication app;

        // like http://127.0.0.1:53211, with no trailing slash
        public string Url { get; }

        // how many requests have reached it, so a test can check one was never even sent
        public int Requests;

        FakeHttpServer(WebApplication app, string url)
        {
            this.app = app;
            Url = url;
        }

        public static async Task<FakeHttpServer> Start()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            var app = builder.Build();

            FakeHttpServer? self = null;
            app.Use(async (context, next) =>
            {
                if (self is not null) Interlocked.Increment(ref self.Requests);
                await next();
            });

            // what it was sent, so a test can check which headers went out
            app.Map("/headers", (HttpRequest r) => Results.Json(new
            {
                authorization = r.Headers.Authorization.ToString(),
                serverKey = r.Headers["X-Server-Key"].ToString(),
                method = r.Method,
            }));
            // echoes the body back, so a test can check what was sent
            app.MapPost("/echo", async (HttpRequest r) => Results.Text(await new StreamReader(r.Body).ReadToEndAsync(), "application/json"));

            app.Map("/ok", () => Results.Json(new { value = "hi" }));
            app.Map("/not-json", () => Results.Text("this is not json", "text/plain"));
            app.Map("/wrong-shape", () => Results.Json(new[] { 1, 2, 3 }));

            app.Map("/unauthorized", () => Results.Json(new { error = "nope" }, statusCode: 401));
            app.Map("/unauthorized-with-code", () => Results.Json(new { error = "unknown, revoked or expired session ticket", code = "invalid_ticket" }, statusCode: 401));
            app.Map("/unauthorized-empty", () => Results.StatusCode(401));
            app.Map("/forbidden", () => Results.Json(new { error = "no" }, statusCode: 403));
            app.Map("/missing", () => Results.Json(new { error = "no such player" }, statusCode: 404));
            app.Map("/bad-request", () => Results.Json(new { error = "bad" }, statusCode: 400));
            app.Map("/boom", () => Results.Json(new { error = "something went wrong on the server" }, statusCode: 500));
            app.Map("/unavailable", () => Results.StatusCode(503));
            app.Map("/bad-gateway", () => Results.StatusCode(502));

            // answers after a delay, and gives up if the caller does
            app.Map("/slow/{ms:int}", async (int ms, HttpContext c) =>
            {
                try
                {
                    await Task.Delay(ms, c.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                }
                return Results.Json(new { value = "late" });
            });

            await app.StartAsync();
            self = new FakeHttpServer(app, app.Urls.First().TrimEnd('/'));
            return self;
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}

using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.ResponseCompression;
using System.Net.WebSockets;
using System.Threading;
using System.Text;
using System.Collections.Concurrent;
using System.IO;

// NOTES: Deebug with the name of the program not IISExpress in the debug play button, this runs .NET ASP in a console so uses Krestrel not IIS
// Set launchsettings.json to the port you want to launch from, and if you want a browser automatically launched or not, and specify the port in .UseUrls("http://*:80)
//TODO: Should this be an extension???


// Check this for Core version of https://github.com/vtortola/WebSocketListener/pull/93
//TODO: Check when Core supports websockets compression
// NUGET: Microsoft.AspNetCore 1.1.0, ..Hosting, ..ResponseCaching, ..ResponseCompression, ..Server.Kestrel, ..StaticFiles, ..WebSockets.Server, Microsoft.AspNetCore.Server.Kestrel.Https

namespace HAServer
{
    public class WebServices
    {
        static ILogger Logger { get; } = ApplicationLogging.CreateLogger<WebServices>();

        public WebServices(string port, string filesLoc)
        {
            //TODO: Should this be a Task??
            Thread webServer = new Thread(() => WebServices.WebServer(port, filesLoc)) { IsBackground = true };       // Background threads will end if main thread ends  
            webServer.Start();
        }

        // THREAD: Run the web server
        public static void WebServer(string port, string webroot)
        {
            try
            {
                Logger.LogInformation("Starting WebServer on port " + port + "...");
                var wwwroot = Path.Combine(Directory.GetCurrentDirectory(), (string)webroot);                       // directory path is relative
                Directory.CreateDirectory(wwwroot);                              // Create wwwroot directory if not existing                                                            // In case this is a new install
                var host = new WebHostBuilder()
                    .UseUrls("http://*:" + port)
                    .UseKestrel(opts => opts.ThreadCount = 4)               // Is 4 optimal?
                                                                            //.UseUrls("https://*:443")                                     // sets port to use
                                                                            //.UseKestrel(options => {
                                                                            //    options.UseHttps(new X509Certificate2("filename...", "password"));
                                                                            //})
                                                                            //.UseIISIntegration()
                    .UseContentRoot(wwwroot)            // Set the default directory for serving files under webroot
                    .UseStartup<WebServerConfig>()
                    .Build();

                host.Run();
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("EACCES")) Logger.LogError("Access error on port - likely there is another webserver already using port " + port);
                Logger.LogCritical("WebServer was unable to start." + Environment.NewLine + ex.ToString());
                throw;
            }
        }

        // Any shutdown code
        public void Shutdown()
        {
        }

        public class WebServerConfig
        {
            // This method gets called by the runtime. Use this method to add services to the container.
            // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
            public const int RECV_BUFF_SIZE = 4096;
            public ConcurrentBag<WebSocket> clients = new ConcurrentBag<WebSocket>();

            public void ConfigureServices(IServiceCollection services)
            {
                services.AddResponseCaching(options =>

                {
                });
                ;              // setup Response caching
                               //services.AddMemoryCache();                  // Used to cache data for fast response. Perhaps use this for the statestore????
                services.AddResponseCompression();          // setup compression
                services.Configure<GzipCompressionProviderOptions>(options =>
                    options.Level = System.IO.Compression.CompressionLevel.Optimal);        // Optimal compresses as much as possible (good for internet transport). Other options are fastest (good for LAN) & no compression.
                services.AddResponseCompression(options =>
                {
                    options.EnableForHttps = true;                          // Enable HTTPS compresson & set it as an option
                    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
                    {
                    "image/svg+xml",                                    // Compress svg as well as default MIME types
                    "application/atom+xml"
                });
                    options.Providers.Add<GzipCompressionProvider>();
                });
            }


            // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
            public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory, IApplicationLifetime appLifetime)
            {
                // Use ASP.NET shutdown event to close all automation services
                // NOT WORKING AS APP WON"T PROPERLY EXIT 
                // appLifetime.ApplicationStopping.Register(() => Core.ShutConsole(Consts.ExitCodes.OK));

                //loggerFactory.AddConsole();

                app.UseResponseCompression();               // Invoke compression for requests (ensure this is before any middleware serving content
                app.UseResponseCaching();                   // Invoke response caching for all requests

                app.UseWebSockets(new WebSocketOptions()
                {
                    ReceiveBufferSize = RECV_BUFF_SIZE,
                    KeepAliveInterval = TimeSpan.FromMinutes(2)
                });

                app.Use(async (http, next) =>
                {
                    if (http.WebSockets.IsWebSocketRequest)
                    {
                        var webSocket = await http.WebSockets.AcceptWebSocketAsync();

                        while (webSocket != null && webSocket.State == System.Net.WebSockets.WebSocketState.Open)
                        {
                            clients.Add(webSocket);


                            var buffer = new ArraySegment<byte>(new byte[RECV_BUFF_SIZE]);
                            var charbuffer = new char[RECV_BUFF_SIZE];                            // TODO: Check buffer size for large sends
                            try
                            {
                                var sb = new StringBuilder();
                                var decoder = Encoding.UTF8.GetDecoder();

                                var received = await webSocket.ReceiveAsync(buffer, System.Threading.CancellationToken.None);

                                switch (received.MessageType)
                                {
                                    case WebSocketMessageType.Text:
                                        //var request = Encoding.UTF8.GetString(buffer.Array, buffer.Offset, buffer.Count);

                                        while (!received.EndOfMessage)
                                        {
                                            var charLen = decoder.GetChars(buffer.Array, 0, received.Count, charbuffer, 0);
                                            sb.Append(charbuffer, 0, charLen);
                                            received = await webSocket.ReceiveAsync(buffer, CancellationToken.None);
                                        }
                                        var charLenFinal = decoder.GetChars(buffer.Array, 0, received.Count, charbuffer, 0, true);
                                        sb.Append(charbuffer, 0, charLenFinal);

                                        var recvmsg = sb.ToString();
                                        WebSocket client;
                                        clients.TryPeek(out client);
                                        SendWSAsync(client, "Got it");
                                        break;
                                    default:
                                        break;
                                }
                            }
                            catch (Exception ex)
                            {
                            }
                        }
                    }
                    else
                    {
                        await next();                       // Pass to next middleware
                    }
                });

                // Setup default start page and serve static files
                DefaultFilesOptions DefaultFile = new DefaultFilesOptions();
                DefaultFile.DefaultFileNames.Clear();
                DefaultFile.DefaultFileNames.Add("myStartPage.html");
                app.UseDefaultFiles(DefaultFile);
                long oneYear = 86400000L * 365;

                if (env.IsDevelopment())
                {
                    //app.UseDeveloperExceptionPage();
                    //app.UseBrowserLink();
                    app.UseStaticFiles(new StaticFileOptions()
                    {
                        OnPrepareResponse = (context) =>
                        {
                            // Disable caching for all static files.
                            context.Context.Response.Headers["Cache-Control"] = "no-cache, no-store";
                            context.Context.Response.Headers["Pragma"] = "no-cache";
                            context.Context.Response.Headers["Expires"] = "-1";
                        }
                    });
                }
                else
                {
                    app.UseStaticFiles(new StaticFileOptions()
                    {
                        OnPrepareResponse = (context) =>
                        {
                            context.Context.Response.Headers["Cache-Control"] = "public, max-age=" + oneYear.ToString();    // Long cache duration for proxy servers and clients
                        }
                    });

                }

                // A Middleware component inserted into the run pipeline
                //app.Use(async (context, next) =>
                //{
                //    await context.Response.WriteAsync("PreProcessing");
                //    await next();                                               // process next middleware, in this case nothing left so RUN is used.
                //    await context.Response.WriteAsync("PostProcessing");
                //});
                // This is the default handler that is run for all requests and terminates all subsequent middleware (app.run terminates, app.use diasychains)
                //app.Run(async (context) =>
                //{
                //    await context.Response.WriteAsync("Hello World!");
                //});
            }

            // Send text out websocket
            private void SendWSAsync(WebSocket clientName, string toSend)
            {
                clientName.SendAsync(new ArraySegment<Byte>(Encoding.UTF8.GetBytes(toSend)), WebSocketMessageType.Text, true, CancellationToken.None);
            }

        }

    }
}

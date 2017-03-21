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
using System.Collections.Generic;

// NOTES: Deebug with the name of the program not IISExpress in the debug play button, this runs .NET ASP in a console so uses Krestrel not IIS
// Set launchsettings.json to the port you want to launch from, and if you want a browser automatically launched or not, and specify the port in .UseUrls("http://*:80)
//TODO: Should this be an extension???
//TODO: Low priority supress console output

//TODO: Check when Core supports websockets compression. Edge does not support but chrome does. Can use https://github.com/vtortola/WebSocketListener
// NUGET: Microsoft.AspNetCore 1.1.0, ..Hosting, ..ResponseCaching, ..ResponseCompression, ..Server.Kestrel, ..StaticFiles, ..WebSockets.Server, Microsoft.AspNetCore.Server.Kestrel.Https

namespace HAServer
{
    public class WebServices
    {
        static public ILogger Logger = ApplicationLogging.CreateLogger<WebServices>();

        public WebServices(string port, string filesLoc)
        {
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
                Directory.CreateDirectory(wwwroot);                              // Create wwwroot directory if not existing

                var host = new WebHostBuilder()
                    .UseUrls("http://*:" + port)
                    .UseKestrel(opts => opts.ThreadCount = 4)               // Is 4 optimal?
                                                                            //.UseUrls("https://*:443")                                     // sets port to use
                                                                            //.UseKestrel(options => {
                                                                            //    options.UseHttps(new X509Certificate2("filename...", "password"));
                                                                            //})
                                                                            //.UseIISIntegration()
                    .UseWebRoot(wwwroot)                                            // Sets the web server root
                                                                                    //.UseContentRoot(Directory.GetCurrentDirectory())            // Set the default directory for serving files under webroot
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
            public const int RECV_BUFF_SIZE = 64;     // <List> will increase dynamically when needed

            //public ConcurrentBag<WebSocket> clients = new ConcurrentBag<WebSocket>();
            public ConcurrentDictionary<string, MQTTServer> clients = new ConcurrentDictionary<string, MQTTServer>();

            public void ConfigureServices(IServiceCollection services)
            {
                services.AddResponseCaching();                          // setup Response caching
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

                app.UseResponseCaching();                   // Invoke response caching for all requests
                app.UseResponseCompression();               // Invoke compression for requests (ensure this is before any middleware serving content

                app.UseWebSockets(new WebSocketOptions()    // Uses same port as http
                {
                    ReceiveBufferSize = RECV_BUFF_SIZE,
                    KeepAliveInterval = TimeSpan.FromMinutes(2)
                });

                app.Use(async (http, next) =>
                {
                    if (http.WebSockets.IsWebSocketRequest)
                    {
                        var webSocket = await http.WebSockets.AcceptWebSocketAsync();
                        if (http.WebSockets.WebSocketRequestedProtocols[0].ToString().ToUpper() == "MQTT")
                        {
                            Logger.LogDebug("Received new MQTT WebSocket connection from " + http.Connection.RemoteIpAddress);
                            MQTTServer myClient = new MQTTServer(webSocket, http.Connection.RemoteIpAddress.ToString(), http.Connection.RemotePort.ToString());

                            //Console.WriteLine("-------------------------------------------------------> WS thread: " + System.Threading.Thread.CurrentThread.ManagedThreadId);

                            // Get MQTT frame
                            while (webSocket != null && webSocket.State == System.Net.WebSockets.WebSocketState.Open)
                            {

                                //TODO: Tune the buffer size for typical HAServer messages received (smaller than 4096 but is 4096 the normal frame size on the network?)
                                var buffer = new ArraySegment<Byte>(new Byte[RECV_BUFF_SIZE]);
                                try
                                {
                                    var received = await webSocket.ReceiveAsync(buffer, System.Threading.CancellationToken.None);

                                    switch (received.MessageType)
                                    {
                                        case WebSocketMessageType.Text:     // Don't accept text websockets
                                            await webSocket.CloseOutputAsync(WebSocketCloseStatus.InvalidPayloadData, "Not accepting text requests", CancellationToken.None);
                                            webSocket.Dispose();
                                            break;

                                        case WebSocketMessageType.Binary:                   // MQTT messages
                                            var bufList = new List<Byte>(buffer);
                                            if (!received.EndOfMessage)                     // Optimise performance for smaller messages
                                            {
                                                var recvCnt = received.Count;
                                                bufList.AddRange(buffer);
                                                while (!received.EndOfMessage)              // Get further messages
                                                {
                                                    received = await webSocket.ReceiveAsync(buffer, CancellationToken.None);
                                                    bufList.AddRange(buffer);
                                                    recvCnt = recvCnt + received.Count;
                                                }
                                            }

                                            if (Core._debug)
                                            {
                                                var sb1 = new StringBuilder();
                                                var decoder1 = Encoding.UTF8.GetDecoder();
                                                var charbuffer1 = new Char[1024];                            // TODO: Check buffer size for large sends
                                                var charLen = decoder1.GetChars(bufList.ToArray(), 0, received.Count, charbuffer1, 0);

                                                sb1.Append("[");
                                                for (var i = 0; i < received.Count; i++)
                                                {
                                                    sb1.Append(bufList.ElementAt(i).ToString() + ",");
                                                }
                                                sb1.Append("] -");
                                                sb1.Append(charbuffer1, 0, received.Count);
                                                sb1.Append("-");
                                                Logger.LogDebug("WS recv [" + myClient.IPAddr + "] " + sb1.ToString());
                                            }

                                            if (myClient.HandleFrame(bufList, received.Count) && myClient.connected && myClient.numFrames == 1)   // process frame
                                            {
                                                var addCnt = 0;                // Add to clients list if new session, allow for multiple sessions from same IP.
                                                do
                                                {
                                                    myClient.name = myClient.IPAddr + "_" + addCnt;
                                                    addCnt++;
                                                } while (!clients.TryAdd(myClient.name, myClient));
                                            };
                                            break;

                                        case WebSocketMessageType.Close:
                                            myClient.CloseMQTTClient("Client closed WebSocket, reason: " + received.CloseStatus.ToString() + " " + received.CloseStatusDescription.ToString());
                                            break;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogError("WebSockets frame error occured: " + ex.ToString());
                                    await webSocket.CloseOutputAsync(WebSocketCloseStatus.InvalidPayloadData, "MQTT Exception raised: " + ex.ToString(), CancellationToken.None);
                                    webSocket.Dispose();
                                }
                            }
                        }
                        else
                        {
                            Logger.LogWarning("Invalid subprotocol '" + http.WebSockets.WebSocketRequestedProtocols[0].ToString() + "' requested from client [" + http.Connection.RemoteIpAddress.ToString() + "]. Ignored.");
                        }
                    }
                    else
                    {
                        await next();                       // Pass to next middleware
                    }
                });

                const string oneYear = "public, max-age=" + "31536000000";                        //86400000L * 365L, use as const for performance

                //if (env.IsDevelopment())
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    loggerFactory.AddConsole();
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
                            context.Context.Response.Headers["Cache-Control"] = oneYear;    // Long cache duration for proxy servers and clients
                        }
                    });

                }
            }

            // Handle incoming websockets String messages - invalid, drop session.
            private void HandleWSStrMsg(string clientIP, string msg)
            {

            }


            // Send text out websocket
            private void SendWSAsync(WebSocket client, string toSend)
            {
                client.SendAsync(new ArraySegment<Byte>(Encoding.UTF8.GetBytes(toSend)), WebSocketMessageType.Text, true, CancellationToken.None);
            }

        }

    }
}

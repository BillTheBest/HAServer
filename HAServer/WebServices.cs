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
using System.Collections;
using Interfaces;
using System.Threading.Tasks;

// NOTES: Deebug with the name of the program not IISExpress in the debug play button, this runs .NET ASP in a console so uses Krestrel not IIS
// Set launchsettings.json to the port you want to launch from, and if you want a browser automatically launched or not, and specify the port in .UseUrls("http://*:80)
//TODO: Should this be an extension???
//TODO: Low priority supress console output

//TODO: Check when Core supports websockets compression. Edge does not support but chrome does. Can use https://github.com/vtortola/WebSocketListener
// NUGET: Microsoft.AspNetCore 1.1.0, ..Hosting, ..ResponseCaching, ..ResponseCompression, ..Server.Kestrel, ..StaticFiles, ..WebSockets.Server, Microsoft.AspNetCore.Server.Kestrel.Https

// TODO: TCP MQTT sockets example: http://stackoverflow.com/questions/12630827/using-net-4-5-async-feature-for-socket-programming 

namespace HAServer
{
    public class WebServices
    {
        public static ConcurrentDictionary<string, MQTTServer> clients = new ConcurrentDictionary<string, MQTTServer>();

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

        public void RouteMessage(string client, Commons.HAMessage myMessage)
        {
            if (WebServices.clients.ContainsKey(client))
            {
                if (WebServices.clients[client].websocket != null || WebServices.clients[client].socket != null)                // Websockets or sockets
                {
                    WebServices.clients[client].MQTTPublish(myMessage.category + "\\" + myMessage.className + "\\" + myMessage.instance + "\\" + myMessage.scope, myMessage.data, 0);
                }
                else
                {
                    Logger.LogError("Trying to send message to client " + client + " but no active websocket or socket open with client");
                }
            }
            else
            {
                Logger.LogInformation("Trying to send message to client " + client + " but no client network session active, unsubscribing.");
                Core.pubSub.UnSubscribe(client, new ChannelKey
                {
                    network = myMessage.network,
                    category = myMessage.category,
                    className = myMessage.className,
                    instance = myMessage.instance,
                });
            }
        }

        public class WebServerConfig
        {
            // This method gets called by the runtime. Use this method to add services to the container.
            // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940

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
                    //ReceiveBufferSize = RECV_BUFF_SIZE,
                    KeepAliveInterval = TimeSpan.FromMinutes(2)
                });
                app.UseMiddleware<WSMiddleware>();

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
        }
    }

    // Handle websockets sessions as middleware
    public class WSMiddleware
    {
        static public ILogger Logger = ApplicationLogging.CreateLogger<WSMiddleware>();

        public const int RECV_BUFF_SIZE = 64;     // <List> will increase dynamically when needed

        private readonly RequestDelegate _next;

        public WSMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                CancellationToken ct = context.RequestAborted;
                WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();

                MQTTServer MQTTClient = new MQTTServer(webSocket, context.Connection.RemoteIpAddress.ToString(), context.Connection.RemotePort.ToString());
                MQTTClient.TimeoutEvent += CloseWSSess;

                if (context.WebSockets.WebSocketRequestedProtocols[0].ToString().ToUpper() == "MQTT")
                {
                    Logger.LogDebug("Received new MQTT WebSocket connection from " + context.Connection.RemoteIpAddress);

                    // receive loop
                    while (!ct.IsCancellationRequested && webSocket.State == WebSocketState.Open)
                    {
                        var response = await ReceiveBin(MQTTClient, ct);
                    }
                }
                else
                {
                    CloseWSSess(MQTTClient, "Invalid subprotocol '" + context.WebSockets.WebSocketRequestedProtocols[0].ToString() + "' requested from client [" + context.Connection.RemoteIpAddress.ToString() + "].");
                }
            }
            else
            {
                await _next.Invoke(context);                // Not a web socket request
            }
        }

        private static async Task<bool> ReceiveBin(MQTTServer myClient, CancellationToken ct = default(CancellationToken))
        {
            var buffer = new ArraySegment<byte>(new byte[RECV_BUFF_SIZE]);

            using (var ms = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    ct.ThrowIfCancellationRequested();

                    result = await myClient.websocket.ReceiveAsync(buffer, ct);
                    ms.Write(buffer.Array, buffer.Offset, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.Count > 0) myClient.StartFrameTimeout();

                ms.Seek(0, SeekOrigin.Begin);

                if (Core._debug)
                {
                    var sb1 = new StringBuilder();
                    var decoder1 = Encoding.UTF8.GetDecoder();
                    var charbuffer1 = new Char[1024];                            // TODO: Check buffer size for large sends
                    var charLen = decoder1.GetChars(ms.ToArray(), 0, (int)ms.Length, charbuffer1, 0);

                    sb1.Append("[");
                    for (var i = 0; i < (int)ms.Length; i++)
                    {
                        sb1.Append(ms.ToArray()[i].ToString() + ",");
                    }
                    sb1.Append("] -");
                    sb1.Append(charbuffer1, 0, (int)ms.Length);
                    sb1.Append("-");
                    Logger.LogDebug("WS recv [" + myClient.IPAddr + "] " + sb1.ToString());
                }


                switch (result.MessageType)
                {
                    case WebSocketMessageType.Text:     // Don't accept text websockets
                        CloseWSSess(myClient, "Not accepting text requests");
                        return false;

                    case WebSocketMessageType.Binary:                   // MQTT messages
                                                                        //var st = new System.Diagnostics.Stopwatch();
                                                                        //st.Start();
                                                                        //var tt = new List<List<Byte>>();
                                                                        //for (var t = 0; t < 10000; t++)
                                                                        // {
                                                                        //     tt.Add(ms.ToArray().ToList<Byte>());
                                                                        // }
                                                                        // Logger.LogCritical(st.ElapsedMilliseconds.ToString());

                        if (myClient.HandleFrame(ms.ToArray().ToList<Byte>(), (int)ms.Length))   // process frame
                        {
                            if (myClient.connected && myClient.name == null)
                            {
                                var addCnt = 0;                // Add to clients list if new session, allow for multiple sessions from same IP.
                                do
                                {
                                    myClient.name = myClient.IPAddr.Replace('.', '-') + "_" + addCnt;
                                    addCnt++;
                                } while (!WebServices.clients.TryAdd(myClient.name, myClient));
                            }
                            return true;
                        }
                        else           // Error in processing frame, drop session
                        {
                            CloseWSSess(myClient, "Error processing MQTT frame, session closed");
                            return false;
                        }

                    case WebSocketMessageType.Close:
                        CloseWSSess(myClient, "Client closed WebSocket, reason: " + result.CloseStatus.ToString() + " " + result.CloseStatusDescription.ToString());
                        return false;
                }
                return false;
            }
        }

        // Shut down session and remove MQTT object from clients list
        private async static void CloseWSSess(MQTTServer MQTTSess, string reason)
        {
            MQTTSess.CloseMQTTClient("WebSocket closed, reason: " + reason);                // Remove subscriptions
            if (MQTTSess.websocket.State == WebSocketState.Open)
            {
                await MQTTSess.websocket.CloseOutputAsync(WebSocketCloseStatus.InvalidPayloadData, "Closing WebSocket, reason: " + reason, CancellationToken.None);
            }
            MQTTSess.websocket.Dispose();
            WebServices.clients.TryRemove(MQTTSess.name, out MQTTServer notUsed);
            MQTTSess = null;
        }
    }
}

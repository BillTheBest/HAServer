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
        static ILogger Logger = ApplicationLogging.CreateLogger<WebServices>();

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
            public const int RECV_BUFF_SIZE = 1024;     // ??? Is this enough??
            public struct ConnFlags
            {
                public bool clean;
                public bool will;
                public int willQoS;
                public bool willRetain;
                public bool passFlg;
                public bool userFlg;
                public int keepAlive;
            }

            public struct MQTTClient
            {
                public bool gotLen;
                public bool connected;
                public uint control;
                public string IPAddr;
                public string port;
                public int bytesToGet;
                public int lenMult;
                public int bufIndex;
                public string clientID;
                public string willTopic;
                public string willMessage;
                public string MQTTver;
                public int procRemLen;
                public ConnFlags connFlags;
                public List<Byte> buffer;
                public WebSocket websocket;
            }
            //public ConcurrentBag<WebSocket> clients = new ConcurrentBag<WebSocket>();
            public ConcurrentDictionary<string, MQTTClient> clients = new ConcurrentDictionary<string, MQTTClient>();

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
                        var addOK = clients.TryAdd(http.Connection.RemoteIpAddress.ToString(), new MQTTClient
                        {
                            gotLen = false,
                            connected = false,
                            control = 0,
                            bytesToGet = 0,
                            lenMult = 1,
                            bufIndex = 0,
                            clientID = null,
                            willTopic = null,
                            willMessage = null,
                            IPAddr = http.Connection.RemoteIpAddress.ToString(),
                            port = http.Connection.RemotePort.ToString(),
                            connFlags = new ConnFlags(),
                            websocket = webSocket,
                            buffer = new List<Byte>()
                        });
                        if (!addOK)
                        {
                            //DO something if duplicate key
                        }

                        while (webSocket != null && webSocket.State == System.Net.WebSockets.WebSocketState.Open)
                        {

                            //TODO: Tune the buffer size for typical HAServer messages received (smaller than 4096 but is 4096 the normal frame size on the network?)
                            var buffer = new ArraySegment<Byte>(new Byte[RECV_BUFF_SIZE]);
                            try
                            {
                                var received = await webSocket.ReceiveAsync(buffer, System.Threading.CancellationToken.None);

                                switch (received.MessageType)
                                {
                                    case WebSocketMessageType.Text:
                                        //var request = Encoding.UTF8.GetString(buffer.Array, buffer.Offset, buffer.Count);
                                        var sb = new StringBuilder();
                                        var decoder = Encoding.UTF8.GetDecoder();
                                        var charbuffer = new Char[RECV_BUFF_SIZE];                            // TODO: Check buffer size for large sends

                                        while (!received.EndOfMessage)
                                        {
                                            var charLen = decoder.GetChars(buffer.Array, 0, received.Count, charbuffer, 0);
                                            sb.Append(charbuffer, 0, charLen);
                                            received = await webSocket.ReceiveAsync(buffer, CancellationToken.None);
                                        }
                                        var charLenFinal = decoder.GetChars(buffer.Array, 0, received.Count, charbuffer, 0, true);
                                        sb.Append(charbuffer, 0, charLenFinal);

                                        HandleWSStrMsg(http.Connection.RemoteIpAddress.ToString(), sb.ToString());
                                        break;

                                    case WebSocketMessageType.Binary:
                                        if (!received.EndOfMessage)                     // Optimise performance for smaller messages
                                        {
                                            var bufList = new List<Byte>(RECV_BUFF_SIZE * 2);
                                            var recvCnt = received.Count;
                                            bufList.AddRange(buffer);
                                            while (!received.EndOfMessage)
                                            {
                                                received = await webSocket.ReceiveAsync(buffer, CancellationToken.None);
                                                bufList.AddRange(buffer);
                                                recvCnt = recvCnt + received.Count;
                                            }
                                            HandleWSBinMsg(http.Connection.RemoteIpAddress.ToString(), new ArraySegment<Byte>(bufList.ToArray<Byte>()), recvCnt);
                                        } else
                                        {
                                            HandleWSBinMsg(http.Connection.RemoteIpAddress.ToString(), buffer, received.Count);
                                        }

                                        break;

                                    case WebSocketMessageType.Close:
                                        //SessClose(received.CloseStatus, received.CloseStatusDescription);
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
                //DefaultFilesOptions DefaultFile = new DefaultFilesOptions();
                //DefaultFile.DefaultFileNames.Clear();
                //Console.WriteLine(Core.webServices._clientFile);
                //DefaultFile.DefaultFileNames.Add(Core.webServices._clientFile);
                //app.UseDefaultFiles(DefaultFile);

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
                } else
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

            // Handle incoming websockets String messages
            private void HandleWSStrMsg(string clientIP, string msg)
            {
            }

            // Handle incoming websockets Binary messages as MQTT
            private void HandleWSBinMsg(string clientIP, ArraySegment<Byte> buffer, int size) 
            {
                var tt = buffer;
                var sb = new StringBuilder();
                var decoder = Encoding.UTF8.GetDecoder();
                var charbuffer = new Char[RECV_BUFF_SIZE];                            // TODO: Check buffer size for large sends

                var charLen = decoder.GetChars(buffer.Array, 0, size, charbuffer, 0);
                sb.Append("[");
                for (var i = 0; i < size; i++)
                {
                    sb.Append(buffer.ElementAt(i).ToString() + ",");
                }
                sb.Append("] -");
                sb.Append(charbuffer, 0, size);
                sb.Append("-");
                Console.WriteLine("WS recv Bin: "+ sb.ToString());

                // process protocol fixed header to find length of frame, handle MQTT frame being split across multiple messages
                var myClient = clients[clientIP];
                //TODO: Timer if completed frame isn't received on time
                for (var i = 0; i < size; i++)
                {
                    if (!myClient.gotLen)                       // Process fixed header to get control byte and remaining bytes
                    {
                        if (myClient.bufIndex == 0)
                        {
                            myClient.control = buffer.ElementAt(0);
                        }
                        else
                        {
                            if (myClient.bufIndex < 5)
                            {
                                myClient.procRemLen += (buffer.ElementAt(i) & 127) * myClient.lenMult;
                                myClient.lenMult *= 128;
                                if (buffer.ElementAt(i) < 128)          // No more bytes to encode length if value < 128
                                {
                                    myClient.bytesToGet = myClient.procRemLen;
                                    myClient.gotLen = true;
                                    myClient.bufIndex = 0;
                                }
                            }
                            else
                            {
                                // TODO: invalid
                                break;
                            }
                        }
                        myClient.bufIndex++;
                    }
                    else                                // process the rest of the message after the fixed header
                    {
                        myClient.buffer.AddRange(buffer.Skip(i).Take(size - i));            // put the remaining buffer into the client object for further processing
                        myClient.bytesToGet = myClient.bytesToGet - (size - i);
                        if (myClient.bytesToGet < 0)
                        {
                            //TODO: Overrun, too many bytes received
                        }

                        //TODO: Chech for buffer overruns when extracting string lengths
                        if (myClient.bytesToGet == 0)           // received all the frame
                        {
                            switch (myClient.control >> 4)
                            {
                                case MQTT.MQTT_MSG_CONNECT_TYPE:
                                    if ((myClient.control & 127) != 0)
                                    {
                                        //TODO: Low nibble of control isn't 0 for Connect packet
                                    }

                                    int bufIndex = 0;
                                    myClient.MQTTver = GetUTF8(ref myClient.buffer, ref bufIndex);
                                    switch (myClient.MQTTver)
                                    {
                                        case MQTT.PROTOCOL_NAME_V31:
                                            if (myClient.buffer.ElementAt(bufIndex) != MQTT.PROTOCOL_NAME_V31_LEVEL_VAL)
                                            {
                                                //TODO: Invalid protocol level for 3.1.1
                                            }
                                            break;
                                        case MQTT.PROTOCOL_NAME_V311:
                                            if (myClient.buffer.ElementAt(bufIndex) != MQTT.PROTOCOL_NAME_V311_LEVEL_VAL)
                                            {
                                                //TODO: Invalid protocol level for 3.1
                                            }

                                            break;
                                        default:
                                            //TODO: protocol name not supported
                                            break;
                                    }
                                    if ((myClient.buffer.ElementAt(++bufIndex) & 0b00000001) == 0b00000001)
                                    {
                                        //TODO: First flag bit can't be 1
                                    }
                                    myClient.connFlags.clean = (myClient.buffer.ElementAt(bufIndex) & 0b00000010) == 0b00000010;
                                    myClient.connFlags.will = (myClient.buffer.ElementAt(bufIndex) & 0b00000100) == 0b00000100;
                                    myClient.connFlags.willQoS = (myClient.buffer.ElementAt(bufIndex) & 0b00011000) >> 4;
                                    myClient.connFlags.willRetain = (myClient.buffer.ElementAt(bufIndex) & 0b00100000) == 0b00100000;
                                    myClient.connFlags.passFlg = (myClient.buffer.ElementAt(bufIndex) & 0b01000000) == 0b01000000;
                                    myClient.connFlags.userFlg = (myClient.buffer.ElementAt(bufIndex) & 0b10000000) == 0b10000000;

                                    myClient.connFlags.keepAlive = myClient.buffer.ElementAt(++bufIndex) * 256 + myClient.buffer.ElementAt(++bufIndex);
                                    if (myClient.connFlags.keepAlive != 0)
                                    {
                                        //TODO: Set timer so that if client does not send anything in 1.5x keepalive seconds, disconnect the client & reset session
                                    }
                                    bufIndex++;
                                    myClient.clientID = GetUTF8(ref myClient.buffer, ref bufIndex);
                                    if (myClient.clientID == "")
                                    {
                                        if (!myClient.connFlags.clean)
                                        {
                                            //TODO: blank clientIDs must have clean flag set, so invalidate with CONNACK return code 0x02 (Identifier rejected) and then close the Network Connection 
                                        }
                                        myClient.clientID = Path.GetRandomFileName().Replace(".", "");          // Generate random name
                                    }
                                    //TODO: Check that clientID is unique else respond to the CONNECT Packet with a CONNACK return code 0x02 (Identifier rejected) and then close the Network Connection 

                                    if (myClient.connFlags.will)
                                    {
                                        bufIndex++;
                                        myClient.willTopic = GetUTF8(ref myClient.buffer, ref bufIndex);
                                        bufIndex++;
                                        myClient.willMessage = GetUTF8(ref myClient.buffer, ref bufIndex);
                                    }

                                    if (myClient.connFlags.userFlg)
                                    {
                                        bufIndex++;
                                        myClient.willTopic = GetUTF8(ref myClient.buffer, ref bufIndex);
                                    }

                                    if (myClient.connFlags.passFlg)
                                    {
                                        bufIndex++;
                                        myClient.willTopic = GetUTF8(ref myClient.buffer, ref bufIndex);
                                    }

                                    myClient.connected = true;
                                    //send back connack
                                    break;
                                case MQTT.MQTT_MSG_PUBLISH_TYPE:
                                    break;
                                default:
                                    // TODO: invalid control byte
                                    break;
                            }
                        }
                        break;                  // exit loop
                    }
                }
                clients[clientIP] = myClient;
                //TODO: rest keepalive timer for this client
                //TODO: Reset the variables with lenMult = 1 for a new packet
            }

            // Get UTF-8 char string of length determined by 1st and 2nd bytes in buffer segment
            private string GetUTF8(ref List<Byte> buffer, ref int bufIndex)
            {
                var len = buffer.ElementAt(bufIndex) * 256 + buffer.ElementAt(++bufIndex);
                var UTF = new String(Encoding.UTF8.GetChars(new ArraySegment<Byte>(buffer.ToArray<Byte>(), ++bufIndex, len).ToArray<Byte>()));
                bufIndex += len;
                return UTF;
            }

            // Send text out websocket
            private void SendWSAsync(WebSocket client, string toSend)
            {
                client.SendAsync(new ArraySegment<Byte>(Encoding.UTF8.GetBytes(toSend)), WebSocketMessageType.Text, true, CancellationToken.None);
            }

        }

    }
}

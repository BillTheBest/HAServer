using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;
using System.Runtime.CompilerServices;


//TODO: Chnage timeout constants to production values
namespace HAServer
{
    // MQTT frame and protocol handler for websockets and TCP sockets
    public class MQTTServer
    {
        public bool connected;
        public ProtFlags flags;
        public uint control;
        public string IPAddr;
        public string port;
        public string name;
        public string routeName;
        public string clientID;
        public string willTopic;
        public string willMessage;
        public string MQTTver;
        public int numFrames;
        public ushort waitOnPubRel;
        public ConnFlags connFlags;
        public List<Byte> buffer;
        public WebSocket websocket;
        public Socket socket;
        public List<Topic> topics;
        public TimerCallback pingCB;
        public Timer MQTTPingTimeout;
        public TimerCallback frameCB;
        public Timer frameTimeout;

        static public ILogger Logger = ApplicationLogging.CreateLogger<MQTTServer>();

        public delegate void TimeoutEventDel(MQTTServer MQTTSess, String reason);
        public event TimeoutEventDel TimeoutEvent;

        // Constructor for MQTT over TCP
        public MQTTServer(Socket TCPSocket, string clientIP, string clientPort, [CallerFilePath] string route = "")
        {
            socket = TCPSocket;
            websocket = null;
            Init(clientIP, clientPort, route);
        }

        // Constructor for MQTT over WebSockets
        public MQTTServer(WebSocket clientWebsocket, string clientIP, string clientPort, [CallerFilePath] string route = "")
        {
            websocket = clientWebsocket;
            socket = null;
            Init(clientIP, clientPort, route);
        }

        private void Init(string clientIP, string clientPort, string route)
        {
            routeName = Path.GetFileNameWithoutExtension(route);

            flags = new ProtFlags
            {
                gotLen = false,
                bytesToGet = 0,
                lenMult = 1,
                bufIndex = 0
            };

            control = 0;
            connected = false;
            clientID = null;
            willTopic = null;
            willMessage = null;
            IPAddr = clientIP;
            name = null;
            port = clientPort;
            numFrames = 0;
            waitOnPubRel = 0;
            connFlags = new ConnFlags();
            topics = new List<Topic>();
            buffer = new List<Byte>();
            pingCB = ClientTimeout;
            MQTTPingTimeout = new Timer(pingCB, null, Timeout.Infinite, Timeout.Infinite);
            frameCB = FrameTimeout;
            frameTimeout = new Timer(frameCB, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void StartFrameTimeout()
        {
            frameTimeout.Change(1000 * FRAME_TIMEOUT, 1000 * FRAME_TIMEOUT);
        }

        // Manage the MQTT frame here assuming multiple websocket/socket frames are sent to make up a MQTT frame, and to extract the frame size to know when a frame is fully received
        public bool HandleFrame(List<Byte> frameBuf, int receivedCnt)
        {
            //TODO: Chech for buffer overruns when extracting string lengths

            if (!connected && control == 0 && frameBuf.ElementAt(0) != (MQTT_MSG_CONNECT_TYPE << 4))  // New session, must be MQTT connect request (1st buffer byte = 16) else the request isn't MQTT
            {
                Logger.LogError("Incorrect MQTT control request received, aborting session");
                return false;
            }
            else
            {
                for (var i = 0; i < receivedCnt; i++)
                {
                    if (!flags.gotLen)                       // Process fixed header to get MQTT control byte and remaining bytes
                    {
                        if (flags.bufIndex == 0)
                        {
                            control = frameBuf.ElementAt(0);
                            if ((control >> 4) == MQTT_MSG_PINGREQ_TYPE || (control >> 4) == MQTT_MSG_DISCONNECT_TYPE)  // handle messages with no variable header / payload bytes to receive
                            {
                                flags.gotLen = true;
                                flags.bytesToGet = 1;
                            }
                        }
                        else
                        {
                            if (flags.bufIndex < 5)
                            {
                                flags.procRemLen += (int)((frameBuf.ElementAt(i) & 127) * flags.lenMult);
                                flags.lenMult *= 128;
                                if (frameBuf.ElementAt(i) < 128)          // No more bytes to encode length if value < 128
                                {
                                    flags.bytesToGet = flags.procRemLen;
                                    flags.gotLen = true;
                                    flags.bufIndex = 0;
                                }
                            }
                            else
                            {
                                Logger.LogError("MQTT Request to receive too many bytes");
                                return false;
                            }
                        }
                        flags.bufIndex++;
                    }
                    else                                // process the rest of the message after the fixed header
                    {
                        //TODO: Is it needed to save the buffer into the clioent object????
                        buffer.AddRange(frameBuf.Skip(i).Take(receivedCnt - i));            // put the remaining buffer into the client object for further processing
                        flags.bytesToGet = flags.bytesToGet - (receivedCnt - i);

                        if (flags.bytesToGet < 0)
                        {
                            Logger.LogError("Incorrect MQTT frame number of bytes received");
                            return false;
                        }

                        if (flags.bytesToGet == 0)           // received all the frame
                        {
                            HandleWSBinMsg();
                        }
                        break;                  // exit loop
                    }
                }
            }
            return true;
        }

        // Handle incoming websockets Binary messages as MQTT
        public bool HandleWSBinMsg()
        {
            //TODO: Chech for buffer overruns when extracting string lengths

            int bufIndex = 0;

            frameTimeout.Change(Timeout.Infinite, Timeout.Infinite);         // stop frame timer as we have received completed frame

            switch (control >> 4)
            {
                case MQTT_MSG_CONNECT_TYPE:
                    if ((control & MSG_FLAG_BITS_MASK) != 0b0000)
                    {
                        Logger.LogError("Incorrect MQTT control flags");                          // Low nibble of control isn't 0 for Connect packet
                        return false;
                    }

                    MQTTver = GetUTF8(ref buffer, ref bufIndex);
                    switch (MQTTver)
                    {
                        case PROTOCOL_NAME_V31:
                            if (buffer.ElementAt(bufIndex) != PROTOCOL_NAME_V31_LEVEL_VAL)
                            {
                                Logger.LogError("Invalid MQTT protocol level for 3.1");                  // Invalid protocol level for 3.1
                                return false;
                            }
                            break;

                        case PROTOCOL_NAME_V311:
                            if (buffer.ElementAt(bufIndex) != PROTOCOL_NAME_V311_LEVEL_VAL)
                            {
                                Logger.LogError("Invalid MQTT protocol level for 3.11");                  // Invalid protocol level for 3.1.1
                                return false;
                            }
                            break;

                        default:
                            Logger.LogError("MQTT protocol number not supported: " + MQTTver);
                            return false;
                    }

                    if ((buffer.ElementAt(++bufIndex) & 0b00000001) == 0b00000001)
                    {
                        Logger.LogError("MQTT connect flag can't be 1");
                        return false;
                    }

                    connFlags.clean = (buffer.ElementAt(bufIndex) & 0b00000010) == 0b00000010;
                    connFlags.will = (buffer.ElementAt(bufIndex) & 0b00000100) == 0b00000100;
                    connFlags.willQoS = (uint)(buffer.ElementAt(bufIndex) & 0b00011000) >> 4;
                    connFlags.willRetain = (buffer.ElementAt(bufIndex) & 0b00100000) == 0b00100000;
                    connFlags.passFlg = (buffer.ElementAt(bufIndex) & 0b01000000) == 0b01000000;
                    connFlags.userFlg = (buffer.ElementAt(bufIndex) & 0b10000000) == 0b10000000;
                    connFlags.keepAlive = buffer.ElementAt(++bufIndex) * 256 + buffer.ElementAt(++bufIndex);

                    if (connFlags.keepAlive == 0)
                    {
                        connFlags.keepAlive = Timeout.Infinite;
                    }

                    bufIndex++;
                    clientID = GetUTF8(ref buffer, ref bufIndex);

                    if (clientID == "")
                    {
                        if (!connFlags.clean)
                        {
                            //TODO: blank clientIDs must have clean flag set, so invalidate with CONNACK return code 0x02 (Identifier rejected) and then close the Network Connection 
                        }
                        clientID = Path.GetRandomFileName().Replace(".", "");          // Generate random name
                    }
                    //TODO: Check that clientID is unique else disconnect existing client & respond with a CONNACK return code 0x02 (Identifier rejected) and then close the Network Connection 

                    if (connFlags.will)
                    {
                        bufIndex++;
                        willTopic = GetUTF8(ref buffer, ref bufIndex);
                        bufIndex++;
                        willMessage = GetUTF8(ref buffer, ref bufIndex);
                    }

                    if (connFlags.userFlg)
                    {
                        bufIndex++;
                        willTopic = GetUTF8(ref buffer, ref bufIndex);
                    }

                    if (connFlags.passFlg)
                    {
                        bufIndex++;
                        willTopic = GetUTF8(ref buffer, ref bufIndex);
                    }

                    //TODO: username / pass check

                    // ConnAck
                    byte connAckFlg = connFlags.clean ? (Byte)0b00000000 : (Byte)0b00000001;
                    byte connAckRet = connFlags.clean ? (Byte)0b00000000 : (Byte)0b00000001;

                    //If the Server accepts a connection with CleanSession set to 0, the value set in Session Present depends on whether the Server already has stored Session state for the supplied client ID.If the Server has stored Session state, it MUST set SessionPresent to 1 in the CONNACK packet[MQTT - 3.2.2 - 2].If the Server does not have stored Session state, it MUST set Session Present to 0 in the CONNACK packet.This is in addition to setting a zero return code in the CONNACK packe
                    MQTTSend(new List<byte> { MQTT_MSG_CONNACK_TYPE << 4, MSG_ACK_LEN, connAckFlg, connAckRet });

                    Logger.LogInformation("Client [" + IPAddr + "] MQTT session established");
                    connected = true;
                    break;

                case MQTT_MSG_PUBLISH_TYPE:
                    //TODO: retain logic for storing messages. 

                    var pubFlags = new PubFlags()
                    {
                        dup = ((control & 0b00001000) == 0b00001000),
                        QoS = (control & 0b00000110) >> 1,
                        retain = ((control & 0b00000001) == 0b00000001)
                    };

                    if (pubFlags.QoS > QOS_LEVEL_EXACTLY_ONCE)
                    {
                        Logger.LogError("Wrong QoS in MQTT request specified: " + pubFlags.QoS);
                        return false;
                    }

                    var pubTopic = GetUTF8(ref buffer, ref bufIndex);

                    Byte pubIDMSB = 0;
                    Byte pubIDLSB = 0;

                    if (pubFlags.QoS > QOS_LEVEL_AT_MOST_ONCE)
                    {
                        pubIDMSB = buffer.ElementAt(bufIndex);
                        pubIDLSB = buffer.ElementAt(++bufIndex);
                        bufIndex++;
                    }

                    var pubMsg = buffer.Skip(bufIndex).ToList<Byte>();                   // remaining bytes is the publish message (binary form not text)

                    var pubDataHA = new ASCIIEncoding().GetString(pubMsg.ToArray());
                    List<String> pubTopicHA = new List<String>(pubTopic.Split('\\'));

                    if (pubTopicHA.Count == 4)                                          // Only publish valid HA format
                    {
                        if (Core._debug) Logger.LogDebug("Client [" + IPAddr + "] published topic: " + pubTopic + " Data: " + pubDataHA);

                        Core.pubSub.Publish(new Interfaces.ChannelKey                   // put on the message queue
                        {
                            network = Commons.Globals.networkName,
                            category = pubTopicHA[0],
                            className = pubTopicHA[1],
                            instance = pubTopicHA[2],
                        }, pubTopicHA[3], pubDataHA);

                        if (pubFlags.QoS == QOS_LEVEL_AT_LEAST_ONCE)
                        {
                            MQTTSend(new List<byte> { MQTT_MSG_PUBACK_TYPE << 4, MSG_ACK_LEN, pubIDMSB, pubIDLSB });
                        }

                        if (pubFlags.QoS == QOS_LEVEL_EXACTLY_ONCE)
                        {
                            MQTTSend(new List<byte> { MQTT_MSG_PUBREC_TYPE << 4, MSG_ACK_LEN, pubIDMSB, pubIDLSB });
                            waitOnPubRel = (ushort)(pubIDMSB * 256 + pubIDLSB);
                            //TODO: Set a state machine so that we are waiting for a pubrel from the client
                        }
                    }
                    else
                    {
                        Logger.LogWarning("Client [" + IPAddr + "] specified incorrect topic: " + pubTopic + ", not published");
                    }
                    break;

                case MQTT_MSG_PUBREL_TYPE:
                    Byte pubRelMSB = (Byte)(waitOnPubRel >> 8);
                    Byte pubRelLSB = (Byte)(waitOnPubRel & 0b0000000011111111);
                    MQTTSend(new List<byte> { MQTT_MSG_PUBCOMP_TYPE << 4, MSG_ACK_LEN, pubRelMSB, pubRelLSB });
                    waitOnPubRel = 0;
                    break;

                case MQTT_MSG_PINGREQ_TYPE:
                    if ((control & MSG_FLAG_BITS_MASK) == 0b0000)
                    {
                        MQTTSend(new List<Byte> { MQTT_MSG_PINGRESP_TYPE << 4, 0 });
                    }
                    break;

                case MQTT_MSG_SUBSCRIBE_TYPE:
                    //TODO: A '#' character represents a complete sub-tree of the hierarchy and thus must be the last character in a subscription topic string, such as SENSOR/#. This will match any topic starting with SENSOR/, such as SENSOR/1/TEMP and SENSOR/2/HUMIDITY. 
                    //TODO: A '+' character represents a single level of the hierarchy and is used between delimiters.For example, SENSOR/ +/ TEMP will match SENSOR / 1 / TEMP and SENSOR/ 2 / TEMP.
                    if ((control & MSG_FLAG_BITS_MASK) != 0b0010)
                    {
                        Logger.LogError("Wrong MQTT control flags received in frame");                      //Low nibble of control isn't 0010 for subscribe packet
                        return false;
                    }
                    var subIDMSB = buffer.ElementAt(bufIndex);
                    var subIDLSB = buffer.ElementAt(++bufIndex);
                    bufIndex++;

                    var subRegRes = new List<Byte>();
                    while (bufIndex < buffer.Count)                                         // Save topics subscribed to and their QoS
                    {
                        var subTopic = new Topic
                        {
                            topicName = GetUTF8(ref buffer, ref bufIndex),
                            qos = buffer.ElementAt(bufIndex++)
                        };

                        //var route = (websocket != null) ? "WebServices" : "SocketServices";         // TODO: Hardcoded message routing....

                        //TODO: Register subscription, remove earlier subscriptions to the same topic, test for QoS > 2, or null topic
                        if (true)               // TODO: Check that server accepts QoS level
                        {
                            List<String> subTopicHA = new List<String>(subTopic.topicName.Split('\\'));
                            if (subTopicHA.Count == 3 || subTopicHA.Count == 4)
                            {
                                topics.Add(subTopic);
                                subRegRes.Add((byte)subTopic.qos);
                                if (Core._debug) Logger.LogDebug("Client [" + IPAddr + "] requested subscription to " + subTopic.topicName);
                                Core.pubSub.Subscribe(name, new Interfaces.ChannelKey
                                {
                                    network = Commons.Globals.networkName,
                                    category = subTopicHA[0],
                                    className = subTopicHA[1],
                                    instance = subTopicHA[2]
                                }, routeName);
                            }
                            else
                            {
                                subRegRes.Add(QOS_LEVEL_GRANTED_FAILURE);
                                Logger.LogWarning("Client [" + IPAddr + "] specified incorrect topic: " + subTopic.topicName + ", not subscribed");
                            }
                        }
                        else
                        {
                            subRegRes.Add(QOS_LEVEL_GRANTED_FAILURE);
                        }
                    }

                    List<Byte> subAck = new List<byte> { MQTT_MSG_SUBACK_TYPE << 4, (Byte)(2 + subRegRes.Count), subIDMSB, subIDLSB };
                    subAck.AddRange(subRegRes);
                    MQTTSend(subAck);
                    break;

                case MQTT_MSG_UNSUBSCRIBE_TYPE:
                    if ((control & MSG_FLAG_BITS_MASK) != 0b0010)
                    {
                        Logger.LogError("Wrong MQTT control flags received in frame");                      //Low nibble of control isn't 0010 for unsubscribe packet
                        return false;
                    }
                    var unsubIDMSB = buffer.ElementAt(bufIndex);
                    var unsubIDLSB = buffer.ElementAt(++bufIndex);

                    bufIndex++;
                    while (bufIndex < buffer.Count)                                         // loop topics unsubscribed to
                    {
                        var untopic = GetUTF8(ref buffer, ref bufIndex);
                        for (int i = topics.Count - 1; i >= 0; i--)
                        {
                            if (topics[i].topicName.ToUpper() == untopic.ToUpper())
                            {
                                UnSubscribe(topics[i]);
                                topics.RemoveAt(i);                                         // remove all topics unsubscribed from client topic list & message queue subscriptions
                            }
                        }
                    }

                    MQTTSend(new List<Byte> { MQTT_MSG_UNSUBACK_TYPE << 4, 2, unsubIDMSB, unsubIDMSB });
                    break;

                case MQTT_MSG_DISCONNECT_TYPE:
                    Logger.LogInformation("Client disconnected");                             // Orderly disconnect
                    return false;

                default:
                    Logger.LogError("Invalid MQTT control request received: " + control);
                    return false;
            }
            resetFrame();
            return true;
        }

        // Publish topic / data strings
        public void MQTTPublish(String topic, String data, Byte QoS)
        {
            //TODO: When sending a PUBLISH Packet to a Client the Server MUST set the RETAIN flag to 1 if a message is sent as a result of a new subscription being made by a Client [MQTT-3.3.1-8]. It MUST set the RETAIN flag to 0 when a PUBLISH Packet is sent to a Client because it matches an established subscription regardless of how the flag was set in the message it received
            Byte header = (MQTT_MSG_PUBLISH_TYPE << 4) | PUB_DUP_FALSE | (QOS_LEVEL_AT_MOST_ONCE << 1) | PUB_RETAIN_FALSE;
            byte[] UTF8Topic = Encoding.UTF8.GetBytes(topic);
            Byte topicMSB = (byte)(UTF8Topic.Count() / 256);
            Byte topicLSB = (byte)(UTF8Topic.Count() - (UTF8Topic.Count() * 256));
            var UTF8Data = Encoding.UTF8.GetBytes(data);
            Byte[] packetID = { };
            if (QoS == 1 || QoS == 2)
            {
                //TODO: add packet id
                throw new Exception("QoS > 0 not supported");
            }

            int remLen = 2 + UTF8Topic.Count() + packetID.Count() + UTF8Data.Count();
            List<Byte> encLen = new List<Byte>();
            do
            {
                Byte encodedByte = (byte)(remLen % 128);
                remLen = remLen / 128;
                if (remLen > 0)                                          // if there are more data to encode, set the top bit of this byte
                {
                    encodedByte = (byte)(encodedByte | 128);
                }
                encLen.Add(encodedByte);
            } while (remLen > 0);

            Byte remLenMSB = (byte)(remLen / 256);
            Byte remLenLSB = (byte)(remLen - (remLen * 256));

            var pub = new List<Byte>() { header };
            pub.AddRange(encLen);
            pub.Add(topicMSB);
            pub.Add(topicLSB);
            pub.AddRange(UTF8Topic);
            pub.AddRange(packetID);
            pub.AddRange(UTF8Data);
            MQTTSend(pub);
        }

        // Send to client as binary
        private async void MQTTSend(List<Byte> resp)
        {
            if (Core._debug)
            {
                var sb1 = new StringBuilder();
                var decoder1 = Encoding.UTF8.GetDecoder();
                var charbuffer1 = new Char[1024];                            // TODO: Check buffer size for large sends
                var charLen = decoder1.GetChars(resp.ToArray(), 0, (int)resp.Count, charbuffer1, 0);

                sb1.Append("[");
                for (var i = 0; i < (int)resp.Count; i++)
                {
                    sb1.Append(resp.ToArray()[i].ToString() + ",");
                }
                sb1.Append("] -");
                sb1.Append(charbuffer1, 0, (int)resp.Count);
                sb1.Append("-");
                Logger.LogDebug("WS Send [" + name + "] <" + System.Threading.Thread.CurrentThread.ManagedThreadId + "> "+ sb1.ToString());
            }
            if (websocket != null && websocket.State == WebSocketState.Open) await websocket.SendAsync(new ArraySegment<Byte>(resp.ToArray<Byte>()), WebSocketMessageType.Binary, true, CancellationToken.None);
            //TODO: sockets untested
            if (socket != null) await socket.SendAsync(new ArraySegment<Byte>(resp.ToArray<Byte>()), SocketFlags.None);
        }

        // Reset frame state variables to prepare for new frame
        private void resetFrame()
        {
            MQTTPingTimeout.Change((int)(1000 * connFlags.keepAlive * 1.5), (int)(1000 * connFlags.keepAlive * 1.5));

            flags.gotLen = false;
            flags.bufIndex = 0;
            flags.lenMult = 1;
            flags.procRemLen = 0;

            waitOnPubRel = 0;
            numFrames++;
            buffer = new List<byte>();
        }

        private void FrameTimeout(Object stateInfo)
        {
            TimeoutEvent(this, "MQTT frame timed out");
        }

        private void ClientTimeout(Object stateInfo)
        {
            TimeoutEvent(this, "MQTT client timed out");
        }

        // Close client MQTT session (network session needs to be closed by network session layer)
        public void CloseMQTTClient(String reason)
        {
            frameTimeout.Dispose();
            MQTTPingTimeout.Dispose();
            Logger.LogInformation("Closing MQTT session for client [" + name + "], reason: " + reason);

            foreach (var topic in topics)                                   // unsubscribe to all client topics subscriptions
            {
                UnSubscribe(topic);
            }
        }

        // Unsubscribe from PubSub subscription store
        private int UnSubscribe(Topic unSubTopic)
        {
            List<String> topicHA = new List<String>(unSubTopic.topicName.Split('\\'));
            return Core.pubSub.UnSubscribe(name, new Interfaces.ChannelKey
            {
                network = Commons.Globals.networkName,
                category = topicHA[0],
                className = topicHA[1],
                instance = topicHA[2]
            }, routeName);

        }

        // Get UTF-8 char string of length determined by 1st and 2nd bytes in buffer segment
        private string GetUTF8(ref List<Byte> buffer, ref int bufIndex)
        {
            var len = buffer.ElementAt(bufIndex) * 256 + buffer.ElementAt(++bufIndex);
            var UTF = new String(Encoding.UTF8.GetChars(new ArraySegment<Byte>(buffer.ToArray<Byte>(), ++bufIndex, len).ToArray<Byte>()));
            bufIndex += len;
            return UTF;
        }

        public struct ConnFlags
        {
            public bool clean;
            public bool will;
            public uint willQoS;
            public bool willRetain;
            public bool passFlg;
            public bool userFlg;
            public int keepAlive;
        }

        public struct Topic
        {
            public string topicName;
            public uint qos;
        }

        public struct PubFlags
        {
            public bool dup;
            public uint QoS;
            public bool retain;
        }

        public struct ProtFlags
        {
            public bool gotLen;
            public int bytesToGet;
            public uint lenMult;
            public uint bufIndex;
            public int procRemLen;
        }

        // Misc
        internal const byte MSG_FLAG_BITS_MASK = 0b00001111;
//        internal const int CLIENT_TIMEOUT = 25;                                             // number of seconds to timeout since client last sent message or ping
        internal const int FRAME_TIMEOUT = 5;                                               // full WS MQTT frame not received
        internal const byte MSG_ACK_LEN = 2;
        internal const byte QOS_LEVEL_GRANTED_FAILURE = 0x80;
        internal const byte PUB_DUP_TRUE = 0b00001000;
        internal const byte PUB_DUP_FALSE = 0b00000000;
        internal const byte PUB_RETAIN_TRUE = 0b00000001;
        internal const byte PUB_RETAIN_FALSE = 0b00000000;

        // protocol names
        internal const string PROTOCOL_NAME_V31 = "MQIsdp";
        internal const string PROTOCOL_NAME_V311 = "MQTT";
        internal const byte PROTOCOL_NAME_V31_LEVEL_VAL = 3;
        internal const byte PROTOCOL_NAME_V311_LEVEL_VAL = 4;

        // Message types
        internal const byte MQTT_MSG_CONNECT_TYPE = 0x01;
        internal const byte MQTT_MSG_CONNACK_TYPE = 0x02;
        internal const byte MQTT_MSG_PUBLISH_TYPE = 0x03;
        internal const byte MQTT_MSG_PUBACK_TYPE = 0x04;
        internal const byte MQTT_MSG_PUBREC_TYPE = 0x05;
        internal const byte MQTT_MSG_PUBREL_TYPE = 0x06;
        internal const byte MQTT_MSG_PUBCOMP_TYPE = 0x07;
        internal const byte MQTT_MSG_SUBSCRIBE_TYPE = 0x08;
        internal const byte MQTT_MSG_SUBACK_TYPE = 0x09;
        internal const byte MQTT_MSG_UNSUBSCRIBE_TYPE = 0x0A;
        internal const byte MQTT_MSG_UNSUBACK_TYPE = 0x0B;
        internal const byte MQTT_MSG_PINGREQ_TYPE = 0x0C;
        internal const byte MQTT_MSG_PINGRESP_TYPE = 0x0D;
        internal const byte MQTT_MSG_DISCONNECT_TYPE = 0x0E;

        // QOS
        public const byte QOS_LEVEL_AT_MOST_ONCE = 0x00;
        public const byte QOS_LEVEL_AT_LEAST_ONCE = 0x01;
        public const byte QOS_LEVEL_EXACTLY_ONCE = 0x02;
    }
}

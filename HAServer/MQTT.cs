using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;


namespace HAServer
{
    public struct ConnFlags
    {
        public bool clean;
        public bool will;
        public uint willQoS;
        public bool willRetain;
        public bool passFlg;
        public bool userFlg;
        public uint keepAlive;
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

    public class MQTTServer
    {
        public bool connected;
        public ProtFlags wsFlags;
        public uint control;
        public string IPAddr;
        public string port;
        public string name;
        public string clientID;
        public string willTopic;
        public string willMessage;
        public string MQTTver;
        public int numFrames;
        public ushort waitOnPubRel;
        public ConnFlags connFlags;
        public List<Byte> buffer;
        public WebSocket websocket;
        public List<Topic> topics;
        public Timer MQTTPingTimeout;
        public Timer frameTimeout;

        static public ILogger Logger = ApplicationLogging.CreateLogger<MQTTServer>();

        public MQTTServer(WebSocket webSocket, string clientIP, string clientPort)
        {
            wsFlags = new ProtFlags {
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
            name = IPAddr + "_0";
            port = clientPort;
            numFrames = 0;
            waitOnPubRel = 0;
            connFlags = new ConnFlags();
            websocket = webSocket;
            topics = new List<Topic>();
            buffer = new List<Byte>();
            MQTTPingTimeout = new Timer(ClientTimeout, null, 1000 * MQTT_CONST.CLIENT_TIMEOUT, 1000 * MQTT_CONST.CLIENT_TIMEOUT);
            frameTimeout = new Timer(FrameTimeout, null, 1000 * MQTT_CONST.FRAME_TIMEOUT, 1000 * MQTT_CONST.FRAME_TIMEOUT);
        }

        // Manage the MQTT frame here assuming multiple websocket frames are sent to make up a MQTT frame, and to extract the frame size to know when a frame is fully received
        public bool HandleFrame(List<Byte> frameBuf, int receivedCnt)
        {
            //TODO: Chech for buffer overruns when extracting string lengths

            if (!connected && control == 0 && frameBuf.ElementAt(0) != (MQTT_CONST.MQTT_MSG_CONNECT_TYPE << 4))  // New session, must be MQTT connect request (1st buffer byte = 16) else the request isn't MQTT
            {
                CloseMQTTClient("Incorrect MQTT control request received, aborting session");
                return false;
            }
            else
            {
                frameTimeout.Change(Timeout.Infinite, Timeout.Infinite);         // stop frame timer as we have received completed frame
                for (var i = 0; i < receivedCnt; i++)
                {
                    if (!wsFlags.gotLen)                       // Process fixed header to get MQTT control byte and remaining bytes
                    {
                        if (wsFlags.bufIndex == 0)
                        {
                            control = frameBuf.ElementAt(0);
                            if ((control >> 4) == MQTT_CONST.MQTT_MSG_PINGREQ_TYPE || (control >> 4) == MQTT_CONST.MQTT_MSG_DISCONNECT_TYPE)  // handle messages with no variable header / payload bytes to receive
                            {
                                wsFlags.gotLen = true;
                                wsFlags.bytesToGet = 1;
                            }
                        }
                        else
                        {
                            if (wsFlags.bufIndex < 5)
                            {
                                wsFlags.procRemLen += (int)((frameBuf.ElementAt(i) & 127) * wsFlags.lenMult);
                                wsFlags.lenMult *= 128;
                                if (frameBuf.ElementAt(i) < 128)          // No more bytes to encode length if value < 128
                                {
                                    wsFlags.bytesToGet = wsFlags.procRemLen;
                                    wsFlags.gotLen = true;
                                    wsFlags.bufIndex = 0;
                                }
                            }
                            else
                            {
                                CloseMQTTClient("Request to receive too many bytes");
                                return false;
                            }
                        }
                        wsFlags.bufIndex++;
                    }
                    else                                // process the rest of the message after the fixed header
                    {
                        //TODO: Is it needed to save the buffer into the clioent object????
                        buffer.AddRange(frameBuf.Skip(i).Take(receivedCnt - i));            // put the remaining buffer into the client object for further processing
                        wsFlags.bytesToGet = wsFlags.bytesToGet - (receivedCnt - i);
                        if (wsFlags.bytesToGet < 0)
                        {
                            CloseMQTTClient("Incorrect number of bytes received");
                            return false;
                        }

                        if (wsFlags.bytesToGet == 0)           // received all the frame
                        {
                            //Console.WriteLine("to get: " + wsFlags.bytesToGet);
                            if (wsFlags.bytesToGet == 0) HandleWSBinMsg();
                        }
                        break;                  // exit loop
                    }
                }
            }
            return true;
        }

        // Handle incoming websockets Binary messages as MQTT
        //TODO: Make this a generic MQTT protocol handler so other network stacks can use it.
        public bool HandleWSBinMsg()
        {
            //TODO: Chech for buffer overruns when extracting string lengths

            int bufIndex = 0;

            switch (control >> 4)
            {
                case MQTT_CONST.MQTT_MSG_CONNECT_TYPE:
                    if ((control & MQTT_CONST.MSG_FLAG_BITS_MASK) != 0b0000)
                    {
                        CloseMQTTClient("Incorrect control flags");                          // Low nibble of control isn't 0 for Connect packet
                        return false;
                    }

                    MQTTver = GetUTF8(ref buffer, ref bufIndex);
                    switch (MQTTver)
                    {
                        case MQTT_CONST.PROTOCOL_NAME_V31:
                            if (buffer.ElementAt(bufIndex) != MQTT_CONST.PROTOCOL_NAME_V31_LEVEL_VAL)
                            {
                                CloseMQTTClient("Invalid protocol level for 3.1");                  // Invalid protocol level for 3.1
                                return false;
                            }
                            break;
                        case MQTT_CONST.PROTOCOL_NAME_V311:
                            if (buffer.ElementAt(bufIndex) != MQTT_CONST.PROTOCOL_NAME_V311_LEVEL_VAL)
                            {
                                CloseMQTTClient("Invalid protocol level for 3.11");                  // Invalid protocol level for 3.1.1
                                return false;
                            }

                            break;
                        default:
                            //TODO: protocol name not supported
                            break;
                    }
                    if ((buffer.ElementAt(++bufIndex) & 0b00000001) == 0b00000001)
                    {
                        //TODO: First flag bit can't be 1
                    }
                    connFlags.clean = (buffer.ElementAt(bufIndex) & 0b00000010) == 0b00000010;
                    connFlags.will = (buffer.ElementAt(bufIndex) & 0b00000100) == 0b00000100;
                    connFlags.willQoS = (uint)(buffer.ElementAt(bufIndex) & 0b00011000) >> 4;
                    connFlags.willRetain = (buffer.ElementAt(bufIndex) & 0b00100000) == 0b00100000;
                    connFlags.passFlg = (buffer.ElementAt(bufIndex) & 0b01000000) == 0b01000000;
                    connFlags.userFlg = (buffer.ElementAt(bufIndex) & 0b10000000) == 0b10000000;

                    connFlags.keepAlive = (uint)(buffer.ElementAt(++bufIndex) * 256 + buffer.ElementAt(++bufIndex));
                    if (connFlags.keepAlive != 0)
                    {
                        //TODO: Set timer so that if client does not send anything in 1.5x keepalive seconds, disconnect the client & reset session
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
                    MQTTSend(new List<byte> { MQTT_CONST.MQTT_MSG_CONNACK_TYPE << 4, MQTT_CONST.MSG_ACK_LEN, connAckFlg, connAckRet });
                    Logger.LogInformation("Client [" + IPAddr + "] MQTT session established");
                    connected = true;
                    break;

                case MQTT_CONST.MQTT_MSG_PUBLISH_TYPE:
                    var pubFlags = new PubFlags()
                    {
                        dup = ((control & 0b00001000) == 0b00001000),
                        QoS = (control & 0b00000110) >> 1,
                        retain = ((control & 0b00000001) == 0b00000001)
                    };
                    if (pubFlags.QoS > MQTT_CONST.QOS_LEVEL_EXACTLY_ONCE)
                    {
                        CloseMQTTClient("Wrong QoS specified: " + pubFlags.QoS);
                        return false;
                    }
                    var pubTopic = GetUTF8(ref buffer, ref bufIndex);

                    Byte pubIDMSB = 0;
                    Byte pubIDLSB = 0;
                    if (pubFlags.QoS > MQTT_CONST.QOS_LEVEL_AT_MOST_ONCE)
                    {
                        pubIDMSB = buffer.ElementAt(++bufIndex);
                        pubIDLSB = buffer.ElementAt(++bufIndex);
                        bufIndex++;
                    }
                    var pubMsg = buffer.Skip(bufIndex).ToList<Byte>();                   // remaining bytes is the publish message (binary form not text)

                    var pubDataHA = new ASCIIEncoding().GetString(pubMsg.ToArray());
                    List<String> pubTopicHA = new List<String>(pubTopic.Split('\\'));

                    // Only publish valid HA format
                    if (pubTopicHA.Count == 4)
                    {
                        if (Core._debug) Logger.LogDebug("Client [" + IPAddr + "] published topic: " + pubTopic + " Data: " + pubDataHA);
                        Core.pubSub.Publish(new Interfaces.ChannelKey
                        {
                            network = Commons.Globals.networkName,
                            category = pubTopicHA[0],
                            className = pubTopicHA[1],
                            instance = pubTopicHA[2],
                        }, pubTopicHA[3], pubDataHA);

                        if (pubFlags.QoS == MQTT_CONST.QOS_LEVEL_AT_LEAST_ONCE)
                        {
                            MQTTSend(new List<byte> { MQTT_CONST.MQTT_MSG_PUBACK_TYPE << 4, MQTT_CONST.MSG_ACK_LEN, pubIDMSB, pubIDLSB });
                        }
                        if (pubFlags.QoS == MQTT_CONST.QOS_LEVEL_EXACTLY_ONCE)
                        {
                            MQTTSend(new List<byte> { MQTT_CONST.MQTT_MSG_PUBREC_TYPE << 4, MQTT_CONST.MSG_ACK_LEN, pubIDMSB, pubIDLSB });
                            waitOnPubRel = (ushort)(pubIDMSB * 256 + pubIDLSB);
                            //TODO: Set a state machine so that we are waiting for a pubrel from the client
                        }
                    }
                    else
                    {
                        Logger.LogWarning("Client [" + IPAddr + "] specified incorrect topic: " + pubTopic + ", not published");
                    }

                    //TODO: retain logic for storing messages
                    //TODO: Submit to queue, check that this client can submit to the queue, else close the connection

                    break;

                case MQTT_CONST.MQTT_MSG_PUBREL_TYPE:
                    Byte pubRelMSB = (Byte)(waitOnPubRel >> 8);
                    Byte pubRelLSB = (Byte)(waitOnPubRel & 0b0000000011111111);
                    MQTTSend( new List<byte> { MQTT_CONST.MQTT_MSG_PUBCOMP_TYPE << 4, MQTT_CONST.MSG_ACK_LEN, pubRelMSB, pubRelLSB });
                    waitOnPubRel = 0;
                    break;

                case MQTT_CONST.MQTT_MSG_PINGREQ_TYPE:
                    if ((control & MQTT_CONST.MSG_FLAG_BITS_MASK) == 0b0000)
                    {
                        MQTTSend(new List<Byte> { MQTT_CONST.MQTT_MSG_PINGRESP_TYPE << 4, 0 });
                    }
                    break;

                case MQTT_CONST.MQTT_MSG_SUBSCRIBE_TYPE:
                    if ((control & MQTT_CONST.MSG_FLAG_BITS_MASK) != 0b0010)
                    {
                        CloseMQTTClient("Wrong control flags");                      //Low nibble of control isn't 0010 for subscribe packet
                        return false;
                    }
                    var subIDMSB = buffer.ElementAt(++bufIndex);
                    var subIDLSB = buffer.ElementAt(++bufIndex);

                    //var subTopics = new List<Topic>();
                    var subRegRes = new List<Byte>();
                    while (bufIndex < buffer.Count)                                         // Save topics subscribed to and their QoS
                    {
                        var subTopic = new Topic
                        {
                            topicName = GetUTF8(ref buffer, ref bufIndex),
                            qos = buffer.ElementAt(bufIndex++)
                        };
                        //TODO: Register subscription, remove earlier subscriptions to the same topic, test for QoS > 2, or null topic
                        if (true)               // TODO: Check that server accepts QoS level
                        {
                            topics.Add(subTopic);
                            List<String> subTopicHA = new List<String>(subTopic.topicName.Split('\\'));
                            if (subTopicHA.Count == 3 || subTopicHA.Count == 4)
                            {
                                subRegRes.Add((byte)subTopic.qos);
                                if (Core._debug) Logger.LogDebug("Client [" + IPAddr + "] requested subscription to " + subTopic.topicName);
                                Core.pubSub.Subscribe(IPAddr, new Interfaces.ChannelKey
                                {
                                    network = Commons.Globals.networkName,
                                    category = subTopicHA[0],
                                    className = subTopicHA[1],
                                    instance = subTopicHA[2]
                                });
                            }
                            else
                            {
                                subRegRes.Add(MQTT_CONST.QOS_LEVEL_GRANTED_FAILURE);
                                Logger.LogWarning("Client [" + IPAddr + "] specified incorrect topic: " + subTopic.topicName + ", not subscribed");
                            }
                        }
                        else
                        {
                            subRegRes.Add(MQTT_CONST.QOS_LEVEL_GRANTED_FAILURE);
                        }
                    }

                    List<Byte> subAck = new List<byte> { MQTT_CONST.MQTT_MSG_SUBACK_TYPE << 4, (byte)(2 + subRegRes.Count), subIDMSB, subIDMSB};
                    subAck.AddRange(subRegRes);
                    MQTTSend(subAck);
                    break;

                case MQTT_CONST.MQTT_MSG_UNSUBSCRIBE_TYPE:
                    if ((control & MQTT_CONST.MSG_FLAG_BITS_MASK) != 0b0010)
                    {
                        CloseMQTTClient("Wrong control flags");                      //Low nibble of control isn't 0010 for unsubscribe packet
                        return false;
                    }
                    var unsubIDMSB = buffer.ElementAt(++bufIndex);
                    var unsubIDLSB = buffer.ElementAt(++bufIndex);

                    while (bufIndex < buffer.Count)                                         // loop topics unsubscribed to
                    {
                        var untopic = GetUTF8(ref buffer, ref bufIndex);
                        for (int i = topics.Count - 1; i >= 0; i--)
                        {
                            if (topics[i].topicName.ToUpper() == untopic.ToUpper())
                            {
                                topics.RemoveAt(i);                                         // remove all topics unsubscribed from client topic list & message queue subscriptions
                                //TODO: Handle queue unsubscribe
                            }
                        }
                    }

                    MQTTSend(new List<Byte> { MQTT_CONST.MQTT_MSG_UNSUBACK_TYPE << 4, 2, unsubIDMSB, unsubIDMSB });
                    break;

                case MQTT_CONST.MQTT_MSG_DISCONNECT_TYPE:
                    CloseMQTTClient("Client disconnected");                             // Orderly disconnect
                    return true;

                default:
                    CloseMQTTClient("Invalid MQTT control request received: " + control);
                    return false;
            }
            resetFrame();
            return true;
        }

        // Send to client 
        public async void MQTTSend(List<Byte> resp)
        {
            await websocket.SendAsync(new ArraySegment<Byte>(resp.ToArray<Byte>()), WebSocketMessageType.Binary, true, CancellationToken.None);
        }

        // Reset frame state variables
        private void resetFrame()
        {
            //if (Core._debug) Logger.LogDebug("MQTT Frame reset");
            wsFlags.gotLen = false;         // reset for next frame to process
            wsFlags.bufIndex = 0;
            wsFlags.lenMult = 1;
            wsFlags.procRemLen = 0;
            waitOnPubRel = 0;
            numFrames++;
            buffer = new List<byte>();
            MQTTPingTimeout.Change(1000 * MQTT_CONST.CLIENT_TIMEOUT, 1000 * MQTT_CONST.CLIENT_TIMEOUT);         // reset timeout
        }

        private void FrameTimeout(Object stateInfo)
        {
            CloseMQTTClient("MQTT WebSockets frame timed out");
        }

        private void ClientTimeout(Object stateInfo)
        {
            CloseMQTTClient("Client timed out");
        }

        // Close client MQTT session
        public async void CloseMQTTClient(string reason)
        {
            Logger.LogInformation("Closing MQTT session for client [" + name + "], reason: " + reason);
            await websocket.CloseOutputAsync(WebSocketCloseStatus.InvalidPayloadData, reason, CancellationToken.None);
            websocket.Dispose();
            //TODO: Remove from client array and dispose object
            //TODO: Remove all subscriptions
        }

        // Get UTF-8 char string of length determined by 1st and 2nd bytes in buffer segment
        private string GetUTF8(ref List<Byte> buffer, ref int bufIndex)
        {
            var len = buffer.ElementAt(bufIndex) * 256 + buffer.ElementAt(++bufIndex);
            var UTF = new String(Encoding.UTF8.GetChars(new ArraySegment<Byte>(buffer.ToArray<Byte>(), ++bufIndex, len).ToArray<Byte>()));
            bufIndex += len;
            return UTF;
        }
    }

    #region MQTT_Constants

    class MQTT_CONST
    {
        // Misc
        internal const byte MSG_FLAG_BITS_MASK = 0b00001111;
        internal const int CLIENT_TIMEOUT = 20;         // number of seconds to timeout since client last sent message
        internal const int FRAME_TIMEOUT = 5;         // full WS MQTT frame not received
        internal const byte MSG_ACK_LEN = 2;
        internal const byte QOS_LEVEL_GRANTED_FAILURE = 0x80;

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

    #endregion MQTT_Constants
    }
}

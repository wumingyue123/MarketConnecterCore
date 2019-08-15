using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Disconnecting;
using MQTTnet.Client.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebSocketSharp;

namespace MarketConnecterCore
{
    public abstract class FeedBase
    {
        internal string domain;
        internal IMqttClient mqttClient = new MqttFactory().CreateMqttClient();
        internal IMqttClientOptions mqttClientOptions = new MqttClientOptionsBuilder()
                                                          .WithTcpServer(server: settings.IPADDR, port: settings.PORT)
                                                          .Build();
        internal abstract EventHandler<MessageEventArgs> MessageReceivedHandler();
        internal abstract EventHandler OpenedHandler(WebSocket socket);

        #region FeedMessage class
        public class FeedMessage
        {
            public string topic;
            public string message;

            public FeedMessage(string topic, string message)
            {
                this.topic = topic;
                this.message = message;
            }
        }
        #endregion

        #region functions


        internal string DecompressData(byte[] byteData)
        {
            using (var decompressedStream = new MemoryStream())
            using (var compressedStream = new MemoryStream(byteData))
            using (var deflateStream = new GZipStream(compressedStream, CompressionMode.Decompress))
            {
                deflateStream.CopyTo(decompressedStream);
                decompressedStream.Position = 0;

                using (var streamReader = new StreamReader(decompressedStream))
                {
                    return streamReader.ReadToEnd();
                }
            }
        }

        internal EventHandler<CloseEventArgs> ClosedHandler(WebSocketSharp.WebSocket socket)
        {
            return (sender, e) =>
            {
                Console.WriteLine($"{socket.Url} Websocket closed with reason:");
                Console.WriteLine(e.Reason);
                Console.WriteLine($"error code {e.Code}");
                var sslProtocolHack = (SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls);
                //TlsHandshakeFailure
                if (e.Code == 1015 && socket.SslConfiguration.EnabledSslProtocols != sslProtocolHack)
                {
                    socket.SslConfiguration.EnabledSslProtocols = sslProtocolHack;
                    socket.Connect();
                }
                else
                {
                    socket.Connect(); // try reconnecting
                } 

            };
        }

        internal static EventHandler<WebSocketSharp.ErrorEventArgs> ErrorHandler()
        {
            return (sender, e) => Console.WriteLine(e.Exception);
        }


        public void publishMessage(string message, string topic)
        {
            mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                        .WithTopic(topic)
                        .WithPayload(message)
                        .WithAtLeastOnceQoS()
                        .WithRetainFlag()
                        .Build());
        }

        public void publishMessage(Stream message, string topic)
        {
            mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                        .WithTopic(topic)
                        .WithPayload(message)
                        .WithAtLeastOnceQoS()
                        .WithRetainFlag()
                        .Build());
        }

        public void publishMessage(byte[] message, string topic)
        {
            mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                        .WithTopic(topic)
                        .WithPayload(message)
                        .WithAtLeastOnceQoS()
                        .WithRetainFlag(true)
                        .Build());
        }

        public void mqttDisconnectedHandler(MqttClientDisconnectedEventArgs e)
        {
            Console.WriteLine($"####### Disconnected from MQTT server with reason {e.Exception} #########");
            Thread.Sleep((int)1e4);
            Console.WriteLine("Retrying connection...");
            mqttClient.ConnectAsync(this.mqttClientOptions);
        }

        #endregion
    }
}

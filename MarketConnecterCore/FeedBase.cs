﻿using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Disconnecting;
using MQTTnet.Client.Options;
using System;
using System.IO;
using System.IO.Compression;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using WebSocketSharp;
using GlobalSettings;

namespace MarketConnecterCore
{
    public abstract class FeedBase
    {
        internal string domain;
        internal IMqttClient mqttClient = new MqttFactory().CreateMqttClient();
        internal IMqttClientOptions mqttClientOptions = new MqttClientOptionsBuilder()
                                                          .WithTcpServer(server: BitmexSettings.MqttIpAddr, port: BitmexSettings.MqttPort)
                                                          .WithCredentials(username: BitmexSettings.MqttUserName, password: BitmexSettings.MqttPassword)
                                                          .Build();
        protected abstract EventHandler<MessageEventArgs> MessageReceivedHandler();
        protected abstract EventHandler OpenedHandler(WebSocket socket);

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
                    Reconnect(socket);
                    
                } 

            };
        }

        internal static EventHandler<WebSocketSharp.ErrorEventArgs> ErrorHandler(WebSocket socket)
        {
            return (sender, e) =>
            {
                Console.WriteLine(e.Exception);
                Reconnect(socket);    
                
            };

        }

        internal static void Reconnect(WebSocket socket, int timeout = 3000)
        {
            if(socket.ReadyState == WebSocketState.Closed)
            {
                socket.Connect();
            }
            else if (socket.ReadyState == WebSocketState.Closing)
            {
                Thread.Sleep(timeout);
                Reconnect(socket, timeout);
            }
            else if (socket.ReadyState == WebSocketState.Connecting)
            {
                Thread.Sleep(timeout);
                Reconnect(socket, timeout);
            }
            else if (socket.ReadyState == WebSocketState.Open)
            {
                return;
            }

        }


        public async Task publishMessage(string message, string topic)
        {
            await mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                        .WithTopic(topic)
                        .WithPayload(message)
                        .WithAtLeastOnceQoS()
                        .WithRetainFlag()
                        .Build());
        }

        public async Task publishMessage(Stream message, string topic)
        {
            await mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                        .WithTopic(topic)
                        .WithPayload(message)
                        .WithAtLeastOnceQoS()
                        .WithRetainFlag()
                        .Build());
        }

        public async Task publishMessage(byte[] message, string topic)
        {
            await mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Threading;
using Newtonsoft.Json;
using WebSocket4Net;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using System.IO;
using System.Net.Sockets;
using MarketConnecterCore;

namespace MarketConnectorCore
{
    public class BitmexFeed
    {
        public string domain = "wss://www.bitmex.com/realtime";
        private string _apiKey = "8YFN7m1nciXgxJru9TCALc-A"; // "-U3zj2B-smGIzZC87Lh4hxlK"
        private string _apiSecret = "UBwm38Beoa_rXaNcnznJvoSVDfLKSS9S40YayqZOza_O0Q1Y"; // "ZDKlW9u8Q-Hr9o09YE13tDo2-dhp0d5_qcaQhRkdupsJemL0"
        public CancellationToken cancelToken = new CancellationToken(false);
        private IMqttClient mqttClient = new MqttFactory().CreateMqttClient();
        private IMqttClientOptions mqttClientOptions = new MqttClientOptionsBuilder()
                                                          .WithTcpServer(server: settings.IPADDR, port: settings.PORT)
                                                          .Build();

        public async Task Start()
        {

            await mqttClient.ConnectAsync(this.mqttClientOptions);

            using (var socket = new WebSocket(domain))
            {
                socket.MessageReceived += (sender, e) =>
                {
                    var data = e.Message;
                    publishMessage(message: data, topic: "marketdata/bitmexdata").ConfigureAwait(false);
                };
                socket.Error += (sender, e) => Console.WriteLine(e.Exception);
                socket.Closed+= (sender, e) =>
                {
                    Console.WriteLine(e.ToString());
                    Console.WriteLine($"socket closed at {domain}");
                    socket.Open();
                };

                socket.Opened += (sender, e) =>
                {
                    Console.WriteLine("Connection open: {0}", domain);
                    Authenticate(socket);
                    foreach (string _symbol in settings.bitmexCurrencyList)
                    {
                        Subscribe(socket, $"trade:{_symbol}");
                    }
                    
                };

                socket.Open();
                
                Console.ReadLine();
            }
            Console.WriteLine("Exiting of program");
        }

        private void Authenticate(WebSocket socket)
        {
            Console.WriteLine("authenticating...");
            long _nonce = GetNonce();
            var message = "GET/realtime" + _nonce;
            byte[] _signatureBytes = hmacsha256(Encoding.UTF8.GetBytes(_apiSecret),
                Encoding.UTF8.GetBytes(message));
            string _signatureString = ByteArrayToString(_signatureBytes);
            object[] _args = { _apiKey, _nonce, _signatureString };
            var toSend = new
            {
                op = "authKey",
                args = _args
            };

            socket.Send(JsonConvert.SerializeObject(toSend));
        }

        private static void Subscribe(WebSocket socket, string channel)
        {
            var toSend = new
            {
                op = "subscribe",
                args = new[] { channel }
            };
            string sendJson = JsonConvert.SerializeObject(toSend);
            socket.Send(sendJson);
        }

        private static string ByteArrayToString(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }

        private static long GetNonce()
        {
            DateTime centuryBegin = new DateTime(1990, 1, 1);
            return (DateTime.UtcNow.Ticks - centuryBegin.Ticks) / 1000;
        }

        private static byte[] hmacsha256(byte[] keyByte, byte[] messageBytes)
        {
            using (var hash = new HMACSHA256(keyByte))
            {
                return hash.ComputeHash(messageBytes);
            }
        }

        public async Task publishMessage(string message, string topic)
        {
            await this.mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                        .WithTopic(topic)
                        .WithPayload(message)
                        .WithAtLeastOnceQoS()
                        .WithRetainFlag()
                        .Build());
        }

        public async Task publishMessage(Stream message, string topic)
        {
            await this.mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                        .WithTopic(topic)
                        .WithPayload(message)
                        .WithAtLeastOnceQoS()
                        .WithRetainFlag()
                        .Build());
        }

        public async Task publishMessage(byte[] message, string topic)
        {
            await this.mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                        .WithTopic(topic)
                        .WithPayload(message)
                        .WithAtLeastOnceQoS()
                        .WithRetainFlag(true)
                        .Build());
        }
    }
}
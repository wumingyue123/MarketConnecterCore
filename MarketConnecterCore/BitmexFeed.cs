using System;
using System.Collections.Generic;
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
using MQTTnet.Client.Disconnecting;
using MQTTnet.Client.Connecting;
using RestSharp;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using GlobalSettings;
using System.Diagnostics;
using System.Linq;
using NLog;

namespace MarketConnectorCore
{
    public class BitmexFeed
    {
        public string domain = BitmexSettings.BitmexWSS;
        private string _apiKey = BitmexSettings.BitmexProdKey; // "-U3zj2B-smGIzZC87Lh4hxlK"
        private string _apiSecret = BitmexSettings.BitmexProdSecret; // "ZDKlW9u8Q-Hr9o09YE13tDo2-dhp0d5_qcaQhRkdupsJemL0"
        private IMqttClient mqttClient = new MqttFactory().CreateMqttClient();
        private IMqttClientOptions mqttClientOptions = new MqttClientOptionsBuilder()
                                                          .WithTcpServer(server: BitmexSettings.MqttIpAddr, port: BitmexSettings.MqttPort)
                                                          .WithCleanSession()
                                                          .WithCredentials(username: BitmexSettings.MqttUserName, password: BitmexSettings.MqttPassword)
                                                          .Build();
        IRestClient restClient = new RestClient(BitmexSettings.BitmexRestURL);
        public static ConcurrentQueue<FeedMessage> BitmexFeedQueue = new ConcurrentQueue<FeedMessage>();

        protected static NLog.Config.LoggingConfiguration config = new NLog.Config.LoggingConfiguration();
        protected static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public void Start(object callback)
        {
            config.AddRuleForOneLevel(LogLevel.Info, new NLog.Targets.FileTarget("logfile") { FileName="./info.txt" });
            config.AddRuleForOneLevel(LogLevel.Debug, new NLog.Targets.FileTarget("logfile") { FileName = "./info.txt" });
            NLog.LogManager.Configuration = config;

            ThreadPool.QueueUserWorkItem(StartPublish);

            mqttClient.UseDisconnectedHandler(mqttDisconnectedHandler); // reconnect mqtt server on disconnect

            mqttClient.UseConnectedHandler(mqttConnectedHandler);

            mqttClient.ConnectAsync(this.mqttClientOptions).Wait();

            BitmexSettings.bitmexCurrencyList = GetBitmexSymbols();

            using (var socket = new WebSocket(domain))
            {
                socket.MessageReceived += MessageReceivedHandler();
                socket.Error += ErrorHandler();
                socket.Closed += ClosedHandler(socket);
                socket.Opened += OpenedHandler(socket);

                socket.Open();

                Console.ReadLine();
            }
            Console.WriteLine("Exiting of bitmex feed");
        }

        #region MQTT publisher
        private void StartPublish(object callback)
        {
            while(true)
            { 
                if (BitmexFeedQueue.TryDequeue(out FeedMessage _out))
                {
                    publishMessage(_out.message, _out.topic);
                    logger.Info(Stopwatch.GetTimestamp());
                };
            }
        }

        public void publishMessage(string message, string topic)
        {
            // TODO: Make this async
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
            Console.WriteLine($"####### BitmexFeed: MQTT server disconnected with reason {e.AuthenticateResult.ReasonString} {e.Exception.Message} #########");
            Thread.Sleep((int)1e4);
            Console.WriteLine("Retrying connection...");
            ReconnectMqtt();
        }

        public void mqttConnectedHandler(MqttClientConnectedEventArgs e)
        {
            Console.WriteLine($"####### BitmexFeed: Connected to MQTT server {e.AuthenticateResult.ResultCode} #########");
        }

        internal void ReconnectMqtt(int timeout = 3000)
        {
            if (!mqttClient.IsConnected)
            {
                mqttClient.ReconnectAsync();
                Thread.Sleep(timeout);
                ReconnectMqtt(timeout);
            }
            else { return; }
        }
        #endregion

        #region Event Handlers
        protected virtual EventHandler OpenedHandler(WebSocket socket)
        {
            return (sender, e) =>
            {
                Console.WriteLine("Connection open: {0}", domain);
                Authenticate(socket);
                foreach (string _symbol in BitmexSettings.bitmexCurrencyList)
                {
                    Console.WriteLine($"BITMEX: loaded contract------{_symbol}");
                    Subscribe(socket, $"trade:{_symbol}");
                }
            };
        }

        protected virtual EventHandler ClosedHandler(WebSocket socket)
        {
            return (sender, e) =>
            {
                Console.WriteLine($"Bitmex websocket closed with reason:");
                Console.WriteLine(e.ToString());
                Console.WriteLine($"socket closed at {domain}");
                socket.Open();
            };
        }

        protected virtual EventHandler<SuperSocket.ClientEngine.ErrorEventArgs> ErrorHandler()
        {
            return (sender, e) => Console.WriteLine(e.Exception);
        }

        protected virtual EventHandler<MessageReceivedEventArgs> MessageReceivedHandler()
        {
            return (sender, e) =>
            {
                string data = e.Message;
                BitmexFeedQueue.Enqueue(new FeedMessage(topic: BitmexSettings.BitmexTradeChannel, message: data));
            };
        }

        #endregion

        #region functions
        protected void Authenticate(WebSocket socket)
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

        protected static void Subscribe(WebSocket socket, string channel)
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

        public List<string> GetBitmexSymbols()
        {
            List<string> symbolList = new List<string> { };
            var request = new RestRequest("instrument/active");
            var response = this.restClient.Get(request);

            List<JObject> symbolData = JsonConvert.DeserializeObject<List<JObject>>(response.Content);
            foreach (JObject _symbolData in symbolData)
            {
                symbolList.Add((string)_symbolData["symbol"]);
            }
            return symbolList;
        }

        #endregion

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

    }
}
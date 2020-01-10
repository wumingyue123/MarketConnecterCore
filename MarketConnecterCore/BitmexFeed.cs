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
using MarketConnecterCore;
using MQTTnet.Client.Disconnecting;
using RestSharp;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;

namespace MarketConnectorCore
{
    public class BitmexFeed
    {
        public string domain = settings.BitmexWSS;
        private string _apiKey = settings.BITMEX_API_KEY; // "-U3zj2B-smGIzZC87Lh4hxlK"
        private string _apiSecret = settings.BITMEX_API_SECRET; // "ZDKlW9u8Q-Hr9o09YE13tDo2-dhp0d5_qcaQhRkdupsJemL0"
        private IMqttClient mqttClient = new MqttFactory().CreateMqttClient();
        private IMqttClientOptions mqttClientOptions = new MqttClientOptionsBuilder()
                                                          .WithTcpServer(server: settings.IPADDR, port: settings.PORT)
                                                          .WithCleanSession()
                                                          .WithCredentials(username:settings.MQTT_USERNAME, password:settings.MQTT_PASSWORD)
                                                          .Build();
        IRestClient restClient = new RestClient(settings.BitmexRestURL);
        public static ConcurrentQueue<FeedMessage> BitmexFeedQueue = new ConcurrentQueue<FeedMessage>();

        public void Start()
        {
            ThreadPool.QueueUserWorkItem(StartPublish);

            mqttClient.UseDisconnectedHandler(mqttDisconnectedHandler); // reconnect mqtt server on disconnect

            mqttClient.ConnectAsync(this.mqttClientOptions);

            settings.bitmexCurrencyList = GetBitmexSymbols();

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
                FeedMessage _out;
                if (BitmexFeedQueue.TryDequeue(out _out))
                {
                    publishMessage(_out.message, _out.topic);
                };

            }
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
            Console.WriteLine($"####### Bitmex Disconnected from MQTT server with reason {e.Exception} #########");
            Thread.Sleep((int)1e4);
            Console.WriteLine("Retrying connection...");
            ReconnectMqtt();
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
        private EventHandler OpenedHandler(WebSocket socket)
        {
            return (sender, e) =>
            {
                Console.WriteLine("Connection open: {0}", domain);
                Authenticate(socket);
                foreach (string _symbol in settings.bitmexCurrencyList)
                {
                    Console.WriteLine($"BITMEX: loaded contract------{_symbol}");
                    Subscribe(socket, $"trade:{_symbol}");
                }

            };
        }

        private EventHandler ClosedHandler(WebSocket socket)
        {
            return (sender, e) =>
            {
                Console.WriteLine($"Bitmex websocket closed with reason:");
                Console.WriteLine(e.ToString());
                Console.WriteLine($"socket closed at {domain}");
                socket.Open();
            };
        }

        private static EventHandler<SuperSocket.ClientEngine.ErrorEventArgs> ErrorHandler()
        {
            return (sender, e) => Console.WriteLine(e.Exception);
        }

        private EventHandler<MessageReceivedEventArgs> MessageReceivedHandler()
        {
            return (sender, e) =>
            {
                string data = e.Message;
                BitmexFeedQueue.Enqueue(new FeedMessage(topic: settings.BitmexDataChannel, message: data));
            };
        }

        #endregion

        #region functions
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
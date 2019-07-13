using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Newtonsoft.Json;
using WebSocketSharp;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using System.IO;
using RestSharp;
using System.Linq;
using Newtonsoft.Json.Linq;
using MarketConnecterCore;
using MQTTnet.Client.Disconnecting;
using System.Collections.Concurrent;

namespace MarketConnectorCore
{
    public class DeribitFeed
    {
        public string domain = "wss://www.deribit.com/ws/api/v2/";
        public CancellationToken cancelToken = new CancellationToken(false);
        private IMqttClient mqttClient = new MqttFactory().CreateMqttClient();
        private IMqttClientOptions mqttClientOptions = new MqttClientOptionsBuilder()
                                                          .WithTcpServer(server: settings.IPADDR, port: settings.PORT)
                                                          .Build();
        IRestClient restClient = new RestClient("https://www.deribit.com/api/v2/");
        public static ConcurrentQueue<FeedMessage> DeribitFeedQueue = new ConcurrentQueue<FeedMessage>();


        public async Task Start()
        {
            ThreadPool.QueueUserWorkItem(new WaitCallback(StartPublish));

            List<string> symbolList = new List<string>();

            foreach (string _currency in settings.deribitCurrencyList)
            {
                symbolList.AddRange(GetDeribitSymbols(_currency));
            }

            mqttClient.UseDisconnectedHandler(mqttDisconnectedHandler); // reconnect to mqtt server on disconnect
            bool mqttconnected = mqttClient.ConnectAsync(this.mqttClientOptions).IsCompleted;

            using (var socket = new WebSocket(domain))
            {
                socket.OnMessage += MessageHandler(socket);
                socket.OnError += ErrorHandler();
                socket.OnClose += CloseHandler(socket);
                socket.OnOpen += OpenHandler(symbolList, socket);

                socket.Connect();

                Console.ReadLine();
            }
            Console.WriteLine("Exiting of program");
        }

        #region Event Handlers
        private EventHandler OpenHandler(List<string> symbolList, WebSocket socket)
        {
            return (sender, e) =>
            {
                Console.WriteLine("Connection open: {0}", domain);
                List<string> channels = (from symbol in symbolList select $"quote.{symbol}").ToList();
                channels.Add("BTC-PERPETUAL");
                channels.Add("ETH-PERPETUAL");
                Subscribe(socket, channels);
                SetHeartbeat(socket);
            };
        }

        private static EventHandler<CloseEventArgs> CloseHandler(WebSocket socket)
        {
            return (sender, e) =>
            {
                Console.WriteLine(e.Reason);
                socket.Connect();
            };
        }

        private static EventHandler<WebSocketSharp.ErrorEventArgs> ErrorHandler()
        {
            return (sender, e) => Console.WriteLine(e.Message);
        }

        private EventHandler<MessageEventArgs> MessageHandler(WebSocket socket)
        {
            return (sender, e) =>
            {
                var rawdata = e.RawData;
                string data = e.Data;

                Dictionary<string, object> result = JsonConvert.DeserializeObject<Dictionary<string, object>>(data);

                try
                {
                    object method;
                    result.TryGetValue("method", out method);
                    if ((string)method == "heartbeat")
                    {
                        JObject @params = (JObject)result["params"];
                        if ((string)@params["type"] == "test_request")
                        {
                            SendHeartbeat(socket);
                            Console.WriteLine("heartbeat");
                        }
                    }
                    else if ((string)method == "subscription")// publish message
                    {
                        DeribitFeedQueue.Enqueue(new FeedMessage(topic: "marketdata/deribitdata", message: data));

                    }
                }
                catch
                {
                }

            };
        }

        #endregion

        #region functions
        private void Subscribe(WebSocket socket, string channel)
        {

            var toSend = new
            {
                method = "public/subscribe",
                @params = new
                {
                    channels = new string[] { channel }
                }
            };
            string sendJson = JsonConvert.SerializeObject(toSend);
            socket.Send(sendJson);
        }

        private void Subscribe(WebSocket socket, List<string> channel)
        {

            var toSend = new
            {
                method = "public/subscribe",
                @params = new
                {
                    channels = channel
                }
            };
            string sendJson = JsonConvert.SerializeObject(toSend);
            socket.Send(sendJson);
        }

        private void SetHeartbeat(WebSocket socket, int interval = 11)
        {
            var toSend = new
            {
                method = "public/set_heartbeat",
                @params = new
                {
                    interval = interval,
                }
            };
            string sendJson = JsonConvert.SerializeObject(toSend);
            socket.Send(sendJson);
        }

        private void SendHeartbeat(WebSocket socket)
        {
            var toSend = new
            {
                method = "public/test",
            };
            string sendJson = JsonConvert.SerializeObject(toSend);
            socket.Send(sendJson);
        }

        public List<string> GetDeribitSymbols(string currency)
        {
            List<string> symbolList = new List<string> { };

            var request = new RestRequest("public/get_instruments");

            request.AddParameter("currency", currency);
            request.AddParameter("expired", "false");
            request.AddParameter("kind", "option");

            var response = this.restClient.Get(request);

            SymbolData jsonResponse = JsonConvert.DeserializeObject<SymbolData>(response.Content);

            foreach (SymbolResult result in jsonResponse.SymbolResult)
            {
                Console.WriteLine($"DERIBIT: loaded contract------{result.instrument_name}       strike: {result.strike}      type: {result.option_type}");
                symbolList.Add(result.instrument_name);
            }

            Console.WriteLine($"retrieved {jsonResponse.SymbolResult.Count} options symbols");
            Console.WriteLine($"loaded {symbolList.Count} options symbols");

            return symbolList;
        }
        #endregion

        #region MQTT publisher
        private void StartPublish(object callback)
        {
            while (true)
            {
                FeedMessage _out;
                if (DeribitFeedQueue.TryDequeue(out _out))
                {
                    publishMessage(message: _out.message, topic: _out.topic);
                    Console.WriteLine(_out.message);
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
            Console.WriteLine($"####### Disconnected from MQTT server with reason {e.Exception} #########");
            Thread.Sleep((int)1e4);
            Console.WriteLine("Retrying connection...");
            mqttClient.ConnectAsync(this.mqttClientOptions);
        }
        #endregion

        #region FeedMessage class
        public class FeedMessage
        {
            public string topic;
            public string message;
            public byte[] bytes;

            public FeedMessage(string topic, string message)
            {
                this.topic = topic;
                this.message = message;
            }

        }
        #endregion
    }
}
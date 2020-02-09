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
using WebSocketSharp.Net;

namespace MarketConnectorCore
{
    public class DeribitFeed
    {
        private enum SslProtocolsHack
        {
            Tls = 192,
            Tls11 = 768,
            Tls12 = 3072
        }

        public string domain = settings.DeribitWSS;
        private IMqttClient mqttClient = new MqttFactory().CreateMqttClient();
        private IMqttClientOptions mqttClientOptions = new MqttClientOptionsBuilder()
                                                          .WithTcpServer(server: settings.IPADDR, port: settings.PORT)
                                                          .WithCleanSession()
                                                          .Build();
        IRestClient restClient = new RestClient(settings.DeribitRESTURL);
        public static ConcurrentQueue<FeedMessage> DeribitFeedQueue = new ConcurrentQueue<FeedMessage>();


        public void Start()
        {
            ThreadPool.QueueUserWorkItem(new WaitCallback(StartPublish));

            List<string> symbolList = new List<string>();

            foreach (string _currency in settings.deribitCurrencyList)
            {
                symbolList.AddRange(GetDeribitSymbols(_currency));
            }

            mqttClient.UseDisconnectedHandler(mqttDisconnectedHandler); // reconnect to mqtt server on disconnect
            mqttClient.ConnectAsync(this.mqttClientOptions);

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
                Console.WriteLine("Deribit websocket closed with reason:" );
                Console.WriteLine(e.Reason);
                Console.WriteLine($"error code {e.Code}");
                var sslProtocolHack = (System.Security.Authentication.SslProtocols)(SslProtocolsHack.Tls12 | SslProtocolsHack.Tls11 | SslProtocolsHack.Tls);
                //TlsHandshakeFailure
                if (e.Code == 1015 && socket.SslConfiguration.EnabledSslProtocols != sslProtocolHack)
                {
                    socket.SslConfiguration.EnabledSslProtocols = sslProtocolHack;
                    socket.Connect();
                }
                else
                {
                    socket.Connect();
                }
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
                        }
                    }
                    else if ((string)method == "subscription")// publish message
                    {
                        DeribitFeedQueue.Enqueue(new FeedMessage(topic: settings.DeribitDataChannel, message: data));

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
            Console.WriteLine($"####### Deribit Disconnected from MQTT server with reason {e.Exception} #########");
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

        class SymbolData
        {
            [JsonProperty("result")]
            public List<SymbolResult> SymbolResult { get; set; }

        }

        class SymbolResult
        {
            [JsonProperty("tick_size")]
            public float tick_size { get; set; }
            [JsonProperty("strike")]
            public float strike { get; set; }
            [JsonProperty("settlement_period")]
            public string settlement_period { get; set; }
            [JsonProperty("quote_currency")]

            public string quote_currency { get; set; }
            [JsonProperty("option_type")]

            public string option_type { get; set; }
            [JsonProperty("min_trade_amount")]

            public float min_trade_amount { get; set; }
            [JsonProperty("kind")]

            public string kind { get; set; }
            [JsonProperty("is_active")]

            public bool is_active { get; set; }
            [JsonProperty("instrument_name")]

            public string instrument_name { get; set; }
            [JsonProperty("expiration_timestamp")]

            public double expiration_timestamp { get; set; }
            [JsonProperty("creation_timestamp")]

            public double creation_timestamp { get; set; }
            [JsonProperty("contract_size")]

            public float contract_size { get; set; }
            [JsonProperty("base_currency")]

            public string base_currency { get; set; }
        }

    }
}
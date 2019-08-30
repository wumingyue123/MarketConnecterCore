using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Newtonsoft.Json;
using MQTTnet.Client;
using System.Net.Sockets;
using MarketConnecterCore;
using RestSharp;
using System.Collections.Concurrent;
using WebSocket4Net;
using FTXLibrary;
using MQTTnet.Client.Options;
using MQTTnet;
using MQTTnet.Client.Disconnecting;

namespace MarketConnectorCore
{
    public class FTXFeed
    {
        enum channelTypes { trades, orderbook, ticker};

        private string API_KEY;
        private string API_SECRET;
        private IMqttClient mqttClient = new MqttFactory().CreateMqttClient();
        private IMqttClientOptions mqttClientOptions = new MqttClientOptionsBuilder()
                                                          .WithTcpServer(server: settings.IPADDR, port: settings.PORT)
                                                          .Build();
        public new string domain = SETTINGS.FTXWSS;
        IRestClient restClient = new RestClient(SETTINGS.FTXRestURL);
        public static ConcurrentQueue<FeedMessage> FTXFeedQueue = new ConcurrentQueue<FeedMessage>();
        
        public FTXFeed(string API_KEY, string API_SECRET)
        {
            this.API_KEY = API_KEY;
            this.API_SECRET = API_SECRET;
        }

        public FTXFeed()
        {
            this.API_KEY = SETTINGS.FTX_API_KEY;
            this.API_SECRET = SETTINGS.FTX_API_SECRET;
        }


        public async Task Start()
        {
            //settings.FTXCurrencyList = GetFTXSymbols();

            ThreadPool.QueueUserWorkItem(new WaitCallback(StartPublish));

            mqttClient.UseDisconnectedHandler(mqttDisconnectedHandler); // reconnect mqtt server on disconnect

            await mqttClient.ConnectAsync(this.mqttClientOptions);

            using (var socket = new WebSocket(domain))
            {
                socket.Error += ErrorHandler(socket);
                socket.Closed += ClosedHandler(socket);
                socket.Opened += OpenedHandler(socket);
                socket.MessageReceived += MessageReceivedHandler();

                socket.Open();

                Console.ReadLine();
            }

            Console.WriteLine("Exiting of FTX Feed");
        }

        #region MQTT publisher
        private void StartPublish(object callback)
        {
            while (true)
            {
                FeedMessage _out;
                if (FTXFeedQueue.TryDequeue(out _out))
                {
                    string message = _out.message;
                    if (message.Contains("error"))
                    {
                        Console.WriteLine($"Error: {message}");
                    }
                    else
                    {
                        publishMessage(_out.message, _out.topic);
                    }
                };

            }
        }

        #endregion

        #region Event Handlers
        internal EventHandler OpenedHandler(WebSocket socket)
        {
            return (sender, e) =>
            {
                foreach (string _symbol in settings.FTXCurrencyList)
                {
                    Subscribe(socket, channel: channelTypes.trades.ToString(), symbol: _symbol).ConfigureAwait(false);
                    Subscribe(socket, channel: channelTypes.ticker.ToString(), symbol: _symbol).ConfigureAwait(false);
                    Subscribe(socket, channel: channelTypes.orderbook.ToString(), symbol: _symbol).ConfigureAwait(false);
                }

                Console.WriteLine("Connection open: {0}", domain);
            };
        }

        internal EventHandler<MessageReceivedEventArgs> MessageReceivedHandler()
        {
            return (sender, e) =>
            {
                string message = e.Message;
                FTXFeedQueue.Enqueue(new FeedMessage(topic: settings.FTXDataChannel, message: message));
            };
        }

        #endregion

        #region functions

        private async Task Subscribe(WebSocket socket, string channel, string symbol)
        {
            var toSend = new
            {
                op = "subscribe",
                channel = channel,
                market = symbol,
            };
            Console.WriteLine(toSend);
            string sendJson = JsonConvert.SerializeObject(toSend);
            socket.Send(sendJson);
        }

        public List<string> GetFTXSymbols()
        {
            List<string> symbolList = new List<string> { };
            var request = new RestRequest("/markets");
            var response = this.restClient.Get(request);
            SymbolData responseData = JsonConvert.DeserializeObject<SymbolData>(response.Content);
            List<SymbolData.SymbolInfo> symbolData = responseData.result;
            foreach (SymbolData.SymbolInfo _symbol in symbolData)
            {
                Console.WriteLine($"FTX: loaded contract------{_symbol.symbol}      {_symbol.enabled}");
                symbolList.Add(_symbol.symbol);
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

        internal EventHandler ClosedHandler(WebSocket socket)
        {
            return (sender, e) =>
            {
                    Reconnect(socket);

            };
        }

        internal static EventHandler<SuperSocket.ClientEngine.ErrorEventArgs> ErrorHandler(WebSocket socket)
        {
            return (sender, e) =>
            {
                Console.WriteLine(e.Exception);
                Reconnect(socket);
            };

        }

        public void mqttDisconnectedHandler(MqttClientDisconnectedEventArgs e)
        {
            Console.WriteLine($"####### Bitmex Disconnected from MQTT server with reason {e.Exception} #########");
            Thread.Sleep((int)1e4);
            Console.WriteLine("Retrying connection...");
            mqttClient.ConnectAsync(this.mqttClientOptions);
        }


        internal static void Reconnect(WebSocket socket, int timeout = 3000)
        {
            if (socket.State == WebSocketState.Closed)
            {
                socket.Open();
            }
            else if (socket.State == WebSocketState.Closing)
            {
                Thread.Sleep(timeout);
                Reconnect(socket, timeout);
            }
            else if (socket.State == WebSocketState.Connecting)
            {
                Thread.Sleep(timeout);
                Reconnect(socket, timeout);
            }
            else if (socket.State == WebSocketState.Open)
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

        public async Task publishMessage(byte[] message, string topic)
        {
            await mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                        .WithTopic(topic)
                        .WithPayload(message)
                        .WithAtLeastOnceQoS()
                        .WithRetainFlag(true)
                        .Build());
        }


        #region SymbolData class

        internal class SymbolData
        {
            [JsonProperty("success")]
            internal bool success;
            [JsonProperty("result")]
            internal List<SymbolInfo> result;

            internal class SymbolInfo
            {
                [JsonProperty("name")]
                internal string symbol;
                [JsonProperty("type")]
                internal string type;
                [JsonProperty("baseCurrency")]
                internal string baseCurrency;
                [JsonProperty("quoteCurrency")]
                internal string quoteCurrency;
                [JsonProperty("ask")]
                internal double ask;
                [JsonProperty("bid")]
                internal double bid;
                [JsonProperty("enabled")]
                internal bool enabled;
                [JsonProperty("last")]
                internal double last;
                [JsonProperty("priceIncrement")]
                internal double priceIncrement;
                [JsonProperty("sizeIncrement")]
                internal double sizeIncrement;

            }
        }

        #endregion

    }
}
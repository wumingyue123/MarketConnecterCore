using System;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Threading;
using Newtonsoft.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using System.IO;
using System.Net.Sockets;
using MarketConnecterCore;
using MQTTnet.Client.Disconnecting;
using RestSharp;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Authentication;
using WebSocketSharp;
using System.IO.Compression;

namespace MarketConnectorCore
{
    public class HuobiFeed:FeedBase
    {
        public new string domain = settings.HuobiWSS;
        IRestClient restClient = new RestClient(settings.HuobiRestURL);
        public static ConcurrentQueue<FeedMessage> HuobiFeedQueue = new ConcurrentQueue<FeedMessage>();
        private WebSocket socket;

        public async Task Start()
        {
            settings.huobiCurrencyList = GetHuobiSymbols();

            socket = new WebSocket(domain);

            ThreadPool.QueueUserWorkItem(new WaitCallback(StartPublish));

            mqttClient.UseDisconnectedHandler(mqttDisconnectedHandler); // reconnect mqtt server on disconnect

            await mqttClient.ConnectAsync(this.mqttClientOptions);

            socket.OnMessage += MessageReceivedHandler();
            socket.OnError += ErrorHandler();
            socket.OnClose += ClosedHandler(socket);

            socket.OnOpen += OpenedHandler(socket);

            socket.Connect();

            Console.ReadLine();
            
            Console.WriteLine("Exiting of Huobi Feed");
        }

        #region MQTT publisher
        private void StartPublish(object callback)
        {
            while (true)
            {
                FeedMessage _out;
                if (HuobiFeedQueue.TryDequeue(out _out))
                {
                    string message = _out.message;
                    if (message.Contains("ping"))
                    {
                        SendPong(message).ConfigureAwait(false);
                    }
                    else if (message.Contains("error"))
                    {
                        Console.WriteLine($"Error: {message}");
                    }
                    else if (message.Contains("subbed"))
                    {
                        Console.WriteLine(message);
                    }
                    else
                    {
                        //Console.WriteLine(_out.message);
                        publishMessage(_out.message, _out.topic);
                    }
                };

            }
        }

        #endregion

        #region Event Handlers
        internal override EventHandler OpenedHandler(WebSocket socket)
        {
            return (sender, e) =>
            {
                Console.WriteLine("Connection open: {0}", domain);
                foreach (string _symbol in settings.huobiCurrencyList)
                {
                    Subscribe(socket, $"market.{_symbol.ToLower()}.depth.step1");
                }

            };
        }

        internal override EventHandler<MessageEventArgs> MessageReceivedHandler()
        {
            return (sender, e) =>
            {
                byte[] rawData = e.RawData;
                string message = DecompressData(rawData);
                HuobiFeedQueue.Enqueue(new FeedMessage(topic: settings.HuobiDataChannel, message: message));
            };
        }

        #endregion

        #region functions

        private static void Subscribe(WebSocket socket, string channel)
        {
            var toSend = new
            {
                sub = channel,
                id = "client1",
            };
            Console.WriteLine(toSend);
            string sendJson = JsonConvert.SerializeObject(toSend);
            socket.Send(sendJson);
        }

        public List<string> GetHuobiSymbols()
        {
            List<string> symbolList = new List<string> { };
            var request = new RestRequest("v1/common/symbols");
            var response = this.restClient.Get(request);
            SymbolData responseData = JsonConvert.DeserializeObject<SymbolData>(response.Content);
            List<SymbolData.SymbolInfo> symbolData = responseData.data;
            foreach (SymbolData.SymbolInfo _symbol in symbolData)
            {
                Console.WriteLine($"HUOBI: loaded contract------{_symbol.symbol}      {_symbol.state}");
                symbolList.Add(_symbol.symbol);

            }
            return symbolList;
        }


        public async Task SendPong(string pingMessage)
        {
            PingMessage ping = JsonConvert.DeserializeObject<PingMessage>(pingMessage);  // {"ping": 1492420473027}
            Dictionary<string, string> data = new Dictionary<string, string>() { { "pong", ping.ping.ToString() } };// { "pong": 1492420473027}
            this.socket.Send(JsonConvert.SerializeObject(data));
            return;
        }

        #endregion

        #region SymbolData class

        internal class SymbolData
        {
            [JsonProperty("status")]
            internal string status;
            [JsonProperty("data")]
            internal List<SymbolInfo> data;

            internal class SymbolInfo
            {
                [JsonProperty("symbol")]
                internal string symbol;
                [JsonProperty("state")]
                internal string state;
                [JsonProperty("base-currency")]
                internal string baseCurrency;
                [JsonProperty("quote-currency")]
                internal string quoteCurrency;
                [JsonProperty("price-precision")]
                internal int pricePrecision;
                [JsonProperty("amount-precision")]
                internal int amountPrecision;
                [JsonProperty("min-order-value")]
                internal double minOrderValue;
                [JsonProperty("min-order-amt")]
                internal double minOrderAmt;
                [JsonProperty("symbol-partition")]
                internal string symbolPartition;

            }
        }

        #endregion

        #region PingMessage class
        internal class PingMessage
        {
            [JsonProperty("ping")]
            internal long ping;
        }

        #endregion

    }
}
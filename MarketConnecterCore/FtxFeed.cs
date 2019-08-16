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
    public class FTXFeed:FeedBase
    {
        enum channelTypes { trades, orderbook, ticker};

        public new string domain = settings.FTXWSS;
        IRestClient restClient = new RestClient(settings.FTXRestURL);
        public static ConcurrentQueue<FeedMessage> FTXFeedQueue = new ConcurrentQueue<FeedMessage>();
        private WebSocket socket;

        public async Task Start()
        {
            //settings.FTXCurrencyList = GetFTXSymbols();

            socket = new WebSocket(domain);

            ThreadPool.QueueUserWorkItem(new WaitCallback(StartPublish));

            mqttClient.UseDisconnectedHandler(mqttDisconnectedHandler); // reconnect mqtt server on disconnect

            await mqttClient.ConnectAsync(this.mqttClientOptions);

            socket.OnError += ErrorHandler();
            socket.OnClose += ClosedHandler(socket);
            socket.OnOpen += OpenedHandler(socket);

            socket.OnMessage += MessageReceivedHandler();

            socket.Connect();

            foreach (string _symbol in settings.FTXCurrencyList)
            {
                Subscribe(socket, channel: channelTypes.trades.ToString(), symbol: _symbol).ConfigureAwait(false);
                Subscribe(socket, channel: channelTypes.ticker.ToString(), symbol: _symbol).ConfigureAwait(false);
                Subscribe(socket, channel: channelTypes.orderbook.ToString(), symbol: _symbol).ConfigureAwait(false);
            }

            Console.ReadLine();
            
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
        internal override EventHandler OpenedHandler(WebSocket socket)
        {
            return (sender, e) =>
            {
                Console.WriteLine("Connection open: {0}", domain);


            };
        }

        internal override EventHandler<MessageEventArgs> MessageReceivedHandler()
        {
            return (sender, e) =>
            {
                string message = e.Data;
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
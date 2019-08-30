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
using WebSocketSharp;
using HuobiLibrary.Model;

namespace MarketConnectorCore
{
    public class HuobiFeed: MarketConnecterCore.FeedBase
    {
        public new string domain = settings.HuobiWSS;
        IRestClient restClient = new RestClient(settings.HuobiRestURL);
        public static ConcurrentQueue<FeedMessage> HuobiFeedQueue = new ConcurrentQueue<FeedMessage>();
        private WebSocket socket;

        public async Task Start()
        {
            //settings.huobiCurrencyList = GetHuobiSymbols();

            socket = new WebSocket(domain);

            ThreadPool.QueueUserWorkItem(new WaitCallback(StartPublish));

            mqttClient.UseDisconnectedHandler(mqttDisconnectedHandler); // reconnect mqtt server on disconnect

            await mqttClient.ConnectAsync(this.mqttClientOptions);

            socket.OnMessage += MessageReceivedHandler();
            socket.OnError += ErrorHandler(socket);
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
                foreach (string _symbol in settings.huobiCurrencyList)
                {
                    Subscribe(socket, $"market.{_symbol.ToLower()}.depth.step1").ConfigureAwait(false);
                }
                Console.WriteLine("Connection open: {0}", domain);
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

        private async Task Subscribe(WebSocket socket, string channel)
        {
            var toSend = new
            {
                sub = channel,
                id = "client1",
            };
            string sendJson = JsonConvert.SerializeObject(toSend);
            socket.Send(sendJson);
        }

        public List<string> GetHuobiSymbols()
        {
            List<string> symbolList = new List<string> { };
            var request = new RestRequest("v1/common/symbols");
            var response = this.restClient.Get(request);
            HuobiLibrary.Model.SymbolData responseData = JsonConvert.DeserializeObject<HuobiLibrary.Model.SymbolData>(response.Content);
            List<HuobiLibrary.Model.SymbolData.SymbolInfo> symbolData = responseData.data;
            foreach (HuobiLibrary.Model.SymbolData.SymbolInfo _symbol in symbolData)
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


    }
}
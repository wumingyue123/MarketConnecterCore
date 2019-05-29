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

        public async Task Start()
        {
            List<string> symbolList = new List<string>();

            foreach (string _currency in settings.deribitCurrencyList)
            {
                symbolList.AddRange(GetSymbols(_currency));
            }
            
            bool mqttconnected = mqttClient.ConnectAsync(this.mqttClientOptions).IsCompleted;

            using (var socket = new WebSocket(domain))
            {
                socket.OnMessage += (sender, e) =>
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
                            publishMessage(message: rawdata, topic: "marketdata/deribitdata").ConfigureAwait(false);

                        }
                    }
                    catch
                    {
                    }

                };
                socket.OnError += (sender, e) => Console.WriteLine(e.Message);
                socket.OnClose += (sender, e) =>
                {
                    Console.WriteLine(e.Reason);
                    socket.Connect();
                };

                socket.OnOpen += (sender, e) =>
                {
                    Console.WriteLine("Connection open: {0}", domain);
                    List<string> channels = (from symbol in symbolList select $"quote.{symbol}").ToList();
                    channels.Add("BTC-PERPETUAL");
                    channels.Add("ETH-PERPETUAL");
                    Subscribe(socket, channels);
                    SetHeartbeat(socket);
                };

                socket.Connect();

                Console.ReadLine();
            }
            Console.WriteLine("Exiting of program");
        }


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

        public List<string> GetSymbols(string currency)
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
                Console.WriteLine($"contract: {result.instrument_name}  strike: {result.strike}  type: {result.option_type}");
                symbolList.Add(result.instrument_name);
            }

            Console.WriteLine($"retrieved {jsonResponse.SymbolResult.Count} options symbols");
            Console.WriteLine($"loaded {symbolList.Count} options symbols");

            return symbolList;
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
using CommandLine;
using MarketConnectorCore;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MarketConnecterCore
{
    class Program
    {

        public class Options
        {
            [Option('v', "verbose", Required = false, HelpText = "Set output to verbose messages.")]
            public bool Verbose { get; set; }
            [Option('i', "host", Required = false, HelpText = "Set host ip address.", Default ="192.168.1.182")]
            public string Host { get; set; }
            [Option('p', "port", Required = false, HelpText = "Set host port address.", Default =1883)]
            public int Port { get; set; }
        }

        static void Main(string[] args)
        {

            CommandLine.Parser.Default.ParseArguments<Options>(args)
                .WithParsed<Options>(options =>
                {
                    settings.IPADDR = options.Host;
                    settings.PORT = options.Port;
                }
                ).WithNotParsed<Options>((errs) => { });

            Console.WriteLine($"Using MQTT host address {settings.IPADDR}:{settings.PORT}");

            BitmexFeed bitmexFeed = new BitmexFeed();
            DeribitFeed deribitFeed = new DeribitFeed();
            //HuobiFeed huobiFeed = new HuobiFeed();
            //FTXFeed FTXFeed = new FTXFeed();

            Task.Run(()=>bitmexFeed.Start());
            Task.Run(()=>deribitFeed.Start());
            //huobiFeed.Start().ConfigureAwait(false);
            //FTXFeed.Start();

            Console.ReadLine();
        }
    }
}

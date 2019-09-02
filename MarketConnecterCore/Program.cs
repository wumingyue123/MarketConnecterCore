using CommandLine;
using MarketConnectorCore;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MarketConnecterCore
{
    class Program
    {
        static void Main(string[] args)
        {

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

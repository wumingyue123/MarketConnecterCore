using MarketConnectorCore;
using System;
using System.Threading;

namespace MarketConnecterCore
{
    class Program
    {
        static void Main(string[] args)
        {
            BitmexFeed bitmexFeed = new BitmexFeed();
            //DeribitFeed deribitFeed = new DeribitFeed();

            bitmexFeed.Start().ConfigureAwait(false);
            //deribitFeed.Start().ConfigureAwait(false);

            Console.ReadLine();
        }
    }
}

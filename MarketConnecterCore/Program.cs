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
            DeribitFeed deribitFeed = new DeribitFeed();
            HuobiFeed huobiFeed = new HuobiFeed();
            FTXFeed FTXFeed = new FTXFeed();

            bitmexFeed.Start().ConfigureAwait(false);
            deribitFeed.Start().ConfigureAwait(false);
            huobiFeed.Start().ConfigureAwait(false);
            FTXFeed.Start().ConfigureAwait(false);

            Console.ReadLine();
        }
    }
}

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
            ThreadPool.QueueUserWorkItem(new WaitCallback(bitmexFeed.Start));

            //DeribitFeed deribitFeed = new DeribitFeed();
            //Task.Run(()=>deribitFeed.Start());

            Console.ReadLine();
        }
    }
}

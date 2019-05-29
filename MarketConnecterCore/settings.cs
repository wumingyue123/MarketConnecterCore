using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace MarketConnecterCore
{
    class settings
    {
        // mqtt server settings
        public const string IPADDR = "192.168.1.182";
        public const int PORT = 13000;
        public static List<string> currencyList = new List<string> { "BTC", "ETH" };

    }
}

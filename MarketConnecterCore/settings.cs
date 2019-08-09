using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace MarketConnecterCore
{
    class settings
    {
        // mqtt server settings
        public const string IPADDR = "localhost";
        public const int PORT = 13000;
        public const string BitmexDataChannel = "marketdata/bitmexdata";
        public const string DeribitDataChannel = "marketdata/deribitdata";
        public const string HuobiDataChannel = "marketdata/huobidata";
        public static List<string> deribitCurrencyList = new List<string> { "BTC", "ETH" };
        public static List<string> bitmexCurrencyList = new List<string> { "XBTUSD", "ETHUSD" };
        public static List<string> huobiCurrencyList = new List<string> { "FTTUSDT", "FTTBTC", "FTTHT" };

    }
}

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

        #region Bitmex settings
        public const string BitmexWSS = "wss://www.bitmex.com/realtime";
        public const string BitmexRestURL = "https://www.bitmex.com/api/v1/";
        public const string BitmexDataChannel = "marketdata/bitmexdata";
        public static List<string> bitmexCurrencyList = new List<string> { "XBTUSD", "ETHUSD" };
        public const string BITMEX_API_KEY = "8YFN7m1nciXgxJru9TCALc-A";
        public const string BITMEX_API_SECRET = "UBwm38Beoa_rXaNcnznJvoSVDfLKSS9S40YayqZOza_O0Q1Y";
        #endregion

        #region Deribit settings
        public const string DeribitWSS = "wss://www.deribit.com/ws/api/v2";
        public const string DeribitRESTURL = "https://www.deribit.com/api/v2/";
        public const string DeribitDataChannel = "marketdata/deribitdata";
        public static List<string> deribitCurrencyList = new List<string> { "BTC", "ETH" };
        #endregion

        #region Huobi settings
        public const string HuobiWSS = "wss://api.huobi.pro/ws";
        public const string HuobiRestURL = "https://api.huobi.pro/";
        public const string HuobiDataChannel = "marketdata/huobidata";
        public static List<string> huobiCurrencyList = new List<string> { "FTTUSDT", "FTTBTC", "FTTHT" };
        public const string HUOBI_API_KEY = "hrf5gdfghe-3e9cb982-3f334417-1adcb";
        public const string HUOBI_API_SECRET = "31dbf87d-9933b39a-57e2a001-32809";
        #endregion
    }
}

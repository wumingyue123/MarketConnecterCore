using Microsoft.SqlServer.Server;
using MQTTnet.Internal;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Collections.Generic;

namespace MarketConnectorCore
{
    class SymbolData
    {
        [JsonProperty("result")]
        public List<SymbolResult> SymbolResult { get; set; }

    }

    class SymbolResult
    {
        [JsonProperty("tick_size")]
        public float tick_size { get; set; }
        [JsonProperty("strike")]
        public float strike { get; set; }
        [JsonProperty("settlement_period")]
        public string settlement_period { get; set; }
        [JsonProperty("quote_currency")]

        public string quote_currency { get; set; }
        [JsonProperty("option_type")]

        public string option_type { get; set; }
        [JsonProperty("min_trade_amount")]

        public float min_trade_amount { get; set; }
        [JsonProperty("kind")]

        public string kind { get; set; }
        [JsonProperty("is_active")]

        public bool is_active { get; set; }
        [JsonProperty("instrument_name")]

        public string instrument_name { get; set; }
        [JsonProperty("expiration_timestamp")]

        public double expiration_timestamp { get; set; }
        [JsonProperty("creation_timestamp")]

        public double creation_timestamp { get; set; }
        [JsonProperty("contract_size")]

        public float contract_size { get; set; }
        [JsonProperty("base_currency")]

        public string base_currency { get; set; }
    }

}

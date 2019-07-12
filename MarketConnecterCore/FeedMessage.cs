using System;
using System.Collections.Generic;
using System.Text;

namespace MarketConnecterCore
{
    public class FeedMessage
    {
        string topic;
        string message;

        public FeedMessage(string topic, string message)
        {
            this.topic = topic;
            this.message = message;
        }
    }
}

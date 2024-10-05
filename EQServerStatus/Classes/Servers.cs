using System;
using System.Collections.Generic;

namespace EQServerStatus.Classes
{
    public class Servers
    {
        public string ServerName { get; set; }
        public int LastUpdated { get; set; }
        public List<ServerDataPoints> ServerHistoryData { get; set; }

        public Servers()
        {
            this.ServerHistoryData = new List<ServerDataPoints>();
        }
    }
}

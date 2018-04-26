using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BlockchainObserver.Currencies
{
    public class LitecoinAdapter : BitcoinBasedCurrencyAdapter
    {
        public LitecoinAdapter(string IP, int port, string rpcUser, string rpcPassword) : base(IP, port, rpcUser, rpcPassword)
        {
        }
    }
}

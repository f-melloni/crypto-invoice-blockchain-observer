using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BlockchainObserver.Currencies
{
    public class BitcoinAdapter : BitcoinBasedCurrencyAdapter
    {
        public BitcoinAdapter(string IP, int port, string rpcUser, string rpcPassword, string currencyCode) : base(IP, port, rpcUser, rpcPassword, currencyCode)
        {
        }
    }
}

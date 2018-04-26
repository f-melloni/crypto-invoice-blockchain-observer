using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BlockchainObserver.Currencies
{
    public class DogecoinAdapter : BitcoinBasedCurrencyAdapter
    {
        public DogecoinAdapter(string IP, int port, string rpcUser, string rpcPassword) : base(IP, port, rpcUser, rpcPassword)
        {
        }
    }
}

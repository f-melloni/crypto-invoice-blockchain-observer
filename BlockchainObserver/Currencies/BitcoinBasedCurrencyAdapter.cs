using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BlockchainObserver.Currencies
{
    public class BitcoinBasedCurrencyAdapter : CurrencyAdapter
    {
        public BitcoinBasedCurrencyAdapter(string IP, int port, string rpcUser, string rpcPassword) : base(IP, port, rpcUser, rpcPassword)
        {

        }

        public override int? TransactionConfirmations(string address)
        {
            JArray addressesInfoArray = (JArray)client.Invoke("listreceivedbyaddress");
            if (addressesInfoArray.HasValues)
            {
                JToken queriedWallet = addressesInfoArray.SingleOrDefault(a => a["address"].ToString() == address);
                if (queriedWallet == null)
                {
                    return null;
                }

                return queriedWallet["confirmations"].ToObject<int>();
            }
            else
            {
                return null;
            }
        }
    }
}

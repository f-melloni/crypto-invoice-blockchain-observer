using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlockchainObserver.Utils;
using Newtonsoft.Json.Linq;

namespace BlockchainObserver.Currencies
{
    public abstract class CurrencyAdapter : ICurrencyAdapter
    {
        // RPC Node Configuration
        public string rpcIP { get; set; }
        public int rpcPort { get; set; }
        public string rpcUser { get; set; }
        public string rpcPassword { get; set; }

        // RPC Client
        public JsonRpcClient client { get; set; }

        public static Dictionary<string, Type> Types = new Dictionary<string, Type>
        {
            { "LTC",  typeof(LitecoinAdapter) },
            { "BTC",  typeof(BitcoinAdapter)  },
            { "DOGE", typeof(DogecoinAdapter) },
        };

        public CurrencyAdapter(string IP, int port, string user, string password, string path = "", string rpcVersion = "1.0")
        {
            rpcIP = IP;
            rpcPort = port;
            rpcUser = user;
            rpcPassword = password;

            client = new JsonRpcClient(rpcVersion);
            client.Url = $"http://{rpcIP}:{rpcPort}" + (!string.IsNullOrEmpty(path) ? $"/{path}" : "");
            client.UserName = rpcUser;
            client.Password = rpcPassword;
        }

        public abstract int? TransactionConfirmations(JToken tx);
        public abstract int? TransactionConfirmations(string txHash);
        public abstract void ImportAddress(string address);
        public abstract JArray GetLastTransactions();
    }
}

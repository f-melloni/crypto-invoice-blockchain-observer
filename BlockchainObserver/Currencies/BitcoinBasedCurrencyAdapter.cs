using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlockchainObserver.Database;
using BlockchainObserver.Database.Entities;
using BlockchainObserver.Utils;
using Newtonsoft.Json.Linq;

namespace BlockchainObserver.Currencies
{
    public class BitcoinBasedCurrencyAdapter : CurrencyAdapter
    {
        protected string CurrencyCode;
        private static List<string> seenInMemPool = new List<string>();

        public BitcoinBasedCurrencyAdapter(string IP, int port, string rpcUser, string rpcPassword, string currencyCode) : base(IP, port, rpcUser, rpcPassword)
        {
            CurrencyCode = currencyCode;
        }

        public override int? TransactionConfirmations(JToken tx)
        {
            return tx["confirmations"] != null ? (int?)tx["confirmations"] : 0;
        }

        public override int? TransactionConfirmations(string txHash)
        {
            object[] p = new object[] {
                txHash,
                true
            };

            JObject txInfo = (JObject)client.Invoke("getrawtransaction", p);

            if (txInfo.HasValues && txInfo["confirmations"] != null) {
                return txInfo["confirmations"].ToObject<int>();
            }
            else {
                return null;
            }
        }

        public override void ImportAddress(string address)
        {
            object[] p = new object[] {
                address,
                "",
                false
            };

            var result = client.Invoke("importaddress", p);
        }

        public override JArray GetLastTransactions()
        {
            JObject result = new JObject();
            using (DBEntities db = new DBEntities(Observer.dbContextOptions.Options)) {
                var lastBlock = db.BlockCaches.SingleOrDefault(b => b.Currency == CurrencyCode);
                string lastBlockHash = "";
                if(lastBlock == null) {
                    string bestBlockHash = ((JValue)client.Invoke("getbestblockhash")).Value<string>();
                    JToken bestBlock = (JToken)client.Invoke("getblock", new object[] { bestBlockHash });
                    lastBlockHash = (string)bestBlock["previousblockhash"];
                }
                else {
                    lastBlockHash = lastBlock.LastSeenBlock;
                }


                
                object[] p = new object[] {
                    lastBlockHash,
                    1,
                    true
                };

                result = (JObject)client.Invoke("listsinceblock", p);

                if(lastBlock == null) {
                    lastBlock = new BlockCache() {
                        Currency = CurrencyCode
                    };
                    db.BlockCaches.Add(lastBlock);
                }
                lastBlock.LastSeenBlock = (string)result["lastblock"];
                db.SaveChanges();

                return (JArray)result["transactions"];
            }
        }
    }
}

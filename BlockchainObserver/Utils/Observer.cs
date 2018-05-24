using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Threading;
using Microsoft.Extensions.Configuration;
using BlockchainObserver.Currencies;
using Microsoft.EntityFrameworkCore;
using BlockchainObserver.Database.Entities;
using BlockchainObserver.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SharpRaven;
using SharpRaven.Data;
using NBitcoin;

namespace BlockchainObserver.Utils
{
    public static class Observer
    {
        private static List<string> Addresses = new List<string>();
        private static Dictionary<string, string> SeenAddresses = new Dictionary<string, string>();
        private static ICurrencyAdapter _currency;
        private static string CurrencyName;
        private static string HostName;
        private static string RpcUserName;
        private static string RpcPassword;
        private static int Port;
        private static int Interval;
        private static int RequiredConfirmations;

        public static DbContextOptionsBuilder<DBEntities> dbContextOptions = new DbContextOptionsBuilder<DBEntities>();

        /// <summary>
        /// Gets the address parameter from the RabbitMQ message
        /// </summary>
        /// <param name="message">JsonRPC consumer message</param>
        private static void AddAddress(JToken message)
        {
            using (DBEntities db = new DBEntities(dbContextOptions.Options)) {
                if (message["params"] is JObject) {
                    Addresses.Add(message["params"].Value<string>());
                    _currency.ImportAddress(message["params"].Value<string>());

                    var addressCache = new AddressCache() {
                        Address = message["params"].Value<string>(),
                        Currency = CurrencyName
                    };
                    db.Addresses.Add(addressCache);
                }
                else if (message["params"] is JArray) {
                    foreach (var address in (JArray)message["params"]) {
                        Addresses.Add(address.ToString());
                        _currency.ImportAddress(address.ToString());

                        var addressCache = new AddressCache() {
                            Address = address.ToString(),
                            Currency = CurrencyName
                        };
                        db.Addresses.Add(addressCache);
                    }
                }
                db.SaveChanges();
            }
        }

        /// <summary>
        /// Route the JsonRpc method
        /// </summary>
        /// <param name="message"></param>
        public static void ParseMessage(JToken message)
        {
            string method = message["method"].ToString().ToLower();
            switch (method)
            {
                case "WachAddress":
                    AddAddress(message);
                    break;
                case "GetNewAddress":
                    var paramaters = (JObject)message["params"];
                    int InvoiceID = paramaters.GetValue("InvoiceID").ToObject<int>();
                    string XPUB = paramaters.GetValue("XPUB").ToObject<string>();

                    OnGetNewAddress(InvoiceID,XPUB);
                    break;
            }
        }
        private static void OnGetNewAddress(int InvoiceID, string XPUB)
        {
            var pubKey = ExtPubKey.Parse(XPUB);
            //get last index for bitcoin addresses
            var last_index = 0;
            var newAddress = "";
            using (DBEntities dbe = new DBEntities()) {
                var lastInd = dbe.LastAddressIndex.SingleOrDefault(x => x.Currency == CurrencyName);

                if (dbe.LastAddressIndex.Any(l => l.Currency == CurrencyName)){
                    last_index = lastInd.Index;
                    var newAddressGenerated = pubKey.Derive(0).Derive((uint)last_index).PubKey.GetSegwitAddress(Network.Main);
                    newAddress = newAddressGenerated.ToString();

                }
                else
                {
                    LastAddressIndex lai = new LastAddressIndex() { Currency = CurrencyName, Index = 1 };
                    dbe.LastAddressIndex.Add(lai);
                    var newAddressGenerated = pubKey.Derive(0).Derive((uint)lai.Index).PubKey.GetSegwitAddress(Network.Main);
                    newAddress = newAddressGenerated.ToString();

                }
                lastInd.Index += 1;
                dbe.LastAddressIndex.Update(lastInd);
                dbe.SaveChanges();

            }
            
            RabbitMessenger.Send($@"{{""jsonrpc"": ""2.0"", ""method"": ""SetAddress"", ""params"": {{""InvoiceID"":{InvoiceID},""CurrencyCode"":""{CurrencyName}"",""Address"":""{newAddress}"" }}");
            _currency.ImportAddress(newAddress);

        }

        private static void OnPaymentSeen(string CurrencyCode, string Address, double Amount, string TXID)
        {
            SeenAddresses.Add(Address, TXID);
            RabbitMessenger.Send($@"{{""jsonrpc"": ""2.0"", ""method"": ""TransactionSeen"", ""params"": {{""CurrencyCode"":""{CurrencyCode}"",""Address"":""{Address}"",""Amount"":""{Amount}"",""TXID"":""{TXID}"" }}");
        }

        private static void OnPaymentConfirmed(string CurrencyCode, string Address, double Amount, string TXID)
        {
            Addresses.Remove(Address);
            SeenAddresses.Remove(Address);
            RabbitMessenger.Send($@"{{""jsonrpc"": ""2.0"", ""method"": ""TransactionConfirmed"", ""params"": {{""CurrencyCode"":""{CurrencyCode}"",""Address"":""{Address}"",""Amount"":""{Amount}"",""TXID"":""{TXID}"" }}");
        }

        public static void Setup(IConfiguration configuration)
        {
            CurrencyName = configuration["Observer:Currency"].ToUpper();
            HostName    = configuration["Observer:HostName"];
            Port        = Convert.ToInt16(configuration["Observer:Port"]);
            RpcUserName = configuration["Observer:RpcUserName"];
            RpcPassword = configuration["Observer:RpcPassword"];

            dbContextOptions.UseMySql(configuration.GetConnectionString("DefaultConnection"));
            DBEntities db = new DBEntities(dbContextOptions.Options);

            object[] args = { HostName, Port, RpcUserName, RpcPassword, CurrencyName };
            _currency = (ICurrencyAdapter)Activator.CreateInstance(CurrencyAdapter.Types[CurrencyName], args);

            Interval = Convert.ToInt16(configuration["Observer:Interval"]);
            RequiredConfirmations = Convert.ToInt16(configuration["Observer:Confirmations"]);

            // Load from cache
            Addresses = db.Addresses.Where(a => a.Currency == CurrencyName).Select(a => a.Address).ToList();

            Begin();
        }

        /// <summary>
        /// Start looping through addresses
        /// </summary>
        private static void Begin()
        {
            Thread observerThread = new Thread(Observe);
            observerThread.Name = "ObserverThread";
            observerThread.Start();
        }

        /// <summary>
        /// Main observing loop
        /// </summary>
        private static void Observe()
        {
            while (true)
            {
                try
                {
                    //Copy of address list (in case of the main list changes)
                    List<string> addresses = new List<string>(Addresses);
                    if (addresses.Count > 0)
                    {
                        JArray transactionList = _currency.GetLastTransactions();

                        if (transactionList.HasValues)
                        {
                            foreach (string address in addresses)
                            {
                                JToken tx = transactionList.FirstOrDefault(a => a["address"].ToString() == address);
                                if (tx != null)
                                {
                                    int? confirmations = _currency.TransactionConfirmations(tx);
                                    if (confirmations != null && !SeenAddresses.ContainsKey(address))
                                        OnPaymentSeen(CurrencyName,(string)tx["address"],tx["amount"].ToObject<double>(),(string)tx["TXID"]);
                                    if (confirmations >= RequiredConfirmations)
                                        OnPaymentConfirmed(CurrencyName, (string)tx["address"], tx["amount"].ToObject<double>(), (string)tx["TXID"]);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    RavenClient ravenClient = new RavenClient(@"http://150379555fca4cf3b1145013d8d740c7:e237b7c99d944bec8a053f81a31f97a3@185.59.209.146:38082/2");
                    ravenClient.Capture(new SentryEvent(ex));

                }

                Thread.Sleep(Interval);
            }
        }
    }
}

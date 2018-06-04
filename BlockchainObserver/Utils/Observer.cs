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
        private static RavenClient ravenClient;

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
                case "wachaddress":
                    AddAddress(message);
                    break;
                case "getnewaddress":
                    var paramaters = (JObject)message["params"];
                    int InvoiceID = paramaters.GetValue("InvoiceID").ToObject<int>();
                    string XPUB = paramaters.GetValue("XPUB").ToObject<string>();

                    OnGetNewAddress(InvoiceID,XPUB);
                    break;
            }
        }
        private static void OnGetNewAddress(int InvoiceID, string XPUB)
        {
            ExtPubKey pubKey = null;
            try {
                pubKey = ExtPubKey.Parse(XPUB);
            }
            catch(Exception e) {
                Exception ex = new Exception($"Invalid {CurrencyName} XPUB - {XPUB}", e);
                ravenClient.Capture(new SentryEvent(ex));
                return;
            }
            //get last index for bitcoin addresses
            Network network = NBitcoin.Altcoins.Litecoin.Instance.Mainnet;
            var newAddressGenerated = "";

            using (DBEntities dbe = new DBEntities()) {
                XpubAddressIndex newestXpubIndex = dbe.XpubAddressIndex.SingleOrDefault(x => x.Xpub == XPUB);

                if (newestXpubIndex == null)
                {
                    XpubAddressIndex xai = new XpubAddressIndex() { Xpub = XPUB, Index = 0 };
                    dbe.XpubAddressIndex.Add(xai);
                }
                else
                {
                    newestXpubIndex.Index += 1;
                    dbe.XpubAddressIndex.Update(newestXpubIndex);
                }
                dbe.SaveChanges();
                int lastInd = dbe.XpubAddressIndex.SingleOrDefault(x => x.Xpub == XPUB).Index;

                try {
                    if (CurrencyName == "LTC") {
                        newAddressGenerated = pubKey.Derive(0).Derive((uint)lastInd).PubKey.GetAddress(network).ToString();
                    }
                    else {
                        newAddressGenerated = pubKey.Derive(0).Derive((uint)lastInd).PubKey.GetSegwitAddress(Network.Main).ToString();
                    }
                }
                catch(Exception e) {
                    Exception ex = new Exception($"Unable to generate {CurrencyName} address from XPUB - {XPUB}", e);
                    ravenClient.Capture(new SentryEvent(ex));
                    throw e;
                }

                dbe.Addresses.Add(new AddressCache() { Address = newAddressGenerated, Currency = CurrencyName });
                Addresses.Add(newAddressGenerated);
                dbe.SaveChanges();
            }

            string message = $@"{{""jsonrpc"": ""2.0"", ""method"": ""SetAddress"", ""params"": {{""InvoiceID"":{InvoiceID},""CurrencyCode"":""{CurrencyName}"",""Address"":""{newAddressGenerated}"" }} }}";
            try {
                RabbitMessenger.Send(message);
            }
            catch(Exception e) {
                Exception ex = new Exception($"Unable to send message to RabbitMQ - {message}", e);
                ravenClient.Capture(new SentryEvent(ex));
                throw e;
            }
            try {
                _currency.ImportAddress(newAddressGenerated);
            }
            catch(Exception e) {
                Exception ex = new Exception($"Unable to import {CurrencyName} address - {newAddressGenerated}", e);
                ravenClient.Capture(new SentryEvent(ex));
                throw e;
            }
        }

        private static void OnPaymentSeen(string CurrencyCode, string Address, double Amount, string TXID)
        {
            SeenAddresses.Add(Address, TXID);
            RabbitMessenger.Send($@"{{""jsonrpc"": ""2.0"", ""method"": ""TransactionSeen"", ""params"": {{""CurrencyCode"":""{CurrencyCode}"",""Address"":""{Address}"",""Amount"":""{Amount}"",""TXID"":""{TXID}"" }} }}");
        }

        private static void OnPaymentConfirmed(string CurrencyCode, string Address, double Amount, string TXID)
        {
            using(DBEntities dbe = new DBEntities())
            {
                dbe.Addresses.Remove(dbe.Addresses.SingleOrDefault(a => a.Address == Address));
                dbe.SaveChanges();
            }
            Addresses.Remove(Address);
            SeenAddresses.Remove(Address);
            RabbitMessenger.Send($@"{{""jsonrpc"": ""2.0"", ""method"": ""TransactionConfirmed"", ""params"": {{""CurrencyCode"":""{CurrencyCode}"",""Address"":""{Address}"",""Amount"":""{Amount}"",""TXID"":""{TXID}"" }} }}");
        }

        public static void Setup(IConfiguration configuration)
        {
            CurrencyName = configuration["Observer:Currency"].ToUpper();
            HostName    = configuration["Observer:HostName"];
            Port        = Convert.ToInt16(configuration["Observer:Port"]);
            RpcUserName = configuration["Observer:RpcUserName"];
            RpcPassword = configuration["Observer:RpcPassword"];

            dbContextOptions.UseMySql(Startup.ConnectionString);
            DBEntities db = new DBEntities(dbContextOptions.Options);

            object[] args = { HostName, Port, RpcUserName, RpcPassword, CurrencyName };
            _currency = (ICurrencyAdapter)Activator.CreateInstance(CurrencyAdapter.Types[CurrencyName], args);

            Interval = Convert.ToInt32(configuration["Observer:Interval"]);
            RequiredConfirmations = Convert.ToInt16(configuration["Observer:Confirmations"]);

            string sentryUrl = configuration["SentryClientUrl"];
            ravenClient = new RavenClient(sentryUrl);

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
                                        OnPaymentSeen(CurrencyName,(string)tx["address"],tx["amount"].ToObject<double>(),(string)tx["txid"]);
                                    if (confirmations >= RequiredConfirmations)
                                        OnPaymentConfirmed(CurrencyName, (string)tx["address"], tx["amount"].ToObject<double>(), (string)tx["txid"]);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ravenClient.Capture(new SentryEvent(ex));
                }

                Thread.Sleep(Interval);
            }
        }
    }
}

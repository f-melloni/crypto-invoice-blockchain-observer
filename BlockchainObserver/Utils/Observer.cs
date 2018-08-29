using BlockchainObserver.Currencies;
using BlockchainObserver.Database;
using BlockchainObserver.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json.Linq;
using SharpRaven;
using SharpRaven.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text.RegularExpressions;

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
            Network network = Network.Main;
            switch(CurrencyName) {
                case "LTC": network = NBitcoin.Altcoins.Litecoin.Instance.Mainnet; break;
            }
                
            var newAddress = "";
            using (DBEntities dbe = new DBEntities()) 
            {
                XpubAddressIndex lastIndex = dbe.XpubAddressIndex.SingleOrDefault(x => x.Xpub == XPUB);

                if (lastIndex == null) {
                    lastIndex = new XpubAddressIndex() { Xpub = XPUB, Index = -1 };
                    dbe.XpubAddressIndex.Add(lastIndex);
                }
                
                try {
                    if(XPUB.StartsWith("ypub") || XPUB.StartsWith("Mtub")) {
                        newAddress = GetAddressFromYPUB(XPUB, (uint)lastIndex.Index + 1, network);
                    }
                    else {
                        newAddress = GetAddressFromXPUB(XPUB, (uint)lastIndex.Index + 1, network);
                    }
                }
                catch(Exception e) {
                    ravenClient.Capture(new SentryEvent(e));
                    return;
                }

                lastIndex.Index++;
                Addresses.Add(newAddress);

                dbe.Addresses.Add(new AddressCache() { Address = newAddress, Currency = CurrencyName });
                dbe.SaveChanges();
            }

            string message = $@"{{""jsonrpc"": ""2.0"", ""method"": ""SetAddress"", ""params"": {{""InvoiceID"":{InvoiceID},""CurrencyCode"":""{CurrencyName}"",""Address"":""{newAddress}"" }} }}";
            try {
                RabbitMessenger.Send(message);
            }
            catch(Exception e) {
                Exception ex = new Exception($"Unable to send message to RabbitMQ - {message}", e);
                ravenClient.Capture(new SentryEvent(ex));
                throw e;
            }
            try {
                _currency.ImportAddress(newAddress);
            }
            catch(Exception e) {
                Exception ex = new Exception($"Unable to import {CurrencyName} address - {newAddress}", e);
                ravenClient.Capture(new SentryEvent(ex));
                throw e;
            }
        }

        private static string GetAddressFromXPUB(string XPUB, uint index, Network network)
        {
            ExtPubKey pubKey = null;
            try {
                pubKey = ExtPubKey.Parse(XPUB);
            }
            catch(Exception e) {
                Exception ex = new Exception($"Invalid {CurrencyName} XPUB - {XPUB}", e);
                throw ex;
            }
            
            try {
                string address = pubKey.Derive(0).Derive(index).PubKey.GetScriptAddress(network).ToString();
                return address;
            }
            catch(Exception e) {
                Exception ex = new Exception($"Unable to generate {CurrencyName} address from XPUB - {XPUB}", e);
                throw ex;
            }
        }

        private static string GetAddressFromYPUB(string YPUB, uint index, Network network)
        {
            string BTC_YPUB = Regex.Replace(YPUB, @"^Mtub", "Ltub"); // Accept litecoin-prefixed format as normal YPUB
            
            DerivationStrategyBase ds = null;
            try {
                var parser = new DerivationSchemeParser(network);
                ds = parser.Parse(BTC_YPUB);
            }
            catch (Exception e) {
                Exception ex = new Exception($"Invalid {CurrencyName} YPUB - {BTC_YPUB}", e);
                throw ex;
            }

            try {
                string address = ds.Derive(new KeyPath(new uint[] { 0, index })).Redeem.GetScriptAddress(network).ToString();
                return address;
            }
            catch (Exception e) {
                Exception ex = new Exception($"Unable to generate {CurrencyName} address from YPUB - {BTC_YPUB}", e);
                throw ex;
            }
        }

        private static void OnPaymentSeen(string CurrencyCode, string Address, double Amount, string TXID, int Timestamp)

        {
            SeenAddresses.Add(Address, TXID);
            RabbitMessenger.Send($@"{{""jsonrpc"": ""2.0"", ""method"": ""TransactionSeen"", ""params"": {{""CurrencyCode"":""{CurrencyCode}"",""Address"":""{Address}"",""Amount"":""{Amount}"",""TXID"":""{TXID}"",""Time"":""{Timestamp}"" }} }}");
        }

        private static void OnPaymentConfirmed(string CurrencyCode, string Address, double Amount, string TXID, int Timestamp)
        {
            using(DBEntities dbe = new DBEntities())
            {
                dbe.Addresses.Remove(dbe.Addresses.SingleOrDefault(a => a.Address == Address));
                dbe.SaveChanges();
            }
            Addresses.Remove(Address);
            SeenAddresses.Remove(Address);
            RabbitMessenger.Send($@"{{""jsonrpc"": ""2.0"", ""method"": ""TransactionConfirmed"", ""params"": {{""CurrencyCode"":""{CurrencyCode}"",""Address"":""{Address}"",""Amount"":""{Amount}"",""TXID"":""{TXID}"",""Time"":""{Timestamp}"" }} }}");
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
                                        OnPaymentSeen(CurrencyName,(string)tx["address"],tx["amount"].ToObject<double>(),(string)tx["txid"],(int)tx["time"]);
                                    if (confirmations >= RequiredConfirmations)
                                        OnPaymentConfirmed(CurrencyName, (string)tx["address"], tx["amount"].ToObject<double>(), (string)tx["txid"],(int)tx["time"]);
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

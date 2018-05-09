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
                case "watchaddress":
                    AddAddress(message);
                break;
            }
        }

        private static void OnPaymentSeen(string address, string txHash)
        {
            SeenAddresses.Add(address, txHash);
            RabbitMessenger.Send($"{{\"jsonrpc\": \"2.0\", \"method\": \"PaymentSeen\", \"params\": [{address}]}}");
        }

        private static void OnPaymentConfirmed(string address)
        {
            Addresses.Remove(address);
            SeenAddresses.Remove(address);
            RabbitMessenger.Send($"{{\"jsonrpc\": \"2.0\", \"method\": \"PaymentConfirmed\", \"params\": [{address}]}}");
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
                //Copy of address list (in case of the main list changes)
                List<string> addresses = new List<string>(Addresses);
                if (addresses.Count > 0) {
                    JArray transactionList = _currency.GetLastTransactions();

                    if (transactionList.HasValues) {
                        foreach (string address in addresses) {
                            JToken tx = transactionList.FirstOrDefault(a => a["address"].ToString() == address);
                            if (tx != null) {
                                int? confirmations = _currency.TransactionConfirmations(tx);
                                if (confirmations != null && !SeenAddresses.ContainsKey(address))
                                    OnPaymentSeen(address, (string)tx["06bddf6fc95915adad123cb93c310f533efbba55d8bb8759cc426fdf7ad0ec4c"]);
                                if (confirmations >= RequiredConfirmations)
                                    OnPaymentConfirmed(address);
                            }
                        }
                    }
                }

                Thread.Sleep(Interval);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Threading;
using Microsoft.Extensions.Configuration;
using BlockchainObserver.Currencies;

namespace BlockchainObserver.Utils
{
    public static class Observer
    {
        private static List<string> Addresses = new List<string>();
        private static List<string> SeenAddresses = new List<string>();
        private static ICurrencyAdapter _currency;
        private static string HostName;
        private static string RpcUserName;
        private static string RpcPassword;
        private static int Port;
        private static int Interval;
        private static int RequiredConfirmations;

        /// <summary>
        /// Gets the address parameter from the RabbitMQ message
        /// </summary>
        /// <param name="message">JsonRPC consumer message</param>
        private static void AddAddress(JToken message)
        {
            if(message["params"] is JObject)
                Addresses.Add(message["params"].Value<string>());
            else if(message["params"] is JArray)
            {
                foreach(var address in (JArray)message["params"])
                {
                    Addresses.Add(address.ToString());
                }
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

        private static void OnPaymentSeen(string address)
        {
            SeenAddresses.Add(address);
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
            string currencyName = configuration["Observer:Currency"].ToUpper();
            HostName    = configuration["Observer:HostName"];
            Port        = Convert.ToInt16(configuration["Observer:Port"]);
            RpcUserName = configuration["Observer:RpcUserName"];
            RpcPassword = configuration["Observer:RpcPassword"];

            object[] args = { HostName, Port, RpcUserName, RpcPassword };
            _currency = (ICurrencyAdapter)Activator.CreateInstance(CurrencyAdapter.Types[currencyName], args);

            Interval = Convert.ToInt16(configuration["Observer:Interval"]);
            RequiredConfirmations = Convert.ToInt16(configuration["Observer:Confirmations"]);
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

                foreach (string address in addresses)
                {
                    int? confirmations = _currency.TransactionConfirmations(address);
                    if (confirmations != null && !SeenAddresses.Contains(address))
                        OnPaymentSeen(address);
                    if (confirmations >= RequiredConfirmations)
                        OnPaymentConfirmed(address);
                }

                Thread.Sleep(Interval);
            }
        }
    }
}

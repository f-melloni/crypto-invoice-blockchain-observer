using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BlockchainObserver.Currencies
{
    public interface ICurrencyAdapter
    {
        /// <summary>
        /// Get number of confirmations of given address
        /// </summary>
        /// <returns>null if transaction was not found</returns>
        int? TransactionConfirmations(string address);
    }
}

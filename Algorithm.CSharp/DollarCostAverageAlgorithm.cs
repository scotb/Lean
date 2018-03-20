using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuantConnect.Data;

namespace QuantConnect.Algorithm.CSharp
{
    public class DollarCostAverageAlgorithm : QCAlgorithm
    {
        private string[] symbols = { "SPY" };
        private bool firstRun = true;
        private DateTime lastPurchaseDate;
        private decimal cashAllotment = 1500m; // buy $1500 of stock every time period.

        public override void Initialize()
        {
            SetStartDate(2009, 01, 01);  //Set Start Date
            SetEndDate(2014, 12, 31);    //Set End Date
            SetCash(15000);             //Set Strategy Cash

            foreach (var symbol in symbols)
            {
                var security = AddSecurity(SecurityType.Equity, symbol, Resolution.Daily);
            }
            SetBenchmark("SPY");
        }

        public override void OnData(Slice slice)
        {
            if (firstRun || slice.Time > lastPurchaseDate.AddDays(14))
            {
                Portfolio.AddUnsettledCashAmount(new Securities.UnsettledCashAmount(slice.Time, "USD", cashAllotment));
                foreach (var symbol in slice)
                {
                    
                    var qty = Math.Floor(cashAllotment  / symbol.Value.Price);
                    Order(symbol.Key, qty);
                }
                lastPurchaseDate = slice.Time;
                firstRun = false;
            }
        }
    }
}

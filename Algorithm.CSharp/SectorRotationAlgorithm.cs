using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuantConnect.Data;
using QuantConnect.Indicators;

namespace QuantConnect.Algorithm.CSharp
{
    public class SectorRotationAlgorithm : QCAlgorithm
    {
        private string[] symbols = { "XLF", "XLRE", "XLE", "XLU", "XLK", "XLB", "XLP", "XLY", "XLI", "XLV" };
        private Dictionary<string, RelativeStrengthIndex> rsi = new Dictionary<string, RelativeStrengthIndex>();
        private int rsiPeriod = 14;
        private Resolution resolution = Resolution.Daily;
        private int rsiBuyTrigger = 20;
        private int rsiBuyCross = 30;
        private int rsiSellTrigger = 80;
        private int rsiSellCross = 70;
        private decimal cashHoldback = .05m;

        public override void Initialize()
        {
            SetStartDate(2005, 01, 01);  //Set Start Date
            SetEndDate(2014, 12, 31);    //Set End Date
            SetCash(100000);             //Set Strategy Cash

            foreach (var symbol in symbols)
            {
                var security = AddSecurity(SecurityType.Equity, symbol, resolution);
                rsi.Add(symbol, RSI(security.Symbol, rsiPeriod));
            }
            SetBenchmark("SPY");
        }

        private bool sellSignal = false;
        private bool buySignal = false;
        public override void OnData(Slice slice)
        {
            foreach (var symbol in slice)
            {
                if (!rsi[symbol.Key.ID.Symbol].IsReady)
                {
                    continue;
                } 
                Plot("RSI", rsi[symbol.Key.Value]);
                if (buySignal && rsi[symbol.Key.Value].Current > rsiBuyCross)
                {
                    if (Portfolio[symbol.Key].Quantity == 0)
                    {
                        var quantity = Math.Floor((Portfolio.Cash * (1 - cashHoldback) / symbols.Length) / symbol.Value.Value);
                        Order(symbol.Key, quantity);
                        buySignal = false;
                        sellSignal = false;
                        Debug($"Purchased {quantity} Shares of {symbol.Key} @ ${symbol.Value.Value}");
                        continue;
                    }
                }
                else if (sellSignal && rsi[symbol.Key.Value].Current < rsiSellCross)
                {
                    if (Portfolio[symbol.Key].Quantity > 0)
                    {
                        Debug($"Selling {Portfolio[symbol.Key].Quantity} Shares of {symbol.Key} @ ${symbol.Value.Value}");
                        Sell(symbol.Key, Portfolio[symbol.Key].Quantity);
                        buySignal = false;
                        sellSignal = false;
                        continue;
                    }
                }
                if (rsi[symbol.Key.Value].Current < rsiBuyTrigger)
                {
                    buySignal = true;
                    sellSignal = false;
                }
                else if (rsi[symbol.Key.Value].Current > rsiSellTrigger)
                {
                    buySignal = false;
                    sellSignal = true;
                }
            }
        }
    }
}

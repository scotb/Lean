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
        private string[] symbols = { "SPY" };
        private Dictionary<string, RelativeStrengthIndex> rsi = new Dictionary<string, RelativeStrengthIndex>();

        public override void Initialize()
        {
            SetStartDate(2010, 01, 01);  //Set Start Date
            SetEndDate(2015, 01, 01);    //Set End Date
            SetCash(100000);             //Set Strategy Cash

            foreach (var symbol in symbols)
            {
                var security = AddSecurity(SecurityType.Equity, symbol, Resolution.Daily);
                rsi.Add(symbol, RSI(security.Symbol, 9));
            }
        }

        private bool sellSignal = false;
        private bool buySignal = false;
        public override void OnData(Slice slice)
        {
            foreach (var symbol in slice)
            {
                if (!rsi[symbol.Key.Value].IsReady)
                {
                    continue;
                } 
                Plot("RSI", rsi[symbol.Key.Value]);
                if (buySignal && rsi[symbol.Key.Value].Current > 30m)
                {
                    if (Portfolio[symbol.Key].Quantity == 0)
                    {
                        var quantity = Math.Floor((Portfolio.Cash * 0.95m) / symbol.Value.Value);
                        Order(symbol.Key, quantity);
                        buySignal = false;
                        sellSignal = false;
                        Debug($"Purchased {quantity} Shares of {symbol.Key}");
                        continue;
                    }
                }
                else if (sellSignal && rsi[symbol.Key.Value].Current < 70m)
                {
                    if (Portfolio[symbol.Key].Quantity > 0)
                    {
                        Debug($"Selling {Portfolio[symbol.Key].Quantity} Shares of {symbol.Key}");
                        Sell(symbol.Key, Portfolio[symbol.Key].Quantity);
                        buySignal = false;
                        sellSignal = false;
                        continue;
                    }
                }
                if (rsi[symbol.Key.Value].Current < 25m)
                {
                    buySignal = true;
                    sellSignal = false;
                }
                else if (rsi[symbol.Key.Value].Current > 75m)
                {
                    buySignal = false;
                    sellSignal = true;
                }
            }
        }
    }
}

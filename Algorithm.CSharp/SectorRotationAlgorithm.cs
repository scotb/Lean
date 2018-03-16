using QuantConnect.Data;
using QuantConnect.Indicators;
using System;
using System.Collections.Generic;

namespace QuantConnect.Algorithm.CSharp
{
    public class SectorRotationAlgorithm : QCAlgorithm
    {
        //private string[] symbols = { "XLF", "XLRE", "XLE", "XLU", "XLK", "XLB", "XLP", "XLY", "XLI", "XLV" };
        private string[] symbols = { "SPY" };
        private Dictionary<string, RelativeStrengthIndex> rsi = new Dictionary<string, RelativeStrengthIndex>();
        private Dictionary<string, SimpleMovingAverage> smaTrigger = new Dictionary<string, SimpleMovingAverage>();
        private Dictionary<string, decimal> previousSMA = new Dictionary<string, decimal>();
        private int rsiPeriod = 14;
        private Resolution resolution = Resolution.Daily;
        private int rsiBuyTrigger = 20;
        private int rsiBuyCross = 30;
        private int rsiSellTrigger = 80;
        private int rsiSellCross = 70;
        private int smaTriggerPeriod = 50;
        private decimal cashHoldback = .05m;
        private int maxPositions = 4;

        public override void Initialize()
        {
            SetStartDate(2005, 01, 01);  //Set Start Date
            SetEndDate(2014, 12, 31);    //Set End Date
            SetCash(100000);             //Set Strategy Cash

            foreach (var symbol in symbols)
            {
                var security = AddSecurity(SecurityType.Equity, symbol, resolution);
                rsi.Add(symbol, RSI(security.Symbol, rsiPeriod));
                smaTrigger.Add(symbol, SMA(security.Symbol, smaTriggerPeriod));
                previousSMA.Add(symbol, 0);
            }
            SetBenchmark("SPY");
        }

        private bool sellSignal = false;
        private bool buySignal = false;
        public override void OnData(Slice slice)
        {
            foreach (var symbol in slice)
            {
                if (symbol.Value == null || 
                    !rsi[symbol.Key.ID.Symbol].IsReady || 
                    !smaTrigger[symbol.Key.ID.Symbol].IsReady)
                {
                    continue;
                } 
                Plot("RSI", rsi[symbol.Key.Value]);
                Plot($"SMA{smaTriggerPeriod}", smaTrigger[symbol.Key.Value]);
                var slope = smaTrigger[symbol.Key.Value].Current.Price - previousSMA[symbol.Key.Value];
                if (buySignal && 
                    //Portfolio.Count < maxPositions && 
                    rsi[symbol.Key.Value].Current > rsiBuyCross && 
                    symbol.Value.Price > smaTrigger[symbol.Key.Value].Current && 
                    slope > 0)
                {
                    if (Portfolio[symbol.Key].Quantity == 0)
                    {
                        var quantity = Math.Floor((Portfolio.Cash * (1 - cashHoldback) / symbols.Length) / symbol.Value.Value);
                        Order(symbol.Key, quantity);
                        buySignal = false;
                        sellSignal = false;
                        Debug($"Purchased {quantity} Shares of {symbol.Key} @ ${symbol.Value.Value}");
                    }
                }
                else if (sellSignal && 
                         rsi[symbol.Key.Value].Current < rsiSellCross && 
                         slope < 0)
                {
                    if (Portfolio[symbol.Key].Quantity > 0)
                    {
                        Debug($"Selling {Portfolio[symbol.Key].Quantity} Shares of {symbol.Key} @ ${symbol.Value.Value}");
                        Sell(symbol.Key, Portfolio[symbol.Key].Quantity);
                        buySignal = false;
                        sellSignal = false;
                    }
                }
                previousSMA[symbol.Key.Value] = smaTrigger[symbol.Key.Value].Current.Price;
                if (rsi[symbol.Key.Value].Current < rsiBuyTrigger && 
                    symbol.Value.Price < smaTrigger[symbol.Key.Value].Current.Price)
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

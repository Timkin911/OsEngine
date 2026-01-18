using OsEngine.Charts.CandleChart.Indicators;
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.Market;
using OsEngine.Market.Servers;
using OsEngine.Market.Servers.TraderNet.Entity;
using OsEngine.Market.Servers.YahooFinance.Entity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using static Google.Api.LabelDescriptor.Types;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

/*Discription
Trading robot for osengine

Trend robot at the intersection of two exponential averages

Buy: the fast Ema is higher than the slow Ema and the value of the last candle is greater than the fast Ema.

Sell: The fast Ema is lower than the slow Ema and the value of the last candle is less than the slow Ema.

Exit: stop and profit in % of the entry price.
*/

namespace OsEngine.Robots._MyBots
{
    [Bot("MyTrendTwoEma")] //We create an attribute so that we don't write anything in the Boot factory
    public class MyTrendTwoEma : BotPanel
    {
        BotTabSimple _tab;

        // Basic Settings
        private StrategyParameterString Regime;
        private StrategyParameterDecimal Volume;
        private StrategyParameterString VolumeType;
        private StrategyParameterDecimal Slippage;
        private StrategyParameterTimeOfDay TimeStart;
        private StrategyParameterTimeOfDay TimeEnd;
        private StrategyParameterString TradeAssetInPortfolio;

        // Indicator
        private Aindicator _ema1;
        private Aindicator _ema2;
        private Aindicator _ema3;

        // Indicator setting
        private StrategyParameterInt _periodEmaFast;
        private StrategyParameterInt _periodEmaSlow;
        private StrategyParameterInt _periodEmaThird;



        // The last value of the indicators and price
        private decimal _lastEmaFast;
        private decimal _lastEmaSlow;
        private decimal _lastEmaThird;

        // The prev last value indicators
        private decimal _prevLastEmaFast;
        private decimal _prevLastEmaSlow;
        private decimal _prevLastEmaThird;

        // Steps
        private bool _firstStep2Long = false;
        private bool _firstStep2Short = false;

        // Exit
        private StrategyParameterDecimal StopValue;
        private StrategyParameterDecimal ProfitValue;

        public MyTrendTwoEma(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];

            // Basic Settings
            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" }, "Base");
            VolumeType = CreateParameter("Volume type", "Number of contracts", new[] { "Number of contracts", "Contract currency", "Deposit percent" }, "Base");
            Volume = CreateParameter("Volume", 10, 1.0m, 50, 4, "Base");
            Slippage = CreateParameter("Slippage %", 0m, 0, 20, 1, "Base");
            TimeStart = CreateParameterTimeOfDay("Start Trade Time", 0, 0, 0, 0, "Base");
            TimeEnd = CreateParameterTimeOfDay("End Trade Time", 24, 0, 0, 0, "Base");
            TradeAssetInPortfolio = CreateParameter("tradeAsset ", "Prime", "Base");

            // Indicator Settings
            _periodEmaFast = CreateParameter("fast EMA1 period", 100, 100, 500, 50, "Indicator");
            _periodEmaSlow = CreateParameter("slow EMA2 period", 1000, 500, 2000, 100, "Indicator");
            _periodEmaThird = CreateParameter("third EMA3 period", 100, 10, 100, 10, "Indicator");

            // Creating indicator Ema1
            _ema1 = IndicatorsFactory.CreateIndicatorByName(nameClass: "Ema", name: name + "EMA1", canDelete: false);
            _ema1 = (Aindicator)_tab.CreateCandleIndicator(_ema1, nameArea: "Prime");
            ((IndicatorParameterInt)_ema1.Parameters[0]).ValueInt = _periodEmaFast.ValueInt;
            _ema1.ParametersDigit[0].Value = _periodEmaFast.ValueInt;
            _ema1.DataSeries[0].Color = Color.Red;
            _ema1.Save();

            // Creating indicator Ema2
            _ema2 = IndicatorsFactory.CreateIndicatorByName(nameClass: "Ema", name: name + "Ema2", canDelete: false);
            _ema2 = (Aindicator)_tab.CreateCandleIndicator(_ema2, nameArea: "Prime");
            _ema2.ParametersDigit[0].Value = _periodEmaSlow.ValueInt;
            _ema2.DataSeries[0].Color = Color.Green;
            _ema2.Save();

            // Creating indicator Ema3
            _ema3 = IndicatorsFactory.CreateIndicatorByName(nameClass: "Ema", name: name + "Ema3", canDelete: false);
            _ema3 = (Aindicator)_tab.CreateCandleIndicator(_ema3, nameArea: "Prime");
            _ema3.ParametersDigit[0].Value = _periodEmaThird.ValueInt;
            _ema3.DataSeries[0].Color = Color.DarkBlue;
            _ema3.Save();

            // Subscribe to the indicator update event
            ParametrsChangeByUser += IntersectionOfTwoEma_ParametrsChangeByUser;

            // Subscribe to the candle completion event
            _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;

            // Exit
            StopValue = CreateParameter("Stop percent", 0.5m, 1, 10, 1, "Exit settings");
            ProfitValue = CreateParameter("Profit percent", 0.5m, 1, 10, 1, "Exit settings");

            Description = "Trend robot at the intersection of two exponential averages " +
                "Buy: the fast Ema is higher than the slow Ema and the value of the last candle is greater than the fast Ema. " +
                "Sell: The fast Ema is lower than the slow Ema and the value of the last candle is less than the slow Ema. " +
                "Exit: stop and profit in % of the entry price.";

        }

        // Indicator Update event
        private void IntersectionOfTwoEma_ParametrsChangeByUser()
        {
            ((IndicatorParameterInt)_ema1.Parameters[0]).ValueInt = _periodEmaFast.ValueInt;
            _ema1.Save();
            _ema1.Reload();
            ((IndicatorParameterInt)_ema2.Parameters[0]).ValueInt = _periodEmaSlow.ValueInt;
            _ema2.Save();
            _ema2.Reload();
            ((IndicatorParameterInt)_ema3.Parameters[0]).ValueInt = _periodEmaThird.ValueInt;
            _ema3.Save();
            _ema3.Reload();
        }

        // The name of the robot in OsEngine
        public override string GetNameStrategyType()
        {
            return "MyTrendTwoEma";
        }

        public override void ShowIndividualSettingsDialog()
        {

        }

        // Candle Finished Event
        private void _tab_CandleFinishedEvent(List<Candle> candles)
        {
            // If the robot is turned off, exit the event handler
            if (Regime.ValueString == "Off")
            {
                return;
            }

            // If there are not enough candles to build an indicator, we exit
            if (candles.Count < _periodEmaFast.ValueInt || candles.Count < _periodEmaSlow.ValueInt || candles.Count < _periodEmaThird.ValueInt)
            {
                return;
            }

            // If the time does not match, we leave
            if (TimeStart.Value > _tab.TimeServerCurrent ||
                TimeEnd.Value < _tab.TimeServerCurrent)
            {
                return;
            }

            LogicOpenPositionP2(candles);


        }

        // Opening logic
        private void LogicOpenPosition(List<Candle> candles)
        {
            List<Position> openPositions = _tab.PositionsOpenAll;
            decimal lastPrice = candles[candles.Count - 1].Close;


            // Long
            // The prevlast, last value of the indicators
            _lastEmaFast = _ema1.DataSeries[0].Last;
            _prevLastEmaFast = _ema1.DataSeries[0].Values[_ema1.DataSeries[0].Values.Count - 2];

            _lastEmaSlow = _ema2.DataSeries[0].Last;
            _prevLastEmaSlow = _ema2.DataSeries[0].Values[_ema2.DataSeries[0].Values.Count - 2];

            _lastEmaThird = _ema3.DataSeries[0].Last;
            _prevLastEmaThird = _ema3.DataSeries[0].Values[_ema3.DataSeries[0].Values.Count - 2];

            if (_lastEmaFast > _prevLastEmaFast && _lastEmaFast > _lastEmaSlow && _prevLastEmaFast <= _prevLastEmaSlow)
            {
                if (openPositions != null && openPositions.Count != 0)
                {
                    for (int i = 0; openPositions != null && i < openPositions.Count; i++)
                    {
                        Position pos = openPositions[i];
                        if (pos.State != PositionStateType.Open)
                        {
                            continue;
                        }

                        if (pos.Direction == Side.Sell) // If the direction of the position is sell
                        {
                            _tab.CloseAtMarket(pos, pos.OpenVolume);
                            _tab.BuyAtMarket(GetVolume(_tab));
                        }
                    }
                }
                else
                {
                    _tab.BuyAtMarket(GetVolume(_tab));
                }
            }


            // Short


            if (_lastEmaFast < _prevLastEmaFast && _lastEmaFast < _lastEmaSlow && _prevLastEmaFast >= _prevLastEmaSlow)
            {
                if (openPositions != null || openPositions.Count != 0)
                {
                    for (int i = 0; openPositions != null && i < openPositions.Count; i++)
                    {
                        Position pos = openPositions[i];
                        if (pos.State != PositionStateType.Open)
                        {
                            continue;
                        }

                        if (pos.Direction == Side.Buy) // If the direction of the position is sell
                        {
                            _tab.CloseAtMarket(pos, pos.OpenVolume);
                            _tab.SellAtMarket(GetVolume(_tab));
                        }
                    }
                }
                else
                {
                    _tab.SellAtMarket(GetVolume(_tab));
                }

            }

        }

        // Opening logic
        private void LogicOpenPositionP1(List<Candle> candles)
        {

            _lastEmaFast = _ema1.DataSeries[0].Last;
            _prevLastEmaFast = _ema1.DataSeries[0].Values[_ema1.DataSeries[0].Values.Count - 2];

            _lastEmaSlow = _ema2.DataSeries[0].Last;
            _prevLastEmaSlow = _ema2.DataSeries[0].Values[_ema2.DataSeries[0].Values.Count - 2];

            List<Position> openPositions = _tab.PositionsOpenAll;
            decimal lastPrice = candles[candles.Count - 1].Close;

            if (openPositions != null && openPositions.Count != 0)
            {
                for (int i = 0; openPositions != null && i < openPositions.Count; i++)
                {
                    Position pos = openPositions[i];
                    if (pos.State != PositionStateType.Open)
                    {
                        continue;
                    }

                    // Long
                    if (pos.Direction == Side.Sell)
                    {
                        if (_lastEmaFast > _lastEmaSlow)
                        {
                            _tab.CloseAtMarket(pos, pos.OpenVolume);
                            _tab.BuyAtMarket(GetVolume(_tab));
                        }
                    }

                    // Short
                    else if (pos.Direction == Side.Buy)
                    {
                        if (_lastEmaFast < _lastEmaSlow)
                        {
                            _tab.CloseAtMarket(pos, pos.OpenVolume);
                            _tab.SellAtMarket(GetVolume(_tab));
                        }
                    }
                }
            }
            else
            {
                // Long
                if (_lastEmaFast > _prevLastEmaFast && _lastEmaFast > _lastEmaSlow && _prevLastEmaFast <= _prevLastEmaSlow)
                {
                    _tab.BuyAtMarket(GetVolume(_tab));
                }

                // Short
                else if (_lastEmaFast < _prevLastEmaFast && _lastEmaFast < _lastEmaSlow && _prevLastEmaFast >= _prevLastEmaSlow)
                {
                    _tab.SellAtMarket(GetVolume(_tab));
                }
            }
        }

        // Opening logic
        private void LogicOpenPositionP2(List<Candle> candles)
        {

            _lastEmaFast = _ema1.DataSeries[0].Last;
            _prevLastEmaFast = _ema1.DataSeries[0].Values[_ema1.DataSeries[0].Values.Count - 2];

            _lastEmaSlow = _ema2.DataSeries[0].Last;
            _prevLastEmaSlow = _ema2.DataSeries[0].Values[_ema2.DataSeries[0].Values.Count - 2];

            _lastEmaThird = _ema3.DataSeries[0].Last;
            _prevLastEmaThird = _ema3.DataSeries[0].Values[_ema3.DataSeries[0].Values.Count - 2];



            List<Position> openPositions = _tab.PositionsOpenAll;
            decimal lastPrice = candles[candles.Count - 1].Close;

            if (openPositions != null && openPositions.Count != 0)
            {
                for (int i = 0; openPositions != null && i < openPositions.Count; i++)
                {
                    Position pos = openPositions[i];
                    if (pos.State != PositionStateType.Open)
                    {
                        continue;
                    }

                    // Long
                    if (pos.Direction == Side.Sell)
                    {
                        if (_lastEmaFast > _lastEmaSlow && _firstStep2Long == false)
                        {
                            _firstStep2Long = true;
                        }
                        if (_firstStep2Long == true && candles[candles.Count - 1].IsDown == true && lastPrice < _lastEmaThird)
                        {
                            _tab.CloseAtMarket(pos, pos.OpenVolume);
                            _tab.BuyAtMarket(GetVolume(_tab));
                            _firstStep2Long = false;
                        }

                    }

                    // Short
                    else if (pos.Direction == Side.Buy)
                    {
                        if (_lastEmaFast < _lastEmaSlow && _firstStep2Short == false)
                        {
                            _firstStep2Short = true;
                        }

                        if (_firstStep2Short == true && candles[candles.Count - 1].IsUp == true && lastPrice > _lastEmaThird)
                        {
                            _tab.CloseAtMarket(pos, pos.OpenVolume);
                            _tab.SellAtMarket(GetVolume(_tab));
                            _firstStep2Short = false;
                        }
                    }
                }
            }
            else
            {
                // First step to Long
                if (_lastEmaFast > _prevLastEmaFast && _lastEmaFast > _lastEmaSlow && _prevLastEmaFast <= _prevLastEmaSlow && _firstStep2Long == false)
                {
                    _firstStep2Long = true;
                }

                // First step to Short
                else if (_lastEmaFast < _prevLastEmaFast && _lastEmaFast < _lastEmaSlow && _prevLastEmaFast >= _prevLastEmaSlow && _firstStep2Long == false)
                {
                    _firstStep2Short = true;
                }

                // Long
                if (candles[candles.Count - 1].IsDown == true && lastPrice < _lastEmaThird && _firstStep2Long == true)
                {
                    _tab.BuyAtMarket(GetVolume(_tab));
                    _firstStep2Long = false;
                }

                // Short
                if (candles[candles.Count - 1].IsUp == true && lastPrice > _lastEmaThird && _firstStep2Short == true)
                {
                    _tab.SellAtMarket(GetVolume(_tab));
                    _firstStep2Short = false;
                }
            }
        }

        // Method for calculating the volume of entry into a position
        private decimal GetVolume(BotTabSimple tab)
        {
            decimal volume = 0;

            if (VolumeType.ValueString == "Number of contracts")
            {
                volume = Volume.ValueDecimal;
            }
            else if (VolumeType.ValueString == "Contract currency")
            {
                decimal contractPrice = tab.PriceBestAsk;
                volume = Volume.ValueDecimal / contractPrice;

                if (StartProgram == StartProgram.IsOsTrader)
                {
                    IServerPermission serverPermission = ServerMaster.GetServerPermission(tab.Connector.ServerType);

                    if (serverPermission != null &&
                        serverPermission.IsUseLotToCalculateProfit &&
                    tab.Security.Lot != 0 &&
                        tab.Security.Lot > 1)
                    {
                        volume = Volume.ValueDecimal / (contractPrice * tab.Security.Lot);
                    }

                    volume = Math.Round(volume, tab.Security.DecimalsVolume);
                }
                else // Tester or Optimizer
                {
                    volume = Math.Round(volume, 6);
                }
            }
            else if (VolumeType.ValueString == "Deposit percent")
            {
                Portfolio myPortfolio = tab.Portfolio;

                if (myPortfolio == null)
                {
                    return 0;
                }

                decimal portfolioPrimeAsset = 0;

                if (TradeAssetInPortfolio.ValueString == "Prime")
                {
                    portfolioPrimeAsset = myPortfolio.ValueCurrent;
                }
                else
                {
                    List<PositionOnBoard> positionOnBoard = myPortfolio.GetPositionOnBoard();

                    if (positionOnBoard == null)
                    {
                        return 0;
                    }

                    for (int i = 0; i < positionOnBoard.Count; i++)
                    {
                        if (positionOnBoard[i].SecurityNameCode == TradeAssetInPortfolio.ValueString)
                        {
                            portfolioPrimeAsset = positionOnBoard[i].ValueCurrent;
                            break;
                        }
                    }
                }

                if (portfolioPrimeAsset == 0)
                {
                    SendNewLogMessage("Can`t found portfolio " + TradeAssetInPortfolio.ValueString, Logging.LogMessageType.Error);
                    return 0;
                }

                decimal moneyOnPosition = portfolioPrimeAsset * (Volume.ValueDecimal / 100);

                decimal qty = moneyOnPosition / tab.PriceBestAsk / tab.Security.Lot;

                if (tab.StartProgram == StartProgram.IsOsTrader)
                {
                    qty = Math.Round(qty, tab.Security.DecimalsVolume);
                }
                else
                {
                    qty = Math.Round(qty, 0);
                }

                return qty;
            }

            return volume;
        }
    }
}



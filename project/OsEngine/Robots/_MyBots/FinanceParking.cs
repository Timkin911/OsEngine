using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.Market.Servers.MoexFixFastSpot.FIX;
using OsEngine.Market.Servers.Transaq.TransaqEntity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OsEngine.Robots._MyBots
{
    [Bot("FinanceParking")]
    public class FinanceParking : BotPanel
    {

        BotTabSimple _tabToTrade;
        StrategyParameterString _regime;
        StrategyParameterTimeOfDay _startToParkTime;
        StrategyParameterDecimal _parkStartValue;
        StrategyParameterDecimal _parkTargetValue;
        StrategyParameterDecimal _brokerComission;
        StrategyParameterString _lastTimeCheckFinance;
        StrategyParameterString _tradeAssetInPortfolio;

        StrategyParameterString OrdersType;
        public StrategyParameterString IcebergType;
        public StrategyParameterInt IcebergSecondsBetweenOrders;
        public StrategyParameterInt IcebergCount;
        public StrategyParameterInt IcebergPercent;

        public FinanceParking(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _tabToTrade = TabsSimple[0];

            _regime = CreateParameter("Regime", "Off", new[] { "Off", "On" }, "Main Regime");
            _startToParkTime = CreateParameterTimeOfDay("Start to park", 18, 34, 00, 00, "Main Regime");
            _parkStartValue = CreateParameter("Park start value", 100.0m, 0, 0, 0.10m, "Main Regime");
            _parkTargetValue = CreateParameter("Park target value", 100.0m, 0, 0, 0.10m, "Main Regime");
            _brokerComission = CreateParameter("Broker comission (percent)", 0.0m, 0, 0, 0.10m, "Main Regime");
            _lastTimeCheckFinance = CreateParameter("Last time check finance ", "", "Main Regime");
            _tradeAssetInPortfolio = CreateParameter("tradeAsset ", "Prime", "Main Regime");
       
            OrdersType = CreateParameter("Orders type ", "Market", new[] { "Market", "Iceberg Market" }, "Main Regime");
            IcebergType = CreateParameter("Iceberg type ", "Evenly", new[] { "Evenly", "Percent" }, "Main Regime");
            IcebergCount = CreateParameter("Iceberg count ", 3, 2, 5, 1, "Main Regime");
            IcebergSecondsBetweenOrders = CreateParameter("Iceberg seconds between orders ", 5, 1, 50, 4, "Main Regime");
            IcebergPercent = CreateParameter("Iceberg percent ", 80, 50, 100, 5, "Main Regime");

            StrategyParameterButton button = CreateParameterButton("Park manual", "Main Regime");
            button.UserClickOnButtonEvent += Button_UserClickOnButtonEvent;


            _tabToTrade.ServerTimeChangeEvent += _tabToTrade_ServerTimeChangeEvent;

        }

        private void _tabToTrade_ServerTimeChangeEvent(DateTime obj)
        {
            DateTime vDt = DateTime.Now.AddDays(-1);
            if (_lastTimeCheckFinance.ValueString != "")
            {
                vDt = Convert.ToDateTime(_lastTimeCheckFinance.ValueString);
            }

            if (obj.Date > vDt.Date && obj.TimeOfDay >= _startToParkTime.TimeSpan)
            {
                //Логика праковщика
                if (_regime.ValueString == "Off")
                {
                    return;
                }
                SendNewLogMessage("Start FinParking", Logging.LogMessageType.Error);
                ParkingLogic();
            }
        }

        private void Button_UserClickOnButtonEvent()
        {
            ParkingLogic();

        }

        private void ParkingLogic()
        {
            // Логика парковщика

            #region Проверки
            if (_tabToTrade.IsReadyToTrade == false)
            {
                _tabToTrade.SetNewLogMessage("Connection not ready to trade", Logging.LogMessageType.System);
                return;
            }

            bool isTradingActive = IsTradingActive(_tabToTrade);
            if (isTradingActive == false)
            {
                _tabToTrade.SetNewLogMessage("There are currently no trades going on", Logging.LogMessageType.System);
                return;
            }

            Portfolio myPortfolio = _tabToTrade.Portfolio;
            if (myPortfolio == null)
            {
                return;
            }
            #endregion

            decimal freeMoney = 0;
            string logMessage = "";
            freeMoney = GetFreeMoney(_tabToTrade);

            if ((Math.Abs(freeMoney) > _parkStartValue.ValueDecimal && freeMoney > 0) || freeMoney < 0)
            {
                decimal parkVolume = 0;
                parkVolume = GetVolumeL(_tabToTrade, freeMoney);

                try
                {
                    List<Position> posesAll = _tabToTrade.PositionsOpenAll;

                    if (freeMoney > 0)
                    {
                        if (posesAll.Count == 0)
                        {
                            _tabToTrade.BuyAtMarket(parkVolume);

                        }
                        else
                        {
                            Position posBuy = posesAll[0];
                            _tabToTrade.BuyAtMarketToPosition(posBuy, parkVolume);
                        }

                        logMessage = "Park Buy: FreeMoney " + freeMoney + " Volume " + parkVolume + " Price " + _tabToTrade.PriceBestBid + " Sum " + parkVolume * _tabToTrade.PriceBestBid;
                    }
                    else if (freeMoney <= 0 && posesAll.Count != 0)
                    {

                        Position posSell = posesAll[0];

                        if (posSell.OpenVolume < parkVolume)
                        {
                            parkVolume = posSell.OpenVolume;
                        }
                        _tabToTrade.CloseAtMarket(posSell, parkVolume);

                        logMessage = "Park Sell: FreeMoney " + freeMoney + " Volume " + parkVolume + " Price " + _tabToTrade.PriceBestBid + " Sum " + parkVolume * _tabToTrade.PriceBestBid;
                    }

                    SendNewLogMessage(logMessage, Logging.LogMessageType.Error);
                }
                catch (Exception error)
                {
                    SendNewLogMessage(error.ToString(), Logging.LogMessageType.Error);
                }
            }
            _lastTimeCheckFinance.ValueString = Convert.ToString(DateTime.Now);
        }

        public decimal GetVolumeL(BotTabSimple tab, decimal portfolioCurrentValue)
        {
            decimal contractPrice = 0;

            if (tab.StartProgram == StartProgram.IsOsTrader)
            {
                if (portfolioCurrentValue > 0)
                {
                    contractPrice = tab.PriceBestAsk;
                    return Math.Round((portfolioCurrentValue - _parkTargetValue.ValueDecimal) / ((1 + _brokerComission.ValueDecimal / 100) * contractPrice));
                }
                else
                {
                    portfolioCurrentValue = Math.Abs(portfolioCurrentValue);
                    contractPrice = tab.PriceBestBid;
                    return Math.Ceiling((portfolioCurrentValue + _parkTargetValue.ValueDecimal) / ((1 - _brokerComission.ValueDecimal / 100) * contractPrice));
                }

            }
            else if (tab.StartProgram == StartProgram.IsTester)
            {
                return 0;
            }
            else
            {
                return 0;
            }
        }

        public decimal GetFreeMoney(BotTabSimple tab)
        {
            if (_tradeAssetInPortfolio.ValueString == "Prime")
            {
                return tab.Portfolio.ValueCurrent;
            }
            else
            {
                List<PositionOnBoard> positionOnBoard = tab.Portfolio.GetPositionOnBoard();
                if (positionOnBoard == null)
                {
                    return 0;
                }

                for (int i = 0; i < positionOnBoard.Count; i++)
                {
                    if (positionOnBoard[i].SecurityNameCode == _tradeAssetInPortfolio.ValueString)
                    {
                        return positionOnBoard[i].ValueCurrent;
                    }
                }
            }
            return 0;
        }

        public bool IsTradingActive(BotTabSimple tab)
        {
            // Проверяем, что таб существует и подключен
            if (tab == null || !tab.IsConnected)
            {
                return false;
            }

            // Получаем последний стакан
            MarketDepth depth = tab.MarketDepth;

            if (depth == null)
            {
                return false;
            }

            // Проверяем, что в стакане есть данные (есть bid и ask)
            if (depth.Bids == null || depth.Bids.Count == 0 ||
                depth.Asks == null || depth.Asks.Count == 0)
            {
                return false;
            }

            // Проверяем, что цены в стакане актуальные (не нулевые)
            if (depth.Bids[0].Price == 0 || depth.Asks[0].Price == 0)
            {
                return false;
            }

            // Если все проверки пройдены - торги идут
            return true;
        }


        public class IcebergMaker
        {
            public int OrdersCount;

            public int SecondsBetweenOrders;

            public decimal VolumeOnAllOrders;

            public int ModuleNum;

            public BotTabSimple Tab;

            public Side Side;

            public Position PositionToClose;

            public void Start()
            {
                if (PositionToClose == null)
                {
                    Thread worker = new Thread(OpenPositionMethod);
                    worker.Start();
                }
                else
                {
                    Thread worker = new Thread(ClosePositionMethod);
                    worker.Start();
                }
            }

            private void OpenPositionMethod()
            {
                try
                {
                    if (OrdersCount < 1)
                    {
                        OrdersCount = 1;
                    }

                    List<decimal> volumes = new List<decimal>();

                    decimal allVolumeInArray = 0;


                    //if ( OrdersType == "Market")
                    //{ 
                    //
                    //}
                    
                    
                    for (int i = 0; i < OrdersCount; i++)
                    {
                        decimal curVolume = VolumeOnAllOrders / OrdersCount;
                        curVolume = Math.Round(curVolume, Tab.Security.DecimalsVolume);
                        allVolumeInArray += curVolume;
                        volumes.Add(curVolume);
                    }

                    if (allVolumeInArray != VolumeOnAllOrders)
                    {
                        decimal residue = VolumeOnAllOrders - allVolumeInArray;

                        volumes[0] = Math.Round(volumes[0] + residue, Tab.Security.DecimalsVolume);
                    }



                    for (int i = 0; i < volumes.Count; i++)
                    {
                        if (Side == Side.Buy)
                        {
                            if (Tab.PositionsOpenAll.Count == 0)
                            {
                                Tab.BuyAtMarket(volumes[i], ModuleNum.ToString());
                            }
                            else
                            {
                                Tab.BuyAtMarketToPosition(Tab.PositionsOpenAll[0], volumes[i]);
                            }
                        }
                        if (Side == Side.Sell)
                        {
                            if (Tab.PositionsOpenAll.Count == 0)
                            {
                                Tab.SellAtMarket(volumes[i], ModuleNum.ToString());
                            }
                            else
                            {
                                Tab.SellAtMarketToPosition(Tab.PositionsOpenAll[0], volumes[i]);
                            }
                        }
                        Thread.Sleep(SecondsBetweenOrders * 1000);
                    }
                }
                catch (Exception error)
                {
                    Tab.SetNewLogMessage(error.ToString(), LogMessageType.Error);
                }
            }

            private void ClosePositionMethod()
            {
                try
                {
                    int iterationCount = 0;

                    if (OrdersCount < 1)
                    {
                        OrdersCount = 1;
                    }

                    VolumeOnAllOrders = PositionToClose.OpenVolume;

                    List<decimal> volumes = new List<decimal>();

                    decimal allVolumeInArray = 0;



                    for (int i = 0; i < OrdersCount; i++)
                    {
                        decimal curVolume = VolumeOnAllOrders / OrdersCount;
                        curVolume = Math.Round(curVolume, Tab.Security.DecimalsVolume);
                        allVolumeInArray += curVolume;
                        volumes.Add(curVolume);
                    }

                    if (allVolumeInArray != VolumeOnAllOrders)
                    {
                        decimal residue = VolumeOnAllOrders - allVolumeInArray;

                        volumes[0] = Math.Round(volumes[0] + residue, Tab.Security.DecimalsVolume);
                    }

                    for (int i = 0; i < volumes.Count; i++)
                    {
                        Tab.CloseAtMarket(PositionToClose, volumes[i]);

                        Thread.Sleep(SecondsBetweenOrders * 1000);
                    }
                }
                catch (Exception error)
                {
                    Tab.SetNewLogMessage(error.ToString(), LogMessageType.Error);
                }
            }
        }
       
        public override string GetNameStrategyType()
        {
            return "FinanceParking";
        }

        public override void ShowIndividualSettingsDialog()
        {

        }
    }
}
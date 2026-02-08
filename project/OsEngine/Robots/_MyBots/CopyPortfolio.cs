/* 
 Версия 1.3
 */


using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.Market;
using OsEngine.Market.Connectors;
using OsEngine.Market.Servers;
using OsEngine.Market.Servers.GateIo.GateIoFutures.Entities.Response;
using OsEngine.Market.Servers.MFD;
using OsEngine.Market.Servers.MoexFixFastSpot.FIX;
using OsEngine.Market.Servers.Transaq.TransaqEntity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Documents;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;


namespace OsEngine.Robots
{
    [Bot("CopyPortfolio")]
    public class CopyPortfolio : BotPanel
    {
        BotTabScreener _tabToTrade1;
        BotTabSimple _tabToTrade2;
        StrategyParameterString _regime;
        StrategyParameterString _onlyInfo;
        StrategyParameterString _changeMoneyFund;
        StrategyParameterString _tradeAssetInPortfolio;
        StrategyParameterString _moneyFundInPortfolio;
        StrategyParameterDecimal _koeff;
        StrategyParameterTimeOfDay _startToWork;
        StrategyParameterTimeOfDay _endToWork;
        StrategyParameterInt _workInterval;
        StrategyParameterString _lastTimeCheckFinance;
        StrategyParameterString _repMoneyFund;
        StrategyParameterString _repMoneyFundNew;
        StrategyParameterDecimal _repMoneyFundKoeff;

        #region Классы MirrorPosition и MirrorPortfolio
        public class MirrorPosition
        {
            public string SecurityNameCode { get; set; }
            public decimal SecurityPrice { get; set; }
            public decimal SecurityValue { get; set; }
            public decimal PoseCurrentValue { get; set; }
            public decimal PoseTargetValue { get; set; }
            public decimal Percent { get; set; }
            public Position Pose { get; set; }
            public BotTabSimple Tab { get; set; }
            public string OriginalMoneyFundNameCode { get; set; }
            public decimal RepMoneyFundKoeff { get; set; }
            public decimal OriginalMoneyFundValue { get; set; }


            public MirrorPosition()
            {
                SecurityNameCode = string.Empty;
                SecurityPrice = 0m;
                SecurityValue = 0m;
                PoseCurrentValue = 0;
                PoseTargetValue = 0;
                Percent = 0m;
                Pose = null;
                Tab = null;
                OriginalMoneyFundNameCode = string.Empty;
                RepMoneyFundKoeff = 1m;
                OriginalMoneyFundValue = 0m;
            }

            public MirrorPosition(string securityNameCode, decimal securityPrice, decimal securityValue, decimal poseCurrentValue = 0, decimal poseTargetValue = 0, decimal percent = 0, BotTabSimple tab = null, Position pose = null, string originalMoneyFundNameCode = "", decimal repMoneyFundKoeff = 1m, decimal originalMoneyFundValue = 0m)
            {
                SecurityNameCode = securityNameCode;
                SecurityPrice = securityPrice;
                SecurityValue = securityValue;
                PoseCurrentValue = poseCurrentValue;
                PoseTargetValue = poseTargetValue;
                Percent = percent;
                Tab = tab;
                Pose = pose;
                OriginalMoneyFundNameCode = originalMoneyFundNameCode;
                RepMoneyFundKoeff = repMoneyFundKoeff;
                OriginalMoneyFundValue = originalMoneyFundValue;
            }

        }

        public class MirrorPortfolio
        {
            public List<MirrorPosition> MirrorPositionsList { get; set; }
            public MirrorPosition myTradeAsset { get; set; }
            public MirrorPosition myMoneyFund { get; set; }
            public decimal Price { get; set; }

            public MirrorPortfolio()
            {
                MirrorPositionsList = new List<MirrorPosition>();
                myTradeAsset = new MirrorPosition();
                myMoneyFund = new MirrorPosition();
                Price = 0m;
            }

            public void myTradeAssetEdit(string securityNameCode, decimal securityPrice, decimal securityValue)
            {
                myTradeAsset.SecurityNameCode = securityNameCode;
                myTradeAsset.SecurityPrice = securityPrice;
                myTradeAsset.SecurityValue = securityValue;
                PercentCalculation();
            }

            public void myMoneyFundEdit(string securityNameCode, decimal securityPrice, decimal securityValue, decimal poseCurrentValue = 0, decimal poseTargetValue = 0, BotTabSimple tab = null, Position pose = null, string originalMoneyFundNameCode = "", decimal repMoneyFundKoeff = 1m, decimal originalMoneyFundValue = 0m)
            {
                myMoneyFund.SecurityNameCode = securityNameCode;
                myMoneyFund.SecurityPrice = securityPrice;
                myMoneyFund.SecurityValue = securityValue;
                myMoneyFund.PoseCurrentValue = poseCurrentValue;
                myMoneyFund.PoseTargetValue = poseTargetValue;
                myMoneyFund.Tab = tab;
                myMoneyFund.Pose = pose;
                myMoneyFund.OriginalMoneyFundNameCode = originalMoneyFundNameCode;
                myMoneyFund.RepMoneyFundKoeff = repMoneyFundKoeff;
                myMoneyFund.OriginalMoneyFundValue = originalMoneyFundValue;
                PercentCalculation();
            }

            public void AddPosition(MirrorPosition mirrorPosition)
            {
                MirrorPositionsList.Add(mirrorPosition);
            }
            public void AddPosition(string securityNameCode, decimal securityPrice, decimal securityValue, decimal poseCurrentPosition = 0, decimal poseTargetPosition = 0, BotTabSimple tab = null, Position pose = null)
            {
                MirrorPositionsList.Add(new MirrorPosition(securityNameCode, securityPrice, securityValue, poseCurrentPosition, poseTargetPosition, 0, tab, pose));
                PercentCalculation();
            }

            public int Count()
            {
                return MirrorPositionsList.Count;
            }

            private void PercentCalculation()
            {
                Price = Math.Abs(myTradeAsset.SecurityValue) * myTradeAsset.SecurityPrice + Math.Abs(myMoneyFund.SecurityValue) * myMoneyFund.SecurityPrice;
                for (int i = 0; i < MirrorPositionsList.Count; i++)
                {
                    Price += Math.Abs(MirrorPositionsList[i].SecurityValue) * MirrorPositionsList[i].SecurityPrice;
                }

                if (Price != 0)
                {
                    myTradeAsset.Percent = Math.Round(Math.Abs(myTradeAsset.SecurityValue) * myTradeAsset.SecurityPrice / Price, 2);
                    myMoneyFund.Percent = Math.Round(Math.Abs(myMoneyFund.SecurityValue) * myMoneyFund.SecurityPrice / Price, 2);

                }

                for (int i = 0; i < MirrorPositionsList.Count; i++)
                {
                    if (Price != 0)
                    {
                        MirrorPositionsList[i].Percent = Math.Round(Math.Abs(MirrorPositionsList[i].SecurityValue) * MirrorPositionsList[i].SecurityPrice / Price, 2);

                    }
                }
            }


            public string CorrectPortfolio(Boolean onlyInfo = true, Boolean changeMoneyFund = true, Boolean repMoneyFund = false)
            {
                string sInfo = "Сравнение " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss") + "\r\n";
                var sortedList = MirrorPositionsList.OrderBy(x => x.SecurityNameCode).ToList();

                Boolean tChange = false;

                for (int i = 0; i < sortedList.Count; i++)
                {
                    sInfo += sortedList[i].SecurityNameCode + " (" + sortedList[i].Percent * 100 + "%) " + sortedList[i].PoseCurrentValue + " => " + sortedList[i].PoseTargetValue + "\r\n";
                    if (onlyInfo == false)
                    {
                        Decimal cur = sortedList[i].PoseCurrentValue;
                        Decimal tar = sortedList[i].PoseTargetValue;

                        if (tar != cur) { tChange = true; }

                        if (sortedList[i].Pose == null)
                        {
                            if (tar > 0)
                            {
                                sortedList[i].Tab.BuyAtMarket(tar);
                            }
                            if (tar < 0)
                            {
                                sortedList[i].Tab.SellAtMarket(Math.Abs(tar));
                            }
                        }
                        else
                        {
                            if (cur < tar)
                            {
                                if (cur > 0 && tar > 0)
                                {
                                    sortedList[i].Tab.BuyAtMarketToPosition(sortedList[i].Pose, tar - cur);
                                }
                                else if (cur < 0 && tar == 0)
                                {
                                    sortedList[i].Tab.CloseAtMarket(sortedList[i].Pose, Math.Abs(cur));
                                }
                                else if (cur < 0 && tar > 0)
                                {
                                    sortedList[i].Tab.CloseAtMarket(sortedList[i].Pose, Math.Abs(cur));
                                    sortedList[i].Tab.BuyAtMarket(tar);
                                }
                                else if (cur < 0 && tar < 0)
                                {
                                    sortedList[i].Tab.CloseAtMarket(sortedList[i].Pose, tar - cur);
                                }
                            }
                            else if (cur > tar)
                            {
                                if (cur > 0 && tar > 0)
                                {
                                    sortedList[i].Tab.CloseAtMarket(sortedList[i].Pose, cur - tar);
                                }
                                else if (cur > 0 && tar == 0)
                                {
                                    sortedList[i].Tab.CloseAtMarket(sortedList[i].Pose, cur);
                                }
                                else if (cur > 0 && tar < 0)
                                {
                                    sortedList[i].Tab.CloseAtMarket(sortedList[i].Pose, cur);
                                    sortedList[i].Tab.SellAtMarket(Math.Abs(tar));
                                }
                                else if (cur < 0 && tar < 0)
                                {
                                    sortedList[i].Tab.SellAtMarketToPosition(sortedList[i].Pose, cur - tar);
                                }
                            }
                        }
                    }

                }
                sInfo += "\r\n ---" + "\r\n";

                if (changeMoneyFund == true)
                {
                    if (repMoneyFund == false)
                    {
                        sInfo += myMoneyFund.SecurityNameCode + " (" + myMoneyFund.Percent * 100 + "%) " + myMoneyFund.PoseCurrentValue + " => " + myMoneyFund.PoseTargetValue + "\r\n";
                    }
                    else
                    {
                        sInfo += myMoneyFund.SecurityNameCode + " (" + myMoneyFund.Percent * 100 + "%) " + myMoneyFund.PoseCurrentValue + " => " + myMoneyFund.PoseTargetValue + " (" + myMoneyFund.OriginalMoneyFundNameCode + " " + myMoneyFund.OriginalMoneyFundValue + " x " + myMoneyFund.RepMoneyFundKoeff + ")" + "\r\n";
                    }
                }
                else
                {
                    sInfo += myMoneyFund.SecurityNameCode + " (" + myMoneyFund.Percent * 100 + "%) " + myMoneyFund.PoseCurrentValue + " => не корректируется\r\n";

                }

                if (onlyInfo == false)
                {

                    if (myMoneyFund.PoseCurrentValue < myMoneyFund.PoseTargetValue && changeMoneyFund == true)
                    {
                        if (myMoneyFund.Pose == null)
                        {
                            myMoneyFund.Tab.BuyAtMarket(myMoneyFund.PoseTargetValue - myMoneyFund.PoseCurrentValue);
                        }
                        else
                        {
                            myMoneyFund.Tab.BuyAtMarketToPosition(myMoneyFund.Pose, myMoneyFund.PoseTargetValue - myMoneyFund.PoseCurrentValue);
                        }
                        tChange = true;
                    }

                    if (myMoneyFund.PoseCurrentValue > myMoneyFund.PoseTargetValue && changeMoneyFund == true)
                    {
                        if (myMoneyFund.Pose == null)
                        {
                            myMoneyFund.Tab.SellAtMarket(myMoneyFund.PoseCurrentValue - myMoneyFund.PoseTargetValue);
                        }
                        else
                        {
                            myMoneyFund.Tab.CloseAtMarket(myMoneyFund.Pose, myMoneyFund.PoseCurrentValue - myMoneyFund.PoseTargetValue);
                        }
                        tChange = true;
                    }
                }

                sInfo += myTradeAsset.SecurityNameCode + " (" + myTradeAsset.Percent * 100 + "%) " + "\r\n";
                if (tChange == false && onlyInfo == false) { sInfo = ""; }
                return sInfo;
            }
        }

        #endregion


        public CopyPortfolio(string name, StartProgram startProgram) : base(name, startProgram)
        {
            // источник для сделок
            TabCreate(BotTabType.Screener);
            _tabToTrade1 = TabsScreener[0];

            // источник для отслеживания
            TabCreate(BotTabType.Simple);
            _tabToTrade2 = TabsSimple[0];

            _regime = CreateParameter("Regime", "Off", new[] { "Off", "On" }, "Main Regime");
            _onlyInfo = CreateParameter("Only calc position (without trade)", "On", new[] { "Off", "On" }, "Main Regime");
            _tradeAssetInPortfolio = CreateParameter("Trade Asset ", "Prime", "Main Regime");
            _moneyFundInPortfolio = CreateParameter("Money Fund ", "Prime", "Main Regime");
            _changeMoneyFund = CreateParameter("Change money fund", "On", new[] { "Off", "On" }, "Main Regime");
            _koeff = CreateParameter("Koeff", 0.1m, 0.1m, 2, 0.1m, "Main Regime");
            _startToWork = CreateParameterTimeOfDay("Start to work", 10, 05, 00, 00, "Main Regime");
            _endToWork = CreateParameterTimeOfDay("End to work", 18, 40, 00, 00, "Main Regime");
            _workInterval = CreateParameter("Work interval (min)", 5, 1, 20, 1, "Main Regime"); ;
            _lastTimeCheckFinance = CreateParameter("Last time work ", "", "Main Regime"); ;
            _repMoneyFund = CreateParameter("Replace Money Fund", "Off", new[] { "Off", "On" }, "Replace Money Fund");
            _repMoneyFundNew = CreateParameter("New Money Fund", "LQDT", "Replace Money Fund");
            _repMoneyFundKoeff = CreateParameter("New Money Fund Koeff", 1.001m, 1.001m, 20.001m, 0.001m, "Replace Money Fund");


            StrategyParameterButton button = CreateParameterButton("Copy manual", "Main Regime");
            button.UserClickOnButtonEvent += Button_UserClickOnButtonEvent;

            _tabToTrade2.ServerTimeChangeEvent += _tabToTrade2_ServerTimeChangeEvent;


        }

        private void _tabToTrade2_ServerTimeChangeEvent(DateTime obj)
        {
            if (_regime.ValueString == "Off")
            {
                return;
            }

            DateTime vDt = DateTime.Now;
            if (_lastTimeCheckFinance.ValueString == "")
            {
                _lastTimeCheckFinance.ValueString = Convert.ToString(vDt);
            }

            if (vDt.TimeOfDay >= _startToWork.TimeSpan && vDt.TimeOfDay <= _endToWork.TimeSpan)
            {
                if (Math.Abs((vDt - Convert.ToDateTime(_lastTimeCheckFinance.ValueString)).TotalMinutes) >= _workInterval.ValueInt)
                {
                    // здесь переход к основному действию
                    CopyPortfolioLogic();
                    _lastTimeCheckFinance.ValueString = Convert.ToString(vDt);
                }


            }
            return;
        }


        private void Button_UserClickOnButtonEvent()
        {
            CopyPortfolioLogic();
        }


        private void CopyPortfolioLogic()
        {
            if (_tabToTrade1.Tabs[0].IsReadyToTrade == false)
            {
                _tabToTrade1.Tabs[0].SetNewLogMessage("Connection not ready to trade", Logging.LogMessageType.System);
                return;
            }

            bool isTradingActive = IsTradingActive(_tabToTrade1.Tabs[0]);
            if (isTradingActive == false)
            {
                //_tabToTrade1.Tabs[0].SetNewLogMessage("There are currently no trades going on", Logging.LogMessageType.System);
                return;
            }

            if (_tabToTrade1.Tabs.Count == 0)
            {
                SendNewLogMessage("Не выбраны инструменты", Logging.LogMessageType.Error);
                return;
            }

            Portfolio myPortfolio = _tabToTrade2.Portfolio;
            if (myPortfolio == null)
            {
                SendNewLogMessage("Portfolio 1 Error", Logging.LogMessageType.Error);
                return;
            }


            // Анализируем все позиции исходного портфеля
            List<PositionOnBoard> positionOnBoard = myPortfolio.GetPositionOnBoard();
            List<Position> posesAll = _tabToTrade1.PositionsOpenAll;
            BotTabSimple tTab = null;
            int[] flag = new int[posesAll.Count];

            MirrorPortfolio mirrorPortfolio = new MirrorPortfolio();

            for (int i = 0; i < positionOnBoard.Count; i++)
            {
                if (positionOnBoard[i].SecurityNameCode == _tradeAssetInPortfolio.ValueString)
                {
                    mirrorPortfolio.myTradeAssetEdit(positionOnBoard[i].SecurityNameCode, 1, positionOnBoard[i].ValueCurrent);
                }
                else if (positionOnBoard[i].SecurityNameCode == _moneyFundInPortfolio.ValueString)
                {
                    string boardSecName = positionOnBoard[i].SecurityNameCode;
                    if (_repMoneyFund == "On") { boardSecName = _repMoneyFundNew; }

                    int tIndex = _tabToTrade1.Tabs.FindIndex(tab => tab.Security.Name == boardSecName);
                    if (tIndex == -1)
                    {
                        SendNewLogMessage("Отсутствует настройка для " + boardSecName + " панель сделок", Logging.LogMessageType.Error);
                        return;
                    }
                    tTab = _tabToTrade1.Tabs[tIndex];

                    decimal secPrice = 0;
                    if (tTab.PriceCenterMarketDepth != 0)
                    {
                        secPrice = tTab.PriceCenterMarketDepth;
                    }
                    else
                    {
                        secPrice = tTab.CandlesAll[tTab.CandlesAll.Count - 1].Close;
                    }

                    tIndex = posesAll.FindIndex(pos => pos.SecurityName == boardSecName);
                    decimal tPoseCurrent = 0;
                    Position tPos = null;
                    if (tIndex != -1)
                    {
                        tPoseCurrent = posesAll[tIndex].OpenVolume;
                        flag[tIndex] = 2;
                        tPos = posesAll[tIndex];
                    }

                    if (_repMoneyFund == "On")
                    {
                        mirrorPortfolio.myMoneyFundEdit(boardSecName, secPrice, Math.Round((positionOnBoard[i].ValueCurrent - positionOnBoard[i].ValueBlocked) * _repMoneyFundKoeff), tPoseCurrent, Math.Round((positionOnBoard[i].ValueCurrent - positionOnBoard[i].ValueBlocked) * _repMoneyFundKoeff * _koeff.ValueDecimal), tTab, tPos, positionOnBoard[i].SecurityNameCode, _repMoneyFundKoeff, positionOnBoard[i].ValueCurrent - positionOnBoard[i].ValueBlocked);
                    }
                    else
                    {
                        mirrorPortfolio.myMoneyFundEdit(positionOnBoard[i].SecurityNameCode, secPrice, positionOnBoard[i].ValueCurrent - positionOnBoard[i].ValueBlocked, tPoseCurrent, Math.Round((positionOnBoard[i].ValueCurrent - positionOnBoard[i].ValueBlocked) * _koeff.ValueDecimal), tTab, tPos);
                    }
                }
                else
                {
                    int tIndex = _tabToTrade1.Tabs.FindIndex(tab => tab.Security.Name == positionOnBoard[i].SecurityNameCode);
                    if (tIndex == -1)
                    {
                        SendNewLogMessage("Отсутствует настройка для " + positionOnBoard[i].SecurityNameCode, Logging.LogMessageType.Error);

                        //Здесь пробуем добавить новый tab для отсутствующей бумаги
                        try
                        {
                            // Проверка 1: сервер брокера должен быть включен
                            List<AServer> servers = ServerMaster.GetAServers();
                            if (servers == null
                                || servers.Count == 0)
                            {
                                SendNewLogMessage("Сначала подключите коннектор к Брокеру", Logging.LogMessageType.Error);
                                return;
                            }

                            int sIndex = servers.FindIndex(s => s.ServerType == _tabToTrade1.ServerType);
                            if (sIndex == -1)
                            {
                                SendNewLogMessage("Проблема с коннектором скринера", Logging.LogMessageType.Error);
                                return;
                            }

                            // Проверка 2: фьючерсная площадка и спот, должны быть подключены к коннектору
                            AServer myServer = servers[sIndex];
                            List<Entity.Security> securitiesAll = myServer.Securities;
                                
                            if (securitiesAll == null || securitiesAll.Count == 0)
                            {
                                SendNewLogMessage("В коннекторе не найдены бумаги. Возможно он не подключен", Logging.LogMessageType.Error);
                                return;
                            }

                            // Добавляем бумагу
                            Entity.Security newSec = securitiesAll.Find(s => s.Name == positionOnBoard[i].SecurityNameCode);
                            if (newSec == null) { return; }

                            ActivatedSecurity sec = new ActivatedSecurity();
                            sec.SecurityClass = newSec.NameClass;
                            sec.SecurityName = newSec.Name;
                            sec.IsOn = true;

                            _tabToTrade1.SecuritiesNames.Add(sec);
                            _tabToTrade1.NeedToReloadTabs = true;
                            SendNewLogMessage("Добавлен инструмент " + newSec.Name + " Класс " + newSec.NameClass, Logging.LogMessageType.Error);
                            continue;
                        }
                        catch (Exception error)
                        {
                            SendNewLogMessage("Ошибка при добавлении " + positionOnBoard[i].SecurityNameCode + " " + error.ToString(), LogMessageType.Error);
                            return;
                        }
                        //
                        
                    }
                    tTab = _tabToTrade1.Tabs[tIndex];

                    decimal lastPrice = 0;
                    if (tTab.PriceCenterMarketDepth != 0)
                    {
                        lastPrice = tTab.PriceCenterMarketDepth;
                    }
                    else
                    {
                        lastPrice = tTab.CandlesAll[tTab.CandlesAll.Count - 1].Close;
                    }

                    tIndex = posesAll.FindIndex(pos => pos.SecurityName == positionOnBoard[i].SecurityNameCode);
                    decimal tPoseCurrent = 0;
                    Position tPos = null;
                    if (tIndex != -1)
                    {
                        if (posesAll[tIndex].Direction == Side.Buy)
                        {
                            tPoseCurrent = posesAll[tIndex].OpenVolume;
                        }
                        else
                        {
                            tPoseCurrent = -posesAll[tIndex].OpenVolume;
                        }

                        flag[tIndex] = 1;
                        tPos = posesAll[tIndex];
                    }

                    mirrorPortfolio.AddPosition(positionOnBoard[i].SecurityNameCode, lastPrice, positionOnBoard[i].ValueCurrent - positionOnBoard[i].ValueBlocked, tPoseCurrent, Math.Round((positionOnBoard[i].ValueCurrent - positionOnBoard[i].ValueBlocked) * _koeff.ValueDecimal), tTab, tPos);
                }
            }

            // Пройдем по всем открытым позициям портфеля-зеркала и дополним позициями, которых нет в исходном портфеле
            for (int i = 0; i < posesAll.Count; i++)
            {
                if (flag[i] != 0) { continue; }

                int tIndex = _tabToTrade1.Tabs.FindIndex(tab => tab.Security.Name == posesAll[i].SecurityName);
                if (tIndex == -1)
                {
                    SendNewLogMessage("Отсутствует настройка для " + positionOnBoard[i].SecurityNameCode, Logging.LogMessageType.Error);
                    return;
                }
                tTab = _tabToTrade1.Tabs[tIndex];


                decimal lastPrice = 0;
                if (tTab.PriceCenterMarketDepth != 0)
                {
                    lastPrice = tTab.PriceCenterMarketDepth;
                }
                else
                {
                    lastPrice = tTab.CandlesAll[tTab.CandlesAll.Count - 1].Close;
                }

                mirrorPortfolio.AddPosition(posesAll[i].SecurityName, lastPrice, 0, posesAll[i].OpenVolume, 0, tTab, posesAll[i]);

            }

            string tInfo = "";

            Boolean repMoneyFund = false;
            if (_repMoneyFund == "On") { repMoneyFund = true; }


            if (_onlyInfo == "On")
            {
                tInfo = mirrorPortfolio.CorrectPortfolio(true, true, repMoneyFund);

            }
            else if (_onlyInfo == "Off" && _changeMoneyFund == "On")
            {
                tInfo = mirrorPortfolio.CorrectPortfolio(false, true, repMoneyFund);

            }

            else if (_onlyInfo == "Off" && _changeMoneyFund == "Off")
            {
                tInfo = mirrorPortfolio.CorrectPortfolio(false, false, repMoneyFund);

            }


            if (tInfo != "") { SendNewLogMessage(tInfo, Logging.LogMessageType.Error); }

            // Определяем необхдимые изменения по портфелю
        }


        #region Checks
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
        #endregion

        public override string GetNameStrategyType()
        {
            return "CopyPortfolio";
        }

        public override void ShowIndividualSettingsDialog()
        {

        }
    }
}

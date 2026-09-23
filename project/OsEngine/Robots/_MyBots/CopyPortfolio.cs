/* 
 Версия 1.11
 */


using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.Market;
using OsEngine.Market.Connectors;
using OsEngine.Market.Servers;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;
using System.Linq;


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
        StrategyParameterString _workIntervalUnit;
        StrategyParameterInt _workInterval;
        StrategyParameterString _lastTimeCheckFinance;
        StrategyParameterString _repMoneyFund;
        StrategyParameterString _repMoneyFundNew;
        StrategyParameterDecimal _repMoneyFundKoeff;
        StrategyParameterDecimal _maxPriceAgeHours;
        StrategyParameterString _verifyPositions;
        StrategyParameterString _icebergIsOn;
        StrategyParameterInt _icebergOrdersCount;
        StrategyParameterInt _icebergTimeoutSec;
        StrategyParameterDecimal _maxDepthAgeSec;
        StrategyParameterDecimal _resubscribeThrottleMin;
        DateTime _lastSystemLogTime = DateTime.MinValue;
        List<string> _deadSecurities = new List<string>();
        private object _deadSecuritiesLocker = new object();
        private Dictionary<string, DateTime> _lastResubscribeBySec = new Dictionary<string, DateTime>();
        private object _resubscribeLocker = new object();

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
            public int IcebergOrdersCount = 1;
            public int IcebergTimeoutSec = 0;

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

            private void IcebergSleep()
            {
                if (IcebergTimeoutSec > 0)
                {
                    System.Threading.Thread.Sleep(IcebergTimeoutSec * 1000);
                }
            }

            // часть объёма, кратная шагу объёма инструмента
            private decimal NormalizePart(BotTabSimple tab, decimal volume)
            {
                if (tab == null || tab.Security == null)
                {
                    return volume;
                }

                decimal step = tab.Security.VolumeStep;

                if (step <= 0)
                {
                    step = tab.Security.Lot;
                }

                if (step <= 0)
                {
                    step = 1;
                }

                return Math.Truncate(volume / step) * step;
            }

            public void IcebergBuy(BotTabSimple tab, decimal volume)
            {
                if (volume <= 0)
                {
                    return;
                }

                if (IcebergOrdersCount <= 1)
                {
                    tab.BuyAtMarket(volume);
                    return;
                }

                decimal part = NormalizePart(tab, volume / IcebergOrdersCount);

                if (part <= 0)
                {
                    // объём меньше лота — одним ордером
                    tab.BuyAtMarket(volume);
                    return;
                }

                Position pos = null;

                for (int i = 0; i < IcebergOrdersCount; i++)
                {
                    // последняя часть забирает остаток и нормализуется вниз до кратности шагу,
                    // чтобы заявка не была отклонена биржей при некратном текущем объёме позиции
                    decimal curPart = (i == IcebergOrdersCount - 1) ? NormalizePart(tab, volume - part * (IcebergOrdersCount - 1)) : part;

                    if (curPart <= 0)
                    {
                        break;
                    }

                    if (pos == null)
                    {
                        pos = tab.BuyAtMarket(curPart);
                    }
                    else
                    {
                        // последующие части докупаем в ту же позицию,
                        // иначе на следующем цикле робот посчитает их позиции лишними и закроет
                        tab.BuyAtMarketToPosition(pos, curPart);
                    }

                    if (pos == null)
                    {
                        // заявка не ушла (нет котировок/коннектор не готов) — дальше не шлём
                        break;
                    }

                    if (i < IcebergOrdersCount - 1)
                    {
                        IcebergSleep();
                    }
                }
            }

            public void IcebergSell(BotTabSimple tab, decimal volume)
            {
                if (volume <= 0)
                {
                    return;
                }

                if (IcebergOrdersCount <= 1)
                {
                    tab.SellAtMarket(volume);
                    return;
                }

                decimal part = NormalizePart(tab, volume / IcebergOrdersCount);

                if (part <= 0)
                {
                    tab.SellAtMarket(volume);
                    return;
                }

                Position pos = null;

                for (int i = 0; i < IcebergOrdersCount; i++)
                {
                    // последняя часть забирает остаток и нормализуется вниз до кратности шагу,
                    // чтобы заявка не была отклонена биржей при некратном текущем объёме позиции
                    decimal curPart = (i == IcebergOrdersCount - 1) ? NormalizePart(tab, volume - part * (IcebergOrdersCount - 1)) : part;

                    if (curPart <= 0)
                    {
                        break;
                    }

                    if (pos == null)
                    {
                        pos = tab.SellAtMarket(curPart);
                    }
                    else
                    {
                        tab.SellAtMarketToPosition(pos, curPart);
                    }

                    if (pos == null)
                    {
                        break;
                    }

                    if (i < IcebergOrdersCount - 1)
                    {
                        IcebergSleep();
                    }
                }
            }

            public void IcebergBuyToPosition(BotTabSimple tab, Position position, decimal volume)
            {
                if (volume <= 0)
                {
                    return;
                }

                if (IcebergOrdersCount <= 1)
                {
                    tab.BuyAtMarketToPosition(position, volume);
                    return;
                }

                decimal part = NormalizePart(tab, volume / IcebergOrdersCount);

                if (part <= 0)
                {
                    tab.BuyAtMarketToPosition(position, volume);
                    return;
                }

                for (int i = 0; i < IcebergOrdersCount; i++)
                {
                    // последняя часть забирает остаток и нормализуется вниз до кратности шагу,
                    // чтобы заявка не была отклонена биржей при некратном текущем объёме позиции
                    decimal curPart = (i == IcebergOrdersCount - 1) ? NormalizePart(tab, volume - part * (IcebergOrdersCount - 1)) : part;

                    if (curPart <= 0)
                    {
                        break;
                    }

                    tab.BuyAtMarketToPosition(position, curPart);

                    if (i < IcebergOrdersCount - 1)
                    {
                        IcebergSleep();
                    }
                }
            }

            public void IcebergSellToPosition(BotTabSimple tab, Position position, decimal volume)
            {
                if (volume <= 0)
                {
                    return;
                }

                if (IcebergOrdersCount <= 1)
                {
                    tab.SellAtMarketToPosition(position, volume);
                    return;
                }

                decimal part = NormalizePart(tab, volume / IcebergOrdersCount);

                if (part <= 0)
                {
                    tab.SellAtMarketToPosition(position, volume);
                    return;
                }

                for (int i = 0; i < IcebergOrdersCount; i++)
                {
                    // последняя часть забирает остаток и нормализуется вниз до кратности шагу,
                    // чтобы заявка не была отклонена биржей при некратном текущем объёме позиции
                    decimal curPart = (i == IcebergOrdersCount - 1) ? NormalizePart(tab, volume - part * (IcebergOrdersCount - 1)) : part;

                    if (curPart <= 0)
                    {
                        break;
                    }

                    tab.SellAtMarketToPosition(position, curPart);

                    if (i < IcebergOrdersCount - 1)
                    {
                        IcebergSleep();
                    }
                }
            }

            public void IcebergClose(BotTabSimple tab, Position position, decimal volume)
            {
                if (volume <= 0)
                {
                    return;
                }

                if (IcebergOrdersCount <= 1)
                {
                    tab.CloseAtMarket(position, volume);
                    return;
                }

                decimal part = NormalizePart(tab, volume / IcebergOrdersCount);

                if (part <= 0)
                {
                    tab.CloseAtMarket(position, volume);
                    return;
                }

                for (int i = 0; i < IcebergOrdersCount; i++)
                {
                    // последняя часть забирает остаток и нормализуется вниз до кратности шагу,
                    // чтобы заявка не была отклонена биржей при некратном текущем объёме позиции
                    decimal curPart = (i == IcebergOrdersCount - 1) ? NormalizePart(tab, volume - part * (IcebergOrdersCount - 1)) : part;

                    if (curPart <= 0)
                    {
                        break;
                    }

                    tab.CloseAtMarket(position, curPart);

                    if (i < IcebergOrdersCount - 1)
                    {
                        IcebergSleep();
                    }
                }
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
                List<MirrorPosition> sortedList = MirrorPositionsList.OrderBy(x => x.SecurityNameCode).ToList();

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
                                IcebergBuy(sortedList[i].Tab, tar);
                            }
                            if (tar < 0)
                            {
                                IcebergSell(sortedList[i].Tab, Math.Abs(tar));
                            }
                        }
                        else
                        {
                            if (cur < tar)
                            {
                                if (cur > 0 && tar > 0)
                                {
                                    IcebergBuyToPosition(sortedList[i].Tab, sortedList[i].Pose, tar - cur);
                                }
                                else if (cur < 0 && tar == 0)
                                {
                                    IcebergClose(sortedList[i].Tab, sortedList[i].Pose, Math.Abs(cur));
                                }
                                else if (cur < 0 && tar > 0)
                                {
                                    IcebergClose(sortedList[i].Tab, sortedList[i].Pose, Math.Abs(cur));
                                    IcebergBuy(sortedList[i].Tab, tar);
                                }
                                else if (cur < 0 && tar < 0)
                                {
                                    IcebergClose(sortedList[i].Tab, sortedList[i].Pose, tar - cur);
                                }
                            }
                            else if (cur > tar)
                            {
                                if (cur > 0 && tar > 0)
                                {
                                    IcebergClose(sortedList[i].Tab, sortedList[i].Pose, cur - tar);
                                }
                                else if (cur > 0 && tar == 0)
                                {
                                    IcebergClose(sortedList[i].Tab, sortedList[i].Pose, cur);
                                }
                                else if (cur > 0 && tar < 0)
                                {
                                    IcebergClose(sortedList[i].Tab, sortedList[i].Pose, cur);
                                    IcebergSell(sortedList[i].Tab, Math.Abs(tar));
                                }
                                else if (cur < 0 && tar < 0)
                                {
                                    IcebergSellToPosition(sortedList[i].Tab, sortedList[i].Pose, cur - tar);
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
                            IcebergBuy(myMoneyFund.Tab, myMoneyFund.PoseTargetValue - myMoneyFund.PoseCurrentValue);
                        }
                        else
                        {
                            IcebergBuyToPosition(myMoneyFund.Tab, myMoneyFund.Pose, myMoneyFund.PoseTargetValue - myMoneyFund.PoseCurrentValue);
                        }
                        tChange = true;
                    }

                    if (myMoneyFund.PoseCurrentValue > myMoneyFund.PoseTargetValue && changeMoneyFund == true)
                    {
                        if (myMoneyFund.Pose == null)
                        {
                            IcebergSell(myMoneyFund.Tab, myMoneyFund.PoseCurrentValue - myMoneyFund.PoseTargetValue);
                        }
                        else
                        {
                            IcebergClose(myMoneyFund.Tab, myMoneyFund.Pose, myMoneyFund.PoseCurrentValue - myMoneyFund.PoseTargetValue);
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
            _workIntervalUnit = CreateParameter("Work interval unit", "Minutes", new[] { "Minutes", "Seconds" }, "Main Regime");
            _workInterval = CreateParameter("Work interval", 5, 1, 3600, 1, "Main Regime"); ;
            _maxPriceAgeHours = CreateParameter("Max price age (hours)", 72m, 0.5m, 8760m, 0.5m, "Main Regime");
            _verifyPositions = CreateParameter("Verify positions with account", "Off", new[] { "Off", "On" }, "Main Regime");
            _repMoneyFund = CreateParameter("Replace Money Fund", "Off", new[] { "Off", "On" }, "Replace Money Fund");
            _repMoneyFundNew = CreateParameter("New Money Fund", "LQDT", "Replace Money Fund");
            _repMoneyFundKoeff = CreateParameter("New Money Fund Koeff", 1.001m, 1.001m, 20.001m, 0.001m, "Replace Money Fund");
            _icebergIsOn = CreateParameter("Iceberg orders", "Off", new[] { "Off", "On" }, "Iceberg");
            _icebergOrdersCount = CreateParameter("Iceberg orders count", 3, 1, 50, 1, "Iceberg");
            _icebergTimeoutSec = CreateParameter("Iceberg timeout (sec)", 5, 0, 300, 1, "Iceberg");
            _maxDepthAgeSec = CreateParameter("Max market depth age (sec)", 60m, 5m, 3600m, 5m, "Market Depth Watchdog");
            _resubscribeThrottleMin = CreateParameter("Resubscribe throttle (min)", 2m, 1m, 60m, 1m, "Market Depth Watchdog");
            _lastTimeCheckFinance = CreateParameter("Last time work ", "", "Main Regime");

            StrategyParameterButton button = CreateParameterButton("Copy manual", "Main Regime");
            button.UserClickOnButtonEvent += Button_UserClickOnButtonEvent;

            _tabToTrade2.ServerTimeChangeEvent += _tabToTrade2_ServerTimeChangeEvent;


        }

        private void _tabToTrade2_ServerTimeChangeEvent(DateTime obj)
        {
            try
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
                    // интервал отсчитываем в минутах или секундах в зависимости от выбранной единицы
                    double elapsedInterval = Math.Abs((vDt - Convert.ToDateTime(_lastTimeCheckFinance.ValueString)).TotalMinutes);

                    if (_workIntervalUnit.ValueString == "Seconds")
                    {
                        elapsedInterval = Math.Abs((vDt - Convert.ToDateTime(_lastTimeCheckFinance.ValueString)).TotalSeconds);
                    }

                    if (elapsedInterval >= _workInterval.ValueInt)
                    {
                        // здесь переход к основному действию
                        CopyPortfolioLogic();
                        _lastTimeCheckFinance.ValueString = Convert.ToString(vDt);
                    }


                }
                return;
            }
            catch (Exception error)
            {
                SendNewLogMessage("Ошибка в _tabToTrade2_ServerTimeChangeEvent: " + error.ToString(), LogMessageType.Error);
            }
        }


        private void Button_UserClickOnButtonEvent()
        {
            CopyPortfolioLogic();
        }


        private void CopyPortfolioLogic()
        {
            try
            {
                if (_tabToTrade1.Tabs.Count == 0)
                {
                    SendNewLogMessage("Не выбраны инструменты", Logging.LogMessageType.Error);
                    return;
                }

                // сторож стакана: переподписываем табы, где котировки идут, а стакан протух
                WatchdogMarketDepth();

                if (_tabToTrade1.Tabs[0].IsReadyToTrade == false)
                {
                    SendThrottledSystemLog("Connection not ready to trade, цикл пропущен");
                    return;
                }

                bool isTradingActive = IsTradingActive(_tabToTrade1.Tabs[0]);
                if (isTradingActive == false)
                {
                    SendThrottledSystemLog("Торговая сессия не активна (нет данных стакана), цикл пропущен");
                    return;
                }

                // не торгуем, пока скринер перезагружает табы: списки табов и позиций в этот момент несогласованы
                if (_tabToTrade1.NeedToReloadTabs == true)
                {
                    SendThrottledSystemLog("Идёт перезагрузка табов скринера, цикл пропущен");
                    return;
                }

                // проверяем, что все включённые бумаги скринера имеют созданные и подключённые табы
                if (AllTabsReady() == false)
                {
                    return;
                }

                if (_tabToTrade2.IsReadyToTrade == false)
                {
                    SendNewLogMessage("Connection 2 not ready to trade", Logging.LogMessageType.System);
                    return;
                }

                Portfolio myPortfolio = _tabToTrade2.Portfolio;
                if (myPortfolio == null)
                {
                    SendNewLogMessage("Portfolio 1 Error", Logging.LogMessageType.Error);
                    return;
                }

                List<PositionOnBoard> positionOnBoard = myPortfolio.GetPositionOnBoard();
                if (positionOnBoard == null)
                {
                    SendNewLogMessage("Не удалось получить позиции портфеля-источника", Logging.LogMessageType.Error);
                    return;
                }


                // до снятия позиций убеждаемся, что для всех бумаг источника есть табы:
                // недостающие добавляем и ждём готовности заранее, чтобы позиции не снимались по устаревшему снапшоту
                for (int i = 0; i < positionOnBoard.Count; i++)
                {
                    if (positionOnBoard[i].SecurityNameCode == _tradeAssetInPortfolio.ValueString
                        || positionOnBoard[i].SecurityNameCode == _moneyFundInPortfolio.ValueString)
                    {
                        continue;
                    }

                    int tIndex = _tabToTrade1.Tabs.FindIndex(tab => tab.Security != null && tab.Security.Name == positionOnBoard[i].SecurityNameCode);

                    if (tIndex == -1)
                    {
                        TryAddSecurityAndWaitTab(positionOnBoard[i].SecurityNameCode);
                    }
                }

                // Анализируем все позиции исходного портфеля
                List<Position> posesAll = _tabToTrade1.PositionsOpenAll;
                BotTabSimple tTab = null;
                int[] flag = new int[posesAll.Count];

                MirrorPortfolio mirrorPortfolio = new MirrorPortfolio();

                // корректировка фонда отключается на этот цикл, если по его табу нет стакана:
                // заявка не пройдёт, а бумаги ребалансировать нужно
                bool moneyFundChangeEnabled = _changeMoneyFund.ValueString == "On";

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

                        // без стакана заявка по фонду не пройдёт (BestAsk == 0) — фонд в этом цикле не корректируем
                        bool moneyFundDepthIsOn = IsTradingActive(tTab);

                        if (moneyFundDepthIsOn == false)
                        {
                            LogDeadOnce(boardSecName, "нет котировок");
                        }

                        decimal secPrice = GetLastPrice(tTab);

                        if (secPrice <= 0)
                        {
                            SendThrottledSystemLog("Нет цены по инструменту " + boardSecName + ", цикл пропущен");
                            return;
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

                        if (moneyFundDepthIsOn == false)
                        {
                            moneyFundChangeEnabled = false;
                            // цель = текущей позиции, чтобы расчёт портфеля не искажался, а торгов не было
                            mirrorPortfolio.myMoneyFundEdit(boardSecName, secPrice, positionOnBoard[i].ValueCurrent - positionOnBoard[i].ValueBlocked, tPoseCurrent, tPoseCurrent, tTab, tPos);
                            continue;
                        }

                        if (_verifyPositions == "On" && IsPositionMatchAccount(tTab, boardSecName, tPoseCurrent) == false)
                        {
                            decimal accountVolume = 0m;
                            GetAccountVolume(tTab, boardSecName, out accountVolume);
                            SendThrottledLog("Сверка со счётом: по " + boardSecName + " журнал " + tPoseCurrent + " ≠ счёт " + accountVolume + ". Цикл пропущен", Logging.LogMessageType.Error);
                            return;
                        }

                        if (_repMoneyFund == "On")
                        {
                            mirrorPortfolio.myMoneyFundEdit(boardSecName, secPrice, Math.Round((positionOnBoard[i].ValueCurrent - positionOnBoard[i].ValueBlocked) * _repMoneyFundKoeff), tPoseCurrent, NormalizeVolume(tTab, (positionOnBoard[i].ValueCurrent - positionOnBoard[i].ValueBlocked) * _repMoneyFundKoeff * _koeff.ValueDecimal), tTab, tPos, positionOnBoard[i].SecurityNameCode, _repMoneyFundKoeff, positionOnBoard[i].ValueCurrent - positionOnBoard[i].ValueBlocked);
                        }
                        else
                        {
                            mirrorPortfolio.myMoneyFundEdit(positionOnBoard[i].SecurityNameCode, secPrice, positionOnBoard[i].ValueCurrent - positionOnBoard[i].ValueBlocked, tPoseCurrent, NormalizeVolume(tTab, (positionOnBoard[i].ValueCurrent - positionOnBoard[i].ValueBlocked) * _koeff.ValueDecimal), tTab, tPos);
                        }
                    }
                    else
                    {
                        int tIndex = _tabToTrade1.Tabs.FindIndex(tab => tab.Security != null && tab.Security.Name == positionOnBoard[i].SecurityNameCode);
                        if (tIndex == -1)
                        {
                            // пре-проход уже пытался добавить таб и дождаться его — пропускаем бумагу в этом цикле
                            continue;
                        }
                        tTab = _tabToTrade1.Tabs[tIndex];

                        // мёртвый инструмент (исключён из коннектора или нет котировок) пропускаем:
                        // не считаем по нему цели, но и не замораживаем весь портфель
                        if (IsSecurityInServer(positionOnBoard[i].SecurityNameCode) == false)
                        {
                            LogDeadOnce(positionOnBoard[i].SecurityNameCode, "исключён из коннектора");
                            continue;
                        }

                        if (IsTradingActive(tTab) == false)
                        {
                            LogDeadOnce(positionOnBoard[i].SecurityNameCode, "нет котировок");
                            continue;
                        }

                        decimal lastPrice = GetLastPrice(tTab);

                        if (lastPrice <= 0)
                        {
                            LogDeadOnce(positionOnBoard[i].SecurityNameCode, "нет цены");
                            continue;
                        }

                        // бумага ожила — снимаем пометку, чтобы при новой проблеме снова залогировать
                        MarkSecurityAlive(positionOnBoard[i].SecurityNameCode);

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

                        if (_verifyPositions == "On" && IsPositionMatchAccount(tTab, positionOnBoard[i].SecurityNameCode, tPoseCurrent) == false)
                        {
                            decimal accountVolume = 0m;
                            GetAccountVolume(tTab, positionOnBoard[i].SecurityNameCode, out accountVolume);
                            SendThrottledLog("Сверка со счётом: по " + positionOnBoard[i].SecurityNameCode + " журнал " + tPoseCurrent + " ≠ счёт " + accountVolume + ". Бумага пропущена", Logging.LogMessageType.Error);
                            continue;
                        }

                        mirrorPortfolio.AddPosition(positionOnBoard[i].SecurityNameCode, lastPrice, positionOnBoard[i].ValueCurrent - positionOnBoard[i].ValueBlocked, tPoseCurrent, NormalizeVolume(tTab, (positionOnBoard[i].ValueCurrent - positionOnBoard[i].ValueBlocked) * _koeff.ValueDecimal), tTab, tPos);
                    }
                }

                // Пройдем по всем открытым позициям портфеля-зеркала и дополним позициями, которых нет в исходном портфеле
                for (int i = 0; i < posesAll.Count; i++)
                {
                    if (flag[i] != 0) { continue; }

                    int tIndex = _tabToTrade1.Tabs.FindIndex(tab => tab.Security.Name == posesAll[i].SecurityName);
                    if (tIndex == -1)
                    {
                        SendNewLogMessage("Отсутствует настройка для " + posesAll[i].SecurityName, Logging.LogMessageType.Error);
                        return;
                    }
                    if (IsSecurityInServer(posesAll[i].SecurityName) == false)
                    {
                        LogDeadOnce(posesAll[i].SecurityName, "исключён из коннектора");
                        continue;
                    }

                    tTab = _tabToTrade1.Tabs[tIndex];

                    if (IsTradingActive(tTab) == false)
                    {
                        LogDeadOnce(posesAll[i].SecurityName, "нет котировок");
                        continue;
                    }

                    decimal lastPrice = GetLastPrice(tTab);

                    if (lastPrice <= 0)
                    {
                        LogDeadOnce(posesAll[i].SecurityName, "нет цены");
                        continue;
                    }

                    MarkSecurityAlive(posesAll[i].SecurityName);

                    if (_verifyPositions == "On")
                    {
                        decimal journalVolume = posesAll[i].Direction == Side.Buy ? posesAll[i].OpenVolume : -posesAll[i].OpenVolume;

                        if (IsPositionMatchAccount(tTab, posesAll[i].SecurityName, journalVolume) == false)
                        {
                            decimal accountVolume = 0m;
                            GetAccountVolume(tTab, posesAll[i].SecurityName, out accountVolume);
                            SendThrottledLog("Сверка со счётом: по " + posesAll[i].SecurityName + " журнал " + journalVolume + " ≠ счёт " + accountVolume + ". Бумага пропущена", Logging.LogMessageType.Error);
                            continue;
                        }
                    }

                    mirrorPortfolio.AddPosition(posesAll[i].SecurityName, lastPrice, 0, posesAll[i].OpenVolume, 0, tTab, posesAll[i]);

                }

                string tInfo = "";

                Boolean repMoneyFund = false;
                if (_repMoneyFund == "On") { repMoneyFund = true; }

                // настройки айсберга для CorrectPortfolio
                mirrorPortfolio.IcebergOrdersCount = _icebergIsOn.ValueString == "On" ? _icebergOrdersCount.ValueInt : 1;
                mirrorPortfolio.IcebergTimeoutSec = _icebergTimeoutSec.ValueInt;


                if (_onlyInfo == "On")
                {
                    tInfo = mirrorPortfolio.CorrectPortfolio(true, moneyFundChangeEnabled, repMoneyFund);

                }
                else if (_onlyInfo == "Off" && moneyFundChangeEnabled == true)
                {
                    tInfo = mirrorPortfolio.CorrectPortfolio(false, true, repMoneyFund);

                }

                else if (_onlyInfo == "Off")
                {
                    tInfo = mirrorPortfolio.CorrectPortfolio(false, false, repMoneyFund);

                }


                if (tInfo != "") { SendNewLogMessage(tInfo, Logging.LogMessageType.Error); }

                // Определяем необхдимые изменения по портфелю
            }
            catch (Exception error)
            {
                SendNewLogMessage("Ошибка в CopyPortfolioLogic: " + error.ToString(), LogMessageType.Error);
            }
        }


        private decimal GetLastPrice(BotTabSimple tab)
        {
            if (tab == null)
            {
                return 0;
            }

            if (tab.PriceCenterMarketDepth != 0)
            {
                return tab.PriceCenterMarketDepth;
            }

            if (tab.CandlesAll == null || tab.CandlesAll.Count == 0)
            {
                SendNewLogMessage("Нет свечных данных для " + tab.Security.Name, Logging.LogMessageType.Error);
                return 0;
            }

            Candle lastCandle = tab.CandlesAll[tab.CandlesAll.Count - 1];

            // цена из протухшей свечи хуже её отсутствия: по мёртвой цене считаются ложные цели и объёмы
            if (IsCandleFresh(lastCandle) == false)
            {
                SendThrottledSystemLog("Цена по инструменту " + tab.Security.Name + " протухла: возраст последней свечи больше " + _maxPriceAgeHours.ValueDecimal + " ч");
                return 0;
            }

            return lastCandle.Close;
        }

        private bool IsCandleFresh(Candle candle)
        {
            if (candle == null)
            {
                return false;
            }

            return (DateTime.Now - candle.TimeStart).TotalHours <= (double)_maxPriceAgeHours.ValueDecimal;
        }

        // сторож стакана: если по бумаге идут котировки, а стакан не приходит,
        // принудительно переподписываем таб (ReconnectHard = Unsubscribe + Subscribe на сервере)
        private void WatchdogMarketDepth()
        {
            try
            {
                for (int i = 0; i < _tabToTrade1.Tabs.Count; i++)
                {
                    BotTabSimple tab = _tabToTrade1.Tabs[i];

                    if (tab == null || tab.Security == null || tab.IsConnected == false)
                    {
                        continue;
                    }

                    // стакан есть и свежий — с табом всё в порядке
                    if (tab.MarketDepth != null
                        && (DateTime.Now - tab.MarketDepth.Time).TotalSeconds < (double)_maxDepthAgeSec.ValueDecimal)
                    {
                        continue;
                    }

                    List<Candle> candles = tab.CandlesAll;

                    if (candles == null || candles.Count == 0)
                    {
                        continue;
                    }

                    // сделок нет — стакан тут ни при чём, это мёртвая бумага, переподписка бессмысленна
                    // свежесть свечи меряем таймфреймом с запасом: последняя свеча началась недавно
                    double maxCandleAgeMin = tab.TimeFrame.TotalMinutes * 3;

                    if (maxCandleAgeMin < 3)
                    {
                        maxCandleAgeMin = 3;
                    }

                    if ((DateTime.Now - candles[candles.Count - 1].TimeStart).TotalMinutes > maxCandleAgeMin)
                    {
                        continue;
                    }

                    string secName = tab.Security.Name;

                    lock (_resubscribeLocker)
                    {
                        DateTime lastResubscribe;

                        if (_lastResubscribeBySec.TryGetValue(secName, out lastResubscribe)
                            && (DateTime.Now - lastResubscribe).TotalMinutes < (double)_resubscribeThrottleMin.ValueDecimal)
                        {
                            continue;
                        }

                        _lastResubscribeBySec[secName] = DateTime.Now;
                    }

                    SendNewLogMessage("По " + secName + " нет стакана при живых котировках. Принудительная переподписка таба", Logging.LogMessageType.Error);
                    tab.Connector.ReconnectHard();
                }
            }
            catch (Exception error)
            {
                SendNewLogMessage("Ошибка в WatchdogMarketDepth: " + error.ToString(), LogMessageType.Error);
            }
        }

        private bool IsSecurityInServer(string securityName)
        {
            // если коннектор не найден или бумаги ещё не подгрузились, проверить нечем — не считаем бумагу мёртвой
            List<AServer> servers = ServerMaster.GetAServers();

            if (servers == null || servers.Count == 0)
            {
                return true;
            }

            AServer server = servers.Find(s => s.ServerType == _tabToTrade1.ServerType);

            if (server == null)
            {
                return true;
            }

            List<Entity.Security> securities = server.Securities;

            if (securities == null || securities.Count == 0)
            {
                return true;
            }

            return securities.Find(s => s.Name == securityName) != null;
        }

        private bool GetAccountVolume(BotTabSimple tab, string securityName, out decimal accountVolume)
        {
            accountVolume = 0m;

            if (tab == null)
            {
                return false;
            }

            Portfolio portfolio = tab.Portfolio;

            if (portfolio == null)
            {
                return false;
            }

            List<PositionOnBoard> positionsOnBoard = portfolio.GetPositionOnBoard();

            if (positionsOnBoard == null)
            {
                return false;
            }

            PositionOnBoard boardPos = positionsOnBoard.Find(p => p.SecurityNameCode == securityName);

            if (boardPos == null)
            {
                // позиции на счёте нет — это валидный ноль
                return true;
            }

            accountVolume = boardPos.ValueCurrent;
            return true;
        }

        // сверка объёма позиции из журнала таба с реальным объёмом на счёте
        private bool IsPositionMatchAccount(BotTabSimple tab, string securityName, decimal journalVolume)
        {
            decimal accountVolume = 0m;

            if (GetAccountVolume(tab, securityName, out accountVolume) == false)
            {
                // сверить не удалось — не блокируем торговлю
                return true;
            }

            return accountVolume == journalVolume;
        }

        private void LogDeadOnce(string securityName, string reason)
        {
            // мёртвую бумагу логируем один раз за сессию, чтобы не спамить каждый цикл
            lock (_deadSecuritiesLocker)
            {
                if (_deadSecurities.Contains(securityName))
                {
                    return;
                }

                _deadSecurities.Add(securityName);
            }

            SendNewLogMessage("Инструмент " + securityName + " " + reason + ". Исключён из ребалансировки, его позиции не корректируются", Logging.LogMessageType.Error);
        }

        private void MarkSecurityAlive(string securityName)
        {
            // бумага ожила — снимаем пометку, чтобы при новой проблеме снова залогировать
            lock (_deadSecuritiesLocker)
            {
                _deadSecurities.Remove(securityName);
            }
        }

        private int TryAddSecurityAndWaitTab(string securityNameCode)
        {
            try
            {
                // Проверка 1: сервер брокера должен быть включен
                List<AServer> servers = ServerMaster.GetAServers();
                if (servers == null
                    || servers.Count == 0)
                {
                    SendNewLogMessage("Сначала подключите коннектор к Брокеру", Logging.LogMessageType.Error);
                    return -1;
                }

                int sIndex = servers.FindIndex(s => s.ServerType == _tabToTrade1.ServerType);
                if (sIndex == -1)
                {
                    SendNewLogMessage("Проблема с коннектором скринера", Logging.LogMessageType.Error);
                    return -1;
                }

                // Проверка 2: фьючерсная площадка и спот, должны быть подключены к коннектору
                AServer myServer = servers[sIndex];
                List<Entity.Security> securitiesAll = myServer.Securities;

                if (securitiesAll == null || securitiesAll.Count == 0)
                {
                    SendNewLogMessage("В коннекторе не найдены бумаги. Возможно он не подключен", Logging.LogMessageType.Error);
                    return -1;
                }

                // Добавляем бумагу
                Entity.Security newSec = securitiesAll.Find(s => s.Name == securityNameCode);
                if (newSec == null)
                {
                    SendNewLogMessage("Инструмент " + securityNameCode + " не найден в списке бумаг коннектора. Проверьте подключение класса бумаг у коннектора скринера", Logging.LogMessageType.Error);
                    return -1;
                }

                ActivatedSecurity sec = new ActivatedSecurity();
                sec.SecurityClass = newSec.NameClass;
                sec.SecurityName = newSec.Name;
                sec.IsOn = true;

                // плагин сам проверяет дубликаты, сохраняет настройки и ставит флаг перезагрузки табов
                if (ScreenerSecuritySync.AddSecurity(_tabToTrade1, sec))
                {
                    SendNewLogMessage("Добавлен инструмент " + newSec.Name + " Класс " + newSec.NameClass, Logging.LogMessageType.Error);
                }

                return WaitTabReady(securityNameCode);
            }
            catch (Exception error)
            {
                SendNewLogMessage("Ошибка при добавлении " + securityNameCode + " " + error.ToString(), LogMessageType.Error);
                return -1;
            }
        }

        private int WaitTabReady(string securityNameCode)
        {
            // фоновый поток скринера подхватывает NeedToReloadTabs примерно за секунду,
            // дальше ждём подключения коннектора таба и появления котировок
            for (int i = 0; i < 60; i++)
            {
                int tIndex = _tabToTrade1.Tabs.FindIndex(tab => tab.Security != null && tab.Security.Name == securityNameCode);

                if (tIndex != -1
                    && _tabToTrade1.Tabs[tIndex].IsConnected
                    && _tabToTrade1.Tabs[tIndex].IsReadyToTrade
                    && IsTradingActive(_tabToTrade1.Tabs[tIndex]))
                {
                    return tIndex;
                }

                System.Threading.Thread.Sleep(500);
            }

            SendNewLogMessage("Таб для " + securityNameCode + " не успел подключиться за 30 секунд. Бумага будет обработана в следующем цикле", Logging.LogMessageType.System);
            return -1;
        }

        private bool AllTabsReady()
        {
            for (int i = 0; i < _tabToTrade1.SecuritiesNames.Count; i++)
            {
                ActivatedSecurity sec = _tabToTrade1.SecuritiesNames[i];

                if (sec.IsOn == false)
                {
                    continue;
                }

                int tIndex = _tabToTrade1.Tabs.FindIndex(tab => tab.Security != null && tab.Security.Name == sec.SecurityName);

                if (tIndex == -1)
                {
                    SendThrottledSystemLog("Таб для " + sec.SecurityName + " ещё не создан, цикл пропущен");
                    return false;
                }

                BotTabSimple tab = _tabToTrade1.Tabs[tIndex];

                if (tab.IsConnected == false || tab.IsReadyToTrade == false)
                {
                    SendThrottledSystemLog("Таб " + sec.SecurityName + " не готов к торговле, цикл пропущен");
                    return false;
                }
            }

            return true;
        }

        private decimal NormalizeVolume(BotTabSimple tab, decimal volume)
        {
            if (tab == null || tab.Security == null)
            {
                return volume;
            }

            decimal step = tab.Security.VolumeStep;

            if (step <= 0)
            {
                step = tab.Security.Lot;
            }

            if (step <= 0)
            {
                step = 1;
            }

            // округляем к кратности шагу объёма в сторону нуля, чтобы не ушла заявка с недопустимым или избыточным объёмом
            decimal result = Math.Truncate(volume / step) * step;

            if (tab.Security.DecimalsVolume > 0)
            {
                result = Math.Round(result, tab.Security.DecimalsVolume);
            }

            return result;
        }

        private void SendThrottledLog(string message, LogMessageType type)
        {
            // событие времени сервера приходит каждую секунду, сообщения об одном и том же не чаще раза в 5 минут
            if ((DateTime.Now - _lastSystemLogTime).TotalMinutes < 5)
            {
                return;
            }

            _lastSystemLogTime = DateTime.Now;
            SendNewLogMessage(message, type);
        }

        private void SendThrottledSystemLog(string message)
        {
            SendThrottledLog(message, Logging.LogMessageType.System);
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

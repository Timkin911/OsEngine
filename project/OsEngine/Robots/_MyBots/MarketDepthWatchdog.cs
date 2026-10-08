/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.Market;
using OsEngine.Market.Servers;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;

namespace OsEngine.Robots
{
    /// <summary>
    /// Plugin-watchdog of market depth subscriptions for robots.
    /// Detects a silently dead depth stream (BestAsk/BestBid is zero or no depth events
    /// while the connection itself stays alive) and recovers it by tab reconnect,
    /// with optional escalation to a full server restart. Core files are not affected:
    /// the plugin uses only public events and methods of BotTabSimple, ConnectorCandles, AServer.
    /// </summary>
    public class MarketDepthWatchdog
    {
        #region Settings

        // робот проставляет настройки из своих параметров на каждом тике

        public bool IsOn = true;

        public int StaleTimeoutSec = 180;

        public int ReconnectCooldownMin = 10;

        public int MaxReconnectAttempts = 3;

        public bool ServerRestartAllowed = false;

        public int ServerRestartCooldownMin = 60;

        // пауза после переподключения, за которую ненулевые цены стакана обязаны вернуться
        private const int HardDeadGraceSec = 30;

        #endregion

        #region State and logging

        public event Action<string, LogMessageType> LogMessageEvent;

        private List<WatchedTab> _tabs = new List<WatchedTab>();

        private object _tabsLocker = new object();

        private DateTime _lastServerRestart = DateTime.MinValue;

        private DateTime _lastServerRestartLog = DateTime.MinValue;

        private class WatchedTab
        {
            public TabListener Listener;
            public DateTime WatchStart;
            public DateTime LastDepthEvent;
            public DateTime LastReconnectTry;
            public int ReconnectAttempts;
            public bool DeadLogged;
        }

        // обёртка с именованными обработчиками: анонимные делегаты не отписать, будет утечка
        private class TabListener
        {
            public BotTabSimple Tab;

            private MarketDepthWatchdog _owner;

            public TabListener(BotTabSimple tab, MarketDepthWatchdog owner)
            {
                Tab = tab;
                _owner = owner;
            }

            public void Subscribe()
            {
                Tab.MarketDepthUpdateEvent += Tab_MarketDepthUpdateEvent;
                Tab.BestBidAskChangeEvent += Tab_BestBidAskChangeEvent;
            }

            public void Unsubscribe()
            {
                Tab.MarketDepthUpdateEvent -= Tab_MarketDepthUpdateEvent;
                Tab.BestBidAskChangeEvent -= Tab_BestBidAskChangeEvent;
            }

            private void Tab_MarketDepthUpdateEvent(MarketDepth depth)
            {
                try
                {
                    _owner.OnDepthEvent(Tab);
                }
                catch (Exception error)
                {
                    ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
                }
            }

            private void Tab_BestBidAskChangeEvent(decimal bid, decimal ask)
            {
                try
                {
                    _owner.OnDepthEvent(Tab);
                }
                catch (Exception error)
                {
                    ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
                }
            }
        }

        private void SendLog(string message, LogMessageType type)
        {
            if (LogMessageEvent != null)
            {
                LogMessageEvent(message, type);
            }
            else if (type == LogMessageType.Error)
            {
                ServerMaster.SendNewLogMessage(message, type);
            }
        }

        #endregion

        #region Tab subscription sync

        public void SyncTabs(List<BotTabSimple> tabs)
        {
            try
            {
                lock (_tabsLocker)
                {
                    // табы, удалённые из скринера, отписываем — иначе утечка слушателей
                    for (int i = _tabs.Count - 1; i >= 0; i--)
                    {
                        if (tabs == null
                            || tabs.Contains(_tabs[i].Listener.Tab) == false)
                        {
                            _tabs[i].Listener.Unsubscribe();
                            _tabs.RemoveAt(i);
                        }
                    }

                    if (tabs == null)
                    {
                        return;
                    }

                    for (int i = 0; i < tabs.Count; i++)
                    {
                        BotTabSimple tab = tabs[i];

                        if (tab == null)
                        {
                            continue;
                        }

                        if (_tabs.Find(w => ReferenceEquals(w.Listener.Tab, tab)) != null)
                        {
                            continue;
                        }

                        TabListener listener = new TabListener(tab, this);
                        listener.Subscribe();

                        WatchedTab wt = new WatchedTab();
                        wt.Listener = listener;
                        wt.WatchStart = DateTime.Now;
                        wt.LastDepthEvent = DateTime.MinValue;
                        wt.LastReconnectTry = DateTime.MinValue;
                        wt.ReconnectAttempts = 0;
                        wt.DeadLogged = false;

                        _tabs.Add(wt);
                    }
                }
            }
            catch (Exception error)
            {
                SendLog("MarketDepthWatchdog.SyncTabs error: " + error.ToString(), LogMessageType.Error);
            }
        }

        private void OnDepthEvent(BotTabSimple tab)
        {
            lock (_tabsLocker)
            {
                WatchedTab wt = _tabs.Find(w => ReferenceEquals(w.Listener.Tab, tab));

                if (wt != null)
                {
                    wt.LastDepthEvent = DateTime.Now;
                }
            }
        }

        #endregion

        #region Dead depth detection

        public bool IsDepthDead(BotTabSimple tab)
        {
            try
            {
                if (tab == null)
                {
                    return false;
                }

                if (tab.StartProgram != StartProgram.IsOsTrader)
                {
                    // в тестере и оптимизаторе редкий стакан легитимен, сторож там не работает
                    return false;
                }

                if (TabTradableNow(tab) == false)
                {
                    return false;
                }

                DateTime lastEvent;
                DateTime watchStart;

                lock (_tabsLocker)
                {
                    WatchedTab wt = _tabs.Find(w => ReferenceEquals(w.Listener.Tab, tab));

                    if (wt == null)
                    {
                        // таб ещё не под наблюдением — решение примет CheckAll после SyncTabs
                        return false;
                    }

                    lastEvent = wt.LastDepthEvent;
                    watchStart = wt.WatchStart;
                }

                // отсчёт тишины: от последнего события, а если событий не было вовсе — от начала наблюдения
                DateTime silenceFrom = lastEvent != DateTime.MinValue ? lastEvent : watchStart;
                double silentSec = (DateTime.Now - silenceFrom).TotalSeconds;

                if (HardDead(tab))
                {
                    // нулевые BestAsk/BestBid — именно это вызывает «Нет данных по стакану» в ядре.
                    // После переподключения цены обязаны вернуться за HardDeadGraceSec
                    return silentSec > HardDeadGraceSec;
                }

                // цены ненулевые, но поток стакана молчит слишком долго
                return silentSec > StaleTimeoutSec;
            }
            catch (Exception error)
            {
                SendLog("MarketDepthWatchdog.IsDepthDead error: " + error.ToString(), LogMessageType.Error);
                return false;
            }
        }

        private bool TabTradableNow(BotTabSimple tab)
        {
            // не подключён или не готов — это зона ответственности проверок готовности робота, не сторожа
            if (tab.IsConnected == false || tab.IsReadyToTrade == false)
            {
                return false;
            }

            // неторговый период: стакана может не быть легитимно
            if (tab.IsNonTradePeriodInConnector)
            {
                return false;
            }

            return true;
        }

        private bool HardDead(BotTabSimple tab)
        {
            if (tab.Connector == null)
            {
                return false;
            }

            return tab.Connector.BestAsk == 0 || tab.Connector.BestBid == 0;
        }

        // стакан подтверждённо жив: таб торгуем, цены ненулевые и события идут
        private bool IsAliveConfirmed(WatchedTab wt)
        {
            BotTabSimple tab = wt.Listener.Tab;

            if (TabTradableNow(tab) == false || HardDead(tab))
            {
                return false;
            }

            DateTime lastEvent;

            lock (_tabsLocker)
            {
                lastEvent = wt.LastDepthEvent;
            }

            if (lastEvent == DateTime.MinValue)
            {
                return false;
            }

            return (DateTime.Now - lastEvent).TotalSeconds <= StaleTimeoutSec;
        }

        #endregion

        #region Recovery

        public void CheckAll(List<BotTabSimple> tabs, ServerType serverType)
        {
            try
            {
                SyncTabs(tabs);

                if (IsOn == false)
                {
                    return;
                }

                List<WatchedTab> snapshot;

                lock (_tabsLocker)
                {
                    snapshot = new List<WatchedTab>(_tabs);
                }

                int exhaustedDead = 0;

                for (int i = 0; i < snapshot.Count; i++)
                {
                    WatchedTab wt = snapshot[i];
                    BotTabSimple tab = wt.Listener.Tab;

                    if (IsDepthDead(tab) == false)
                    {
                        // сброс инцидента — только при подтверждённо живом стакане, иначе окно
                        // переподключения (IsConnected == false) выглядело бы как «восстановление»
                        if (IsAliveConfirmed(wt))
                        {
                            ResetIfRecovered(wt);
                        }

                        continue;
                    }

                    if (wt.DeadLogged == false)
                    {
                        wt.DeadLogged = true;
                        SendLog("Стакан по " + GetSecurityName(tab) + " мёртв: нет обновлений при живом соединении. Запущено восстановление подписки", LogMessageType.System);
                    }

                    TryReconnectTab(wt);

                    if (wt.ReconnectAttempts >= MaxReconnectAttempts)
                    {
                        exhaustedDead++;
                    }
                }

                if (exhaustedDead >= 2)
                {
                    // мёртвы несколько бумаг с исчерпанными попытками — сбой подписок на уровне сервера
                    TryRestartServer(serverType);
                }
                else if (exhaustedDead == 1)
                {
                    LogRestartRecommendation(serverType);
                }
            }
            catch (Exception error)
            {
                SendLog("MarketDepthWatchdog.CheckAll error: " + error.ToString(), LogMessageType.Error);
            }
        }

        public bool RequestReconnect(BotTabSimple tab, string reason)
        {
            try
            {
                if (tab == null || tab.Connector == null)
                {
                    return false;
                }

                WatchedTab wt;

                lock (_tabsLocker)
                {
                    wt = _tabs.Find(w => ReferenceEquals(w.Listener.Tab, tab));
                }

                if (wt == null)
                {
                    // таб не под наблюдением — переподключаем напрямую, кулдаун вести негде
                    tab.Connector.ReconnectHard();
                    return true;
                }

                return TryReconnectTab(wt);
            }
            catch (Exception error)
            {
                SendLog("MarketDepthWatchdog.RequestReconnect error: " + error.ToString(), LogMessageType.Error);
                return false;
            }
        }

        private bool TryReconnectTab(WatchedTab wt)
        {
            BotTabSimple tab = wt.Listener.Tab;

            if (tab.Connector == null)
            {
                return false;
            }

            lock (_tabsLocker)
            {
                if (wt.ReconnectAttempts >= MaxReconnectAttempts)
                {
                    // попытки исчерпаны — дальше эскалация на сервер или ожидание вмешательства
                    return false;
                }

                if ((DateTime.Now - wt.LastReconnectTry).TotalMinutes < ReconnectCooldownMin)
                {
                    return false;
                }

                wt.LastReconnectTry = DateTime.Now;
                wt.ReconnectAttempts++;
            }

            SendLog("Переподключение подписки " + GetSecurityName(tab) + " (попытка " + wt.ReconnectAttempts + " из " + MaxReconnectAttempts + ")", LogMessageType.System);

            tab.Connector.ReconnectHard();

            return true;
        }

        private void ResetIfRecovered(WatchedTab wt)
        {
            bool hadIncident;

            lock (_tabsLocker)
            {
                hadIncident = wt.ReconnectAttempts > 0 || wt.DeadLogged;

                wt.ReconnectAttempts = 0;
                wt.DeadLogged = false;
            }

            if (hadIncident)
            {
                SendLog("Стакан по " + GetSecurityName(wt.Listener.Tab) + " восстановлен, бумага снова участвует в торговле", LogMessageType.System);
            }
        }

        private void LogRestartRecommendation(ServerType serverType)
        {
            if ((DateTime.Now - _lastServerRestartLog).TotalMinutes < ServerRestartCooldownMin)
            {
                return;
            }

            _lastServerRestartLog = DateTime.Now;

            SendLog("Стакан не восстановлен после " + MaxReconnectAttempts + " переподключений. Рекомендуется переподключить коннектор " + serverType + " (Disconnect/Connect) или перезапустить OsEngine", LogMessageType.Error);
        }

        private void TryRestartServer(ServerType serverType)
        {
            if (ServerRestartAllowed == false)
            {
                LogRestartRecommendation(serverType);
                return;
            }

            if ((DateTime.Now - _lastServerRestart).TotalMinutes < ServerRestartCooldownMin)
            {
                return;
            }

            List<AServer> servers = ServerMaster.GetAServers();

            if (servers == null || servers.Count == 0)
            {
                return;
            }

            AServer server = servers.Find(s => s.ServerType == serverType);

            if (server == null)
            {
                return;
            }

            _lastServerRestart = DateTime.Now;
            _lastServerRestartLog = DateTime.Now;

            SendLog("Автоматический перезапуск сервера " + serverType + ": пересоздание всех подписок стакана", LogMessageType.Error);

            // StopServer/StartServer неблокирующие: выставляют флаг, сам реконнект делает фоновый поток AServer
            server.StopServer();
            server.StartServer();
        }

        private string GetSecurityName(BotTabSimple tab)
        {
            if (tab.Security != null)
            {
                return tab.Security.Name;
            }

            return tab.TabName;
        }

        #endregion
    }
}

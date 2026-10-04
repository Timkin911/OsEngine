using OsEngine.Logging;
using OsEngine.Market;
using OsEngine.Market.Connectors;
using System;

namespace OsEngine.OsTrader.Panels.Tab
{
    /// <summary>
    /// Плагин для программного добавления и удаления бумаг в скринере без участия UI.
    /// Дублирует логику окна «Настройка данных»: изменяет SecuritiesNames,
    /// сохраняет настройки в Engine/<TabName>ScreenerSet.txt и ставит флаг перезагрузки табов.
    /// Если бумага уже есть в списке, но выключена — включает её вместо повторного добавления.
    /// </summary>
    public static class ScreenerSecuritySync
    {
        public static event Action<BotTabScreener, ActivatedSecurity> SecurityAddedEvent;

        public static event Action<BotTabScreener, ActivatedSecurity> SecurityRemovedEvent;

        public static bool AddSecurity(BotTabScreener screener, ActivatedSecurity security)
        {
            try
            {
                if (screener == null || security == null)
                {
                    return false;
                }

                int index = screener.SecuritiesNames.FindIndex(s => s.SecurityName == security.SecurityName);

                if (index != -1)
                {
                    // бумага уже в списке. Если выключена — включаем, таб создастся при перезагрузке
                    if (screener.SecuritiesNames[index].IsOn == true)
                    {
                        // уже включена и ждёт перезагрузки табов
                        return false;
                    }

                    screener.SecuritiesNames[index].IsOn = true;
                    screener.SecuritiesNames[index].SecurityClass = security.SecurityClass;
                    screener.SaveSettings();
                    screener.NeedToReloadTabs = true;

                    if (SecurityAddedEvent != null)
                    {
                        SecurityAddedEvent(screener, screener.SecuritiesNames[index]);
                    }

                    return true;
                }

                screener.SecuritiesNames.Add(security);
                screener.SaveSettings();
                screener.NeedToReloadTabs = true;

                if (SecurityAddedEvent != null)
                {
                    SecurityAddedEvent(screener, security);
                }

                return true;
            }
            catch (Exception error)
            {
                ServerMaster.SendNewLogMessage("ScreenerSecuritySync.AddSecurity error: " + error.ToString(), LogMessageType.Error);
                return false;
            }
        }

        public static bool RemoveSecurity(BotTabScreener screener, string securityName)
        {
            try
            {
                if (screener == null || string.IsNullOrEmpty(securityName))
                {
                    return false;
                }

                int index = screener.SecuritiesNames.FindIndex(s => s.SecurityName == securityName);

                if (index == -1)
                {
                    return false;
                }

                ActivatedSecurity security = screener.SecuritiesNames[index];

                screener.SecuritiesNames.RemoveAt(index);
                screener.SaveSettings();
                screener.NeedToReloadTabs = true;

                if (SecurityRemovedEvent != null)
                {
                    SecurityRemovedEvent(screener, security);
                }

                return true;
            }
            catch (Exception error)
            {
                ServerMaster.SendNewLogMessage("ScreenerSecuritySync.RemoveSecurity error: " + error.ToString(), LogMessageType.Error);
                return false;
            }
        }
    }
}

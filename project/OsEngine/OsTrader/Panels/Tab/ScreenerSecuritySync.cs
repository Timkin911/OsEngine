using OsEngine.Logging;
using OsEngine.Market;
using OsEngine.Market.Connectors;
using System;

namespace OsEngine.OsTrader.Panels.Tab
{
    /// <summary>
    /// Плагин для программного добавления бумаг в скринер без участия UI.
    /// Дублирует логику окна «Настройка данных»: добавляет бумагу в SecuritiesNames,
    /// сохраняет настройки в Engine/<TabName>ScreenerSet.txt и ставит флаг перезагрузки табов.
    /// </summary>
    public static class ScreenerSecuritySync
    {
        public static event Action<BotTabScreener, ActivatedSecurity> SecurityAddedEvent;

        public static bool AddSecurity(BotTabScreener screener, ActivatedSecurity security)
        {
            try
            {
                if (screener == null || security == null)
                {
                    return false;
                }

                // не добавляем бумагу повторно, если она уже в списке и ждёт перезагрузки табов
                if (screener.SecuritiesNames.FindIndex(s => s.SecurityName == security.SecurityName) != -1)
                {
                    return false;
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
    }
}

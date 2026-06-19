using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System.Collections.Generic;

/*Описание
Шаблон
Многострочный комментарий
*/

namespace OsEngine.Robots.MyBots //путь к папке, в которой лежит файлик с роботом внутри проекта
{
    [Bot("Template")]// Название бота. Не забудь поменять тут и еще в 5 местах. Должно совпадать с названием файла (класса)
    public class _Template : BotPanel                                                        // меняем название тут 1
    {
        // Объявляем переменные, которые нужны всему боту
        // Вкладки с графиками. Часто нужна только одна
        BotTabSimple _tab;
        BotTabSimple _tab2;

        // переменные для параметров. Описание внутри параметра чуть дальше говорит само за себя. 3 основных типа тут представлены

        private StrategyParameterString Regime;
        private StrategyParameterDecimal TradingVolume;
        private StrategyParameterInt maLength;
        private StrategyParameterDecimal SL;
        private StrategyParameterDecimal EntrySlippage;
        private StrategyParameterDecimal ExitSlippage;

        // Кнопки. Это тоже типа как параметр, но на событие "нажатие на кнопку" надо отдельно подписываться
        private StrategyParameterButton ResendOrders;

        //глобальные переменные, которые видны всем кускам кода

        private int SomeInt;
        private decimal lastMA;
        private decimal previousMA;


        //индикаторы. Называем так, как будет понятно для нас.

        private Aindicator _ma;


        public _Template(string name, StartProgram startProgram) : base(name, startProgram)  // меняем название тут 2
        {
            // Создаем необходимое количество вкладок

            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];

            TabCreate(BotTabType.Simple);
            _tab2 = TabsSimple[1];

            // Создаем параметры. Для целых int чисел и дробных decimal указываем через запятую значение по умолчанию, а потом еще 3 числа (от, до и шаг оптимизации. можно везде 1 проставить) 

            Regime = CreateParameter("Робот работает?", "Только на закрытие", new[] { "Остановлен", "Включен в обе стороны", "Только в лонг", "Только на закрытие" });
            TradingVolume = CreateParameter("Торговый объем", 1m, 0, 2359, 1);
            maLength = CreateParameter("Длина Скользящей средней", 20, 10, 40, 2);
            SL = CreateParameter("Стоп-лосс в абсолютных значениях", 2000m, 0, 5000, 1);

            ResendOrders = CreateParameterButton("Что-то будет!");

            // У следующих 2 параметров дополнительно в конце указано название вкладки. Так они получают свое "персональное" место. Можно не делать
            EntrySlippage = CreateParameter("Максимально допустимое проскальзывание для входа в поизицию", 100.0m, 0, 2359, 1, "Настройки проскальзывания");
            ExitSlippage = CreateParameter("Максимально допустимое проскальзывание на выход", 200.0m, 0, 2359, 1, "Настройки проскальзывания");

            // Создаем индикатор "Скользящее среднее" на основной вкладке
            _ma = IndicatorsFactory.CreateIndicatorByName("Sma", name + "Sma", false);
            _ma = (Aindicator)_tab.CreateCandleIndicator(_ma, "Prime");                 //именно тут определяется, к какой вкладке относится индикатор
            _ma.ParametersDigit[0].Value = maLength.ValueInt;
            _ma.Save();


            // Подписываемся на события
            _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;
            ParametrsChangeByUser += Template_ParametrsChangeByUser;                        // меняем название тут 3
            _tab.PositionOpeningSuccesEvent += _tab_PositionOpeningSuccesEvent;

            // Включаем кнопки
            ResendOrders.UserClickOnButtonEvent += ResendOrders_UserClickOnButtonEvent;

            // Стартовый расчет переменных (если надо)
            SomeInt = 0;
        }

        //событие "пользователь что-то поменял в окошке с параметрами". Нужно для пересчета индикатора
        private void Template_ParametrsChangeByUser()                                       // меняем название тут 4
        {
            if (_ma.ParametersDigit[0].Value != maLength.ValueInt)
            {
                _ma.ParametersDigit[0].Value = maLength.ValueInt;
                _ma.Reload();
            }
        }


        // Событие, которое запускается при успешном открытии позиции. Обычно тут выставляем стопы.
        private void _tab_PositionOpeningSuccesEvent(Position position)
        {
            if (position.Direction == Side.Buy)
            {
                _tab.CloseAtStop(position, position.EntryPrice - SL, position.EntryPrice - SL - ExitSlippage.ValueDecimal);
            }
            else
            {
                _tab.CloseAtStop(position, position.EntryPrice + SL, position.EntryPrice + SL + ExitSlippage.ValueDecimal);
            }
        }

        //Событие, которое запускается, когда дорисовывается свеча на основном графике.

        private void _tab_CandleFinishedEvent(List<Candle> candles)
        {
            int lastIndex = candles.Count - 1; //индекс последней завершенной свечи на основном ТаймФрейме
            int curTime = 100 * candles[lastIndex].TimeStart.Hour + candles[lastIndex].TimeStart.Minute; //Превращает время в простое число

            List<Position> positions = _tab.PositionsOpenAll; // список всех открытых позиций
            List<Candle> DayCandles = _tab2.CandlesFinishedOnly; // все завершенные свечи на второй вкладке


            // выходим из метода (игнорируем все, что написано дальше), если пользователь выключил робота

            if (Regime.ValueString == "Остановлен" || candles.Count < maLength.ValueInt)
            {
                return;
            }

            lastMA = _ma.DataSeries[0].Last;
            previousMA = _ma.DataSeries[0].Values[_ma.DataSeries[0].Values.Count - 2];


            // Пример создания цикла for

            decimal HH = 0;

            for (int i = candles.Count - 1; i >= candles.Count - maLength.ValueInt; i--)
            {
                if (candles[i].High > HH)
                {
                    HH = candles[i].High;
                }
            }

            // сюда можно прописать правила для выхода из позиции

            if (positions != null && positions.Count != 0 && curTime == 2340)
            {
                _tab.CloseAllAtMarket();
            }

            // выходим из метода (игнорируем все, что написано дальше), если пользователь запретил открытие новых сделок

            if (Regime.ValueString == "Только на закрытие")
            {
                return;
            }

            // Дальше можно прописать логику для входа

            if (positions == null || positions.Count == 0)
            {
                if (curTime == 1700)
                {
                    _tab.BuyAtMarket(TradingVolume.ValueDecimal);
                }
            }
        }

        // Что происходит, когда пользователь нажимает на кнопку
        private void ResendOrders_UserClickOnButtonEvent()
        {
            List<Candle> candles = _tab.CandlesFinishedOnly; // список всех свечей с основной вкладки
            List<Position> positions = _tab.PositionsOpenAll; // список всех открытых позиций

            SendNewLogMessage("Нажата кнопка, но ничего не произошло", Logging.LogMessageType.Error);

        }

        // Передаем название в проект. Просто так надо
        public override string GetNameStrategyType()
        {
            return "Template";                                                                  // меняем название тут 5
        }

        public override void ShowIndividualSettingsDialog()
        {
        }
    }
}
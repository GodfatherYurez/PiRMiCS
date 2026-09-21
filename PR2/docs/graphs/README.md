# Графики

Графики построены по шести CSV-файлам из `../data` согласно [конфигурации эксперимента](../Experiment_Config.md).

| Файл | Содержание |
|---|---|
| `rtt-by-series.png` | Линейные графики RTT по номеру измерения; красный крест означает `timeout`. |
| `summary-metrics.png` | Сравнение mean RTT, финального SRTT и loss rate по сериям. |
| `rtt-distribution.png` | Box plot RTT для `baseline`, `jitter` и `combined`. |
| `summary.csv` | Числовые показатели, использованные в графиках. |

Каждая исходная серия содержит 50 пакетов. Данные можно повторно визуализировать в Excel, LibreOffice или matplotlib без изменения исходного кода.

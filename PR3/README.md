# Практическая работа 3: надёжная доставка поверх UDP

ПР3 расширяет протокол ПР2 заголовочным флагом `requiresAck` и типом `ACK`. Для серии экспериментов надёжной сделана команда `SHOOT`; `MOVEMENT` остаётся ненадёжной. Модуль `Reliability` работает с сериализованными байтами и не обращается к сокетам. В качестве тайм-аута используется оценка RTT из ПР2, дополненная измерениями времени до ACK.

## Состав

| Проект | Назначение |
|---|---|
| `../PR2/Protocol` | Общая сериализация версионированного протокола, включая `requiresAck` и `ACK` |
| `../PR2/Telemetry` | Учёт PING/PONG и получение RTT-сэмплов |
| `Reliability` | `ReliableChannel` и `AdaptiveTimeout`, без сетевого ввода-вывода |
| `Client`, `Server` | Обмен PING/PONG и надёжными SHOOT, эмуляция потерь/задержек |
| `Tests` | Проверки протокола, повтора, предела попыток, ACK-дубликатов и RTO |
| `docs` | Конфигурация шести серий, CSV, отчёт и скрипт графиков |

## Сборка и тесты

Из корня репозитория:

```powershell
dotnet build PR2/Protocol/Protocol.csproj -c Release
dotnet build PR2/Telemetry/Telemetry.csproj -c Release
dotnet build PR3/Reliability/Reliability.csproj -c Release
dotnet build PR3/Server/Server.csproj -c Release
dotnet build PR3/Client/Client.csproj -c Release
dotnet run --project PR3/Tests/Tests.csproj -c Release
```

## Один эксперимент

Сервер принимает параметры `port lossRate delayMs jitterMinMs jitterMaxMs seed`:

```powershell
dotnet run --project PR3/Server -- 7777 0.05 0 0 0 20261004
```

Клиент принимает `host port experiment_id count interval_ms maxAttempts csv_path`:

```powershell
dotnet run --project PR3/Client -- 127.0.0.1 7777 loss_5 50 200 5 PR3/docs/reliability_samples.csv
```

Сервер независимо моделирует потерю входящих датаграмм и исходящих ответов. Он подтверждает надёжный пакет до применения игрового эффекта; дубликат получает повторный ACK, но не вызывает эффект повторно. Каждый запуск клиента добавляет строки в CSV.

## Шесть серий из методички

Перед каждой серией перезапусти сервер с соответствующими параметрами из [Experiment_Config.md](docs/Experiment_Config.md), затем запусти клиент с указанным `experiment_id`. Перед новым полным прогоном удали или переименуй CSV, чтобы не смешать запуски. После получения реальных результатов построй графики командой:

```powershell
python -m pip install matplotlib
python PR3/docs/plot_reliability.py --input PR3/docs/reliability_samples.csv --output PR3/docs/graphs
```

## Документация и границы

Расширенный формат описан в [Protocol_Specification.md](docs/Protocol_Specification.md), методика и место для результатов — в [Reliability_Protocol.md](docs/Reliability_Protocol.md). Для работы используем GitHub и GitHub Desktop: создай ветку `feature/reliable-delivery`, открой issues по подзадачам и Pull Request в `main`, затем попроси участника команды проверить изменения. Методичка формально называет GitVerse; если преподаватель требует именно эту площадку, GitHub нужно согласовать отдельно.

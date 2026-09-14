# Практическая работа 2: измерение RTT UDP

ПР2 продолжает [ПР1](../README.md), но размещена в отдельном каталоге и не изменяет её исходники. Реализованы изолированные модули `Protocol`, `Telemetry` и `Transport`, клиент PING/PONG, сервер с воспроизводимой эмуляцией задержки/джиттера/потерь, автоматические тесты и отчётные данные.

Все команды ниже выполняются из каталога `PR2`. Требуется .NET SDK 8 или новее и runtime .NET 8. [Отчёт и ограничения измерений](docs/Latency_Report.md), [конфигурация экспериментов](docs/Experiment_Config.md), [спецификация](docs/Protocol_Specification.md). CSV содержит только baseline; остальные серии и графики ещё предстоит получить.

## Сборка и тесты

```powershell
dotnet build Protocol/Protocol.csproj -c Release
dotnet build Telemetry/Telemetry.csproj -c Release
dotnet build Transport/Transport.csproj -c Release
dotnet build Server/Server.csproj -c Release
dotnet build Client/Client.csproj -c Release
dotnet run --project Tests/Tests.csproj -c Release
```

## Локальный эксперимент

В первом терминале:

```powershell
dotnet Server/bin/Release/net8.0/Server.dll 7777 0 0 0 0 20260914
```

Во втором:

```powershell
dotnet Client/bin/Release/net8.0/Client.dll 127.0.0.1 7777 baseline 50 200 docs/latency_samples.csv
```

Аргументы сервера: `port delay_ms jitter_min_ms jitter_max_ms loss_rate seed`. Аргументы клиента: `host port experiment_id count interval_ms csv_path`. Тайм-аут фиксирован на 1000 мс. Повторная отправка в ПР2 отсутствует.

## Структура

| Каталог | Ответственность |
|---|---|
| `Protocol` | Явная big-endian сериализация и проверка PING/PONG |
| `Telemetry` | `inFlight`, RTT, SRTT, джиттер и статусы ответов |
| `Transport` | Тонкая UDP-обёртка |
| `Client`, `Server` | Экспериментальный обмен |
| `Tests` | Автоматические проверки контракта и метрик |
| `docs` | Конфигурация, CSV и отчёт |

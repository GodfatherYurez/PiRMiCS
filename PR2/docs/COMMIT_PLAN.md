# Коммитность и наследование

ПР2 наследует архитектурные решения ПР1: C#/.NET 8, UDP, `ushort` sequence и явный `big-endian`. Исходники ПР1 не копируются в рабочие проекты и не изменяются.

Рекомендуемая история ветки `feature/latency-measurement`:

1. `feat(pr2): add explicit ping pong protocol`
2. `feat(pr2): add telemetry and udp transport`
3. `feat(pr2): add configurable measurement client and server`
4. `test(pr2): cover malformed packets and metrics`
5. `docs(pr2): add experiment configuration and latency report`

Публикация в GitVerse/GitHub выполняется владельцем репозитория после проверки имени и email: `git switch -c feature/latency-measurement`, затем отдельные `git add PR2` и коммиты по этапам. Pull Request следует открыть из этой ветки в `main`.
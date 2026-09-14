# Публикация проекта в GitVerse

Команды ниже выполняются после создания пустого репозитория в GitVerse. Значения имени, email и URL нужно брать из своего аккаунта, поэтому они намеренно не подставлены автоматически.

```powershell
git init -b main
git config user.name "ВАШЕ ИМЯ В GITVERSE"
git config user.email "ВАШ EMAIL В GITVERSE"

git add .gitignore .gitattributes
git commit -m "chore: initialize repository"

git switch -c feature/udp-echo
git add PR1_UdpBasics.slnx Protocol Server Client
git commit -m "feat: implement UDP client and server"

git add docs README.md GITVERSE_STEPS.md COMPLIANCE_CHECKLIST.md demo_scripts "КАК_ЗАПУСТИТЬ.txt"
git commit -m "docs: describe architecture and UDP protocol"

git add archives
git commit -m "chore: add source snapshot and Windows demo archives"

git remote add origin URL_РЕПОЗИТОРИЯ_GITVERSE
git push -u origin main
git push -u origin feature/udp-echo
```

После отправки открой Pull Request из `feature/udp-echo` в `main`. В описании укажи:

- реализованы `MOVEMENT` и `SHOOT`;
- сервер хранит авторитетное состояние и журналирует команды;
- заголовок и payload сериализуются в `big-endian`;
- документация находится в каталоге `docs`;
- проверка выполнена локально на `127.0.0.1:7777`.

В результате ветка `main` содержит только стартовый коммит, а вся реализация и документация находятся в `feature/udp-echo`, поэтому Pull Request не будет пустым.

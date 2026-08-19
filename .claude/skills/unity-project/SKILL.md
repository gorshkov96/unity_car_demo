---
name: unity-project
description: Базовые факты проекта Unity — версия редактора, версии пакетов, раскладка папок, git и meta-файлы, проверка компиляции и тестов из терминала. Использовать при создании Unity-проекта, добавлении пакета в manifest.json, изменении структуры Assets, коммите Unity-ассетов, запуске тестов или batch-компиляции.
---

# Проект: версии, структура, проверка

Детали — [docs/unity/14-git-project-setup.md](../../../docs/unity/14-git-project-setup.md)
и [docs/unity/13-testing-ci.md](../../../docs/unity/13-testing-ci.md).

## Жёсткие факты

- Редактор — **6000.3.21f1**, установлен в `/Applications/Unity/Hub/Editor/6000.3.21f1/`.
  Версию проекта брать из `ProjectSettings/ProjectVersion.txt`, а не из головы.
- Render Pipeline — **URP**. HDRP не берём: он не собирается под iOS/Android, а модули стоят.
- Версии пакетов под 6000.3.21f1: URP `17.3.0`, Input System `1.20.0`,
  Cinemachine `3.1.7`, Splines `2.9.0`, AI Navigation `2.0.14`, Test Framework `1.6.0`.
- Весь наш контент — под `Assets/_Project/`. Подчёркивание держит папку сверху списка.

## Правила

1. Пакеты добавляем правкой `Packages/manifest.json` из терминала, а не кликами в Package Manager.
2. В коммит идут **все** `.meta`-файлы, `ProjectSettings/`, `Packages/manifest.json`,
   `packages-lock.json`. Никогда — `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `Build/`.
3. Удаляя ассет, удаляем и его `.meta`. Удаляя папку — её `.meta` тоже.
4. `Force Text` и `Visible Meta Files` в Unity 6.3 стоят по умолчанию — проверить и не трогать.
5. Перед тем как сказать «работает», прогнать проверку компиляции. Она же гоняет EditMode-тесты:

```bash
"/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -accept-apiupdate -projectPath "$PWD" \
  -runTests -testPlatform EditMode \
  -testResults "$PWD/Artifacts/editmode-results.xml" \
  -logFile "$PWD/Artifacts/editmode.log"
```

Коды возврата: `0` — всё хорошо, `2` — тесты упали, `3` — ошибка запуска, в том числе
**ошибка компиляции**, `4` — неизвестная. Занимает десятки секунд; редактор при этом
должен быть закрыт, иначе проект залочен.

6. «Скомпилировалось» и «работает» — разные утверждения. Второе проверяет пользователь
   в Play Mode по чек-листу, который мы ему даём.

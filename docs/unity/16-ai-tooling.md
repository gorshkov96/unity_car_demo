# AI-инструменты и автоматизация редактора Unity

Как агент в терминале меняет Unity-проект без ручных кликов: Unity CLI, MCP-серверы, Editor-скрипты и почему нельзя править YAML сцен. Читать перед тем, как создавать Unity-проект и настраивать рабочий процесс.

Дата сбора материала: 2026-08-19. Unity 6.3 (6000.3.21f1), macOS Apple Silicon.

## TL;DR — решения для нашего проекта

1. **Источник правды для сцены — наш C# Editor-код, а не файл `.unity`.** Пишем класс `WorldBuilder` в `Assets/_Project/Scripts/Editor/`, который создаёт сцену с нуля (`EditorSceneManager.NewScene` → расстановка объектов → `EditorSceneManager.SaveScene`). Файл `.unity` становится сгенерированным артефактом. Это единственный слой, который работает всегда и не зависит от beta-инструментов.
2. **Ставим Unity CLI** — новый отдельный бинарник `unity` (июль 2026), сейчас `1.0.0-beta.5`: `brew install --cask unity-cli`. Он даёт структурированный JSON и предсказуемые коды возврата — то, что агент умеет парсить.
3. **Ставим пакет `com.unity.pipeline`** командой `unity pipeline install`. Он превращает запущенный редактор в программируемую цель: `unity command create_scene`, `unity command run_tests`, `unity eval "return ..."`. Это и есть быстрый цикл «изменил → проверил».
4. **Свои операции проекта регистрируем атрибутом `[CliCommand]`** — любой `static`-метод становится командой, которую агент видит через `unity command` без единой строки интеграционного кода. Например `[CliCommand("build_world")]` поверх нашего `WorldBuilder`.
5. **Подключение к Claude Code — одной командой**: `unity mcp configure claude-code` (MCP-сервер встроен в бинарник `unity`). Плюс `unity skill install claude-code` — Unity кладёт свой навык по работе с CLI прямо в конфиг агента.
6. **Сторонний CoplayDev/unity-mcp — запасной вариант, не основной.** Он зрелее (MIT, 13.5k звёзд, v10.1.2), но это внешняя зависимость на Python/uv и 47 инструментов, из которых `execute_code` выполняет произвольный C# в редакторе. Держим в уме, если экспериментальный Pipeline окажется нестабилен.
7. **Официальный Unity MCP Server (из пакета `com.unity.ai.assistant`) — не берём: он помечен deprecated** в документации пакета, Unity сама рекомендует вместо него CLI. Unity AI Assistant/Generators — платная beta, к автоматизации редактора отношения почти не имеет.
8. **Никогда не правим `.unity`, `.prefab`, `.asset` руками.** Формат — подмножество YAML с числовыми `fileID`, ссылками по GUID из `.meta` и float-ами в hex. Любая правка «на глаз» рвёт ссылки.
9. **Проверка компиляции из терминала** — `-batchmode -nographics -accept-apiupdate -logFile -` + grep по `error CS`, а надёжный гейт — прогон EditMode-тестов (`unity test` или `-runTests -testPlatform EditMode`).
10. **Batch-режим и открытый редактор несовместимы** — Unity не откроет проект в `-batchmode`, пока он открыт в GUI. Значит в повседневной работе цикл идёт через `unity command`/`unity eval` по живому редактору, а `-batchmode` оставляем для чистой проверки.

## 1. Ландшафт на август 2026: три слоя, а не один

За 2026 год картина изменилась радикально. Раньше единственным способом дать агенту доступ к редактору был сторонний MCP-сервер (Model Context Protocol — открытый протокол, по которому LLM вызывает внешние инструменты как функции). Теперь есть три слоя, и они не конкуренты:

| Слой | Что это | Статус | Кому нужен |
|---|---|---|---|
| **Editor-скрипты + `-batchmode`** | Наш собственный C#-код, вызываемый из меню и из командной строки | Стабильно с 2010-х | Всем. База, которая не ломается |
| **Unity CLI + `com.unity.pipeline`** | Официальный бинарник `unity` + пакет, открывающий локальный HTTP-API в живой редактор | CLI — experimental beta, пакет — experimental | Основной путь для агента в 2026 |
| **MCP-серверы** | Обёртка над одним из предыдущих слоёв, отдающая LLM список tool-ов | `unity mcp` — встроен в CLI; CoplayDev — зрелый сторонний | Когда хочется tool-calling вместо bash |

Ключевая мысль из официального анонса: «CLI управляет Unity, пакет Pipeline управляет редактором, а `eval` дотягивается внутрь него». MCP при этом — один из потребителей этой поверхности, а не замена ей.

## 2. Unity CLI — основной инструмент

### Что это

Самостоятельный бинарник `unity` без зависимостей (не путать с исполняемым файлом редактора). Заменяет медленный headless-режим Unity Hub (`"Unity Hub" -- --headless`). Помечен как **experimental**; последний релиз на момент сбора — `1.0.0-beta.5` от 2026-08-13. Совместимость: macOS 14+, Windows 10 21H1+, Linux с glibc 2.34+.

### Установка на macOS

```bash
# Вариант 1 — Homebrew (обновляется вместе с системой)
brew install --cask unity-cli

# Вариант 2 — установочный скрипт с beta-канала
curl -fsSL https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.sh | UNITY_CLI_CHANNEL=beta bash

unity --version
unity doctor        # диагностика окружения, лицензий, конфигурации
```

Обновление: `brew upgrade unity-cli` либо `unity upgrade` (для установки из скрипта).

### Что умеет

```bash
unity --help                       # авторитетный список команд для вашей версии
unity editors -i --format json     # установленные редакторы, машинно-читаемо
unity install 6000.3.21f1 -m ios android
unity open ./MyProject
unity shell                        # много команд в одном тёплом процессе
```

Группы команд: редакторы (`install`, `install-modules`, `editors`, `modules`, `releases`), проекты (`open`, `projects`, `templates`, `build`, `run`, `test`), аккаунт (`auth`, `license`, `cloud`), **живой редактор** (`command`, `list`, `pipeline`, `status`, `mcp`), диагностика (`doctor`, `diagnose`, `logs`, `env`, `config`, `shell`, `completion`).

### Почему это удобно агенту

- **Форматы вывода**: `human` (интерактивный терминал), `tsv` (при пайпе), `json` (`--format json` / `--json`), `ndjson` (потоковые кадры прогресса). Ошибки идут в `stderr`, данные — в `stdout`.
- **Коды возврата**: `0` успех, `1` общая ошибка, `2` ошибка использования (неверные флаги), `3` авторизация, `4` не хватает конфигурации, `6` сама операция провалилась (упал билд, упали тесты), `7` сервис Unity недоступен — можно ретраить, `130` Ctrl+C, `143` SIGTERM.
- Отдельный код `6` для «тесты прогнались и упали» против `1` «джоба сломалась» — ровно то различие, которое нужно скрипту.

Осторожно: прогресс-бары предназначены человеку, парсить их нельзя. Для машинного результата — `--format json`, для потокового — `ndjson`.

## 3. `com.unity.pipeline` — как агент управляет живым редактором

### Установка

```bash
cd /path/to/UnityProject
unity pipeline install          # добавляет пакет в проект
unity pipeline list             # какие проекты его имеют + версия
unity pipeline list-versions
unity status                    # живые редакторы: порт, путь проекта, версия, PID
```

Работает на Unity 6.0 LTS и новее, значит наш 6.3 подходит. Версия пакета на 2026-08-10 — `0.5.0-exp.1`, статус **experimental**.

### Как устроено

Запущенный редактор поднимает локальный HTTP-сервер:

- Слушает **только петлю** `127.0.0.1`; ходить надо именно на `127.0.0.1`, а не на `localhost` (Mono `HttpListener` некорректно обрабатывает IPv6-петлю `::1` и отвечает 400). Порты редактора `7800–7849`, Player `7900–7949`. Запросы с заголовком `Origin` отклоняются, чтобы страница в браузере не достучалась.
- Дескриптор для обнаружения: `<projectPath>/Library/Pipeline/.unity-pipeline-port` — JSON с `pid`, `port`, `unityVersion`, `evalToken`. Лежит в git-ignored `Library/`, права ограничены пользователем.
- Каждый запрос требует `Authorization: Bearer <evalToken>` (256 бит CSPRNG). Токен хранится в `SessionState`, поэтому **переживает domain reload** (перезагрузку скриптовых сборок при рекомпиляции и входе в Play Mode) — начиная с версии 0.4.0. До этого клиент получал вечный 401 после первого же входа в Play Mode.

### Готовые команды (выборка, релевантная нашему проекту)

- **Сцены**: `create_scene`, `open_scene`, `save_scene`, `save_all`, `get_scene_hierarchy`, `add_scene_to_build`.
- **GameObject/компоненты**: `create_gameobject`, `create_gameobjects` (пакетно), `find_gameobjects`, `set_transform`, `set_parent`, `add_component`, `set_component_properties`, `get_component_properties`.
- **Префабы**: `create_prefab`, `instantiate_prefab`, `create_prefab_variant`, `apply_prefab_overrides`, `save_prefab_contents`.
- **Ассеты и скрипты**: `create_asset` (создаёт `ScriptableObject`-ассет), `find_assets`, `set_import_settings`, `create_script`, `attach_script`, `set_serialized_field`, `get_serialized_fields`.
- **Сборка/тесты**: `recompile` + `recompile_status`, `run_tests` + `test_status`, `list_tests`, `build` + `build_status`.
- **Наблюдение**: `get_console_logs`, `get_performance_stats`, `capture_game_view`, `capture_scene_view`, `editor_play` / `editor_stop` / `editor_pause`, `menu` (выполнить пункт меню), `search` (запрос Unity Search).
- **Физика/свет/навигация**: `get_physics_settings` / `set_physics_settings`, `bake_lighting`, `bake_navmesh_surfaces`, `bake_occlusion_culling` — прямо по нашему списку демонстрируемых технологий.

### Свои команды: `[CliCommand]`

Любой статический метод в проекте становится командой. Регистрация не нужна — пакет находит их сам.

```csharp
using Unity.Pipeline.Commands;
using UnityEngine;

public static class WorldPipelineCommands
{
    [CliCommand("build_world", "Regenerate the demo scene from code", Tags = "scenes")]
    public static object BuildWorld(
        [CliArg("seed", "Deterministic seed", Required = false)] int seed = 0)
    {
        var report = WorldBuilder.Rebuild(seed);
        return new { report.SpawnedCount, report.ScenePath };
    }
}
```

- `MainThreadRequired` (по умолчанию `true`) — выполнять в главном потоке Unity; `RuntimeOnly` (по умолчанию `false`) — прятать команду из листинга редактора.
- Метод должен быть `static`; модификатор доступа не важен (`private` подойдёт). Нестатический метод не зарегистрируется, только предупреждение в логе.
- Возвращать можно `string`, число, анонимный объект, типизированную модель или `null` — сервер сам обернёт в JSON.

### Конвенции безопасности, которые стоит скопировать в свой код

Пакет задаёт три правила для мутирующих команд, и они разумны для любого агентского инструмента:

1. **`confirm` / `dry_run`.** Разрушающие или перезаписывающие команды принимают два bool-аргумента: при `dry_run=true` — только описать намерение, при `confirm=false` — отказаться. Чисто аддитивные команды (`create_folder`) этого не требуют.
2. **`AuthoringUndoScope`** — группирует правки сцены в один шаг Undo (Ctrl+Z). Важно: операции `AssetDatabase`, изменения UPM-пакетов и часть настроек **не входят в Undo** — валидируйте до записи, а не надейтесь откатить.
3. **`ProjectPaths.Resolve`** — нормализует путь, отбрасывает `..` и запирает его внутри «authoring root» (по умолчанию `Assets`), чтобы путь от агента не выехал за пределы проекта.

### `eval` — живой REPL внутри редактора

```bash
unity eval "return UnityEditor.EditorApplication.isPlaying;"
unity eval "return UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Length;" --json
unity eval --detach "…"          # длинная операция: вернёт job id
unity command eval_file path/to/script.cs
```

Код компилируется Roslyn и выполняется в главном потоке редактора, то есть достаёт **любой** API движка и редактора. Домен не перезагружается, ответ приходит за миллисекунды вместо секунд полного цикла «правка → рекомпиляция → перезапуск». Доступ закрыт bearer-токеном. С версии 0.5 таймаут поднят с 30 секунд до 24 часов, а долгие команды можно отцеплять (`--detach`) и опрашивать через `unity job status`.

**Нюанс именования:** в анонсе Unity команда пишется как `unity command eval`, а в release notes и справке — как отдельная `unity eval`. Обе формы встречаются в официальных источниках; **сверяйтесь с `unity --help` на своей версии**.

Практическая стратегия: сначала прототипируйте операцию как выражение `eval` по живому редактору, и только когда она докажет свою полезность — «повышайте» её до зарегистрированной `[CliCommand]`. Там и лежит основная экономия времени и токенов.

## 4. Подключение к Claude Code на macOS

### Вариант А (рекомендуемый) — встроенный MCP от Unity CLI

```bash
unity mcp configure claude-code     # регистрирует MCP-сервер в конфиге Claude Code
unity skill install claude-code     # кладёт официальный навык Unity CLI в директорию правил агента
claude mcp list                     # проверить статус подключения
```

`unity mcp` поднимает MCP-сервер, встроенный прямо в бинарник `unity`, и отдаёт команды подключённого редактора как MCP-инструменты. Поддерживается 16 клиентов, включая Claude и Claude Code. С beta.4 сервер сам находит нужный редактор, сопоставляя текущую директорию с зарегистрированными проектами.

Навыки Unity также можно поставить напрямую из официального репозитория: `npx skills add Unity-Technologies/skills` — там `unity-cli`, `unity-package-management`, `ui-uitk`, `validate-urp-render-graph-renderer-feature` и другие.

### Вариант Б — просто bash

MCP не обязателен: `unity` — обычная утилита с JSON-выводом, агент вызывает её через Bash. Минус против MCP: у CLI нет самоописания в виде списка tool-ов, поэтому модель каждый раз «переизобретает» синтаксис — ровно поэтому Unity и выпустила навык. Плюс: нулевая конфигурация и никакого лишнего процесса.

### Вариант В — сторонний CoplayDev/unity-mcp

```bash
# 1. В Unity: Window → Package Manager → + → Add package from git URL
#    https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main
#    (или зафиксировать тег: ...#v10.1.2 ; или: openupm add com.coplaydev.unity-mcp)
# 2. В Unity: Window → MCP for Unity → Configure All Detected Clients
# 3. Если авто-конфигурация не сработала — вручную:
claude mcp add --transport http unityMCP http://localhost:8080/mcp
```

Факты по состоянию на 2026-08-19: лицензия MIT, 13.5k звёзд, последний релиз `v10.1.2` (2026-08-02). Требует Unity 2021.3 LTS → 6.x и Python 3.10+ через `uv`. 47 инструментов. Транспорт по умолчанию HTTP на `localhost:8080/mcp`; stdio-вариант — `uvx --from mcpforunityserver mcp-for-unity --transport stdio`.

**Группы инструментов.** По умолчанию включена только группа `core` (30 инструментов), остальные (`animation`, `ui`, `vfx`, `scripting_ext`, `testing`, `probuilder`, `profiling`, `docs`, `asset_gen`) выключены и активируются мета-инструментом `manage_tools(action="activate", group="vfx")`. Это сделано намеренно: каждый видимый инструмент добавляет токены в каждый вызов, а выбор из 47 инструментов вместо 30 измеримо повышает долю ошибочной маршрутизации. Хорошая дисциплина — включать группу только на время задачи.

**Подводные камни, задокументированные самим проектом:**
- На macOS Unity, запущенная из Finder или Hub, **не наследует PATH вашего шелла**. Если `claude` не находится — запускайте Hub из терминала либо укажите абсолютный путь через «Choose Claude Install Location» в окне MCP for Unity.
- При переключении транспорта HTTP ⇄ stdio **нужно перезапустить Claude Code**. Claude Desktop поддерживает только stdio — плагин настроит его так в любом случае.
- Инструмент `execute_code` выполняет **произвольный C# внутри редактора**; относитесь к нему как к `eval`.
- Опционально ставится Roslyn-валидация (`Window → MCP for Unity → Install Roslyn DLLs`, добавляет define `USE_ROSLYN`), чтобы `validate_script` ловил несуществующие типы до полной компиляции. Большинству не нужна.

Альтернатива того же класса — `IvanMurzak/Unity-MCP` (Apache-2.0, ~3.9k звёзд, релиз `0.88.0` от 2026-08-16), тоже даёт CLI и превращение C#-метода в tool одной строкой.

## 5. Официальные AI-возможности Unity: что реально в 6.3

| Продукт | Пакет | Статус на 2026-08 | Полезно нам? |
|---|---|---|---|
| **Unity AI Assistant** | `com.unity.ai.assistant` | Open beta в Unity 6.3 | Ограниченно: это чат в редакторе, не терминал |
| **Unity AI Generators** | `com.unity.ai.generators` | Beta | Генерация текстур/спрайтов/звуков/анимаций — может пригодиться для контента |
| **Unity MCP Server** | внутри `com.unity.ai.assistant` | **Deprecated**, Unity рекомендует CLI | Нет |
| **AI Gateway** | внутри пакета Assistant | Beta | Подключить свою подписку на модель; бесплатен, кредитов не тратит |
| **Sentis / Inference Engine** | `com.unity.ai.inference` (2.6) | Released | Только для инференса ONNX/LiteRT/PyTorch **в рантайме игры**, к автоматизации редактора отношения не имеет |
| **Unity Muse** | — | **Снят с продукта** | Нет |

Разбор:

- **Muse умер.** Официальная FAQ Unity: «Unity Muse is a deprecated product offering» — это был продукт на собственных моделях Unity, тогда как Unity AI использует сторонние. Все гайды 2023–2024 про Muse Chat / Muse Sprite устарели.
- **Sentis переименовали дважды.** Стал `Inference Engine` при запуске Unity AI в 6.2 (август 2025), а в версии пакета 2.4 отображаемое имя вернули обратно в `Sentis`. Идентификатор всё это время — `com.unity.ai.inference`; в Package Manager ищется по обоим именам. Кода менять не надо.
- **Официальный MCP-сервер Unity помечен deprecated** прямо в документации пакета `com.unity.ai.assistant@2.18`: «Unity MCP server is deprecated. Use the Unity command-line interface (CLI) instead». Если всё же поднимать: релей ставится в `~/.unity/relay/` (Apple Silicon — `relay_mac_arm64.app/Contents/MacOS/relay_mac_arm64`), настраивается в `Edit > Project Settings > AI > Unity MCP Server`, первое подключение требует ручного `Accept`.
- **Про деньги источники расходятся.** Блог Unity перечисляет среди пререквизитов MCP «активный триал или подписку» и привязку к Unity Cloud; FAQ на странице продукта утверждает: «Does the MCP server require an AI tools subscription? No. MCP server is free, with no concurrency limits». Для Personal ассистент после триала стоит $10/мес, Pro/Enterprise — в подписке. Поскольку сервер deprecated, вопрос академический.
- Общее: Unity AI требует Unity 6.0+ (обратной совместимости нет) и привязки проекта к Unity Cloud.

**Вывод для проекта:** из официального AI-стека нам интересны только Generators (когда понадобится контент) и, возможно, Sentis — если захотим показать нейросетевого водителя-бота. К задаче «агент меняет сцену из терминала» ни то, ни другое отношения не имеет.

## 6. Автоматизация редактора без MCP: базовый слой

Этот слой работает независимо от beta-инструментов и обязан существовать в любом случае.

### Найти бинарник Unity на macOS

```bash
ls -1 /Applications/Unity/Hub/Editor
UNITY="/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity"
```

Формат пути `/Applications/Unity/Hub/Editor/<Version>/Unity.app/Contents/MacOS/Unity` приведён в официальной документации по `-logFile`. Никаких `Unity.exe` — это Windows. Лог редактора на macOS: `~/Library/Logs/Unity/Editor.log`.

### Ключевые аргументы командной строки (Unity 6.3)

| Аргумент | Что делает |
|---|---|
| `-batchmode` | Без GUI и без диалогов. При исключении в скрипте Unity немедленно выходит с кодом `1` |
| `-quit` | Выйти после выполнения. **Не поддерживается при `-runTests`** и может подвесить редактор при асинхронном коде |
| `-quitTimeout <sec>` | По умолчанию `-quit` ждёт незавершённые async-задачи 300 секунд |
| `-executeMethod <Namespace.Class.Method>` | Выполнить статический метод при старте. Скрипт **обязан** лежать в папке `Editor`, метод — `static` |
| `-projectPath <path>` | Какой проект открыть |
| `-logFile -` | Дефис = писать лог в консоль (stdout), а не в файл |
| `-nographics` | Не инициализировать графику. Логи в этом режиме выключены — обязательно указывайте `-logFile` |
| `-accept-apiupdate` | **Без него API Updater в batch-режиме не запускается**, что может дать ошибки компиляции |
| `-ignorecompilererrors` | Продолжить старт даже при ошибках компиляции |
| `-disable-assembly-updater` | Пропустить апдейт указанных сборок (ускоряет импорт чужих DLL) |

Способы вернуть ошибку из `-executeMethod`: бросить исключение (код возврата 1) или вызвать `EditorApplication.Exit(код)`. Параметры передаются обычными аргументами и читаются через `System.Environment.GetCommandLineArgs()`.

### Проверка компиляции

```bash
UNITY="/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity"
PROJ="$HOME/IdeaProjects/ai/unity"

"$UNITY" -batchmode -nographics -accept-apiupdate -quit \
  -projectPath "$PROJ" \
  -logFile - 2>&1 | tee /tmp/unity-compile.log

grep -E "error CS[0-9]+" /tmp/unity-compile.log && echo "COMPILE FAILED" || echo "OK"
```

Надёжный сигнал — именно `grep` по `error CS`. То, что Unity сама завершится с ненулевым кодом при ошибках компиляции, косвенно следует из существования флага `-ignorecompilererrors` («продолжить старт даже при ошибках»), но явной гарантии в документации нет — **не полагайтесь только на exit code, проверяйте лог**.

### Прогон тестов

```bash
# через Unity CLI (предпочтительно: NUnit XML + отчёты для CI из коробки)
unity test --project-path "$PROJ"

# напрямую через редактор
"$UNITY" -runTests -batchmode -nographics \
  -projectPath "$PROJ" \
  -testPlatform EditMode \
  -testResults /tmp/results.xml \
  -logFile -
```

Аргументы Unity Test Framework: `-runTests`, `-testPlatform` (`EditMode` / `PlayMode` / значение из `BuildTarget`; по умолчанию EditMode), `-testResults` (NUnit XML), `-testFilter` (список или regex, поддерживает отрицание через `!`), `-testCategory`, `-assemblyNames`, `-repeat`, `-retry`, `-runSynchronously` (всё за один Editor-update, только EditMode). **`-quit` при запущенных тестах не поддерживается.** Единого соглашения по кодам возврата отдельных компонентов Unity нет — читайте XML и текст ошибок; через `unity test` тесты, которые прогнались и упали, дают код `6`.

### Ограничение, которое ломает наивные скрипты

> «You can't open a project in batch mode while the Editor has the same project open; only a single instance of Unity can run at a time.»

То есть если владелец держит проект открытым в редакторе, `-batchmode` упадёт. Отсюда рабочая схема: **повседневный цикл идёт через `unity command` / `unity eval` по живому редактору**, а `-batchmode` используется только когда редактор закрыт (чистая проверка, CI).

### Когда Unity вообще замечает изменения на диске

Asset Database обновляется: при возврате фокуса редактору (если в настройках включён Auto-Refresh), по `Assets > Refresh`, либо по вызову `AssetDatabase.Refresh()` из кода. Агент, который записал `.cs`-файл и сразу спрашивает «скомпилировалось?», получает ответ про старое состояние. Правильно: вызвать `unity command recompile` (Pipeline) / `refresh_unity` (CoplayDev MCP) / `CompilationPipeline.RequestScriptCompilation()`, и только потом читать консоль.

Отдельная жалоба сообщества: редактор иногда требует, чтобы окно было в фокусе, прежде чем действительно обновит ассеты или перезагрузит домен, а модальные диалоги (например «сохранить сцену?») блокируют агента, которому нечем кликнуть. В качестве обходного пути упоминается запуск редактора с флагом `-automated`, который берёт действие по умолчанию во всех диалогах. **Этот флаг не задокументирован в справочнике аргументов Unity 6.3 — не подтверждено официальным источником, проверяйте на своей версии.**

## 7. Создание сцен, префабов и конфигов кодом

Это то, ради чего всё остальное затевалось. API актуальны для 6000.3.

**Сцена.** `EditorSceneManager.NewScene(NewSceneSetup setup, NewSceneMode mode)` — создаёт сцену (`EmptyScene` или `DefaultGameObjects`; `Single` или `Additive`). `EditorSceneManager.SaveScene(scene, "Assets/_Project/Scenes/World.unity")` — путь относительно папки проекта, **папки в пути должны существовать заранее**, иначе сохранение вернёт `false`.

**Префаб.** `PrefabUtility.SaveAsPrefabAsset(go, "Assets/_Project/Prefabs/Car.prefab")` — путь обязан быть внутри `Assets` и заканчиваться на `.prefab`. Есть вариант `SaveAsPrefabAssetAndConnect`, который дополнительно связывает объект в сцене с созданным префабом — обычно нужен именно он. Два подвоха из документации: при перезаписи существующего префаба Unity сопоставляет объекты **по имени**, поэтому дубли имён в иерархии ломают сохранение ссылок; то же касается нескольких компонентов одного типа на одном объекте. И: внутри `StartAssetEditing`/`StopAssetEditing` метод вернёт `null`, даже если сохранение прошло успешно.

**ScriptableObject-конфиг.** `ScriptableObject.CreateInstance<CarConfig>()` → заполнить поля → `AssetDatabase.CreateAsset(obj, "Assets/_Project/Settings/CarConfig.asset")`. Расширение обязано быть нативным (`.asset` для произвольных объектов, `.mat`, `.anim`). `AssetDatabase.CreateAsset` **не создаёт префабы** — для них только `PrefabUtility`. Для ручного создания из редактора добавляйте типу `[CreateAssetMenu(fileName = ..., menuName = ...)]`.

**Пакетные операции.** Оборачивайте серию изменений в `using (new AssetDatabase.AssetEditingScope()) { ... }` — это современная замена паре `StartAssetEditing`/`StopAssetEditing`, которая гарантированно закроется даже при исключении. Внутри такого блока безопасны `ImportAsset`, `MoveAsset`, `CopyAsset`, `AddObjectToAsset`; операции, требующие уже импортированного ассета, — нет.

**Точка входа.** `[MenuItem("Tools/World/Rebuild")]` на статическом методе даёт пункт меню; тот же метод вызывается из `-executeMethod` и оборачивается в `[CliCommand]`. Три способа вызова — одна реализация.

**Где лежит код.** Папка `Editor` (в любом месте под `Assets`) исключает код из сборки игры. Альтернатива и предпочтительный вариант для нас — отдельный `.asmdef` с платформой `Editor`. Важно: `MonoBehaviour`-скрипты из папки `Editor` нельзя навесить на GameObject как компонент.

## 8. Почему `.unity` и `.prefab` нельзя редактировать текстом

В Unity 6.3 режим сериализации по умолчанию — **Force Text** (`Edit > Project Settings > Editor > Asset Serialization`), так что файлы читаемы. «Читаемы» не значит «редактируемы».

Формат — собственное подмножество YAML. Каждый объект (GameObject, компонент, кусок данных сцены) — отдельный YAML-документ:

```yaml
--- !u!1 &6
GameObject:
  m_Component:
  - 4: {fileID: 8}
  m_Name: Cube
--- !u!4 &8
Transform:
  m_GameObject: {fileID: 6}
  m_LocalPosition: {x: -2.618721, y: 1.028581, z: 1.131627}
```

Четыре причины, по которым правки вручную ломают проект:

1. **`!u!1` — числовой ID класса**, `&6` — произвольный `fileID`, уникальный внутри файла; ссылки между объектами (`{fileID: 6}`) идут на эти числа. Добавить объект «по образцу» = придумать непротиворечивый набор ID.
2. **Ссылки между файлами идут по GUID из `.meta`.** Потерянный или пересозданный `.meta` рвёт связи в сценах и префабах.
3. **Float-ы могут храниться в hex IEEE 754** (`myValue: 0x3F800000`), иногда с десятичной подсказкой в скобках `0x3f800000(1)` — парсится только hex.
4. **Префабы сериализуются через модификации.** Инстанс в сцене — это ссылка + список overrides; вложенные префабы и варианты добавляют ещё слой.

### Что делать вместо

- **Генерировать сцену кодом** (раздел 7) — тогда `.unity` вообще не редактируется человеком, а пересоздаётся.
- **Менять точечно через API**: `unity command set_component_properties` / `set_transform` (Pipeline), `set_serialized_field`, `SerializedObject`/`SerializedProperty` в собственном Editor-скрипте.
- **Читать** YAML можно — например, чтобы понять, что в сцене. Но для этого лучше `unity command get_scene_hierarchy`, который вернёт структурированное дерево.

### Git

`UnityYAMLMerge` идёт в комплекте с редактором и умеет семантически сливать `.unity`/`.prefab`. Документация даёт путь `/Applications/Unity/Unity.app/Contents/Helpers/UnityYAMLMerge`; для Hub-установки ищите через `find /Applications/Unity/Hub/Editor/6000.3.21f1 -name UnityYAMLMerge`. Конфигурация git:

```
[merge]
    tool = unityyamlmerge
[mergetool "unityyamlmerge"]
    trustExitCode = false
    cmd = '<path to UnityYAMLMerge>' merge -p "$BASE" "$REMOTE" "$LOCAL" "$MERGED"
```

Альтернатива — пометить `*.unity`, `*.prefab`, `*.asset` в `.gitattributes` как бинарные, чтобы git не мержил их сам. Для проекта из одного человека это разумный дефолт: конфликтов не будет, а генерация сцены кодом делает мерж ненужным. `.meta`-файлы коммитим всегда — правило уже зафиксировано в нашем CLAUDE.md.

## 9. Рекомендуемый процесс для этого проекта

**Слой 1 — фундамент (делаем сразу, не зависит ни от чего).**
`Assets/_Project/Scripts/Editor/` с отдельным `.asmdef` (платформа Editor). Внутри — `WorldBuilder` со статическим методом `Rebuild()`, который целиком создаёт демо-сцену: террейн/дорога, машина из префаба, трафик, свет, партиклы. Три точки входа на один метод: `[MenuItem("Tools/World/Rebuild")]`, `-executeMethod`, `[CliCommand("build_world")]`. Сцена `.unity` — сгенерированный артефакт; в git она попадает, но правится только через `Rebuild()`.

**Слой 2 — быстрый цикл (ставим сразу после создания проекта).**
`brew install --cask unity-cli`, затем `unity pipeline install`. Агент работает так: правит C# → `unity command recompile` → `unity command get_console_logs` → `unity command build_world` → `unity command capture_scene_view` (посмотреть результат) → `unity command run_tests`. Для разовых вопросов о живой сцене — `unity eval`.

**Слой 3 — удобство (опционально).**
`unity mcp configure claude-code` + `unity skill install claude-code`, если захочется, чтобы Claude Code видел команды редактора как tool-ы, а не вызывал bash.

**Что даёт эта схема.** Владелец никуда не кликает: он открывает Unity, оставляет редактор запущенным, а всё остальное агент делает из терминала. При этом ни один результат не «живёт» только в чате — любое изменение сцены выражено кодом в репозитории и воспроизводимо одной командой. Если Pipeline (experimental) сломается на очередном апдейте, слой 1 продолжает работать, а слой 2 можно временно заменить CoplayDev MCP или чистым `-batchmode -executeMethod`.

**Что зафиксировать в CLAUDE.md проекта, когда создадим Unity-проект:** путь к бинарнику редактора, команду проверки компиляции, команду прогона тестов и правило «сцена меняется только через WorldBuilder».

## 10. Антипаттерны

1. **Править `.unity` / `.prefab` / `.asset` руками или скриптом на sed.** `fileID`, GUID и hex-float делают это лотереей. Даже успешная на вид правка может тихо разорвать ссылку.
2. **Запускать `-batchmode` при открытом редакторе.** Unity откажется: один инстанс на проект. Скрипт, который «иногда падает», обычно падает именно поэтому.
3. **Забыть `-accept-apiupdate` в batch-режиме.** API Updater не отработает, и вы получите ошибки компиляции, которых нет в GUI.
4. **Комбинировать `-quit` с `-runTests`.** Официально не поддерживается: редактор выйдет до завершения тестов.
5. **Считать, что Unity сразу увидела записанный файл.** Без `Refresh`/`recompile` агент читает старое состояние и «чинит» несуществующие ошибки.
6. **Включать в MCP-сервере все группы инструментов.** Раздувает промпт и измеримо повышает долю неверных вызовов. Держите `core`, активируйте остальное точечно.
7. **Строить продакшн-логику на `execute_code` / `eval`.** Это отладочный инструмент. Всё, что повторяется, должно стать `[CliCommand]` или Editor-методом в репозитории.
8. **Опираться на официальный Unity MCP Server из пакета Assistant.** Он помечен deprecated самой Unity.
9. **Гуглить «Unity Muse» и «Unity Sentis» как актуальные решения.** Muse снят с производства, Sentis — рантайм-инференс, к автоматизации редактора не относится.
10. **Использовать headless-режим Unity Hub (`"Unity Hub" -- --headless`).** Заменён на `unity` CLI; старый путь заметно медленнее, и это накапливается на каждом вызове.
11. **Писать Editor-код вне папки `Editor` / Editor-asmdef.** Он попадёт в билд и сломает сборку игры (`UnityEditor` недоступен в рантайме). И наоборот: совет 2019–2022 «переключите Asset Serialization в Force Text» устарел — в 6.3 это дефолт.
12. **Ждать, что MCP заменит архитектуру.** Агент, который расставляет кубы через `create_gameobject` и не сохраняет это в код, оставляет проект без воспроизводимости — прямое нарушение принципа «сцены насколько возможно как код».
13. **Парсить человекочитаемый вывод и прогресс-бары `unity`.** Документация прямо просит этого не делать: есть `--format json` и `ndjson`.

## 11. Чек-лист

**Перед созданием Unity-проекта**

- [ ] `brew install --cask unity-cli`, затем `unity --version` и `unity doctor`
- [ ] `unity editors -i --format json` — убедиться, что 6000.3.21f1 виден CLI
- [ ] Зафиксировать путь `/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity`

**Сразу после создания проекта**

- [ ] `Assets/_Project/Scripts/Editor/` + `.asmdef` с `includePlatforms: ["Editor"]`
- [ ] `WorldBuilder.Rebuild()` со всеми тремя точками входа (`[MenuItem]`, `-executeMethod`, `[CliCommand]`)
- [ ] `unity pipeline install`; проверить `unity status` и `unity command` (список команд)
- [ ] Скрипт проверки компиляции с `grep -E "error CS"`; первый EditMode-тест, `unity test` возвращает 0
- [ ] `Asset Serialization` = `Force Text` (дефолт, но проверить); `.meta`-файлы коммитятся

**Перед каждым изменением сцены агентом**

- [ ] Изменение выражено кодом, а не только вызовом MCP-инструмента
- [ ] После правки `.cs` — `unity command recompile`, потом чтение консоли
- [ ] Мутирующая своя команда имеет `confirm` / `dry_run`; пути проходят через `ProjectPaths.Resolve`
- [ ] После генерации — визуальная проверка через `unity command capture_scene_view`

**Если вместо Pipeline используем CoplayDev MCP**

- [ ] Версия пакета зафиксирована тегом (`#v10.1.2`), а не `#main`; включена только группа `core`
- [ ] После смены транспорта HTTP ⇄ stdio перезапущен Claude Code; `claude mcp list` показывает `✔ Connected`

## 12. Видео и доклады

- **Unity command-line interface (CLI)** — Unity (официальный канал) — https://www.youtube.com/watch?v=DgNrgZeJOxQ — обзор от Unity к анонсу CLI; тот самый ролик, на который ссылается блог-пост. Смотреть первым.
- **Get started with Unity MCP** — Unity — https://www.youtube.com/watch?v=2sswkdV1y3c — официальный разбор MCP-сервера из пакета Assistant. Полезен для понимания архитектуры, но помните, что сам сервер помечен deprecated.
- **Opening Unity to everyone — one API, every role** — Unity — https://www.youtube.com/watch?v=naXXD0U9hYQ — как Unity видит единую программируемую поверхность движка; контекст, зачем появился Pipeline.
- **Setup Unity CLI + AI Agent From Scratch (Full Guide)** — immersive insiders — https://www.youtube.com/watch?v=t4-ZUi6D8oE — практическая сборка связки «Unity CLI + агент» с нуля, свежее (август 2026).
- **Unity + Claude Code Tutorial — How to set up Claude Code to work with Unity MCP Server 6 on MacOS** — Alan O'Toole — https://www.youtube.com/watch?v=ZrlaCST7YCM — единственный найденный разбор именно под macOS; закрывает вопрос с PATH и правами.
- **How to Setup Unity MCP with Claude Code, Codex & Gemini CLI** — Building Aeon — https://www.youtube.com/watch?v=EDBZ7xkCvdo — сравнение настройки под три разных CLI-агента.
- **Claude AI + MCP in Unity: The Complete Setup & Workflow (2026)** — Synty Studios — https://www.youtube.com/watch?v=UZCekUCWgGc — про рабочий процесс целиком, а не только про установку.
- **14 Months of Unity Dev with Claude Code** — Mythmatic — https://www.youtube.com/watch?v=bT4638EScek — длинный опыт «как не надо»: где агент ломает проект и какие ограничения выставлять.
- **TUTORIAL: How to use Claude Code with Unity Engine** — Ivan Murzak — https://www.youtube.com/watch?v=xUYV2yxsaLs — от автора альтернативного MCP-сервера; полезен как второе мнение об архитектуре моста.
- **Stop Wasting Time! Intro to Unity Editor Scripting & Automation** — MUS Labs — https://www.youtube.com/watch?v=gGQRGZPLLaA — базовый Editor-scripting: `[MenuItem]`, кастомные окна, автоматизация рутины.

## 13. Источники

Все ссылки проверены 2026-08-19.

**Unity CLI и Pipeline (официальное)**
- https://unity.com/blog/meet-the-unity-cli — анонс, 2026-07-20
- https://docs.unity.com/en-us/unity-cli + `/use-unity-cli` (установка, macOS 14+), `/unity-cli-reference` (команды, форматы, коды возврата), `/release-notes` (1.0.0-beta.5 от 2026-08-13: `unity mcp`, `unity eval`, `unity skill install`)
- https://docs.unity3d.com/Packages/com.unity.pipeline@0.5/manual/index.html + `creating-commands.html` (`[CliCommand]`/`[CliArg]`), `safety-and-mutations.html` (`confirm`/`dry_run`, `AuthoringUndoScope`, `ProjectPaths`), `connectivity.html` (порты, дескриптор, токен, пути логов), `../changelog/CHANGELOG.html` (0.5.0-exp.1 от 2026-08-10)
- https://github.com/Unity-Technologies/skills — официальные AI-навыки Unity

**Unity AI (официальное)**
- https://unity.com/features/ai — FAQ: Muse deprecated, стоимость, Unity 6.0+ и Unity Cloud
- https://unity.com/blog/unity-ai-mcp-how-to-get-started — MCP-сервер Unity, пути релея
- https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.18/manual/integration/unity-mcp-overview.html — пометка «Unity MCP server is deprecated. Use the Unity CLI instead»
- https://docs.unity3d.com/Packages/com.unity.ai.inference@2.6/manual/index.html — Sentis / Inference Engine, смена имени в 2.4

**Unity Manual / Scripting API 6000.3** (префикс `https://docs.unity3d.com/6000.3/Documentation/`)
- `Manual/EditorCommandLineArguments.html` — аргументы редактора; `Manual/test-framework/reference-command-line.html` и `.../run-tests-from-command-line.html` — аргументы Test Framework
- `Manual/SmartMerge.html` — UnityYAMLMerge; `Manual/TextSceneFormat.html` и `Manual/FormatDescription.html` — YAML, `!u!`, `fileID`, hex-float; `Manual/class-EditorManager.html` — Force Text по умолчанию
- `Manual/AssetDatabaseRefreshing.html` — когда обновляется Asset Database; `Manual/SpecialFolders.html` — папка `Editor`
- `ScriptReference/<Page>.html` для: `SceneManagement.EditorSceneManager.NewScene`, `...SaveScene`, `PrefabUtility.SaveAsPrefabAsset`, `AssetDatabase.CreateAsset`, `AssetDatabase.AssetEditingScope`, `ScriptableObject.CreateInstance`, `CreateAssetMenuAttribute`, `MenuItem`, `Compilation.CompilationPipeline.RequestScriptCompilation`, `EditorApplication.Exit`

**Сторонние MCP-серверы**
- https://github.com/CoplayDev/unity-mcp (README, MIT) и https://coplaydev.github.io/unity-mcp/ → `getting-started/install`, `reference/tools` (каталог 47 инструментов), `guides/tool-groups`, `guides/roslyn`
- https://github.com/CoplayDev/unity-mcp/wiki/2.-Fix-Unity-MCP-and-Claude-Code — PATH на macOS, перезапуск при смене транспорта
- https://github.com/IvanMurzak/Unity-MCP — альтернативный сервер, Apache-2.0

**Прочее**
- https://docs.claude.com/en/docs/claude-code/mcp — `claude mcp add --transport http`, `claude mcp list`
- https://vindler.solutions/blog/unity-cli-agent-automation — разбор Unity CLI beta: баг с токеном при domain reload, требование фокуса редактора, флаг `-automated`, замеры латентности
- https://support.unity.com/hc/en-us/articles/40828087523092-Resolving-the-The-project-is-currently-open-in-the-Unity-Editor-Please-close-it-in-the-Editor-to-proceed-with-this-operation-Error — ограничение одного инстанса

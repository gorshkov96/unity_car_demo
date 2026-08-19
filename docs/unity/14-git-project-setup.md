# Создание проекта, структура и git (Unity 6.3)

Разовая настройка: как создать Unity-проект из терминала, что положить в git, как разложить папки и `.asmdef`. Заглядывать при старте проекта, при добавлении модуля кода и когда git ведёт себя странно.

Помеченное «проверено» реально выполнено здесь: macOS (Apple Silicon), Unity **6000.3.21f1**, Unity Hub **3.20.0**, git **2.50.1**, 2026-08-19.

---

## TL;DR — решения для нашего проекта

1. **Проект создаём из терминала**: `-createProject` + `-cloneFromTemplate` с локальным `.tgz`. Команда проверена, exit 0 (раздел 1).
2. **Шаблон — `com.unity.template.3d-cross-platform-17.0.14.tgz`** из состава редактора. Внутри это `com.unity.template.urp-blank`, displayName «3D URP». Имя файла обманчиво: «cross-platform» здесь = URP.
3. **Настройки под VCS в 6.3 уже верные по умолчанию**: `Force Text` + `Visible Meta Files`. Менять нечего — надо проверить и закоммитить `ProjectSettings/`.
4. **Режим VCS переехал** из `EditorSettings.asset` в отдельный `ProjectSettings/VersionControlSettings.asset`. Советы «поставь `m_ExternalVersionControlSupport`» устарели.
5. **`.gitignore` — официальный** github/gitignore + `.DS_Store`. Контрольная цифра: свежий URP-проект даёт ровно **61 файл** в индексе (проверено). Тысячи файлов = `.gitignore` не подхватился.
6. **Коммитим:** `Assets/` целиком (все `.meta`), `ProjectSettings/`, `Packages/manifest.json`, `Packages/packages-lock.json`. Никогда: `Library/`, `Temp/`, `Logs/`, `UserSettings/`.
7. **UnityYAMLMerge подключаем сразу**, не «когда появятся конфликты». Проверено: две независимые правки одной сцены слились без конфликтов.
8. **Git LFS пока НЕ включаем** — он даже не установлен, а бинарных ассетов нет. Включить до первого коммита FBX/текстуры, не после.
9. **`Assets/_Project/` + `.asmdef` на модуль.** Проверено: четыре руками написанных `.asmdef` компилируются из терминала без ошибок, EditMode-тесты проходят headless.
10. **Cinemachine ставим явно версией `3.1.7`** — редактор по умолчанию подтянет 2.10.7 со старым API.

---

## 1. Создание проекта из терминала

`Unity.app` — это *бандл* (папка, которую Finder показывает одним файлом), поэтому путь идёт «внутрь» приложения. Шаблоны — `.tgz`-архивы; лежат либо внутри редактора, либо в `~/Library/Application Support/UnityHub/Templates/` (скачанные Hub'ом).

Проверено, в 6000.3.21f1 встроены три шаблона: `2d-cross-platform-2d-6.1.2`, **`3d-cross-platform-17.0.14`** (URP, наш) и `3d-high-end-17.0.7` (HDRP). Документация Unity в примере называет URP-шаблон `com.unity.template.urp-blank-17.1.0.tgz` — так называется файл, скачанный Hub'ом. Не копируйте имя из документации вслепую, сначала `ls`.

### Команда (проверена, exit 0)

```bash
ls -1 /Applications/Unity/Hub/Editor/                  # какие версии установлены
E=/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents
UNITY="$E/MacOS/Unity"
ls -1 "$E/Resources/PackageManager/ProjectTemplates/"  # какие шаблоны есть
TPL="$E/Resources/PackageManager/ProjectTemplates/com.unity.template.3d-cross-platform-17.0.14.tgz"

"$UNITY" -batchmode -quit -nographics \
  -createProject "$HOME/IdeaProjects/ai/unity/MachineWorld" \
  -cloneFromTemplate "$TPL" \
  -logFile "$PWD/unity-create.log"
echo "exit=$?"
```

Аргументы (справочник Unity 6.3): `-createProject <path>` — создать проект; `-cloneFromTemplate <path>` — взять содержимое шаблона, работает только вместе с `-createProject`; `-batchmode` — без окон и диалогов; `-nographics` — не инициализировать GPU; `-quit` — выйти по завершении; `-logFile <path>` — лог (`-logFile -` шлёт в stdout). Первый запуск идёт минуты: импорт пакетов и компиляция шейдеров, смотреть `tail -f unity-create.log`.

**`-quit` нельзя совмещать с `-runTests`** — редактор закроется до окончания тестов.

### Альтернативы

**Unity Hub (GUI)** — годится для первого проекта, если хочется выбрать шаблон глазами; не автоматизируется.

**Unity CLI** — отдельный бинарник, не редактор. `brew install --cask unity-cli`, команда `unity`. Умеет `install`, `editors`, `open`, `projects` (в т.ч. `create`), `templates`, `build`, `test`, `mcp`. Документация помечает его как **experimental**, а флаги `projects create` не расписывает — смотреть `unity projects create --help`. Пока не наш выбор.

**Hub CLI** (`"Unity Hub.app" -- --headless ...`) **устарел с Hub 3.18.0** (у нас 3.20.0), создавать проекты он и не умел. Все рецепты 2021–2023 с `--headless` — в мусор.

---

## 2. Версия редактора: `ProjectVersion.txt`

Файла `.unityversion` в Unity нет. Версия лежит в `ProjectSettings/ProjectVersion.txt` — обычный текст, который Unity пишет сам (проверено):

```
m_EditorVersion: 6000.3.21f1
m_EditorVersionWithRevision: 6000.3.21f1 (c02631ffc030)
```

По нему Hub выбирает редактор для открытия. **Обязателен в git**, руками не трогать. Апгрейд минорной версии редактора — отдельный коммит, где меняется только этот файл (и, возможно, `packages-lock.json`).

---

## 3. Настройки проекта под контроль версий

Совет «зайди в Editor Settings и поставь Force Text + Visible Meta Files» родом из 2018 года. В 6.3 **оба значения дефолтные**: документация про Asset Serialization Mode — «Force Text… This is the default option», про Visible Meta Files — «This is the default setting».

Более того, **режим VCS переехал в свой файл** (проверено на свежем проекте):

```yaml
# ProjectSettings/VersionControlSettings.asset
VersionControlSettings:
  m_Mode: Visible Meta Files

# ProjectSettings/EditorSettings.asset
EditorSettings:
  m_SerializationMode: 2              # 2 = Force Text
  m_LineEndingsForNewScripts: 0       # 0 = OS Native
  m_SerializeInlineMappingsOnOneLine: 1
```

Ключа `m_ExternalVersionControlSupport` в `EditorSettings.asset` больше нет.

**Что это значит по-русски.** `.meta` — спутник каждого ассета, хранит его GUID (уникальный идентификатор) и настройки импорта; сцены и префабы ссылаются друг на друга **по GUID, а не по пути**. Потерянный `.meta` = у всех остальных отвалились ссылки, а Unity молча выдаст новый GUID. **Force Text** — Unity пишет сцены и префабы в YAML (текст), который можно смотреть в diff и сливать; Binary — нельзя. **Reduce version control noise** (`m_SerializeInlineMappingsOnOneLine`) пишет ссылки в одну строку вместо переноса на 80 символах, убирая ложные изменения в diff.

**Как задать режим VCS из терминала** — документированный способ один, аргумент редактора:

```bash
"$UNITY" -batchmode -quit -projectPath "$PROJ" -vcsMode "Visible Meta Files" -logFile -
```

Программного API нет: `EditorSettings.serializationMode` и `EditorSettings.externalVersionControl` **отсутствуют в справочнике Scripting API Unity 6.3** (проверено по полному индексу `ScriptReference`). Editor-скрипт вокруг них писать бессмысленно.

---

## 4. `.gitignore`

```bash
curl -fsSL https://raw.githubusercontent.com/github/gitignore/main/Unity.gitignore -o .gitignore
printf '\n# macOS\n.DS_Store\n._*\n' >> .gitignore
```

Что закрывает и почему (формулировки Unity из «Default project directories»): `Library/` — локальный кеш импорта, «unique to your computer»; `Temp/` — чистится при закрытии редактора; `Logs/`; `UserSettings/` — личные настройки, «exclude to avoid overwriting your teammates' personal Unity preferences»; `obj/`, `*.csproj`, `*.sln`, `*.slnx` — Unity генерирует их из `.asmdef` при каждом открытии; `Build/`, `Builds/`.

**Контрольная проверка (проверено).** `git add -A && git diff --cached --name-only | wc -l` на свежем URP-проекте даёт **61** файл: 34 в `Assets`, 24 в `ProjectSettings`, 2 в `Packages`, плюс сам `.gitignore`; `.git` весит 360K. Тысячи файлов или сотни мегабайт = `.gitignore` создан не в корне проекта либо `Library/` попал в индекс раньше него. Лечится `git rm -r --cached Library`.

---

## 5. `.gitattributes`

Два дела: концы строк и подключение smart merge.

```gitattributes
* text=auto eol=lf

*.unity  merge=unityyamlmerge eol=lf
*.prefab merge=unityyamlmerge eol=lf
*.asset  merge=unityyamlmerge eol=lf
*.mat    merge=unityyamlmerge eol=lf
*.anim   merge=unityyamlmerge eol=lf
*.controller        merge=unityyamlmerge eol=lf
*.meta              merge=unityyamlmerge eol=lf
*.physicMaterial    merge=unityyamlmerge eol=lf
*.physicsMaterial2D merge=unityyamlmerge eol=lf

*.cs diff=csharp text eol=lf
*.shader text eol=lf
*.hlsl   text eol=lf
*.asmdef text eol=lf
*.json   text eol=lf

# бинарное — не трогать концы строк; список пополняем по мере появления ассетов
*.fbx binary
*.FBX binary
*.png binary
*.jpg binary
*.tga binary
*.exr binary
*.wav binary
*.ogg binary
*.blend binary
*.dll binary
```

**Ловушка macOS: регистр.** APFS здесь **регистронечувствительна** (проверено: `touch CaseTest.txt` находится как `casetest.txt`), а паттерны `.gitattributes` — регистрочувствительны. Поэтому `*.fbx` и `*.FBX` пишутся обеими строками. По той же причине переименование `Player.cs` → `player.cs` git не заметит без `git mv`, а два ассета, различающиеся только регистром, сломают проект у любого на регистрочувствительной ФС.

`merge=unityyamlmerge` сам по себе ничего не делает — драйвер объявляется в git-конфиге, см. ниже.

---

## 6. UnityYAMLMerge (smart merge)

**Что это.** Утилита из состава редактора, сливающая сцены и префабы с пониманием структуры Unity-YAML, а не построчно. Без неё две правки *разных* объектов одной сцены дают конфликт.

**Где лежит на macOS (проверено).** Документация даёт устаревший путь `/Applications/Unity/Unity.app/Contents/Helpers/UnityYAMLMerge` — он верен для ручной установки, но не для Hub. Реально бинарник `<...>/6000.3.21f1/Unity.app/Contents/Helpers/UnityYAMLMerge`, а правила — `<...>/Unity.app/Contents/Resources/UnityYAMLMerge/{mergerules,mergespecfile}.txt` (документация указывает `Editor/Data/Tools` — это раскладка Windows).

**Подключение (проверено, git принимает).** Конфиг локальный для репозитория, т.к. содержит версию редактора в пути:

```bash
YM="/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/Helpers/UnityYAMLMerge"
git config merge.tool unityyamlmerge
git config mergetool.unityyamlmerge.trustExitCode false
git config mergetool.unityyamlmerge.cmd "\"$YM\" merge -p \"\$BASE\" \"\$REMOTE\" \"\$LOCAL\" \"\$MERGED\""
git config mergetool.keepBackup false
```

При конфликте — `git mergetool`. Флаги: `-p` premerge (слить бесконфликтное, остальное отдать fallback-инструменту), `-h` headless без диалогов (обязателен в скриптах), `--force` — сливать файлы с неизвестным расширением.

**Функциональная проверка (выполнена).** Взяли `SampleScene.unity`; в одной версии `Directional Light` → `Sun Light`, в другой `Main Camera` → `Player Camera`. Команда `UnityYAMLMerge merge -p -h base left right out` вернула `exit=0`, а в результате оказались **обе** правки — ноль конфликтов. Построчный merge здесь бы сломался.

---

## 7. Git LFS — когда и как

**Что это.** Git Large File Storage: вместо файла в репозиторий кладётся текстовый указатель, содержимое уезжает на отдельный сервер.

**Сейчас не нужен** — проверено, `git lfs` не установлен, а бинарных ассетов нет: примитивы Unity весят ноль. **Включать до первого коммита крупного бинарника**: ретроспективный перенос требует `git lfs migrate import --everywhere`, который переписывает историю и ломает клоны у всех.

```bash
brew install git-lfs
git lfs install                       # ставит хуки в ТЕКУЩИЙ репозиторий
git lfs track "*.fbx" "*.FBX" "*.png" "*.psd" "*.tga" "*.exr" "*.wav" "*.ogg" "*.mp4" "*.blend"
git add .gitattributes                # track дописывает правила именно туда
```

В LFS имеет смысл: модели, текстуры, звук, видео, шрифты, `.dll`, `.unitypackage`. **Нельзя**: `.unity`, `.prefab`, `.asset`, `.meta`, `.cs`, `.asmdef`, `.json` — это текст, он должен диффиться и сливаться.

Подводные камни:

- **`git lfs install` — на репозиторий, не на систему.** Классический провал: LFS «установлен», хуков в свежем клоне нет, гигабайты тихо уходят в обычные packfile'ы. Проверка: `git lfs ls-files | head`, `git lfs status`.
- **LFS не дельтит.** Файл, меняющийся каждый коммит, хранится целиком каждый раз.
- **Блокировки требуют LFS.** Позиция Unity: «Git requires Git LFS to be initialized in order to support file locking». В команде — `git lfs track "*.fbx" --lockable`, после чего файлы read-only, пока не залочены.
- **Проект в iCloud Drive / Dropbox — путь к порче данных.** Unity: «Using cloud-based storage methods to store your project is an unsupported workflow. It can cause synchronization issues which corrupt your project.» На macOS `~/Documents` часто синхронизируется iCloud'ом по умолчанию — держать проект вне её.

---

## 8. Что коммитить

**Всегда:** `Assets/**` целиком, включая **каждый** `.meta`; `ProjectSettings/**` (в т.ч. `ProjectVersion.txt`, `EditorSettings.asset`, `VersionControlSettings.asset`, `URPProjectSettings.asset`, `TagManager.asset`, `DynamicsManager.asset`, `QualitySettings.asset`); `Packages/manifest.json`; `Packages/packages-lock.json` — документация Unity: «Put the lock file under source control so you can consistently reproduce the same package set»; `.gitignore`, `.gitattributes`.

**Никогда:** `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `Build/`, `obj/`, `*.csproj`, `*.sln`.

**Правило:** ассет и его `.meta` — всегда в одном коммите. Переименование и перемещение — только внутри редактора либо `git mv` обоих файлов сразу.

---

## 9. Структура `Assets/` и зачем `_Project`

Шаблон кладёт (проверено): `Scenes/SampleScene.unity`, `Settings/` (URP-ассеты и Volume-профили), `TutorialInfo/`, `Readme.asset`, `InputSystem_Actions.inputactions`. `TutorialInfo/` и `Readme.asset` — памятка шаблона, удалить вместе с `.meta` в первом же коммите.

**Зарезервированные имена папок** (особый смысл где угодно внутри `Assets/`): `Editor` — код только для редактора, не идёт в сборку; `Resources` — грузится через `Resources.Load`, **раздувает билд и память**, использовать точечно; `Plugins` — сторонние библиотеки; `StreamingAssets` — файлы в исходном виде; `Gizmos` и `Editor Default Resources` — только в корне `Assets/`.

Импортёр **игнорирует**: скрытые папки, всё начинающееся с `.`, всё заканчивающееся на `~`, папки `cvs`, файлы `.tmp`. Отсюда трюк: `Docs~/` рядом с кодом не индексируется Unity, но остаётся в git.

**`_Project` — почему подчёркивание.** Это соглашение сообщества, **а не требование Unity**. Подчёркивание сортируется раньше букв, поэтому наша папка всегда первая в Project-окне, выше всего, что притащат пакеты и Asset Store. Второй смысл — граница «наше / чужое»: чужое можно снести целиком, наше нет. Оговорка из документации: папку с именем на точку редактор автоматически переименует в подчёркивание (`.folder` → `_folder`); с подчёркиванием такого не происходит, оно безопасно.

```
Assets/
  _Project/
    Scripts/{Core,Vehicle,World,Gameplay,UI}/
    Prefabs/  Materials/  Models/  Audio/  Scenes/  Settings/
    Tests/{EditMode,PlayMode}/
  Plugins/
  Settings/            # URP-ассеты из шаблона — оставляем на месте
```

Имя папки = зона ответственности. Папки `Misc`/`Common`/`Utils` — признак того, что модулю не хватает своего имени.

---

## 10. Assembly Definitions

**Что это.** `.asmdef` — JSON-файл, превращающий папку и всё под ней в отдельную сборку (`.dll`). Даёт две вещи: правка одного модуля не перекомпилирует остальные, и зависимости становятся явными — `Vehicle` физически не сможет обратиться к `UI`, если не сослался.

Обязательное поле одно — `name`. Остальные: `references`, `includePlatforms`/`excludePlatforms` (взаимоисключающие), `allowUnsafeCode`, `autoReferenced`, `noEngineReferences`, `overrideReferences` + `precompiledReferences`, `defineConstraints`, `versionDefines`. Поле `rootNamespace` в таблице формата не перечислено, но документировано в инспекторе как **Root Namespace** и работает (проверено).

**Проверенная раскладка.** Четыре файла написаны руками в терминале, `Unity -batchmode` собрал их без единой ошибки; Unity **не переписал** JSON, только сгенерировал `.meta`. Собрались `Game.Core.dll`, `Game.Vehicle.dll`, `Game.Vehicle.Editor.dll`, `Game.Tests.EditMode.dll`.

```json
// Assets/_Project/Scripts/Core/Game.Core.asmdef — фундамент, ни от кого не зависит
{ "name": "Game.Core", "rootNamespace": "Game.Core", "references": [], "autoReferenced": false }

// Assets/_Project/Scripts/Vehicle/Game.Vehicle.asmdef
{ "name": "Game.Vehicle", "rootNamespace": "Game.Vehicle",
  "references": ["Game.Core", "Unity.InputSystem"], "autoReferenced": false }

// Assets/_Project/Scripts/Vehicle/Editor/Game.Vehicle.Editor.asmdef — редакторный код модуля
{ "name": "Game.Vehicle.Editor", "rootNamespace": "Game.Vehicle.Editor",
  "references": ["Game.Vehicle", "Game.Core"], "includePlatforms": ["Editor"],
  "autoReferenced": false }

// Assets/_Project/Tests/EditMode/Game.Tests.EditMode.asmdef
{ "name": "Game.Tests.EditMode", "rootNamespace": "Game.Tests",
  "references": ["Game.Core", "Game.Vehicle", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"], "autoReferenced": false,
  "overrideReferences": true, "precompiledReferences": ["nunit.framework.dll"],
  "defineConstraints": ["UNITY_INCLUDE_TESTS"] }
```

(Каждый блок — отдельный файл; комментарии `//` в реальном `.asmdef` не пишутся.)

**Правила зависимостей:**

- **Циклы запрещены** — ошибка компиляции, а не предупреждение; лечится рефакторингом или объединением сборок. Отсюда направление: `Core` ← `Vehicle` ← `Gameplay` ← `UI`, обратно — только через события и интерфейсы, объявленные в `Core`. **Runtime не может ссылаться на Editor**; обратное можно и нужно.
- `"autoReferenced": false` отключает автоссылку из предопределённой сборки `Assembly-CSharp` (куда попадает код без `.asmdef`). Ставим везде — заставляет объявлять зависимости честно.
- **Ссылки по имени или по GUID, но не вперемешку.** Документация советует GUID (переименование не ломает ссылку). Мы пишем по имени: агенту в терминале имена читаемы, GUID пришлось бы вычитывать из `.meta`. Цена — при переименовании поправить ссылки; их у нас единицы. Имена уникальны в проекте, рекомендован обратно-DNS-стиль; `Game.*` достаточно.

---

## 11. Package Manager из терминала

Пакет добавляется правкой `Packages/manifest.json` — Unity подхватит при следующем открытии. Ключи файла (справочник 6.3): `dependencies` — прямые зависимости `"имя": "минимальная версия"`; `enableLockFile` (по умолчанию `true`, не трогаем); `resolutionStrategy` — как обновлять *косвенные* зависимости: `lowest` (по умолчанию), `highestPatch`, `highestMinor`, `highest`; `scopedRegistries`; `testables` — пакеты, чьи тесты подгружать в Test Runner; `pinnedPackages` — новое в Unity 6: имена, которые Package Manager обязан взять **ровно указанной версии**, даже если для редактора «подходит» другая (каждое имя обязано быть в `dependencies`).

```bash
python3 - <<'PY'
import json, pathlib
p = pathlib.Path("Packages/manifest.json")
m = json.loads(p.read_text())
m["dependencies"]["com.unity.cinemachine"] = "3.1.7"
m["dependencies"] = dict(sorted(m["dependencies"].items()))
p.write_text(json.dumps(m, indent=2) + "\n")
PY
"$UNITY" -batchmode -quit -nographics -projectPath "$PWD" -logFile -   # обновит packages-lock.json
```

### Версии для Unity 6.3 (6000.3.21f1)

Первые пять — из реально сгенерированного `manifest.json` (проверено); Cinemachine и Splines — из `dist-tags.latest` реестра `packages.unity.com`.

| Пакет | Версия | Комментарий |
|---|---|---|
| `com.unity.render-pipelines.universal` | **17.3.0** | Привязана к редактору. В публичном реестре URP есть только до 10.x — 12+ поставляются с редактором. Не пиньте руками. |
| `com.unity.inputsystem` | **1.20.0** | новый Input System |
| `com.unity.test-framework` | **1.6.0** | В публичном реестре latest = 1.4.6; 1.6.0 идёт встроенным пакетом редактора. Не «понижайте» до реестровой. |
| `com.unity.ai.navigation` | **2.0.14** | NavMesh для ИИ-агентов |
| `com.unity.timeline` | 1.8.12 | |
| `com.unity.cinemachine` | **3.1.7** | Редактор по умолчанию подставит **2.10.7** — Cinemachine 2 с другим API. Для 3.x указывать явно. |
| `com.unity.splines` | **2.9.0** | сплайны для траекторий трафика |

Шаблон в архиве прописывает более старые версии (URP 17.0.1, inputsystem 1.12.0, test-framework 1.4.2), но при создании проекта Unity поднимает их до версий редактора — проверено, в готовом `manifest.json` уже 17.3.0 / 1.20.0 / 1.6.0.

---

## 12. Editor-скрипт первоначальной настройки

Автоматизируем только то, у чего есть **документированное** API: создание папок и пару настроек. Режим сериализации и режим VCS — аргументами командной строки или правкой YAML (раздел 3).

```csharp
// Assets/_Project/Scripts/Core/Editor/ProjectBootstrap.cs
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.Core.EditorTools
{
    /// <summary>One-shot project scaffolding. Run headlessly via -executeMethod.</summary>
    public static class ProjectBootstrap
    {
        // Parents must precede children: AssetDatabase.CreateFolder needs an existing parent.
        private static readonly string[] Folders =
        {
            "Assets/_Project", "Assets/_Project/Scripts", "Assets/_Project/Scripts/Core",
            "Assets/_Project/Scripts/Vehicle", "Assets/_Project/Scripts/World",
            "Assets/_Project/Scripts/Gameplay", "Assets/_Project/Scripts/UI",
            "Assets/_Project/Prefabs", "Assets/_Project/Materials", "Assets/_Project/Models",
            "Assets/_Project/Audio", "Assets/_Project/Scenes", "Assets/_Project/Settings",
            "Assets/_Project/Tests", "Assets/_Project/Tests/EditMode", "Assets/_Project/Tests/PlayMode",
        };

        [MenuItem("Game/Setup/Scaffold Project")]
        public static void Scaffold()
        {
            foreach (var path in Folders)
            {
                if (AssetDatabase.IsValidFolder(path)) continue;
                AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'),
                                           Path.GetFileName(path));
            }
            EditorSettings.serializeInlineMappingsOnOneLine = true;
            EditorSettings.lineEndingsForNewScripts = LineEndingsMode.Unix;
            EditorSettings.projectGenerationRootNamespace = "Game";
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Bootstrap] Project scaffolded.");
        }
    }
}
```

```bash
"$UNITY" -batchmode -quit -nographics -projectPath "$PWD" \
  -executeMethod Game.Core.EditorTools.ProjectBootstrap.Scaffold -logFile -
```

### Проверка компиляции и тестов из терминала

```bash
# компиляция: открыть и закрыть проект, ошибки окажутся в логе
"$UNITY" -batchmode -quit -nographics -projectPath "$PWD" -logFile "$PWD/compile.log"
grep -E "error CS" compile.log        # пусто = собралось

# EditMode-тесты (проверено: exit=0, total="1" passed="1" failed="0"); БЕЗ -quit
"$UNITY" -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform EditMode -testResults "$PWD/results.xml" -logFile "$PWD/tests.log"
```

Есть также `-testFilter` и `-testCategory` для отбора тестов.

---

## Антипаттерны

- **Коммитить `Library/`.** Сотни мегабайт машинно-зависимого кеша, который Unity восстановит за минуты.
- **Забыть `.meta`.** Самое разрушительное: у всех остальных отваливаются ссылки в сценах и префабах, Unity выдаёт новые GUID.
- **Переименовывать и двигать ассеты в Finder.** `.meta` остаётся на старом месте, ссылка мертва. Только в редакторе либо `git mv` обоих файлов.
- **Ставить Asset Serialization = Force Binary «ради размера».** Убивает мерж сцен и префабов. В 6.3 дефолт и так Force Text — не «чините».
- **Искать `m_ExternalVersionControlSupport` в `EditorSettings.asset`.** В 6.3 его там нет, режим в `VersionControlSettings.asset`.
- **Писать Editor-скрипт вокруг `EditorSettings.serializationMode` / `.externalVersionControl`.** Этих свойств нет в справочнике Scripting API 6.3.
- **Скрипты с `"Unity Hub.app" -- --headless`.** Hub CLI устарел с Hub 3.18.0 и создавать проекты не умел.
- **Включать Git LFS «потом», когда репозиторий раздуется.** Миграция переписывает историю и ломает клоны.
- **Отдавать в LFS `.unity` / `.prefab` / `.cs`.** Текст должен диффиться и сливаться.
- **Один `.asmdef` на весь `_Project`.** Тогда любая правка перекомпилирует всё — ровно та боль, от которой asmdef придуманы.
- **Взаимные ссылки между `.asmdef`.** Ошибка компиляции, а не предупреждение.
- **Складывать всё в `Resources/`.** Всё содержимое попадает в билд и в память независимо от использования.
- **Держать проект в iCloud Drive / Dropbox.** Unity прямо называет это неподдерживаемым и приводящим к порче проекта.
- **`git commit -a` в Unity-проекте.** Сцены, префабы и атласы помечаются изменёнными без ваших правок — в коммит попадает чужая работа.
- **Обновлять минорную версию редактора «заодно» с фичей.** Апгрейд — отдельный коммит.

---

## Чек-лист

Разовая настройка репозитория:

- [ ] Проект создан `-createProject` + `-cloneFromTemplate` с шаблоном `3d-cross-platform` (URP); в логе `exit=0`.
- [ ] Удалены `Assets/TutorialInfo/` и `Assets/Readme.asset` вместе с `.meta`.
- [ ] `.gitignore` из github/gitignore лежит **в корне проекта**, дополнен `.DS_Store`.
- [ ] `git add -A && git diff --cached --name-only | wc -l` даёт десятки, а не тысячи (эталон — 61).
- [ ] `git check-ignore -v Library/PackageCache` и `UserSettings/Layouts` — оба игнорируются.
- [ ] `.gitattributes` содержит `merge=unityyamlmerge` для `*.unity`, `*.prefab`, `*.asset`, `*.meta`, `*.mat`, `*.anim`, `*.controller`; бинарные расширения перечислены **в обоих регистрах** (`*.fbx` и `*.FBX`).
- [ ] `git config merge.tool` = `unityyamlmerge`, путь в `mergetool.unityyamlmerge.cmd` существует (проверить `ls`).
- [ ] `ProjectVersion.txt` в индексе и содержит `6000.3.21f1`; `VersionControlSettings.asset` → `m_Mode: Visible Meta Files`; `EditorSettings.asset` → `m_SerializationMode: 2`, `m_SerializeInlineMappingsOnOneLine: 1`.
- [ ] `Packages/manifest.json` **и** `Packages/packages-lock.json` в индексе; если нужен Cinemachine — явно указана версия `3.x`.
- [ ] Проект лежит **вне** iCloud Drive / Dropbox.
- [ ] Git LFS не ставим, пока нет бинарных ассетов; в задачи занесено «включить до первого FBX».

При добавлении модуля кода:

- [ ] Папка внутри `Assets/_Project/Scripts/`.
- [ ] Рядом `.asmdef` с `"name": "Game.<Модуль>"`, `"rootNamespace"` и `"autoReferenced": false`.
- [ ] В `references` только реально нужные модули; циклов нет.
- [ ] Редакторный код — в подпапке `Editor/` со своим `.asmdef` и `"includePlatforms": ["Editor"]`.
- [ ] `grep -E "error CS" compile.log` после batch-прогона пуст.
- [ ] `.asmdef` и его `.meta` — в одном коммите.

---

## Видео и доклады

- **Version control & project organization best practices** — Unity — https://www.youtube.com/watch?v=GEOqwtzmeP0 — 8 минут официального обзора: что коммитить, как раскладывать проект. Записано до Unity 6, но база не изменилась.
- **Unity command-line interface (CLI)** — Unity — https://www.youtube.com/watch?v=DgNrgZeJOxQ — ссылка взята из официальной документации Unity CLI; показывает новый терминальный воркфлоу (у нас пока experimental).
- **Unity Assembly Definitions Explained (Architecture, Not Just Compile Times)** — git-amend — https://www.youtube.com/watch?v=OqKEaiQrDHY — самое свежее (~март 2026) и самое релевантное разделу 10: asmdef как инструмент архитектуры и границ зависимостей.
- **Speed Up Compile Times in Unity with Assembly Definitions** — Game Dev Guide — https://www.youtube.com/watch?v=eovjb5xn8y0 — механика asmdef «на пальцах», вводный темп.
- **Assembly Definitions, Explained | Unity Tutorial** — LlamAcademy — https://www.youtube.com/watch?v=qprZHOPu2OI — 6 минут, разбор полей инспектора asmdef.
- **Master Version Control and Github in Unity 6 (FREE)** — Zenva — https://www.youtube.com/watch?v=MEpeJX-7Ds4 — 28 минут именно про Unity 6: инициализация репозитория, `.gitignore`, типовые ошибки.
- **How to use version control with GitHub on a Unity 6 project** — Anchorpoint — https://www.youtube.com/watch?v=0LGdGqxHLVo — Unity 6 + GitHub + LFS, с акцентом на бинарные ассеты и блокировки.
- **How to Set Up Git, Git LFS & .gitignore for Unity (Beginner Guide)** — Pintig Laro Games — https://www.youtube.com/watch?v=BmkabA7kFOE — самое свежее по LFS (август 2026), пошаговый скринкаст на момент, когда дойдём до бинарников.
- **Organizing Your Unity Project — Content vs Feature Folders** — Infallible Code — https://www.youtube.com/watch?v=o8HIGKObG1Q — 4 минуты про выбор между раскладкой «по типу ассета» и «по фиче»; прямо про раздел 9.
- **A BETTER way to setup new unity projects!** — Jason Storey — https://www.youtube.com/watch?v=nVieP57TD20 — идея переиспользуемого стартового набора проекта.

---

## Источники

Все ссылки проверены (HTTP 200) 2026-08-19.

Документация Unity 6.3, префикс `https://docs.unity3d.com/6000.3/Documentation/`:

- `Manual/EditorCommandLineArguments.html` — `-createProject`, `-cloneFromTemplate`, `-batchmode`, `-quit`, `-nographics`, `-logFile`, `-vcsMode`
- `Manual/class-EditorManager.html` — Asset Serialization Mode, «Force Text is the default», Reduce version control noise
- `Manual/Versioncontrolintegration.html` + `Manual/class-VersionControlSettings.html` — режимы VCS, «Visible meta files… This is the default setting»
- `Manual/SmartMerge.html` — UnityYAMLMerge, конфиг для git, mergerules.txt
- `Manual/default-directories.html` — исключить Library/Temp/UserSettings; предупреждение про облачные хранилища
- `Manual/SpecialFolders.html` — зарезервированные папки, игнорируемые файлы, `.folder` → `_folder`
- `Manual/upm-manifestPrj.html` — формат `manifest.json`, `resolutionStrategy`, `pinnedPackages`, `testables`
- `Manual/upm-conflicts-auto.html` — «Put the lock file under source control»
- `Manual/assembly-definition-file-format.html`, `Manual/assembly-definitions-referencing.html`, `Manual/class-AssemblyDefinitionImporter.html` — схема `.asmdef`, правила ссылок, запрет циклов, Root Namespace
- `ScriptReference/EditorSettings.html` — список свойств (подтверждает отсутствие `serializationMode` и `externalVersionControl`); также `EditorSettings-serializeInlineMappingsOnOneLine.html`, `LineEndingsMode.html`, `AssetDatabase.CreateFolder.html`
- https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-command-line.html — `-runTests`, `-testPlatform`, `-testResults`, `-testFilter`, `-testCategory`

Unity CLI, Hub, blog:

- https://docs.unity.com/en-us/unity-cli/use-unity-cli и `.../unity-cli-reference` — установка (`brew install --cask unity-cli`), статус experimental, команды, коды выхода, миграция с Hub CLI
- https://docs.unity.com/en-us/hub/hub-cli-reference — «The Hub CLI is deprecated from version 3.18.0 of the Unity Hub»
- https://docs.unity.com/en-us/hub/project-create — создание проекта через Hub, категории шаблонов, интеграция с GitHub/GitLab
- https://unity.com/blog/author-scenes-and-prefabs-with-verson-control — Adrian Woods, Unity, 15.07.2024: `.meta` всегда в VCS, Smart Merge, «Git requires Git LFS… to support file locking», глубина префабов 5–7
- https://unity.com/how-to/version-control-systems и e-book https://resources.unity.com/games/version-control-project-organization-best-practices-ebook

Прочее:

- https://github.com/github/gitignore/blob/main/Unity.gitignore — канонический `.gitignore` (обновлён 20.04.2026); macOS-дополнение — `.../main/Global/macOS.gitignore`
- https://gist.github.com/nemotoo/b8a1c3a0f1225bb9231979f389fd4f3f — популярный `.gitattributes` для Unity + LFS (сообщество, не Unity)
- https://git-lfs.com/ — Git LFS
- https://packages.unity.com/com.unity.cinemachine — реестр UPM, `dist-tags.latest` = 3.1.7 (аналогично `com.unity.splines`, `com.unity.inputsystem`, `com.unity.ai.navigation`)

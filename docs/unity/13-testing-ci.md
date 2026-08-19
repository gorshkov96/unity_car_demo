# Тесты, проверка компиляции и CI (Unity 6.3)

Как AI-агенту убедиться из терминала, что код собирается и работает: Unity Test Framework, запуск редактора в batch-режиме на macOS, коды возврата, Code Coverage, GameCI. Заглядывать сюда при настройке тестовой сборки, при написании скриптов проверки и при падении CI.

---

## TL;DR — решения для нашего проекта

1. **Вся чистая логика — в отдельных `.asmdef`-сборках без `MonoBehaviour`** (расчёт скорости, состояния гонки, парсинг конфигов). Тестовая сборка не может ссылаться на `Assembly-CSharp`, поэтому без своих `.asmdef` тесты писать физически невозможно.
2. **Основной режим тестов — EditMode.** PlayMode-тесты только там, где нужна физика в динамике: они на порядок медленнее (домен перезагружается, сцена стартует).
3. **Основная команда проверки** — она же проверка компиляции:
   `.../Unity -batchmode -runTests -testPlatform EditMode -projectPath <проект> -testResults <xml> -logFile <log>` — **без `-quit`**.
4. **Коды возврата: `0` — ок, `2` — тесты упали, `3` — ошибка запуска (в том числе ошибки компиляции), `4` — неизвестная платформа.** Проверено по исходникам пакета в установленном редакторе, а не по памяти.
5. **`-quit` вместе с `-runTests` использовать нельзя** — редактор выйдет до окончания тестов. Это документировано и подтверждается предупреждением в логе.
6. **Ловушка:** если фильтр не совпал ни с одним тестом, Unity вернёт **`0`**, а не ошибку. В скрипте обязательно проверять `total` в XML-отчёте.
7. **Для быстрой итерации по чистой логике** — второй, «параллельный» путь: отдельный `.csproj`, который линкует те же `.cs` файлы, и `dotnet test`. ~2 с вместо ~18 с и без лицензии Unity.
8. **Code Coverage** (`com.unity.testtools.codecoverage` 1.3.0) ставим позже, когда появится что мерить; включать в CI аккуратно — на Unity 6 инструментирование покрытия ломало PlayMode-прогоны.
9. **CI сейчас не нужен** (нет удалённого репозитория). Когда появится — GameCI `game-ci/unity-test-runner@v4` на `ubuntu-latest`, образ `unityci/editor:6000.3.21f1-base-3` существует.
10. **Что покрывать в демо:** математику машины, конечные автоматы, конфиги-`ScriptableObject`, валидацию сцены. Что не покрывать: визуал, партиклы, звук, шейдеры — там тест дороже пользы.


## 1. Unity Test Framework: что это и что изменилось в Unity 6.3

**Unity Test Framework (UTF)** — пакет `com.unity.test-framework`, обёртка вокруг NUnit («стандартная библиотека модульных тестов для .NET») с добавками под Unity: кадры, корутины, перезагрузка домена. Что важно знать:

- В Unity 6.3 UTF — **core package**: не ставится через Package Manager, поставляется с редактором и привязан к его версии. В `6000.3.21f1` это **1.6.0** (проверено в `BuiltInPackages/com.unity.test-framework/package.json`). Вывод: **не прописывать `com.unity.test-framework` в `Packages/manifest.json`** — это привычка эпохи 2019–2022, пакет уже есть.
- Внутри — **кастомный NUnit на базе версии 3.5** (`com.unity.ext.nunit` 2.0.5); части возможностей NUnit 3.13+ нет.
- Окно тестов: `Window > General > Test Runner`. Создание сборки/скрипта: `Assets > Create > Testing > Tests Assembly Folder` и `… > C# Test Script`. В документации первый пункт назван «Test Assembly Folder», в коде — «**Tests** Assembly Folder»; верна вторая формулировка.

### EditMode против PlayMode

| | Edit mode | Play mode |
|---|---|---|
| Где выполняется | цикл `EditorApplication.update` | игровой цикл (Play mode или собранный плеер) |
| Доступ к `UnityEditor` | да | нет (только через `#if UNITY_EDITOR` в pre-build шагах) |
| Корутины | **нельзя** (можно только yield-инструкции редактора) | да, `[UnityTest]` работает как корутина |
| `includePlatforms` в asmdef | `["Editor"]` | `[]` (пусто) или конкретные платформы |
| Скорость | быстро | медленно: вход/выход из Play mode + domain reload |

Рекомендация из официальной документации дословно: используйте NUnit-атрибут `[Test]`, а не `[UnityTest]`, если вам не нужно yield-ить инструкции редактора (EditMode) или пропускать кадры / ждать (PlayMode).


## 2. Сборки тестов (`.asmdef`)

**`.asmdef`** («assembly definition») — файл, объявляющий отдельную C#-сборку (DLL) из папки; тестовой сборкой Unity считает любую, которая ссылается на NUnit. Жёсткие правила: тестовая сборка **не может ссылаться на `Assembly-CSharp`** (сборку «всех скриптов без asmdef»), поэтому тестируемый код обязан жить в своём `.asmdef`; скрипты тестов лежат в папке рядом с `.asmdef`; признак тестовой сборки — комбинация ссылок `nunit.framework.dll` + `UnityEngine.TestRunner` + `UnityEditor.TestRunner`, причём последняя доступна только EditMode-тестам.

### Актуальный формат (Unity 6.3)

EditMode — `.asmdef`, который генерирует сам UTF (взято из семплов пакета в установленном редакторе):

```json
{
    "name": "Project.Vehicle.EditModeTests",
    "references": ["UnityEngine.TestRunner", "UnityEditor.TestRunner", "Project.Vehicle"],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "defineConstraints": ["UNITY_INCLUDE_TESTS"]
}
```

PlayMode — то же самое, но **без `UnityEditor.TestRunner`** в `references` и с **пустым** `includePlatforms: []`.

Разбор полей (по справочнику формата asmdef):

- `overrideReferences: true` — обязательно, иначе массив `precompiledReferences` **игнорируется** и `nunit.framework.dll` не подключится. Самая частая ошибка при ручной правке.
- `defineConstraints: ["UNITY_INCLUDE_TESTS"]` — сборка компилируется только когда тесты включены, то есть **не попадает в билд игры**.

**Устаревшая форма — не копировать.** Шаблоны в `Unity.app/Contents/Resources/ScriptTemplates/` (меню `Assets > Create > Assembly Definition`) и один пример в самой документации Unity 6.3 всё ещё показывают `{ "name": "Tests", "optionalUnityReferences": ["TestAssemblies"] }`. Это форма 2018–2019 годов: она пока работает, но `optionalUnityReferences` даже не упоминается в справочнике формата asmdef для Unity 6.3 — поля нет в списке. **Пиши явные `references` + `precompiledReferences`.**

### Раскладка под нашу структуру

```
Assets/_Project/Scripts/
  Vehicle/            Project.Vehicle.asmdef          ← чистая логика + MonoBehaviour
  Vehicle/Tests/      Project.Vehicle.EditModeTests.asmdef
  World/              Project.World.asmdef
  Gameplay/           Project.Gameplay.asmdef
  Tests/PlayMode/     Project.PlayModeTests.asmdef    ← одна на проект, тестов мало
```


## 3. Атрибуты и API, которые реально нужны

```csharp
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class EngineCurveTests
{
    [SetUp]    public void SetUp()    { }  // перед каждым тестом
    [TearDown] public void TearDown() { }  // после каждого, даже если тест упал

    [Test]  // обычный NUnit-тест, один кадр
    public void Torque_AtZeroRpm_IsIdleTorque()
        => Assert.That(new EngineCurve(idleTorque: 120f).Evaluate(0f),
                       Is.EqualTo(120f).Within(0.001f));

    [UnityTest]  // корутина: PlayMode — кадры, EditMode — yield-инструкции редактора
    public IEnumerator Car_AfterOneSecond_HasMoved()
    {
        yield return new WaitForSeconds(1f);
        Assert.That(_car.transform.position.z, Is.GreaterThan(0f));
    }

    [Test]  // async тоже поддерживается: возвращает Task
    public async Task LoadConfig_Completes() { await Task.Yield(); }
}
```

Отдельно стоит знать:

- **`[UnitySetUp]` / `[UnityTearDown]` / `[UnityOneTimeSetUp]` / `[UnityOneTimeTearDown]`** — аналоги NUnit-атрибутов, возвращают `IEnumerator` и умеют yield-ить инструкции редактора.
- **`LogAssert.Expect(LogType.Error, "…")`** обязателен: **любое сообщение в консоли строже Warning автоматически валит тест**. Вызывать `Expect` **до** проверяемого кода — сверка ожидаемых логов идёт в конце кадра.
- **Компараторы с допуском** — `UnityEngine.TestTools.Utils` для `Vector3`, `Quaternion`, `Color`. Сравнивать float-векторы через `Is.EqualTo` без допуска бессмысленно.
- **Параметризация:** `[Test]` поддерживает `[TestCase]` и `[ValueSource]`; **`[UnityTest]` — только `[ValueSource]`**.
- **Yield-инструкции редактора** (только EditMode): `EnterPlayMode`, `ExitPlayMode`, `RecompileScripts`, `WaitForDomainReload`. Домен, перезагрузившийся без явного yield такой инструкции, роняет тест. Поля, которые должны пережить перезагрузку, помечаются `[SerializeField]`.
- **`IPrebuildSetup` / `IPostBuildCleanup`** — подготовка данных до сборки плеера. **`MonoBehaviourTest<T>`** — хелпер: `yield return new MonoBehaviourTest<MyTest>();` создаёт объект с компонентом и ждёт, пока `IMonoBehaviourTest.IsTestFinished` не станет `true`.


## 4. Как тестировать чистую логику без сцены

Главный рычаг: **чем меньше кода зависит от `MonoBehaviour`, тем больше проверяется быстрыми EditMode-тестами**. Наш архитектурный принцип «ввод → логика → визуал» и есть подготовка к тестируемости.

```csharp
// Обычный C#-класс, не MonoBehaviour — тестируется тривиально в EditMode
public sealed class SuspensionSolver
{
    public float ComputeSpringForce(float compression, float stiffness, float damping, float velocity)
        => compression * stiffness - velocity * damping;
}

// MonoBehaviour — только склейка и вызовы движка, тестировать нечего
public sealed class CarBody : MonoBehaviour
{
    private SuspensionSolver _solver;
    private void FixedUpdate() => _rb.AddForce(_solver.ComputeSpringForce(/*...*/));
}
```

### Быстрый путь: `dotnet test` мимо редактора

Приём из свежей статьи gamedev.center (на Unity 6000.3.2f1): рядом с проектом создаётся отдельный `.csproj`, который **линкует те же `.cs`-файлы** и ссылается на управляемые DLL редактора.

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
  <PackageReference Include="NUnit" Version="3.14.0" />
  <PackageReference Include="NUnit3TestAdapter" Version="4.5.0" />
  <Compile Include="..\Assets\_Project\Scripts\**\*.cs" LinkBase="Runtime" />
  <Reference Include="UnityEngine.CoreModule">
    <!-- проверенный macOS-путь на этой машине; в статье он для Windows -->
    <HintPath>/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/Resources/Scripting/Managed/UnityEngine/UnityEngine.CoreModule.dll</HintPath>
  </Reference>
</ItemGroup>
```

Замеры автора статьи на 20 тестах: Unity Test Runner ≈ 18 с (компиляция 0.38 с + domain reload 3.45 с + ручные шаги), `dotnet test` ≈ 2 с. Лицензия Unity не нужна.

Ограничение честное: работает только для кода, которому не нужен нативный движок — `Physics.Raycast`, `Time.deltaTime` и прочее, требующее запущенного рантайма, там не поедет. Вывод для нас: **сначала обычные EditMode-тесты, `dotnet test` — только если цикл обратной связи станет мешать.**


## 5. Запуск Unity из терминала на macOS

### Где лежит бинарник

Проверено на этой машине: `/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity`. Общий шаблон — `/Applications/Unity/Hub/Editor/<версия>/Unity.app/Contents/MacOS/Unity`. Как найти версию, не зашивая её в скрипт:

```bash
# 1. Из проекта (единственный правильный способ — версия проекта, а не «какая стоит»)
awk -F': ' '/^m_EditorVersion:/ {print $2}' ProjectSettings/ProjectVersion.txt | tr -d '\r'

# 2. Что вообще установлено (Unity Hub CLI)
"/Applications/Unity Hub.app/Contents/MacOS/Unity Hub" -- --headless editors -i
# → 6000.3.21f1 (Apple silicon) installed at /Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app
```

### Команда запуска тестов и её аргументы

```bash
"/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode \
  -projectPath "$PWD" \
  -runTests \
  -testPlatform EditMode \
  -testResults "$PWD/Artifacts/editmode-results.xml" \
  -logFile "$PWD/Artifacts/editmode.log" \
  -accept-apiupdate
```

Важные аргументы (по справочникам Unity 6.3):

| Аргумент | Что делает |
|---|---|
| `-batchmode` | без GUI и диалогов; при исключении в скриптах немедленный выход с кодом `1` |
| `-projectPath <путь>` | какой проект открыть |
| `-runTests` | запустить тесты |
| `-testPlatform EditMode\|PlayMode\|<BuildTarget>` | режим; **если не указан — EditMode** |
| `-testResults <файл>` | куда положить NUnit-XML |
| `-logFile <файл>` | лог редактора; `-logFile -` шлёт в stdout |
| `-testFilter "A;B"` / `-testCategory "…"` / `-assemblyNames "…"` | фильтры по имени, категории и сборке; поддерживают `!` для отрицания |
| `-runSynchronously` | весь прогон в одном update-вызове; **только EditMode**, `[UnityTest]` и `[UnitySetUp]`/`[UnityTearDown]` отфильтровываются |
| `-retry N` / `-repeat N` | перезапуск упавших / повтор успешных — для ловли «плавающих» тестов |
| `-accept-apiupdate` | в batch-режиме API Updater **не запускается без этого флага**, что может дать ошибки компиляции |
| `-nographics` | без графического устройства; **выключает вывод логов, если не задан `-logFile`** |
| `-executeMethod Class.Method` | вызвать статический метод из папки `Editor` |

**Регистр имени аргумента важен:** парсер UTF сравнивает строку точно (`"-" + ArgName == argName`). В таблице документации Unity 6.3 опечатка — напечатано `-testfilter`, рабочее написание **`-testFilter`**. Значение `-testPlatform`, наоборот, регистронезависимо (`editmode` == `EditMode`).

### Коды возврата

Значения из `Executer.cs` установленного пакета UTF 1.6.0; совпадают с тем, что проверяет скрипт GameCI:

| Код | Enum | Когда |
|---|---|---|
| `0` | `Ok` | все тесты прошли — **или не выполнилось ни одного теста** |
| `2` | `Failed` | один или несколько тестов упали |
| `3` | `RunError` | ошибки компиляции, не найден `TestSettings.json`, не получены колбэки, исключение при старте |
| `4` | `PlatformNotFound` | значение `-testPlatform` не разобралось |
| `1` | — | общий сбой batch-режима (исключение в скриптах, сбой операции) |

Документация по `-testResults` честно оговаривает, что общего определения кодов выхода для компонентов Unity нет: коды выше верны для самого прогона тестов, но не для произвольного `-executeMethod`.

### Как понять из лога, что произошло

UTF пишет в лог однозначные маркеры: `Running tests for …` (прогон стартовал), `Test run completed. Exiting with code 3 (RunError). Scripts had compilation errors.` (**компиляция упала**), `… code 2 (Failed). One or more tests failed.`, `… code 0 (Ok). No tests were executed.` (фильтр не совпал ни с чем), `Running tests from command line arguments will not work when "quit" is specified.` (вы передали `-quit`). Плюс обычные строки компилятора вида `Assets/…/File.cs(12,5): error CS0103: …` и `Scripts have compiler errors`.

### `-quit` — главная ловушка

Документация Unity 6.3 говорит об этом дважды: «обычный аргумент редактора `-quit` не поддерживается во время выполнения тестов» и «если редактор выполняет тесты с `-runTests`, то `-quit` заставляет редактор выйти немедленно, до того как выполняющиеся тесты успеют завершиться».

Также `-quit` не любит асинхронный код (может подвесить процесс) и требует `-cacheServerWaitForUploadCompletion` при подключённом Accelerator. `-quit` уместен только там, где тестов нет: `-createProject`, `-executeMethod`, генерация отчёта покрытия.


## 6. Скрипт быстрой проверки компиляции

Самый надёжный способ проверить «собирается ли проект» — **прогнать EditMode-тесты**: UTF перед стартом сам вызывает `EditorUtility.scriptCompilationFailed` и при провале компиляции выходит с кодом `3` и внятным сообщением. Отдельный `-executeMethod` писать не нужно.

```bash
#!/usr/bin/env bash
# Tools/check.sh — компиляция + EditMode-тесты. Возврат: 0 ок, иначе код Unity.
set -uo pipefail

PROJECT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$(awk -F': ' '/^m_EditorVersion:/ {print $2}' "$PROJECT/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
UNITY="/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity"
OUT="$PROJECT/Artifacts"; LOG="$OUT/editmode.log"; XML="$OUT/editmode-results.xml"
mkdir -p "$OUT"; rm -f "$LOG" "$XML"
[ -x "$UNITY" ] || { echo "Unity $VERSION не найден: $UNITY"; exit 127; }

"$UNITY" -batchmode -accept-apiupdate -projectPath "$PROJECT" \
         -runTests -testPlatform EditMode -testResults "$XML" -logFile "$LOG"
CODE=$?

case $CODE in
  0) echo "OK";; 2) echo "ТЕСТЫ УПАЛИ";;
  3) echo "ОШИБКА ЗАПУСКА (скорее всего компиляция)";; *) echo "Код $CODE";;
esac

# Ошибки компиляции — ради них весь скрипт
grep -E "error CS[0-9]+|Scripts have compiler errors" "$LOG" | sort -u | head -40

# Защита от «0 при нуле тестов»
TOTAL=$(sed -n 's/.*<test-run[^>]* total="\([0-9]*\)".*/\1/p' "$XML" 2>/dev/null | head -1)
echo "Выполнено тестов: ${TOTAL:-0}"
[ "$CODE" -eq 0 ] && [ "${TOTAL:-0}" -eq 0 ] && { echo "ПОДОЗРИТЕЛЬНО: тестов не выполнено"; exit 1; }
exit $CODE
```

**Про время — честно: точные цифры для нашего проекта не измерены, его ещё нет.** Порядок величин: одна перезагрузка домена в редакторе на небольшом проекте — 3–4 с, компиляция — доли секунды (замеры gamedev.center на Unity 6000.3.x). В batch-режиме сверху ложится старт редактора и сканирование ассетов, поэтому холодный первый прогон (пустая `Library/`) — **минуты**, тёплый — **десятки секунд**. `Library/` — кэш импортированных ассетов; она в `.gitignore`, но удалять её локально ради «чистоты» не надо, это самая дорогая операция в цикле.

Ускорители: `-runSynchronously` (только EditMode, без `[UnityTest]`), `-assemblyNames` для одной сборки, `-testFilter` для одного теста.

Два условия, без которых скрипт просто не заработает:

- **Проект не должен быть открыт в редакторе** — Unity держит одну инстанцию на проект. Для агента это означает: либо пользователь закрывает редактор, либо проверка идёт в отдельном git-worktree с копией проекта.
- **Нужна активированная лицензия.** На этой машине файла `/Library/Application Support/Unity/Unity_lic.ulf` нет — если batch-режим падает на лицензировании, разбираться надо с этим, а не с аргументами.


## 7. Code Coverage

Пакет `com.unity.testtools.codecoverage`, для Unity 6.3 — **1.3.0** (обычный released-пакет, ставится через Package Manager). Аргументы командной строки:

```bash
"$UNITY" -projectPath "$PROJECT" -batchmode \
  -runTests -testPlatform EditMode -testResults "$XML" \
  -debugCodeOptimization \
  -enableCodeCoverage \
  -coverageResultsPath "$OUT/Coverage" \
  -coverageOptions "generateHtmlReport;generateBadgeReport;generateAdditionalMetrics;assemblyFilters:+Project.*;pathFilters:-**/Tests/**"
```

Ключевое: **`-debugCodeOptimization` обязателен** — без режима Debug покрытие считается неточно. `assemblyFilters` надо сузить до своих сборок, иначе в отчёт попадут пакеты Unity (алиасы `<all>`, `<assets>`, `<packages>`). **Burst ломает покрытие** — если появятся Burst-джобы, компиляцию Burst на время замера выключать (`--burst-disable-compilation`). Исключить код — атрибут `ExcludeFromCoverage` или стандартный .NET `ExcludeFromCodeCoverage`. Объединить EditMode и PlayMode — три запуска: у первых двух в `-coverageOptions` добавляется `dontClear`, третий (без `-runTests`, с `-quit`) только генерирует отчёт.

Для нашего демо **процент покрытия как цель — вредная метрика**. Ставим пакет, только если понадобится увидеть, какие ветки логики машины ни разу не исполняются.


## 8. Performance Testing Extension

Пакет `com.unity.test-framework.performance`, для Unity 6.3 — **3.5.0**. Расширяет UTF замерами: время метода, время кадра, маркеры профайлера, свои метрики. После установки добавить ссылку на сборку `Unity.PerformanceTesting` в свой `.asmdef`.

```csharp
using Unity.PerformanceTesting;

[Test, Performance, Version("2")]
public void SuspensionSolver_Performance()
    => Measure.Method(() => _solver.ComputeSpringForce(0.3f, 30000f, 2500f, 1.2f))
        .WarmupCount(10).MeasurementCount(20).IterationsPerMeasurement(100)
        .GC()                      // дополнительно считает аллокации (GC.Alloc)
        .Run();

[UnityTest, Performance]
public IEnumerator Traffic_FrameTime()
    => Measure.Frames().MeasurementCount(120).Run();
```

Что стоит запомнить: `[Performance]` ставится **вместе** с `[Test]`/`[UnityTest]`, не вместо; `[Version("N")]` поднимать при каждом изменении тела теста, иначе результаты несравнимы; `Measure.Frames()` **не работает в EditMode**; результаты смотреть в `Window > General > Performance Test Report`, в CLI — аргумент `-perfTestResults <файл.json>`. Советы пакета для стабильности замеров: один уровень качества в `Project Settings > Quality`, выключенные VSync и HW Reporting, без камеры и в batch-режиме, если рендер не мерится. При прогоне тестов UTF **всегда собирает development-плеер**, переопределяя настройку в Build Settings.

Для витрины технологий инструмент интересный («сколько стоит 200 машин трафика»), но подключать его стоит после того, как появится сам трафик.


## 9. CI: GameCI на GitHub Actions

Удалённого репозитория пока нет, поэтому раздел — заготовка. **GameCI** — открытый набор экшенов для Unity (`game-ci/unity-test-runner`, `game-ci/unity-builder`), не аффилированный с Unity Technologies; работает через Docker-образы `unityci/editor`.

```yaml
name: CI
on: [push, pull_request]

jobs:
  tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with: { lfs: true }
      - uses: actions/cache@v4
        with:
          path: Library
          key: Library-${{ hashFiles('Assets/**', 'Packages/**', 'ProjectSettings/**') }}
          restore-keys: Library-
      - uses: game-ci/unity-test-runner@v4
        id: tests
        env:
          UNITY_LICENSE: ${{ secrets.UNITY_LICENSE }}
          UNITY_EMAIL: ${{ secrets.UNITY_EMAIL }}
          UNITY_PASSWORD: ${{ secrets.UNITY_PASSWORD }}
        with:
          testMode: EditMode
          githubToken: ${{ secrets.GITHUB_TOKEN }}

      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: test-results
          path: ${{ steps.tests.outputs.artifactsPath }}
```

**Лицензирование.** Personal: активировать локально через Unity Hub (`Preferences > Licenses > Add > Get a free personal license`), содержимое файла `/Library/Application Support/Unity/Unity_lic.ulf` (macOS) положить в секрет `UNITY_LICENSE`, плюс `UNITY_EMAIL` и `UNITY_PASSWORD`. Pro: вместо `.ulf` — `UNITY_SERIAL`. Лицензия не привязана к версии Unity и ОС: активируется на macOS, используется в Linux-контейнере.

**Кэш `Library/`** по документации GameCI ускоряет прогон более чем на 50% (только для проектов, не для пакетов). **Образы:** `unityci/editor` содержит теги для нашей версии — `6000.3.21f1-base-3`, `ubuntu-6000.3.21f1-base-3`, `windows-6000.3.21f1-base-3` (проверено через Docker Hub API, обновлены 2026-07-29); самая свежая — `6000.3.22f1`. По умолчанию экшен берёт версию из `ProjectSettings/ProjectVersion.txt` (`unityVersion: auto`).

**Подводные камни GameCI.** Экшен **по умолчанию включает Code Coverage** (`-enableCodeCoverage`), и в его собственной документации сказано прямо: инструментирование покрытия бывало причиной падений и ошибок компиляции, включая **SIGSEGV в PlayMode на Unity 6** — при непонятных зависаниях первым делом ставить `coverageEnabled: false`. `testMode: All` гоняет только PlayMode и EditMode, `Standalone` запрашивается явно. Для результатов в GitHub Check Run репозиторию нужны `Read and write permissions` в `Settings > Actions > General`. Файлы покрытия создаются от root — дальше может понадобиться `sudo`. `packageMode` работает только на Linux-раннерах и требует явного `unityVersion`.

Скрипт GameCI разбирает коды выхода ровно так же: `0` — успех, `2` — часть тестов упала, `3` — прочая ошибка. Это независимое подтверждение таблицы из раздела 5.


## 10. Что реально покрывать тестами в демо-проекте

**Покрывать (EditMode, дёшево и полезно):**

- математику машины: кривая крутящего момента, расчёт подвески, ограничение скорости, переключение передач;
- конечные автоматы (состояния игры, ИИ-водителя) и переходы между ними;
- валидацию `ScriptableObject`-конфигов: масса > 0, кривые непустые, ссылки заполнены;
- утилиты: пул объектов (взяли/вернули/переполнение), генератор карты (детерминированность по seed);
- валидацию сцен: `EditorSceneManager.OpenScene(...)` → проверить наличие нужных объектов и компонентов, в `[TearDown]` открыть пустую сцену.

**Не покрывать:** визуал, шейдеры, партиклы, постпроцессинг, звук, UI-разметку (глазами дешевле); точные позиции после физической симуляции (тест будет «плавающим» из-за недетерминированности PhysX); геттеры-сеттеры и тонкие обёртки над Unity API.

**PlayMode — точечно, 3–5 тестов максимум:** машина под газом едет вперёд; при столкновении срабатывает событие; трафик спавнится и не проваливается сквозь дорогу. Каждый такой тест стоит секунд, а не миллисекунд. Схема — **Arrange–Act–Assert**, имя — `Method_Condition_ExpectedResult` (`Torque_AtZeroRpm_IsIdleTorque`).


## Антипаттерны

1. **`-quit` вместе с `-runTests`.** Редактор выйдет до окончания прогона; в логе появится предупреждение, но код возврата будет обманчивым.
2. **Считать `exit 0` доказательством, что тесты прошли.** При нуле выполненных тестов (опечатка в `-testFilter`, `-assemblyNames`, не собралась тестовая сборка) код тоже `0`. Всегда проверять `total` в XML.
3. **Прописывать `com.unity.test-framework` в `manifest.json`.** В Unity 6.3 это core-пакет, поставляется с редактором.
4. **`optionalUnityReferences: ["TestAssemblies"]` в новых asmdef.** Наследие 2018–2019; поля даже нет в справочнике формата для Unity 6.3.
5. **`precompiledReferences` без `overrideReferences: true`.** Массив молча игнорируется, `nunit.framework.dll` не подключается, тесты не компилируются.
6. **Пытаться тестировать код из `Assembly-CSharp`.** Тестовая сборка не имеет права на него ссылаться — это не ограничение, которое можно обойти.
7. **Писать всю логику в `MonoBehaviour`.** Тогда любой тест становится PlayMode-тестом со сценой, и набор тестов начинает занимать минуты.
8. **`[UnityTest]` там, где хватает `[Test]`.** Официальная рекомендация — обратная: `[Test]` по умолчанию.
9. **Забыть `LogAssert.Expect` перед кодом, который логирует ошибку.** Любой `Debug.LogError` валит тест, даже ожидаемый.
10. **Сравнивать `Vector3`/`Quaternion` без допуска.** Для этого в `UnityEngine.TestTools.Utils` есть компараторы.
11. **`-nographics` без `-logFile`.** Логи в этом режиме выключены — падение станет невидимым.
12. **Забыть `-accept-apiupdate` в batch-режиме.** API Updater не отработает — можно получить ошибки компиляции, которых в редакторе нет.
13. **Гонять batch-режим на проекте, открытом в редакторе.** Unity допускает одну инстанцию на проект.
14. **Гнаться за процентом покрытия** и держать Code Coverage включённым в CI «на всякий случай». В демо с большой долей визуального кода это метрика ради метрики, а на Unity 6 инструментирование покрытия ломало PlayMode-прогоны (документировано GameCI).


## Чек-лист

**Настройка (один раз, при создании проекта):**

- [ ] Логика вынесена из `MonoBehaviour` в обычные C#-классы, у каждого модуля свой `.asmdef`.
- [ ] Тестовые сборки созданы через `Assets > Create > Testing > Tests Assembly Folder`, а не руками.
- [ ] В каждом тестовом `.asmdef`: `overrideReferences: true`, `precompiledReferences: ["nunit.framework.dll"]`, `defineConstraints: ["UNITY_INCLUDE_TESTS"]`; EditMode — `includePlatforms: ["Editor"]` + `UnityEditor.TestRunner`, PlayMode — `includePlatforms: []` без него.
- [ ] `Artifacts/` в `.gitignore`; `Tools/check.sh` создан, `chmod +x`, версия редактора читается из `ProjectVersion.txt`.

**Перед каждым коммитом:**

- [ ] `./Tools/check.sh` вернул `0`, в логе нет `error CS`.
- [ ] Число выполненных тестов не ноль и не уменьшилось.
- [ ] `.meta`-файлы новых ассетов и `.asmdef` попали в коммит.

**При падении:** `3` → искать `error CS` в `Artifacts/editmode.log`; `2` → открыть XML, найти `<failure>`; `4` → опечатка в значении `-testPlatform`; `1` или зависание → лицензия либо редактор держит этот же проект открытым; пустой лог → забыли `-logFile` при `-nographics`.


## Видео и доклады

- **QA your code: The new Unity Test Framework — Unite Copenhagen** — Unity — https://www.youtube.com/watch?v=wTiF2D0_vKA — единственное видео, на которое ссылается официальная документация Unity 6.3 по тестам; доклад от команды UTF: архитектура фреймворка, EditMode/PlayMode, запуск в CI. Старый (2019), но концепции с тех пор не менялись.
- **Unit Testing in Unity: Everything You Need to Know** — GAMEDEV ENGINE ROOM — https://www.youtube.com/watch?v=3YvnGzuwDbk — свежий (конец 2025) обзорный разбор: настройка тестовой сборки, первые тесты, типичные ошибки.
- **Testing Unity: Unit & Integration Tests Explained** — GAMEDEV ENGINE ROOM — https://www.youtube.com/watch?v=5TI0uIyjP6I — про границу между модульными и интеграционными тестами, то есть ровно про выбор EditMode против PlayMode.
- **Unity Assembly Definitions Explained (Architecture, Not Just Compile Times)** — git-amend — https://www.youtube.com/watch?v=OqKEaiQrDHY — самое актуальное (2026) по `.asmdef` как инструменту границ между модулями; без этого тесты не настроить.
- **Learn Unit Testing for MVP/MVC Architecture in Unity** — git-amend — https://www.youtube.com/watch?v=Wh27sG0DXzU — как разделить логику и визуал, чтобы логику вообще можно было тестировать.
- **Assembly Definitions and Unit Tests Part 2 — creating the asmdefs** — Sam "Shimi" Halperin — https://www.youtube.com/watch?v=Ag3aVOMN1Cc — пошаговое создание тестовых asmdef на экране (ноябрь 2025).
- **Automate Unity Game Development with GitHub Actions | CI/CD for Game Dev Simplified** — DebugDevin — https://www.youtube.com/watch?v=gUZ9YXrJOAo — настройка GameCI-пайплайна с нуля.
- **Getting started with Build Automation in Unity** — Unity — https://www.youtube.com/watch?v=DV_TCXtl35I — официальная альтернатива GameCI (облачный сервис Unity Build Automation).
- **Unity 3D Tests — How To Setup Unity Tests And Code Coverage Reports?** — Dilmer Valecillos — https://www.youtube.com/watch?v=y75yxUkLB50 — единственный найденный подробный разбор именно Code Coverage; старый, аргументы сверять с документацией 1.3.


## Источники

Все ссылки проверены 2026-08-19.

**Unity 6.3 Manual**, база `https://docs.unity3d.com/6000.3/Documentation/Manual/` — страницы `test-framework/`: `test-framework-introduction`, `edit-mode-vs-play-mode-tests`, `workflow-create-test-assembly`, `workflow-create-test`, `reference-command-line`, `run-tests-from-command-line`, `running-tests`, `running-tests-from-code`, `before-and-after-tests`, `reference-unitysetup-and-unityteardown`, `reference-setup-and-cleanup`, `asserting-and-comparing`, `reference-custom-yield-instructions`, `reference-tests-parameterized`, `reference-async-tests`, `course/overview`, `course/domain-reload`, `course/preserve-test-state`, `course/play-mode-tests`, `course/scene-based-tests`; плюс `EditorCommandLineArguments`, `assembly-definition-file-format`, `domain-reloading`, `com.unity.test-framework`, `com.unity.testtools.codecoverage`, `com.unity.test-framework.performance`.

**Unity 6.3 Scripting API**, база `https://docs.unity3d.com/6000.3/Documentation/ScriptReference/` — `EditorUtility-scriptCompilationFailed`, `EditorApplication.Exit`, `Compilation.CompilationPipeline-compilationFinished`, `Compilation.CompilationPipeline.GetAssemblies`.

**Документация пакетов:**

- https://docs.unity3d.com/Packages/com.unity.testtools.codecoverage@1.3/manual/CoverageBatchmode.html и `.../UsingCodeCoverage.html`
- https://docs.unity3d.com/Packages/com.unity.test-framework.performance@3.5/manual/index.html и `.../taking-measurements.html`, `.../measure-method.html`, `.../measure-frames.html`, `.../test-attributes.html`, `.../cmd-line-args.html`, `.../viewing-results.html`
- https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-command-line.html

**GameCI:**

- https://game.ci/docs/github/activation/
- https://github.com/game-ci/documentation/blob/main/docs/03-github/01-getting-started.mdx , `.../03-test-runner.mdx`
- https://github.com/game-ci/unity-test-runner/blob/main/action.yml
- https://github.com/game-ci/unity-test-runner/blob/main/dist/platforms/ubuntu/run_tests.sh
- https://github.com/game-ci/unity-builder/blob/main/src/model/unity-versioning.ts
- https://hub.docker.com/r/unityci/editor/tags

**Прочее:**

- https://gamedev.center/run-unity-tests-faster-dotnet/ — «Run Unity Tests 10x Faster with .NET», замеры на Unity 6000.3.x
- https://github.com/needle-mirror/com.unity.test-framework/blob/master/CHANGELOG.md — история изменений UTF (зеркало, до 1.4.6)

**Проверено локально** (Unity 6000.3.21f1, macOS, Apple Silicon), а не по документации: путь к бинарнику и вывод `Unity Hub -- --headless editors -i`; версия UTF 1.6.0 и `com.unity.ext.nunit` 2.0.5 (`BuiltInPackages/com.unity.test-framework/package.json`); коды возврата и тексты лог-сообщений (`UnityEditor.TestRunner/CommandLineTest/Executer.cs`, `SettingsBuilder.cs`); регистрозависимость аргументов (`CommandLineParser/CommandLineOptionSet.cs`); имена пунктов меню (`GUI/TestAssets/TestScriptAssetMenuItems.cs`, `TestRunnerWindow.cs`); эталонный тестовый `.asmdef` (`Samples~/2_ActArrangeAssert/Tests_2/Tests_2.asmdef`).

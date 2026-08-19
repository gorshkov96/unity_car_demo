# Стиль кода C# в Unity 6 и типичные ловушки

Как мы пишем C#, чем это проверяется автоматически и какие конструкции в Unity ведут себя не так, как в
обычном .NET. Заглядывать сюда перед новым модулем, спором о названии или подключением внешней библиотеки.
Всё сверено с документацией Unity 6.3 LTS (6000.3) и первоисточниками на 2026-08-19.

---

## TL;DR — решения для нашего проекта

1. **Стиль Microsoft/Unity, скобки Allman** (`{` на новой строке), отступ 4 пробела, скобки не опускаем даже в однострочных `if`.
2. **Приватные поля — `_camelCase`**, типы и публичные члены — `PascalCase`, параметры и локальные — `camelCase`. Префиксы `m_`/`k_`/`s_` из старого гайда Unity не берём.
3. **Никаких публичных полей.** В инспектор — только `[SerializeField] private`. Наружу — свойства.
4. **`.editorconfig` в корне репозитория** — единственный источник правды по форматированию и severity. Unity 6.3 официально читает из него `dotnet_diagnostic.<ID>.severity`.
5. **`Microsoft.Unity.Analyzers` ставим вручную** из NuGet с меткой `RoslynAnalyzer`: в Unity Editor он **не** подключается сам (в отличие от Visual Studio).
6. **`== null` для всего, что наследует `UnityEngine.Object`.** `?.`, `??`, `??=`, `is null` на Unity-объектах запрещены.
7. **Асинхронность — `Awaitable`** (встроенный в Unity 6 аналог корутин на `async/await`), не корутины и не голый `Task`. UniTask и DOTween не тянем, пока не упрёмся в конкретное ограничение.
8. **Никаких `#region`.** XML-`<summary>` — только на публичном API модуля.
9. **Namespace обязателен** (`MachineWorld.Vehicle`) и совпадает с папкой; один `MonoBehaviour` на файл, имя файла = имя класса.
10. **Компиляцию проверяем batch-запуском Unity из терминала** (раздел 7) — «скомпилировалось» и «работает» это разные утверждения.

## 1. Источники правды

Базовый набор правил — Unity e-book «Create a C# style guide: Write cleaner code that scales» и его редакция
«C# Code Style Guide (Unity 6 edition)», плюс сопровождающий файл-пример `StyleExample.cs` (408 строк
с комментариями). Именование и опции `.editorconfig` — из Microsoft Framework Design Guidelines и .NET
code-style rules. Unity-специфичные диагностики — из `microsoft/Microsoft.Unity.Analyzers`.

Unity прямо пишет: это не свод законов, а стартовая точка, «нет единственно верного способа», важнее
последовательность. Ниже — наш выбор из предложенных вариантов.

## 2. Именование и форматирование

| Сущность | Стиль | Пример |
|---|---|---|
| Класс, struct, enum, метод, свойство, событие | `PascalCase`; типы — существительные, методы — глаголы | `VehicleController`, `GetDirection` |
| Интерфейс | `I` + описание способности | `IDamageable`, `IDrivable` |
| Приватное поле | `_camelCase` | `_maxSpeed`, `_rigidbody` |
| Константа | `PascalCase` (не `k_`, не `SCREAMING_CASE`) | `MaxWheelCount` |
| Параметр, локальная переменная | `camelCase` | `deltaTime` |
| Namespace | `PascalCase` через точку | `MachineWorld.World.Traffic` |
| Bool-поле и bool-метод | префикс-глагол, читается как вопрос | `_isGrounded`, `HasFuel()` |
| Событие | глагольная фраза: настоящее причастие = «до», прошедшее = «после» | `OpeningDoor`, `DoorOpened` |
| Метод, поднимающий событие | префикс `On` | `OnDoorOpened()` |

Правила, которые Unity выделяет отдельно:

- **Не сокращать.** `HorizontalAlignment` лучше, чем `AlignmentHorizontal`, и тем более `HorAlign`. Однобуквенные имена — только циклы и математика.
- **Не дублировать контекст.** В классе `Player` поле называется `_score`, а не `_playerScore`.
- Одно объявление на строку; без избыточных инициализаторов (`= 0`, `= null`). **Имя файла = имя класса**, если в файле есть `MonoBehaviour` — это требование Unity, не стиль.
- `enum` — существительное в единственном числе; `[Flags]`-enum — во множественном (`AttackModes`).
- Скобки **Allman**, пробел перед условием (`while (x == y)`), без пробелов внутри `[]` и между именем метода и `(`. Максимум 120 символов в строке.
- **Порядок членов класса:** поля → свойства → события → сообщения Unity (`Awake`, `OnEnable`, `Start`, `Update`, `FixedUpdate`, `OnDisable`, `OnDestroy`) → публичные методы → приватные → вложенные типы.
- Комментарий объясняет **почему**, а не **что**. Атрибуции `// Created by ...` не пишем — для этого есть git.

## 3. `[SerializeField] private` против `public`

Unity сериализует поле (пишет его в сцену, префаб или ассет), если оно `public` **или** помечено
`[SerializeField]`; не `static`, не `const`, не `readonly`; и имеет сериализуемый тип: примитивы,
`enum` ≤ 32 бит, встроенные типы (`Vector3`, `AnimationCurve`), наследники `UnityEngine.Object`,
свои класс/структура с `[Serializable]`, массив или `List<T>` из перечисленного.

```csharp
[SerializeField] private float _maxSpeed = 30f;   // так
public float maxSpeed;                            // не так: ломает инкапсуляцию

public float MaxSpeed => _maxSpeed;               // наружу — свойство
```

- `[SerializeField]` на `public`-поле избыточен, на `static`/`readonly` недопустим (`UNT0013`).
- `readonly` и сериализация несовместимы: нужно неизменяемое после настройки — `[SerializeField] private` + read-only свойство.
- `[NonSerialized]` убирает поле и из сериализации, и из инспектора; `[HideInInspector]` прячет только из инспектора.
- IDE будет ругаться «поле не присваивается» (`CS0649`), «сделай readonly» (`IDE0044`), «не используется» (`IDE0051`, `CA1823`). Не глушите вручную — за это отвечают супрессоры `USP0004`, `USP0006`, `USP0007`, `USP0013` из `Microsoft.Unity.Analyzers`.
- `[Tooltip("...")]` вместо комментария над сериализованным полем: и документирует, и видно в инспекторе. `[Range(0f, 1f)]` вместо ручной валидации.

### Свойства, события, интерфейсы

```csharp
public int MaxHealth => _maxHealth;               // read-only — expression-bodied
public int Score { get; private set; }            // авто-свойство, бэкинг-поле не нужно
public event Action<int> PointsScored;            // System.Action, не кастомный делегат

private void RaisePointsScored(int points) => PointsScored?.Invoke(points);
```

Свойство предпочтительнее публичного поля всегда. Если геттер считает что-то тяжёлое или имеет побочные
эффекты — это метод, а не свойство. Подписка в `OnEnable`, отписка в `OnDisable` — строго симметрично.

## 4. Версия C# в Unity 6.3: что можно и чего нельзя

Официально (Manual → «C# compiler and language version reference»): компилятор **Roslyn**, язык **C# 9.0**,
сборщик мусора Boehm-Demers-Weiser с инкрементальным режимом по умолчанию.

**Неподдерживаемые фичи C# 9 — компиляция падает с ошибкой:** `init`-only сеттеры, ковариантные
возвращаемые типы, module initializers, подавление `localsinit`, расширяемые calling conventions.

**`record`** формально есть, но с двумя оговорками из документации: (1) нужен тип
`System.Runtime.CompilerServices.IsExternalInit`, которого в поддерживаемых Unity рантаймах нет — придётся
объявлять самому; (2) **`record` нельзя использовать в сериализуемых типах**. Вывод: `record` не применяем.
Конфиги — `ScriptableObject`, структуры данных между системами — обычные `class`/`struct` с `[Serializable]`.

**Доступно и полезно:** `target-typed new` (`private readonly List<Wheel> _wheels = new();`), pattern
matching, `switch`-выражения, `static` локальные функции, `using`-декларации, `in`/`ref readonly`.

**Nullable reference types (`#nullable enable`)** — фича C# 8, то есть подмножество C# 9. Отдельной страницы
про NRT в документации Unity 6.3 нет (*официально не подтверждено*), косвенное подтверждение работоспособности —
супрессор **`USP0016`** для `CS8618` в `Microsoft.Unity.Analyzers`. Практическая проблема в другом: API
`UnityEngine` не аннотирован, поэтому шума много, пользы мало. **Решение: не включаем.**

## 5. `.editorconfig` и `.ruleset`

Unity 6.3 **официально документирует** `.editorconfig` в корне проекта как способ задать severity диагностик
(страница «Analyzer scope and rule set files», раздел «Alternatives to rule set files»). Тот же файл читают
Rider, Visual Studio и VS Code — одна настройка на всё. Кладём рядом с `Assets/`:

```ini
root = true

[*.cs]
indent_style = space
indent_size = 4
end_of_line = lf
charset = utf-8
insert_final_newline = true
trim_trailing_whitespace = true

# Allman + скобки всегда
csharp_new_line_before_open_brace = all
csharp_new_line_before_else = true
csharp_new_line_before_catch = true
csharp_new_line_before_finally = true
csharp_prefer_braces = true:warning
csharp_indent_case_contents = true
csharp_indent_switch_labels = true
csharp_space_after_keywords_in_control_flow_statements = true
csharp_space_between_method_call_name_and_opening_parenthesis = false
dotnet_sort_system_directives_first = true
dotnet_separate_import_directive_groups = false

# var — только когда тип очевиден из правой части
csharp_style_var_for_built_in_types = false
csharp_style_var_when_type_is_apparent = true
csharp_style_var_elsewhere = false

# форматирование, неявный модификатор доступа, opt-in UNT0021
dotnet_diagnostic.IDE0055.severity = warning
dotnet_diagnostic.IDE0040.severity = warning
dotnet_diagnostic.UNT0021.severity = suggestion

# приватные поля: _camelCase
dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private
dotnet_naming_style.underscore_camel.capitalization = camel_case
dotnet_naming_style.underscore_camel.required_prefix = _
dotnet_naming_rule.private_fields_underscored.symbols = private_fields
dotnet_naming_rule.private_fields_underscored.style = underscore_camel
dotnet_naming_rule.private_fields_underscored.severity = warning
```

Имена опций взяты из `.editorconfig` самого репозитория `dotnet/roslyn` и документации Microsoft, не по памяти.

**`.ruleset`** — второй, более старый механизм; его единственное преимущество в том, что severity задаётся
**на уровне конкретной сборки**. `Assets/Default.ruleset` действует на все сборки проекта. В корне `Assets/`
допустимы только имена `Default.ruleset`, `Assembly-CSharp.ruleset`, `Assembly-CSharp-firstpass.ruleset`,
`Assembly-CSharp-Editor.ruleset`, `Assembly-CSharp-Editor-firstpass.ruleset`. Для кастомной сборки файл
кладётся рядом с `.asmdef` и может называться как угодно.

Внутри — XML с элементами `<Rule Id="UNT0001" Action="Warning" />`, сгруппированными по
`<Rules AnalyzerId="..." RuleNamespace="...">`. **Наш выбор:** основное — `.editorconfig`; `.ruleset`
заводим, только если понадобится разная строгость для рантайм- и Editor-сборок.

## 6. Roslyn-анализаторы в Unity

Анализатор (плагин к компилятору, проверяющий код по правилам) подключается как managed-плагин с меткой:

1. Скачать NuGet-пакет `Microsoft.Unity.Analyzers` (`.nupkg` — это zip), взять `analyzers/dotnet/cs/Microsoft.Unity.Analyzers.dll`.
2. Положить `.dll` в `Assets/` (например `Assets/Plugins/Analyzers/`).
3. Выделить `.dll`, в **Plugin Inspector** снять **Any Platform**, снять **Editor** и **Standalone** в Include Platforms.
4. Внизу инспектора нажать иконку метки (**Asset Labels**) и добавить метку **`RoslynAnalyzer`** — ровно так, с учётом регистра.

**Важно:** документация Unity явно предупреждает, что из-за различий в компиляции
`Microsoft.Unity.Analyzers.dll` **не настраивается автоматически в Unity Editor** — в Visual Studio он идёт
из коробки, а в консоли Unity правил не будет, пока не сделаете шаги выше.

**Область действия анализатора = сборка.** Анализатор в корне `Assets/` действует на все предопределённые
сборки; анализатор внутри папки с `.asmdef` — только на эту сборку и на те, что её ссылаются. Значит, правила
можно включать точечно для одного модуля.

Правила, которые бьют прямо по нашим задачам:

| ID | Правило | Почему важно |
|---|---|---|
| `UNT0001` | Пустое Unity-сообщение | Пустой `Update()` всё равно вызывается каждый кадр |
| `UNT0002` | `tag == "..."` вместо `CompareTag` | Медленнее и аллоцирует |
| `UNT0004` | `Time.fixedDeltaTime` в `Update` | Путаница петель |
| `UNT0007/0008/0023/0029` | `??`, `?.`, `??=`, `is null` на Unity-объектах | Обходят перегруженный `==` (раздел 8.1) |
| `UNT0013` | Некорректный или избыточный `[SerializeField]` | |
| `UNT0018` | `System.Reflection` в `Update`/`FixedUpdate`/`LateUpdate`/`OnGUI` | |
| `UNT0022`, `UNT0032` | Раздельная установка position и rotation | Есть `SetPositionAndRotation` |
| `UNT0026` | `GetComponent` всегда аллоцирует | Замена — `TryGetComponent` |
| `UNT0028` | Аллоцирующие физические API | `Physics.RaycastNonAlloc` и аналоги |
| `UNT0038` | `new WaitForSeconds(...)` без кеша | Мусор на каждой итерации корутины |
| `UNT0039` | Самовызов `GetComponent` без `[RequireComponent]` | |
| `UNT0041` | Строковые вызовы `Animator` в цикле | Нужен `Animator.StringToHash` |

`UNT0021` («Unity-сообщения должны быть `protected`») — opt-in, включается только через `.editorconfig`.
Смысл: если база объявила `private void Awake()`, а наследник объявил свой `Awake()`, Unity вызовет
**только** наследника, молча.

## 7. IDE на macOS и проверка компиляции

Выбор редактора: `Unity > Settings > External Tools > External Script Editor`.

**VS Code** — дефолт Unity на macOS. Нужны три расширения (`C#`, `C# Dev Kit`, `Unity` от Visual Studio
Tools for Unity) и пакет `com.unity.ide.visualstudio` версии **2.0.20+**. Старый пакет `com.unity.ide.vscode`
**снят с поддержки и использовать его нельзя** — прямая цитата из документации Unity 6.3.
**Rider** — нужен пакет `com.unity.ide.rider` (feature set Engineering); читает тот же `.editorconfig`
и добавляет свои Unity-инспекции, включая предупреждение об обходе lifetime-проверки Unity-объектов.

Путь к бинарнику Unity зависит от версии, поэтому сначала смотрим, что установлено:

```bash
ls /Applications/Unity/Hub/Editor/
```

Затем, подставив свою версию:

```bash
"/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -quit -nographics \
  -projectPath "$PWD" \
  -logFile -
```

- `-batchmode` — без окон и диалогов; при исключении Unity выходит с кодом 1.
- `-quit` — выйти после выполнения команд.
- `-nographics` — не инициализировать графику. Логи в этом режиме отключены, поэтому `-logFile` обязателен.
- `-logFile -` — писать лог в stdout.

Занимает десятки секунд, но это единственный надёжный способ узнать, что код собирается. Для запуска
своего статического метода — `-executeMethod Namespace.Class.Method`; метод должен быть `static`
и лежать в папке `Editor`.

## 8. Ловушки Unity

### 8.1 Фейковый null

Unity-объект состоит из managed-обёртки (C#) и нативного объекта (C++). Unity **перегружает `==` и `!=`**
для `UnityEngine.Object`, чтобы `obj == null` давал `true` в двух случаях: объект — «фейковый null»
(плейсхолдер, которым редактор заполняет неинициализированные поля), либо нативная часть уже уничтожена
через `Destroy`, а managed-обёртка ещё жива.

```csharp
if (_target == null) { }        // правильно
if (_target != null) { }        // правильно

if (_target is null) { }        // НЕТ (UNT0029)
_target?.DoSomething();         // НЕТ (UNT0008)
var t = _target ?? _fallback;   // НЕТ (UNT0007)
_target ??= FindSomething();    // НЕТ (UNT0023)
ReferenceEquals(_target, null)  // НЕТ: проверяет только C#-обёртку
```

Второй нюанс: перегруженный `==` дороже обычного сравнения ссылок, потому что уходит в нативный код.
В `Update`/`FixedUpdate` и в циклах проверок на null быть не должно — ссылки кешируем в `Awake`.
Для не-Unity-типов (обычные классы, `Action`, коллекции) `?.` и `??` разрешены и желательны.

### 8.2 `Update` против `FixedUpdate` против `LateUpdate`

| Метод | Когда | Что туда класть |
|---|---|---|
| `FixedUpdate` | фиксированный шаг физики, 0..N раз за кадр | всё, что трогает `Rigidbody`, силы, `linearVelocity` |
| `Update` | раз в кадр, шаг плавающий | чтение ввода, таймеры, игровая логика |
| `LateUpdate` | раз в кадр, после всех `Update` | камера, IK, всё, что должно видеть финальные позиции |

- **Порядок вызова одного метода на разных объектах не гарантирован** — даже между родителем и ребёнком. Документация Unity прямо запрещает на него полагаться.
- Нужен детерминированный порядок между разными скриптами — `[DefaultExecutionOrder(-100)]` или Project Settings → Script Execution Order. При одинаковом значении порядок снова недетерминирован, и «стабильно работает у меня» не гарантируется между сборками, машинами и версиями Unity.
- `SceneManager.sceneLoaded` приходит после `OnEnable`, но до `Start` всех объектов сцены.

### 8.3 Корутины

- Корутина останавливается, когда `GameObject.activeSelf` становится `false` или объект уничтожен. **Отключение самого компонента (`enabled = false`) корутину НЕ останавливает** — частый источник багов.
- `yield return null` не аллоцирует, `new WaitForSeconds(1f)` аллоцирует каждый раз (`UNT0038`). Кешируйте: `private readonly WaitForSeconds _wait = new(0.5f);` Лямбды в `WaitUntil`/`WaitWhile` создают делегаты и замыкания — тоже мусор.
- Дешевле одна долгоживущая корутина с `while (true) { ...; yield return null; }`, чем перезапуск новой каждый кадр: каждый `StartCoroutine` создаёт объект машины состояний.
- Корутина держит ссылку на владельца и захваченные переменные — незавершённая корутина это утечка.

### 8.4 `async/await` и `Awaitable`

Unity 6 даёт собственный тип **`UnityEngine.Awaitable`** — эквивалент корутин на синтаксисе `async/await`:

```csharp
private async Awaitable BlinkAsync()
{
    while (true)
    {
        await Awaitable.NextFrameAsync();          // = yield return null
        await Awaitable.WaitForSecondsAsync(0.5f);
    }
}
```

Почему он, а не `Task`: экземпляры `Awaitable` **пулятся**, поэтому вызов обычно не аллоцирует; продолжение
выполняется **синхронно, в том же кадре**, тогда как у `Task` оно откладывается через
`UnitySynchronizationContext` до следующего `Update`. Есть `Awaitable.MainThreadAsync()` /
`BackgroundThreadAsync()` для переключения потоков и `AwaitableCompletionSource` для ручного завершения.
Ловушки, названные в документации Unity:

- **`Awaitable` нельзя await-ить дважды** — из-за пулинга это неопределённое поведение вплоть до deadlock. Нужен многократный await — конвертируйте в `Task`.
- Нет встроенных `WhenAll`/`WhenAny`; тысячи параллельных `Awaitable` на каждом объекте сцены медленнее, чем один общий менеджер.
- **Unity не останавливает фоновый код при выходе из Play Mode.** Токен отмены обязателен: `MonoBehaviour.destroyCancellationToken` (пока жив объект) или `Application.exitCancellationToken` (пока не вышли из Play Mode). `destroyCancellationToken` нужно закешировать **до** уничтожения объекта.
- `async void` не использовать: исключения из него не ловятся, возвращайте `Awaitable`. Можно `yield return` `Awaitable` из обычной корутины, но **нельзя** `yield return` `Awaitable<T>`.

**UniTask** (`Cysharp/UniTask`, ставится по git-URL
`https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask`) — надмножество `Awaitable`:
`WhenAll`, `DelayFrame`, контроль `PlayerLoopTiming`, окно `UniTaskTracker` для поиска утечек, `UniTaskVoid`
для fire-and-forget. Автор пишет, что дизайн `Awaitable` был вдохновлён UniTask, и рекомендует UniTask для
прикладного кода, а `Awaitable` — для библиотек без зависимостей. **Наше решение:** живём на `Awaitable`;
UniTask — только когда упрёмся в `WhenAll` или трекинг задач. То же про **DOTween**: для твинов в демо
сначала пробуем `Awaitable` + `Mathf.Lerp` и `AnimationCurve`.

### 8.5 Порядок инициализации, `[RequireComponent]`, `OnValidate`

`Awake` → `OnEnable` → (`sceneLoaded`) → `Start` → цикл кадров.

- В `Awake` — только собственная инициализация (`GetComponent`, кеширование). Обращаться к **другим** объектам опасно: их `Awake` мог ещё не выполниться. Связывание с чужими объектами — в `Start`.
- Полевые инициализаторы и конструкторы `MonoBehaviour` выполняются на загрузочном потоке — Unity API оттуда вызывать нельзя.
- Код при старте редактора — `[InitializeOnLoad]` (обязателен статический конструктор, иначе `UNT0009`) или `[InitializeOnLoadMethod]`; при старте приложения — `[RuntimeInitializeOnLoadMethod]`.

```csharp
[RequireComponent(typeof(Rigidbody))]
[DisallowMultipleComponent]
public sealed class VehicleMotor : MonoBehaviour
{
    private Rigidbody _rigidbody;

    private void Awake() => _rigidbody = GetComponent<Rigidbody>();
}
```

`[RequireComponent]` добавляет зависимость **только в момент `AddComponent`** — в редакторе и в рантайме.
На уже существующие объекты без компонента он задним числом ничего не добавит: это защита от ошибки сборки
сцены, а не гарантия.

`OnValidate` — **только для валидации сериализованных полей** (`Mathf.Clamp` и т.п.). Документация Unity 6.3
предупреждает: он вызывается часто (загрузка сцен, импорт ассетов, сборка плеера, вход в Play Mode), может
вызываться **не из главного потока** (нельзя создавать объекты и трогать не-thread-safe API), **ничего не
сохраняет сам**, а в вариантах префабов присвоенные из него значения не попадают в файл варианта.

### 8.6 Сериализация: полиморфизм, `SerializeReference`, словари

Чего система сериализации Unity **не умеет** по умолчанию:

- **Полиморфизм.** Поле типа `BaseAbility` с лежащим внутри `FireAbility` сериализуется как `BaseAbility` — при загрузке получите базовый класс, данные наследника потеряны.
- **`null` у вложенных классов** — инлайновая сериализация заменяет `null` объектом с пустыми полями.
- **Общие ссылки** — один объект в двух полях после загрузки станет двумя разными.
- **Циклы в данных** — вплоть до зависаний и ошибок инспектора.
- **Многоуровневые типы:** многомерные и «зубчатые» массивы, **`Dictionary`**, вложенные контейнеры (`List<List<T>>`).

Решения: `[SerializeReference]` включает сериализацию по ссылке и снимает первые четыре ограничения; цена —
накладные расходы, поэтому документация советует держать инлайновую сериализацию по умолчанию.
Словарь — либо два `List<T>` внутри обёртки, либо `ISerializationCallbackReceiver` со сборкой `Dictionary`
в `OnAfterDeserialize`. Для наших конфигов полиморфизм чаще всего не нужен: разные варианты — это разные
`ScriptableObject`-ассеты, а не наследники в одном поле.

### 8.7 Строки, `Debug.Log`, аллокации

- Любая конкатенация строк создаёт объект в managed-куче — это `GC.Alloc`, который профайлер отмечает отдельно. В `Update` строк быть не должно: ни `"Score: " + score`, ни интерполяции, ни лишних `ToString()`. UI обновляем только при изменении значения.
- `Debug.Log` дорог не столько записью, сколько **разрешением стек-трейса**. По умолчанию Unity пишет managed-стек (`ScriptOnly`) для всех типов сообщений. Документация прямо рекомендует не выпускать сборку с включёнными стек-трейсами. Настройка: Console → Stack Trace Logging, Player Settings → Other Settings → Stack Trace, либо `Application.SetStackTraceLogType`.
- Глобальный выключатель: `Debug.unityLogger.logEnabled = false`; признак дев-сборки — `Debug.isDebugBuild`. Свою обёртку над логом помечайте `[HideInCallstack]`, чтобы она не засоряла стек в консоли.
- Логи, которые должны исчезать из релиза полностью, оборачивайте в `[System.Diagnostics.Conditional("UNITY_EDITOR")]`-метод или `#if` по символу из `Assets/csc.rsp` (строка `-define:MACHINEWORLD_DEBUG`). В batch mode перекомпиляция не запускается, поэтому символы для batch-сборок задаются именно через `csc.rsp`.
- Боксинг: `foreach` по интерфейсной коллекции, LINQ, передача `struct` в параметр `object` — всё это аллокации в кадре.

## Антипаттерны

- **`public float speed;` вместо `[SerializeField] private float _speed;`** — ломает инкапсуляцию и не даёт добавить валидацию позже.
- **`?.` / `??` / `is null` на `GameObject`, `Transform`, компонентах** — уничтоженный объект пройдёт проверку как живой.
- **`GetComponent`, `Find*`, `Camera.main`, LINQ в `Update`/`FixedUpdate`** — поиск и аллокации в горячем пути. `GetComponent` → `TryGetComponent`, поиск объектов → кеш в `Awake`.
- **`Object.FindObjectOfType<T>()`** — устаревшее API; в Unity 6 это `FindFirstObjectByType<T>()` или `FindAnyObjectByType<T>()`.
- **`Rigidbody.velocity`** — переименовано в `linearVelocity`; совет из туториалов 2019–2022 больше не работает.
- **Пустые `Update()`/`FixedUpdate()`** из шаблона скрипта — рантайм вызывает их каждый кадр (`UNT0001`).
- **`gameObject.tag == "Player"`** вместо `CompareTag("Player")`; **`#region` вокруг половины класса** — маскирует то, что класс пора разбить.
- **`async void` в `MonoBehaviour`** — необработанные исключения, нельзя дождаться завершения; **await одного `Awaitable` дважды** — из-за пулинга неопределённое поведение.
- **Асинхронная работа без `CancellationToken`** — после выхода из Play Mode код продолжает выполняться.
- **Полиморфное поле без `[SerializeReference]`** — данные наследника молча теряются.
- **Подписка на событие без парной отписки** — утечка и вызовы на мёртвых объектах.
- **`private void Awake()` в базовом классе иерархии `MonoBehaviour`** — наследник перекрывает сообщение молча (`UNT0021`).
- **`new WaitForSeconds(...)` в теле корутины** — мусор на каждой итерации.
- **`record` в сериализуемых типах** и **`init`-сеттеры** — первое запрещено документацией, второе не компилируется.

## Чек-лист

Перед коммитом C#-кода:

- [ ] Имя файла совпадает с именем класса; в файле один `MonoBehaviour`.
- [ ] Есть `namespace`, соответствующий папке и `.asmdef`.
- [ ] Приватные поля `_camelCase`, публичные члены `PascalCase`, модификатор доступа указан явно.
- [ ] Нет `public`-полей; данные в инспектор идут через `[SerializeField] private`.
- [ ] Сериализованные поля снабжены `[Tooltip]` или `[Range]` там, где смысл неочевиден.
- [ ] Сравнения Unity-объектов — только `== null` / `!= null`.
- [ ] В `Update`/`FixedUpdate`/`LateUpdate` нет `GetComponent`, `Find*`, `Camera.main`, LINQ, строк, `new`.
- [ ] Всё, что трогает `Rigidbody`, — в `FixedUpdate`; камера — в `LateUpdate`.
- [ ] Нет пустых Unity-сообщений.
- [ ] Подписки и отписки на события парные (`OnEnable` / `OnDisable`).
- [ ] Асинхронные методы возвращают `Awaitable`, а не `void`, и принимают `CancellationToken`.
- [ ] Один `Awaitable` не await-ится дважды.
- [ ] `WaitForSeconds` закеширован, если корутина крутится циклически.
- [ ] Нет `#region`; XML-`<summary>` есть на публичном API модуля.
- [ ] Полиморфные сериализованные поля помечены `[SerializeReference]`, словари обёрнуты вручную.
- [ ] Проект собирается: batch-запуск Unity из раздела 7 отработал без ошибок в логе.

## Видео и доклады

- **Tips for creating your own C# code style guide | Tutorial** — Unity (официальный канал) — https://www.youtube.com/watch?v=bKGMkkf4X0o — 11 минут по тому самому e-book: именование, форматирование, организация класса; ближайший аналог нашего раздела 2.
- **Getting Started with Awaitables** — Unity (официальный канал) — https://www.youtube.com/watch?v=eXkFPoEp3BA — свежий (2026) разбор `Awaitable`: отличия от корутин и `Task`, отмена. Смотреть перед первым асинхронным кодом.
- **Best practices: Async vs. coroutines — Unite Copenhagen** — Unity — https://www.youtube.com/watch?v=7eKi6NKri6I — глубокий доклад о модели исполнения; **старый (2019)**, `Awaitable` в нём нет, но объяснение синхронизационного контекста и цены `Task` актуально.
- **UniTask: How It Replaces Coroutines, Tasks and Awaitable** — git-amend — https://www.youtube.com/watch?v=OlNJ2GkaPTE — сравнение UniTask и встроенного `Awaitable`; помогает понять, чего именно не хватит без зависимости.
- **Unity Assembly Definitions Explained (Architecture, Not Just Compile Times)** — git-amend — https://www.youtube.com/watch?v=OqKEaiQrDHY — `.asmdef` как границы между модулями, а не только ускорение компиляции; прямо про нашу структуру `Scripts/Core`, `Scripts/Vehicle`.
- **5 MUST-KNOW Rider Features for Intermediate Game Devs!** — git-amend — https://www.youtube.com/watch?v=mpOighpYCvU — Unity-специфичные инспекции и рефакторинги в Rider.
- **The Secrets of C# Script Optimization in Unity** — Pirani Dev — https://www.youtube.com/watch?v=7cu14NHE2V0 — аллокации, кеширование, горячий путь.
- **Unity Clean Code: Do Games Really Need SOLID principles?** — Sunny Valley Studio — https://www.youtube.com/watch?v=w_ndKZ7KM8E — скептический взгляд: где чистая архитектура окупается в геймдеве, а где становится оверинжинирингом.

## Источники

Дата обращения — 2026-08-19.

**Документация Unity 6.3 LTS** — префикс `https://docs.unity3d.com/6000.3/Documentation/`:

- `Manual/csharp-compiler.html` (C# 9.0, неподдерживаемые фичи, `record`), `Manual/custom-scripting-symbols.html` (`csc.rsp`), `Manual/EditorCommandLineArguments.html` (`-batchmode`, `-nographics`, путь на macOS)
- `Manual/roslyn-analyzers.html`, `Manual/install-existing-analyzer.html` (метка `RoslynAnalyzer`), `Manual/analyzer-scope-and-diagnostics.html` (`.ruleset` и `.editorconfig`), `Manual/roslyn-analyzers-additional-files.html`
- `Manual/scripting-ide-support.html` — Rider / VS Code / Visual Studio, устаревший `com.unity.ide.vscode`
- `Manual/execution-order.html`, `Manual/Coroutines.html`, `Manual/async-awaitable-introduction.html`, `Manual/async-awaitable-continuations.html`, `Manual/async-awaitable-examples.html`
- `Manual/script-serialization-rules.html`, `Manual/stack-trace.html`, `Manual/performance-managed-memory-introduction.html`
- `ScriptReference/`: `MonoBehaviour.OnValidate.html`, `RequireComponent.html`, `DefaultExecutionOrder.html`, `MonoBehaviour-destroyCancellationToken.html`, `SerializeReference.html`, `HideInCallstackAttribute.html`, `Debug.html`
- https://docs.unity3d.com/6000.1/Documentation/Manual/null-reference-exception.html — «Custom == operator» и фейковый null (в 6.3 страницы нет, содержание актуально)

**Гайдлайны по стилю:**

- https://unity.com/resources/create-code-c-sharp-style-guide-e-book и https://unity.com/resources/c-sharp-style-guide-unity-6 — e-book и его редакция для Unity 6
- https://unity.com/blog/engine-platform/clean-up-your-code-how-to-create-your-own-c-code-style — блог Unity: форматирование и именование
- https://unity.com/how-to/naming-and-code-style-tips-c-scripting-unity — краткая памятка Unity
- https://github.com/thomasjacobsen-unity/Unity-Code-Style-Guide — репозиторий с `StyleExample.cs`
- https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/naming-rules и `.../formatting-rules` — `dotnet_naming_rule`, опции форматирования
- https://github.com/dotnet/roslyn/blob/main/.editorconfig — эталонный `.editorconfig` с проверенными именами опций

**Анализаторы и библиотеки:**

- https://github.com/microsoft/Microsoft.Unity.Analyzers — репозиторий; `doc/index.md` — полный список правил UNT и супрессоров USP; `doc/UNT0021.md` — пример opt-in через `.editorconfig`
- https://nuget.org/packages/Microsoft.Unity.Analyzers — NuGet-пакет
- https://github.com/Cysharp/UniTask — разделы «vs Awaitable» и «async void vs async UniTaskVoid»

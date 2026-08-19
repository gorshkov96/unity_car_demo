# Архитектура Unity-проекта: композиция, ScriptableObject, события

Как раскладывать код нашего демо-проекта (машина + живой мир) на модули, чем связывать модули между собой и чего не делать. Заглядывать сюда перед созданием нового скрипта, нового модуля или нового `.asmdef`. Все проверки API — по документации Unity 6.3 LTS (6000.3), сборка документации от 2026-08-19.

---

## TL;DR — решения для нашего проекта

1. **Машина — набор компонентов, а не класс-наследник.** `CarInput` (читает Input System), `CarMotor` (силы в `FixedUpdate`), `CarAudio`, `CarVfx`, `CarTelemetry`. Общий `MonoBehaviour`-предок им не нужен.
2. **Данные машины и мира — в ScriptableObject-ассетах** (`CarSpecSO`, `TrafficSettingsSO`). Один ассет — одна копия в памяти, правится без перекомпиляции, переиспользуется между префабами.
3. **Внутри модуля — прямые ссылки через `[SerializeField]`. Между модулями — события.** Ссылка `Vehicle → UI` запрещена физически, через `.asmdef`, а не «по договорённости».
4. **Событийная шина — ScriptableObject event channels** (`VoidEventChannelSO`, `GenericEventChannelSO<T>` по образцу официального сэмпла Unity PaddleBall). Достаточно 5–7 каналов, а не по каналу на каждый чих.
5. **`.asmdef` сразу, с первого коммита кода:** `Game.Core` (без зависимостей) → `Game.Vehicle`, `Game.World`, `Game.Gameplay` → `Game.UI`. Стрелки только вниз, циклы Unity запретит компилятором.
6. **DI-контейнер (VContainer/Zenject) на старте не подключаем.** Для демо на одну сцену выигрыш меньше, чем стоимость обучения владельца проекта. Возврат к вопросу — когда появится 3+ сцены и потребность подменять реализации в тестах.
7. **Синглтон допустим ровно один** — точка входа/бутстрап (`GameBootstrap`), и только если событийных каналов реально не хватило. Не `AudioManager.Instance`, не `GameManager.Instance` по всему коду.
8. **Физика и расчёты — в обычных C#-классах** (`SuspensionSolver`, `GearBox`), которые не наследуют `MonoBehaviour`. Их покрывают EditMode-тестами за секунды; `MonoBehaviour` остаётся тонкой оболочкой.
9. **Никакой зависимости от Script Execution Order.** `Awake` — только свои ссылки, `Start` — общение с чужими объектами, `OnEnable/OnDisable` — подписка/отписка. `[DefaultExecutionOrder]` — крайняя мера с комментарием «почему».
10. **Ввод — через project-wide Input Actions** (`InputSystem.actions`), подписка на колбэки, а не опрос `Keyboard.current` в `Update`. Буфер ввода читается в `Update`, применяется в `FixedUpdate`.

---

## 1. Композиция вместо наследования

Unity построен на компонентах: GameObject — это контейнер, поведение задаётся набором прикреплённых `MonoBehaviour`. Официальная документация прямо описывает такой стиль как «объектно-ориентированную разработку», где логика и данные инкапсулированы в компонентах.

Практический критерий: **если новый класс нужен только чтобы «добавить одно поведение» — это компонент, а не наследник.**

```csharp
// Плохо: иерархия, которая через полгода станет неразрешимой
public class Car : Vehicle { }          // Vehicle : Entity : MonoBehaviour

// Хорошо: машина = префаб с набором независимых компонентов
[RequireComponent(typeof(Rigidbody))]
public sealed class CarMotor : MonoBehaviour
{
    [SerializeField] private CarSpecSO _spec;   // ScriptableObject-конфиг
    private Rigidbody _body;

    private void Awake() => _body = GetComponent<Rigidbody>();

    private void FixedUpdate()
    {
        // Unity 6: velocity переименован в linearVelocity
        float speed = _body.linearVelocity.magnitude;
        _body.AddForce(transform.forward * _spec.EngineForce(speed), ForceMode.Force);
    }
}
```

Полезные атрибуты: `[RequireComponent(typeof(Rigidbody))]` (Unity сама добавит зависимость), `[DisallowMultipleComponent]` (запрет двух копий на объекте), `sealed` на классах-листах (сигнал «наследовать не надо»).

**Где наследование всё-таки уместно:** базовый абстрактный класс для семейства состояний FSM, базовый `GenericEventChannelSO<T>`, базовый класс редакторских инструментов. То есть там, где подтипы реально взаимозаменяемы (принцип подстановки Лисков из SOLID).

---

## 2. Разделение ввод → логика → визуал

Три слоя, между которыми ходят только данные:

| Слой | Что делает | Где живёт | Апдейт |
|------|-----------|-----------|--------|
| Ввод | читает устройство, отдаёт структуру `CarInputState` | `Game.Vehicle` | `Update` |
| Логика | считает силы, передачи, состояние | обычные C#-классы + `FixedUpdate` | `FixedUpdate` |
| Визуал/звук | крутит меши, мигает светом, играет звук | `MonoBehaviour` | `Update` / `LateUpdate` |

```csharp
// Снимок ввода: структура, без аллокаций, легко подменить на ИИ-водителя
public readonly struct CarInputState
{
    public readonly float Steer, Throttle, Brake;   // -1..1, 0..1, 0..1
    public CarInputState(float steer, float throttle, float brake)
        => (Steer, Throttle, Brake) = (steer, throttle, brake);
}

public interface ICarInputSource { CarInputState Read(); }
```

Дальше `CarMotor` знает только про `ICarInputSource`. Реализаций две: `PlayerCarInput` (Input System) и `AiCarInput` (ИИ-водитель) — и подменять их можно, не трогая физику. Это тот самый принцип инверсии зависимостей из SOLID: высокоуровневый модуль (физика) зависит от абстракции, а не от геймпада.

**Про Input System (Unity 6).** Начиная с Input System 1.8 есть *project-wide actions* — один ассет `InputSystem_Actions`, назначаемый в `Edit > Project Settings > Input System Package`, доступный из кода как `InputSystem.actions.FindAction("Move")` без ручной ссылки. Документация рекомендует именно один такой ассет на проект. Отдельные Action Maps («Gameplay», «UI», «Debug») включаются и выключаются целиком при смене состояния игры — так снимается классическая проблема «пробел одновременно подтверждает меню и жмёт на газ».

Официальный блог Unity (кейс Backyard Baseball 2026 — гостевой пост Mega Cat Studios от 22.06.2026) отдельно предупреждает: даже с новым Input System многие продолжают опрашивать `Keyboard.current.spaceKey.isPressed` в `Update` — это привязывает ввод к частоте кадров и теряет короткие нажатия. Событийные колбэки очередь ввода не теряют.

---

## 3. ScriptableObject как конфиг

`ScriptableObject` (SO) — сериализуемый тип Unity, который живёт **ассетом в проекте**, а не компонентом на объекте. Документация Unity 6.3 называет его основным назначением хранение данных и прямо указывает выигрыш: если 200 префабов трафика читают массу и мощность из одного SO, в памяти лежит одна копия значений, а не 200.

```csharp
[CreateAssetMenu(fileName = "CarSpec", menuName = "Game/Vehicle/Car Spec")]
public sealed class CarSpecSO : ScriptableObject
{
    [SerializeField] private float _mass = 1200f;
    [SerializeField] private AnimationCurve _torqueBySpeed;

    public float Mass => _mass;
    public float EngineForce(float speed) => _torqueBySpeed.Evaluate(speed);
}
```

**Подводный камень №1 (главный).** Изменения SO во время игры ведут себя по-разному в редакторе и в билде:

- В **редакторе** правки сохраняются на диск и переживают выход из Play Mode — то есть «поигрался, случайно изменил здоровье — оно таким и осталось». Документация: сохранение изменений, сделанных из скрипта в Edit mode, требует `EditorUtility.SetDirty`, но правки через инспектор пишутся автоматически.
- В **собранном плеере** ассеты SO доступны только на чтение; изменения живут до конца сессии и никуда не сохраняются.

Вывод для нас: **SO — для read-only конфигов.** Изменяемое состояние (текущая скорость, счёт, прогресс) — в обычных объектах времени выполнения. Если очень нужен «изменяемый SO», делайте `Instantiate(configSO)` на старте и правьте копию.

**Подводный камень №2.** SO не сериализует интерфейсы «как есть». Для полиморфных полей внутри SO/MonoBehaviour нужен `[SerializeReference]` — он поддерживает поля типа интерфейса, абстрактного класса и `System.Object`, но значением может быть только обычный C#-класс с `[Serializable]`, **не** наследник `UnityEngine.Object`. И он дороже по памяти и времени загрузки, чем сериализация по значению, — применять точечно.

---

## 4. ScriptableObject как event channel

Идея (доклад Ryan Hipple, Unite Austin 2017; официально закреплена в e-book Unity, издание для Unity 6 от 22.08.2025 и сэмпле PaddleBall): событие — это тоже ассет. Отправитель и получатель ссылаются на один ассет-канал и не знают друг о друге.

Минимальный канал по образцу официального сэмпла:

```csharp
using UnityEngine;
using UnityEngine.Events;   // UnityAction живёт здесь, а не в UnityEngine

[CreateAssetMenu(menuName = "Events/Void Event Channel", fileName = "VoidEventChannel")]
public class VoidEventChannelSO : ScriptableObject
{
    public UnityAction OnEventRaised;
    public void RaiseEvent() => OnEventRaised?.Invoke();
}

public abstract class GenericEventChannelSO<T> : ScriptableObject
{
    public UnityAction<T> OnEventRaised;
    public void RaiseEvent(T parameter) => OnEventRaised?.Invoke(parameter);
}

[CreateAssetMenu(menuName = "Events/Float Event Channel", fileName = "FloatEventChannel")]
public sealed class FloatEventChannelSO : GenericEventChannelSO<float> { }
```

Одно отличие от сэмпла: там оба класса наследуют не `ScriptableObject` напрямую, а
`DescriptionSO` — крохотный базовый SO с полем `[TextArea] m_Description`, чтобы у каждого
канала-ассета в инспекторе была подпись «что это за событие». При десятке каналов вещь
полезная, механики не меняет.

Подписка — строго парой `OnEnable` / `OnDisable`, иначе будут «мёртвые» подписчики и утечки:

```csharp
[SerializeField] private FloatEventChannelSO _speedChanged;

private void OnEnable()  => _speedChanged.OnEventRaised += HandleSpeed;
private void OnDisable() => _speedChanged.OnEventRaised -= HandleSpeed;
```

Что это даёт нам конкретно:

- Спидометр в UI не знает про машину. Машина не знает про UI. Можно удалить UI-модуль целиком — физика продолжит работать.
- Каналы живут на уровне проекта, поэтому переживают загрузку сцен и связывают объекты из разных additive-сцен. Документация Unity прямо пишет, что это «часто снимает необходимость в синглтоне».
- Дизайнерский бонус: компонент-слушатель без кода (`VoidEventChannelListener` в сэмпле) вызывает `UnityEvent` в инспекторе — можно повесить звук на событие без программиста.

**Честные минусы, которые надо принять заранее** (сводка из практики сообщества, не из официальных материалов):

- IDE не покажет «кто слушает это событие» — «Find Usages» ломается, отладка идёт по точкам останова.
- Каждое событие — отдельный файл. При 80 каналах папка становится помойкой.
- Потерянная ссылка в инспекторе = молча неработающая механика. Официальный сэмпл лечит это классом-валидатором (`NullRefChecker`), вызываемым в `Awake`.
- Для соло-разработчика чисто кодовая шина (`static event Action` или event bus на структурах) часто проще и быстрее в отладке. SO-каналы окупаются, когда правки делают не только программисты.

**Наша дозировка:** каналы — только для межмодульных сигналов (`OnRaceStarted`, `OnCarCrashed`, `OnCheckpointPassed`, `OnSpeedChanged`, `OnGameStateChanged`). Внутри модуля — обычные C#-`event Action`.

---

## 5. Assembly Definition (.asmdef)

`.asmdef` — файл, который говорит Unity «скомпилируй эту папку в отдельную библиотеку (DLL)». По умолчанию весь код проекта падает в одну сборку `Assembly-CSharp.dll`; документация Unity 6.3 перечисляет три её недостатка: правка одного скрипта пересобирает всё, любой скрипт может залезть в любой другой, всё компилируется под все платформы.

Раскладка под наш проект:

```
Game.Core       — утилиты, интерфейсы, каналы событий. Ссылок нет ни на кого.
Game.Vehicle    — → Game.Core
Game.World      — → Game.Core
Game.Gameplay   — → Game.Core, Game.Vehicle, Game.World
Game.UI         — → Game.Core, Game.Gameplay
Game.Editor     — → все runtime-сборки (только в папке Editor)
Game.Tests.*    — → тестируемые сборки + test-framework
```

Правила, которые Unity 6.3 обеспечивает жёстко:

- **Циклические ссылки запрещены.** Если A ↔ B — надо либо слить их в одну сборку, либо вынести общее в третью (у нас это `Game.Core`). Ошибка компиляции, а не совет.
- **Custom-сборка не может ссылаться на предопределённые** (`Assembly-CSharp`). То есть, начав выносить код в `.asmdef`, надо выносить всё.
- **Runtime-код не может ссылаться на Editor-код**, обратное — можно, через явную ссылку.
- Порядок компиляции определяется зависимостями, задать вручную нельзя.

Тонкости, о которых спотыкаются: папка `Editor` внутри папки с `.asmdef` **перестаёт** попадать в `Assembly-CSharp-Editor` и уезжает в вашу runtime-сборку (лечится своим `.asmdef` с `Include Platforms = Editor` либо ассетом Assembly Reference, отправляющим эту папку в общую editor-сборку); `Auto Referenced` (включён по умолчанию) заставляет предопределённые сборки пересобираться при любой правке вашей; `Override References` позволяет не тянуть автоматом все precompiled-плагины; `Use GUIDs` — включать, чтобы переименование `.asmdef` не ломало ссылки.

Практический эффект: правка кода UI не пересобирает физику. У `.asmdef` нет рантайм-накладных расходов — это чисто про время компиляции, границы зависимостей и размер билда.

---

## 6. Интерфейсы и события вместо прямых ссылок

Три допустимых способа связать два модуля, от простого к сложному:

1. **Интерфейс + `[SerializeField]` на конкретной реализации.** Работает внутри сцены, но Unity не умеет сериализовать поле типа интерфейса напрямую. Официальный сэмпл Unity решает это в лоб — сериализует `MonoBehaviour` и кастует:

```csharp
[SerializeField] private MonoBehaviour _clientBehaviour;
private ISwitchable Client => _clientBehaviour as ISwitchable;
```

Это костыль, о чём сами авторы пишут в комментарии. Альтернатива для не-`UnityEngine.Object` реализаций — `[SerializeReference]`.

2. **Событие/канал** — когда получателей может быть несколько и отправителю всё равно, кто слушает. Наш основной инструмент.
3. **Интерфейс, переданный извне (инъекция).** Кто-то один (бутстрап, фабрика, спавнер) знает обе стороны и связывает их: `motor.SetInputSource(new AiCarInput(...))`. Самый прозрачный для отладки способ и по факту «ручной DI».

Наблюдение из обсуждений Unity Discussions, полезное для нас: **префаб — это уже форма инъекции зависимостей.** Вы кладёте в слот `Suspension` разные реализации подвески и получаете разную машину без единой строчки кода в контейнере.

---

## 7. Конечные автоматы для состояний

Конечный автомат (FSM) — объект в один момент времени находится ровно в одном состоянии. Для нас: состояние игры (`Loading → Menu → Driving → Paused → Results`) и состояние ИИ-водителя (`Cruise → Overtake → Avoid → Recover`).

Официальный e-book Unity «Level up your code with game programming patterns» включает State Pattern в набор из 11 паттернов и реализует его классами-состояниями, а не `switch`. Причина простая: `switch` растёт квадратично при добавлении состояний и переходов.

```csharp
public interface IState { void Enter(); void Tick(float deltaTime); void Exit(); }

public sealed class StateMachine
{
    private IState _current;

    public void ChangeState(IState next)
    {
        if (ReferenceEquals(_current, next)) return;
        _current?.Exit();
        _current = next;
        _current.Enter();
    }

    public void Tick(float deltaTime) => _current?.Tick(deltaTime);
}
```

Важно: `StateMachine` и состояния — **обычные C#-классы**, не `MonoBehaviour`. Тогда их покрывают EditMode-тестами без сцены. `MonoBehaviour` только дёргает `Tick` из `Update`.

Отдельно: не путайте это с Animator State Machine — та отвечает за анимации, и класть в неё игровую логику через `StateMachineBehaviour` можно, но отлаживать тяжело.

---

## 8. DI-контейнеры, сервис-локатор, синглтоны

**Синглтон** (`Instance` через статическое поле). Официальный e-book Unity включает его в список паттернов и отдельно разбирает «подводные камни». Реальные проблемы: скрытая зависимость (по сигнатуре класса не видно, что он лезет в `AudioManager`), невозможность подменить в тесте, порядок инициализации, и — специфично для Unity 6 — при **выключенном Domain Reload** статические поля переживают выход из Play Mode, так что `Instance` может указывать на уничтоженный объект. Это лечится сбросом через `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`, который вызывается при старте рантайма до загрузки первой сцены. Документация предлагает и второй путь — чистить статику при *выходе* из Play Mode через `EditorApplication.playModeStateChanged` и `PlayModeStateChange.ExitingPlayMode`; он не годится, если код работает и в Edit mode.

Забавная деталь: сама документация Unity 6.3 на `Object.FindFirstObjectByType` пишет, что метод крайне тяжёлый и «в большинстве случаев лучше использовать паттерн синглтон». То есть Unity не считает синглтон злом — она считает злом поиск по сцене в горячем пути.

**Сервис-локатор** (реестр `Get<IAudioService>()`). Формально лучше синглтона — можно подменить реализацию. Но наследует главный минус: зависимость по-прежнему не видна в сигнатуре, компилятор не подскажет забытую регистрацию, падение будет в рантайме. В обсуждениях Unity его регулярно называют антипаттерном ровно по этой причине. Если применять — то для небольшого набора настоящих сквозных сервисов (звук, загрузка сцен), а не для всего подряд.

**DI-контейнеры.** Состояние экосистемы на 2026-08:

| Библиотека | Последний релиз | Комментарий |
|---|---|---|
| VContainer | 1.19.0 (2026-07-01) | активно поддерживается, минимальные аллокации, IL2CPP/AOT; требует Unity 2018.4+ |
| Reflex | 14.3.1 (2026-06-18) | минималистичный, быстрый, IL2CPP/WebGL |
| Zenject (modesttree) | последний push 2024-01-26 | де-факто заморожен |
| Extenject (форк Mathijs-Bakker) | активность есть (2026-08) | автор объявлял об уходе ещё в 2023; сообщается об удалении из Asset Store по копирайт-претензии — **не подтверждено первоисточником** |

Сравнения производительности публикуют сами авторы библиотек: «Fast Resolve: Basically 5-10x faster than Zenject» — дословная строка из README VContainer, превосходство Reflex — из бенчмарков, лежащих в его же репозитории (`Assets/Reflex.Benchmark`). Независимых замеров на Unity 6 нет; это заинтересованная сторона, к цифрам относиться соответственно.

**Рекомендация для нашего проекта: контейнер не подключать.** Аргументы: (1) владелец проекта не пишет на C#, контейнер добавляет невидимый слой магии, который тяжело отлаживать; (2) у `MonoBehaviour` нет конструктора, поэтому DI в Unity всё равно «половинчатый» — инъекция в поля/методы; (3) для одной сцены с 6–8 модулями связывание вручную в бутстрапе занимает 30 строк. Пересмотреть решение, когда появится 3+ сцен и потребность гонять логику в тестах с моками.

---

## 9. Порядок выполнения и инициализация

Документированный порядок в жизненном цикле одного скрипта: `Awake` → `OnEnable` → `Start` → ... → `FixedUpdate` → `Update` → `LateUpdate`.

Что **гарантировано** документацией Unity 6.3:

- `Awake` вызывается один раз за время жизни экземпляра скрипта; вызывается даже если компонент выключен (но GameObject активен).
- Все `Awake` завершаются до первого `Start`.
- Для активных объектов сцены `Awake` вызывается после инициализации всех активных GameObject — поэтому в `Awake` уже безопасно искать объекты.
- `SceneManager.sceneLoaded` поднимается после `OnEnable`, но до `Start`.

Что **не гарантировано** и на что нельзя опираться:

- Порядок `Awake` между разными GameObject — недетерминирован. Документация прямо: «не рассчитывайте, что ссылка, настроенная в `Awake` одного объекта, будет доступна в `Awake` другого».
- Порядок между скриптами с одинаковым (или дефолтным) приоритетом выполнения — недетерминирован и не гарантирован между билдами, машинами и версиями Unity.

Отсюда рабочее правило, которое снимает 90% проблем:

> **`Awake` — только свои собственные ссылки (`GetComponent`, инициализация полей). `Start` — любое общение с чужими объектами. `OnEnable`/`OnDisable` — подписка и отписка от событий.**

**Script Execution Order** настраивается двумя способами: в `Project Settings > Script Execution Order` и атрибутом `[DefaultExecutionOrder(50)]` в коде. Тонкости из документации:

- Значение из окна Project Settings **перебивает** атрибут в коде, и атрибут в окне не отображается — идеальный источник «магии, которую никто не найдёт».
- Порядок не влияет на `OnDisable`, `OnDestroy` и на методы с `[RuntimeInitializeOnLoadMethod]`.
- При additive-загрузке порядок применяется целиком к одной сцене, потом к следующей — не «размазывается» между сценами.
- Порядок задаётся типу скрипта, а не объекту: все экземпляры скрипта с меньшим значением отработают раньше любого экземпляра скрипта с бо́льшим, независимо от того, на каких GameObject они висят.

**Инициализация сцены.** Официальный сэмпл к e-book по паттернам держит бутстрап-сцену первой в Build Settings, а класс `SceneBootstrapper` (`[InitializeOnLoad]`, лежит в папке `Editor`) принудительно подгружает её при входе в Play Mode из любой сцены и возвращает исходную при выходе. Это редакторное удобство, а не рантайм-логика: саму сборку зависимостей делает обычный `MonoBehaviour` в самой сцене. Для нас разумная схема:

```csharp
public sealed class GameBootstrap : MonoBehaviour
{
    [SerializeField] private CarSpecSO _carSpec;
    [SerializeField] private WorldSettingsSO _worldSettings;

    private void Start()   // Start, а не Awake: все Awake уже отработали
    {
        var world = WorldBuilder.Build(_worldSettings);   // процедурная генерация карты
        var car   = CarFactory.Spawn(_carSpec, world.StartPoint);
        // явная сборка зависимостей — «ручной DI»
        car.SetInputSource(new PlayerCarInput(InputSystem.actions));
    }
}
```

Плюс `[RuntimeInitializeOnLoadMethod]` — для кода, который должен отработать до сцены (`SubsystemRegistration` — при старте рантайма до загрузки первой сцены; `BeforeSceneLoad` — объекты первой сцены загружены, но `Awake` ещё не вызывались).

---

## 10. Тестируемость: чистая логика вне MonoBehaviour

EditMode-тесты гоняются без запуска сцены и потому быстрые; PlayMode-тесты поднимают рантайм и медленные. Отсюда практика: всё, что можно посчитать без сцены, — считать в обычном C#-классе.

```csharp
// Game.Vehicle: чистая логика, ноль Unity-зависимостей → EditMode-тест
public sealed class GearBox
{
    private readonly float[] _ratios;
    public int CurrentGear { get; private set; }
    public GearBox(float[] ratios) => _ratios = ratios;
    public float Torque(float engineTorque) => engineTorque * _ratios[CurrentGear];
    public void ShiftUp() => CurrentGear = Mathf.Min(CurrentGear + 1, _ratios.Length - 1);
}
```

`MonoBehaviour` в такой схеме — «humble object» (тонкая оболочка): читает ввод, дёргает логику, применяет результат к `Rigidbody`. Тестировать в нём нечего, и это правильно.

Раскладка тестов: `Game.Tests.EditMode` (ссылается на runtime-сборки) и `Game.Tests.PlayMode` — обе отдельными `.asmdef` с ссылками на `UnityEngine.TestRunner` / `UnityEditor.TestRunner`.

---

## Антипаттерны

| Что часто советуют | Почему для нас плохо |
|---|---|
| `GameManager.Instance`, `AudioManager.Instance`, `UIManager.Instance` — «менеджер на каждую подсистему» | Скрытые зависимости, невозможность тестировать, порядок инициализации, а при выключенном Domain Reload статика ещё и переживает Play Mode. Замена — 5–7 SO-каналов |
| `FindObjectOfType<T>()` | Не удалён, но помечен `[Obsolete]`: компилируется с предупреждением. В Unity 6 — `FindFirstObjectByType<T>()` / `FindAnyObjectByType<T>()` / `FindObjectsByType<T>(FindObjectsSortMode.None)`. Документация называет их «очень ресурсоёмкими» — не в `Update` |
| `rigidbody.velocity` | В Unity 6 — `Rigidbody.linearVelocity`. Старое имя из туториалов 2019–2022 |
| «Поправь Script Execution Order, и заработает» | Это отложенный баг. Настройка невидима в коде, значение из Project Settings перебивает `[DefaultExecutionOrder]`, между сценами применяется по-своему. Правильно — переставить инициализацию из `Awake` в `Start` |
| «SO-переменная вместо каждого поля» (ScriptableObject Variables из доклада 2017) | Сотни ассетов, «Find Usages» не работает, значения в редакторе мутируют между сессиями. Для конфига машины хватит одного `CarSpecSO` |
| «Наследуй всё от `BaseEntity : MonoBehaviour`» | Unity — про композицию. Глубокие иерархии `MonoBehaviour` ломаются на первом же объекте, которому нужны две «половинки» из разных веток |
| Подписка на события в `Start`, отписка в `OnDestroy` | Объект, выключенный и включённый обратно (пул трафика!), подпишется дважды. Только `OnEnable` / `OnDisable` |
| Мутировать SO во время игры как «глобальное состояние» | В редакторе изменения переживут выход из Play Mode, в билде — нет. Расхождение поведения «у меня работает / в билде нет» |
| DI-контейнер «чтобы было по-взрослому» | Ещё один невидимый слой + внешняя зависимость. Для одной сцены выигрыш отрицательный |
| Один `.asmdef` на весь `Assets/_Project` | Даёт ускорение компиляции, но не даёт главного — границ. `Vehicle` по-прежнему сможет дёрнуть `UI` |
| Сборка сцены руками в редакторе для всего | Для нас `.unity`/`.prefab` — плохо редактируемый YAML. Процедурная генерация мира и спавн из скриптов надёжнее и ревьюится в диффе |

---

## Чек-лист

Перед созданием нового скрипта:

- [ ] Это новое поведение существующего объекта? → компонент, а не наследник.
- [ ] Тут есть расчёт, который можно проверить без сцены? → вынести в обычный C#-класс.
- [ ] Числа-настройки в коде? → в `ScriptableObject`-конфиг.
- [ ] Скрипт лезет в другой модуль напрямую? → интерфейс или event channel.

Перед созданием нового `.asmdef`:

- [ ] Понятно, от кого он зависит, и стрелки идут только «вниз»?
- [ ] Общий код вынесен в `Game.Core`, а не продублирован?
- [ ] Внутри есть папка `Editor`? → ей нужен свой `.asmdef` с `Include Platforms = Editor` или Assembly Reference на общую editor-сборку.
- [ ] `Use GUIDs` включён в ссылках?

Перед созданием нового event channel:

- [ ] Событие действительно межмодульное? (внутри модуля — обычный `event Action`)
- [ ] Есть подписка в `OnEnable` и симметричная отписка в `OnDisable`?
- [ ] В `Header` в инспекторе написано, канал на отправку или на приём?
- [ ] Есть проверка на незаполненную ссылку в `Awake`?

Перед коммитом:

- [ ] Нет `GetComponent` / `Find*` / `Camera.main` / LINQ / `new` в `Update` и `FixedUpdate`.
- [ ] Все обращения к `Rigidbody` — в `FixedUpdate`.
- [ ] Публичных полей нет: `[SerializeField] private float _maxSpeed;`.
- [ ] Ни один класс не рассчитывает на порядок `Awake` между объектами.
- [ ] Все `.meta` для новых ассетов (включая `.asmdef.meta`) в коммите.

---

## Видео и доклады

- Unite Austin 2017 — Game Architecture with Scriptable Objects — Unity (Ryan Hipple) — https://www.youtube.com/watch?v=raQ3iHhE_Kk — первоисточник SO-архитектуры: event channels, runtime sets, SO-переменные. Смотреть критически: часть советов (SO-переменная на каждое поле) сообщество считает избыточной, но идея каналов оттуда.
- Game architecture with ScriptableObjects | Open Projects Devlog — Unity (2020) — https://www.youtube.com/watch?v=WLDgtRNK2VE — официальный разбор на реальном опенсорс-проекте Unity, ближе к продакшену, чем доклад 2017.
- Event Bus & Scriptable Object Event Channels | Unity Game Architecture Tutorial — LlamAcademy (2024) — https://www.youtube.com/watch?v=95eFgUENnTc — сравнение двух подходов в одном видео: чисто кодовая шина событий против SO-каналов. Лучший материал, чтобы выбрать между ними осознанно.
- CLEAN Game Architecture with ScriptableObjects | Unity Tutorial — Sasquatch B Studios (2024) — https://www.youtube.com/watch?v=wzPputN4Ts4 — разбор «почему у синглтонов плохая репутация» и как их заменить.
- Service Locator: Inversion of Control in Unity C# — git-amend (2023) — https://www.youtube.com/watch?v=D4r5EyYQvwY — реализация сервис-локатора со скоупами (объект / сцена / глобально) и честное обсуждение, почему его называют антипаттерном.
- Finally, a Unity Dependency Injection Framework That Just Works — git-amend (2025) — https://www.youtube.com/watch?v=6bJmEnpxVoI — Reflex: жизненные циклы singleton/scoped/transient, AOT и IL2CPP. Смотреть, если вернёмся к вопросу контейнера.
- Introduction to Dependency Injection and VContainer — Tale Forge (2025) — https://www.youtube.com/watch?v=7IeSsaD1lcY — вводная часть свежей серии по VContainer; продолжения: установка и основы https://www.youtube.com/watch?v=17U3bLkFgEU, скоупы и времена жизни https://www.youtube.com/watch?v=3yV9O8J1f54, фабрики https://www.youtube.com/watch?v=pzkjnhRhKKw.
- The State Pattern (C# and Unity) — Finite State Machine — One Wheel Studio (2020) — https://www.youtube.com/watch?v=nnrOhb5UdRc — классическая реализация FSM классами-состояниями; API не устарел.
- Improve your code with Event Channels — Fluffy GameDev (2021) — https://www.youtube.com/watch?v=29H30OEZ1d4 — компактное объяснение самой идеи каналов, если предыдущие видео длинные.
- Using Scriptable Objects for Events in Unity | Scene Independent Event System — Dan Pos (2021) — https://www.youtube.com/watch?v=e8WHdMI8hxk — акцент на связывании объектов из разных сцен, что нам актуально при additive-загрузке.

---

## Источники

Дата обращения ко всем ссылкам — 2026-08-19.

Официальная документация Unity 6.3 LTS (6000.3):

- https://docs.unity3d.com/6000.3/Documentation/Manual/object-oriented-development.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/class-ScriptableObject.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/class-MonoBehaviour.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/execution-order.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/script-execution-order.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/domain-reloading.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/assembly-definition-files.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/assembly-definitions-intro.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/assembly-definitions-referencing.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/assembly-definitions-creating.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/cus-asmdef.html
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/MonoBehaviour.Awake.html
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/DefaultExecutionOrder.html
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SerializeReference.html
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Object.FindFirstObjectByType.html
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/RuntimeInitializeLoadType.html
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rigidbody-linearVelocity.html
- https://docs.unity3d.com/Packages/com.unity.inputsystem@1.15/manual/ProjectWideActions.html

Официальные материалы Unity (блог, e-book, форум):

- https://unity.com/how-to/scriptableobjects-event-channels-game-code
- https://unity.com/resources/create-modular-game-architecture-scriptableobjects-unity-6
- https://unity.com/resources/level-up-your-code-with-game-programming-patterns
- https://unity.com/blog/input-system-event-driven-architecture-for-scalable-controls
- https://discussions.unity.com/t/unity-6-edition-ebook-and-sample-project-on-scriptable-objects/1680222
- https://discussions.unity.com/t/updated-e-book-more-design-patterns-and-solid-principles/1490730
- https://github.com/UnityTechnologies/PaddleGameSO
- https://github.com/Unity-Technologies/game-programming-patterns-demo

Сообщество и сторонние библиотеки:

- https://discussions.unity.com/t/game-architecture-and-patterns-so-events-unityevents-servicelocator-singleton/1515336
- https://github.com/roboryantron/Unite2017
- https://github.com/hadashiA/VContainer
- https://vcontainer.hadashikick.jp/comparing/comparing-to-zenject
- https://github.com/gustavopsantos/Reflex
- https://github.com/modesttree/Zenject
- https://gamedevbeginner.com/events-and-delegates-in-unity/
- https://wallstop.github.io/unity-tips/assembly-definitions/05-best-practices/

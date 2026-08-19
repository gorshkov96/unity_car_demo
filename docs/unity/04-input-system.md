# Новый Input System: правильная работа с вводом

Как в Unity 6.3 читать ввод так, чтобы машина не «залипала», нажатия не терялись, а геймпад, клавиатура и руль работали без переписывания кода. Заглядывать сюда перед тем, как писать любой скрипт, трогающий ввод.

---

## TL;DR — решения для нашего проекта

1. **Один Input Actions asset, назначенный project-wide.** В Unity 6.3 (6000.3) ставится Input System **1.20.0** — других версий для этого редактора нет. Ассет `InputSystem_Actions.inputactions` создаётся кнопкой в `Edit > Project Settings > Input System Package > Input Actions`, доступ в коде — `InputSystem.actions`.
2. **Update Mode оставляем `Process Events in Dynamic Update`** (значение по умолчанию) — это даёт минимальную задержку и корректные дискретные нажатия в `Update`.
3. **Ввод читаем в `Update`, буферизуем в поля, применяем к `Rigidbody` в `FixedUpdate`** — прямая рекомендация документации для физических сценариев.
4. **`WasPressedThisFrame()` вызываем только в `Update`.** В `FixedUpdate` при dynamic-режиме он теряет нажатия либо срабатывает несколько раз подряд. Дискретное событие переносим в физику через `bool`-флаг, который гасится после применения.
5. **Непрерывные оси (руль, газ, тормоз) — тип `Value`; дискретные (ручник, рестарт, смена камеры) — тип `Button`.** `Pass Through` нам не нужен.
6. **Никакого `PlayerInput`-компонента.** У нас один игрок; `PlayerInput` нужен ради локального мультиплеера и назначения устройств, а взамен даёт `SendMessage` и связи, которых нет в коде.
7. **Ссылки на действия — `[SerializeField] InputActionReference`** в компоненте-читателе, а не строковый `FindAction("Move")` по всему проекту. Строки не проверяются компилятором и ломаются при переименовании.
8. **Генерацию C#-класса (`Generate C# Class`) не включаем** — документация называет её альтернативой project-wide actions; смешивать два подхода незачем.
9. **Дельту мыши никогда не умножаем на `Time.deltaTime`;** стик геймпада — умножаем. Это разные по природе величины.
10. **Пауза = переключение action maps, а не `Time.timeScale = 0`.** `timeScale` ввод не выключает: карту `Player` нужно `Disable()`, карту `UI` оставить включённой.
11. **В `Start()` явно выключаем лишние action maps** — при project-wide actions включены все карты сразу, это задокументированное поведение 1.20 и источник «фантомных» нажатий.
12. **В `.asmdef` модуля `Vehicle` добавляем ссылку на сборку `Unity.InputSystem`** — иначе код в своей сборке не увидит namespace `UnityEngine.InputSystem`.

---

## 1. Что у нас стоит и почему legacy — действительно legacy

Unity 6.3 поставляется с Input System **1.20.0** (выпущен 21.07.2026), пакет включён в новые проекты по умолчанию: «By default, the Input System Package provides input support in Unity».

Старый `Input.GetAxis` / `Input.GetKey` — это **Input Manager**, и Unity пишет о нём прямо: «This is not the recommended workflow, as it is less flexible than the Input System Package, and will be removed in future versions of Unity». Речь не о вкусовщине, а об удалении API из движка.

Практические причины не трогать legacy:

- `Input.GetAxis("Horizontal")` каждый кадр **ищет ось по строке** — поиск и чтение слиты. Новый API разделяет их: ссылку находим один раз в `Start()`, читаем значение каждый кадр. Документация называет это прямой причиной лучшей производительности.
- Legacy не знает про типы устройств, control schemes, ремаппинг, процессоры и интеракции.
- `Active Input Handling` (`Project Settings > Player > Other Settings`) в режиме `Both` заставляет Unity обрабатывать ввод дважды — «could introduce a small performance impact». Нам нужен режим `Input System Package (New)`. Проверка в коде — `#if ENABLE_INPUT_SYSTEM` / `#if ENABLE_LEGACY_INPUT_MANAGER`.

**Отдельная ловушка Unity 6.3:** события `MonoBehaviour.OnMouseDown` / `OnMouseUp` с новым Input System поддерживаются только с **Unity 6.4** (добавлено в Input System 1.16.0 для 6000.4+). В нашей 6.3 они молча не работают — кликам по объектам нужен собственный raycast.

## 2. Project-wide Input Actions — что это и что меняет

**Actions Asset** — файл `.inputactions` с описанием «действий» (Move, Jump, Look) и их привязок к физическим контролам. **Action Map** — именованная группа действий под режим игры («Player», «UI»).

Project-wide actions (pre-release в 1.8.0-pre.1, сентябрь 2023; релиз — 1.8.0, март 2024) — это назначение **одного** ассета глобальным:

- ассет **предзагружается** при старте приложения и живёт до его завершения;
- действия доступны как `InputSystem.actions` без сериализованных ссылок;
- **все действия в нём включены автоматически**, вызывать `Enable()` не нужно;
- назначить ассет можно **только в edit mode**: присвоение `InputSystem.actions` в play mode бросает исключение (с 1.8.0), и только сохранённым на диск ассетом.

Дефолтный ассет приходит с картами `Player` (Move, Look, Jump, Attack, Interact, Crouch, Sprint, Previous, Next) и `UI` (Navigate, Submit, Cancel, Point, Click, ScrollWheel, …).

```csharp
using UnityEngine.InputSystem;
// Start():  _moveAction = InputSystem.actions.FindAction("Move");
// Update(): Vector2 move = _moveAction.ReadValue<Vector2>();
```

Документация предупреждает: **не вызывать `FindAction` в `Update`** — это строковый поиск. И: при одинаковых именах в разных картах путь указывается как `"Player/Move"`.

> **Ловушка №1 проекта.** Документация 1.20 пишет прямо: «Currently, when using project-wide actions all the action maps are enabled by default. It is advisible to manually disable them and manually enable the default map … during `Start()`». То есть `UI` и `Player` слушают ввод одновременно с первого кадра.

## 3. Три workflow: что выбрать и почему

| Workflow | Как выглядит | Когда нужен |
|---|---|---|
| **Actions** (рекомендация Unity) | Настройка в Actions Editor, `FindAction` / `InputActionReference`, чтение в `Update` | Большинство проектов, в том числе наш |
| **Actions + `PlayerInput`** | Компонент в инспекторе связывает действия с методами | Локальный мультиплеер, split-screen, автоназначение устройств |
| **Прямое чтение устройств** | `Gamepad.current.leftStick.ReadValue()` | Быстрый прототип, одна фиксированная платформа |

Четвёртый вариант — **генерация C#-класса** из ассета (`Generate C# Class` в инспекторе): типобезопасный доступ `controls.gameplay.Move` и интерфейсы `IGameplayActions` под `SetCallbacks(this)`. Документация помечает его как «alternative workflow to project-wide actions».

**Наш выбор.**

- **`PlayerInput` отклоняем.** Документация сама перечисляет минусы: связи «действие → метод» живут в инспекторе, а не в коде, что «can make debugging more difficult»; мультиплеерная часть — «somewhat of a black box». Плюс режимы `Send Messages` / `Broadcast Messages` работают через `GameObject.SendMessage` — рефлексия, опечатка в имени метода проваливается молча. Если бы он всё же понадобился, единственный приемлемый режим — `Invoke CSharp Events`.
- **Генерацию C#-класса отклоняем** — она конфликтует с project-wide подходом (два API к одному ассету), а типобезопасность мы получаем дешевле.
- **Берём Actions + `InputActionReference`.** Это `ScriptableObject`, который «References a specific InputAction in an InputActionMap stored inside an InputActionAsset»: поле в инспекторе, никаких строк, действие переназначается без правки скрипта — ровно то, что требует наш принцип «данные в ассетах».

> Если `PlayerInput` всё-таки появится: читать ввод надо из **его копии** действий (`playerInput.actions`), а не из `InputSystem.actions` — компонент фильтрует устройства по игрокам, и обход копии ломает автоназначение.

## 4. Типы действий: Value, Button, Pass Through

| Тип | Для чего | Фазы и conflict resolution |
|---|---|---|
| `Button` | Клавиша, кнопка — только вкл/выкл | Да |
| `Value` | Стик, мышь, ось педали — плавное значение | Да |
| `Pass Through` | То же, что `Value`, но без фаз и конфликтов | Нет |

**Conflict resolution** — выбор «главного» контрола, когда к одному действию привязаны несколько (стик и WASD): система берёт наиболее актуированный. `Pass Through` этого не делает — любое изменение любого контрола даёт свой callback. Полезно для сырого ввода и записи реплеев, вредно для игрового управления.

**Control Type** — тип значения (`Axis`, `Vector2`, `Stick`, `Dpad`, …). Фильтрует список доступных привязок и обязан совпадать с `T` в `ReadValue<T>()`, иначе исключение в рантайме.

**Initial state check** — проверка состояния в момент включения действия. Для `Value` включена неявно: если стик уже отклонён, машина поедет сразу. Для `Button` и `Pass Through` выключена — зажатую кнопку надо отпустить и нажать снова; включается галочкой `Initial State Check`.

Раскладка для машины:

| Действие | Action Type | Control Type | Привязки |
|---|---|---|---|
| `Steer` | Value | Axis | 1D Axis (A/D), `<Gamepad>/leftStick/x`, ось руля |
| `Throttle` | Value | Axis | W, `<Gamepad>/rightTrigger`, педаль газа |
| `Brake` | Value | Axis | S, `<Gamepad>/leftTrigger`, педаль тормоза |
| `Handbrake` | Button | Button | Space, `<Gamepad>/buttonSouth` |
| `Look` | Value | Vector2 | `<Mouse>/delta`, `<Gamepad>/rightStick` |
| `ResetCar` | Button | Button | R, `<Gamepad>/buttonNorth` |

Раздельные `Throttle` и `Brake` вместо одной оси — потому что на геймпаде это два независимых аналоговых триггера, а на руле две независимые педали, которые можно жать одновременно.

## 5. Как читать: polling против callbacks

```csharp
Vector2 move = moveAction.ReadValue<Vector2>();   // Value / Pass Through
bool held    = jumpAction.IsPressed();            // Button: удерживается
bool down    = jumpAction.WasPressedThisFrame();  // Button: нажата в этом кадре
bool up      = jumpAction.WasReleasedThisFrame(); // Button: отпущена в этом кадре
```

Плюс методы по фазе интеракции: `phase`, `WasPerformedThisFrame()`, `WasCompletedThisFrame()` (добавлен в 1.8.0 — `true` в кадр, когда действие вышло из фазы `Performed`; позволяет отличить «отпустил кнопку» от «отпустил после успешного Hold»).

Документация рекомендует polling как основной подход: «For most common scenarios, especially action games where the user's input has a continuous centralized effect on an in-game character, polling is usually simpler and easier to implement».

Порог нажатия — `InputSettings.defaultButtonPressPoint`, порог отпускания — `buttonReleaseThreshold`, по умолчанию **75 % от порога нажатия** (гистерезис против дребезга).

> **Изменение в 1.20, то есть именно в нашей версии:** `IsPressed`, `WasPressedThisFrame`, `WasReleasedThisFrame` для привязок к `Vector2Control` / `StickControl` **больше не смотрят на per-control `pressPoint`** — поле удалено. Свой порог задаётся интеракцией `Press` или глобальным `defaultButtonPressPoint`. Советы 2021–2023 годов про `pressPoint` на стике в 6.3 уже неверны.

**Callbacks.** Фазы действия: `Disabled`, `Waiting`, `Started`, `Performed`, `Canceled`.

```csharp
jumpAction.started   += ctx => { /* интеракция началась */ };
jumpAction.performed += ctx => { /* интеракция завершена */ };
jumpAction.canceled  += ctx => { /* интеракция прервана */ };
```

Ограничение: «The contents of the callback context structure are only valid during the callback» — сохранять `InputAction.CallbackContext` и читать позже нельзя. Есть агрегаты: `InputActionMap.actionTriggered` (все три фазы одним обработчиком) и глобальный `InputSystem.onActionChange`.

Колбэки уместны для редких событийных вещей (фары, смена камеры, показ UI). Для непрерывного управления машиной они дают лишний слой без выгоды.

## 6. Ключевое для машины: Update, FixedUpdate и Update Mode

Раздел, из-за которого имеет смысл читать весь файл.

### Как ходят события

Устройство генерирует события в свои моменты времени, не совпадающие с кадрами. Система складывает их в очередь и разбирает **пачками**: либо перед очередным `Update`, либо перед очередным `FixedUpdate` — в зависимости от `Update Mode` (`Project Settings > Input System Package`, в коде `InputSystem.settings.updateMode`):

| Режим | Enum | Когда обрабатываются события |
|---|---|---|
| Process Events In Dynamic Update | `ProcessEventsInDynamicUpdate` | Перед каждым `Update` (по умолчанию) |
| Process Events In Fixed Update | `ProcessEventsInFixedUpdate` | Перед каждым `FixedUpdate` |
| Process Events Manually | `ProcessEventsManually` | Только по вызову `InputSystem.Update()` |

Режима «оба сразу» **не существует**: `ProcessEventsInBothFixedAndDynamicUpdate` был удалён — Unity объяснила это тем, что механизм был «increasingly complex and brittle».

### Почему fixed-режим даёт лишний лаг

`FixedUpdate` не запускается через равные интервалы: Unity в начале кадра прогоняет столько фиксированных шагов, сколько успело истечь. Между концом последнего завершённого шага и началом кадра почти всегда остаётся необработанный «хвост» времени, и попавший в него ввод обработается **только через кадр** — документация приводит пример, где событие произошло в кадре 3, а обработано было в кадре 7.

Отсюда рекомендация Unity: «To minimize input latency in input code in FixedUpdate calls, set the input system update mode to Process Events in Dynamic Update».

### Правило, которое нарушают чаще всего

> При Update Mode = Dynamic Update методы `WasPressedThisFrame()` / `WasReleasedThisFrame()` можно вызывать **только в `Update`**: вызов в `FixedUpdate` «might either miss events, or return true across multiple consecutive frames». Симметрично: при Fixed Update — только в `FixedUpdate`.

Причина: «этот кадр» для этих методов означает «этот input update». При FPS выше физического шага один и тот же `WasPressedThisFrame` вернёт `true` несколько раз; при FPS ниже — нажатие потеряется.

### Шаблон для нашей машины

Непрерывные оси читаем поллингом (потеря промежуточных значений стика не страшна — это «sample-and-hold»), дискретные события буферизуем.

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Reads raw car input once per frame and exposes it to physics code.</summary>
public sealed class CarInputReader : MonoBehaviour
{
    [SerializeField] private InputActionReference _steer;     // Value / Axis
    [SerializeField] private InputActionReference _throttle;  // Value / Axis
    [SerializeField] private InputActionReference _handbrake; // Button

    public float Steer { get; private set; }
    public float Throttle { get; private set; }
    private bool _handbrakePressed;

    private void Update()
    {
        // Непрерывные оси: последнее обработанное значение кадра.
        Steer = _steer.action.ReadValue<float>();
        Throttle = _throttle.action.ReadValue<float>();
        // Дискретное событие читаем строго здесь и запоминаем до физики.
        if (_handbrake.action.WasPressedThisFrame())
            _handbrakePressed = true;
    }

    /// <summary>Returns true once per press; call from FixedUpdate.</summary>
    public bool ConsumeHandbrakePressed()
    {
        bool value = _handbrakePressed;
        _handbrakePressed = false;
        return value;
    }
}

// Потребитель:
// void FixedUpdate() {
//     _rigidbody.AddForce(transform.forward * (_input.Throttle * _config.EnginePower));
//     if (_input.ConsumeHandbrakePressed()) ToggleHandbrake();
// }
```

Ровно этот приём — «use a variable to pass through the pressed/released state … and then clear it once your FixedUpdate code has acted on it» — описан в документации в разделе Mixed timing scenarios.

**Запасной вариант.** Если придётся переключиться на `ProcessEventsInFixedUpdate` или `ProcessEventsManually`, но реагировать нужно в `Update` (мгновенный отклик UI или анимации), есть отдельное семейство: `WasPressedThisDynamicUpdate()`, `WasReleasedThisDynamicUpdate()`, `WasPerformedThisDynamicUpdate()`, `WasCompletedThisDynamicUpdate()` (с 1.13.1, февраль 2025). В dynamic-режиме они ведут себя как обычные `*ThisFrame`.

### `Time.deltaTime` и ввод — где умножать, а где нет

- **Стик геймпада, зажатая клавиша, руль** — это *состояние*. Умножать на `Time.deltaTime` **нужно**.
- **`<Mouse>/delta`, колесо прокрутки** — это уже *дельта за кадр*. Умножать **нельзя**: при просадке FPS кадр длиннее, мышь прошла больше, и повторное домножение делает чувствительность зависимой от частоты кадров. Признак бага: при ограничении FPS камера крутится заметно быстрее.

Внутри кадра дельты мыши **аккумулируются** механизмом event merging (несколько событий 8000-герцовой мыши схлопываются в одно с суммарной дельтой), так что ручное усреднение не нужно.

Если `Look` привязан и к мыши, и к стику одним действием, единого коэффициента не существует: либо развести на два действия, либо навесить процессор `Scale` на каждую привязку отдельно — документация приводит ровно этот пример («Custom mouse sensitivity», выравнивание диапазонов мыши и стика через `Scale`).

> **Вывод из механики, не прямая цитата:** при `Time.timeScale = 0` `FixedUpdate` не вызывается, значит в режиме `ProcessEventsInFixedUpdate` обработка ввода на паузе останавливается полностью. Ещё один довод за dynamic-режим.

## 7. Processors и Interactions

**Processor** — преобразование значения без смены типа; навешивается на контрол, привязку или действие. Встроенные: `Clamp`, `Invert`, `InvertVector2`, `InvertVector3`, `Normalize`, `NormalizeVector2`, `NormalizeVector3`, `Scale`, `ScaleVector2`, `ScaleVector3`, `AxisDeadzone`, `StickDeadzone`. Порядок важен: процессоры на привязке применяются **до** процессоров на действии, а в строковой записи выполняются слева направо (`"invert,normalize(min=0,max=10)"`).

Что реально пригодится:

- `StickDeadzone` — почти все геймпады не отдают ровно `(0,0)` в покое и не всегда доходят до максимума: `min` убирает дрейф, `max` нормализует верх.
- `AxisDeadzone` на осях руля и педалей — та же проблема, но острее: потенциометры дешёвых рулей шумят сильно.
- `Scale` на оси руля — «острое» или «вялое» руление без правки кода.
- `Clamp` — если сырой диапазон устройства выходит за `[0..1]`.

**Interaction** — паттерн активации: `Press`, `Hold`, `Tap`, `SlowTap`, `MultiTap`. Дефолт для `Button` — сработать сразу при нажатии. Детали: интеракции работают по **магнитуде актуации**, поэтому `Hold` можно повесить и на стик; при нескольких интеракциях на одной привязке порядок меняет поведение («If you get unexpected behaviour, you may need to experiment with a different ordering»); `GetTimeoutCompletionPercentage()` возвращает 0..1 — готовый прогресс для UI-полоски «зажми, чтобы перезапустить машину».

```csharp
// Перезапуск машины по удержанию (Hold, duration = 0.6)
_resetAction.action.started   += _ => _hud.ShowHoldProgress();
_resetAction.action.performed += _ => RespawnCar();
_resetAction.action.canceled  += _ => _hud.HideHoldProgress();
```

Новое в 1.20: `InputSettings.shortcutKeysUseActionPriority` и поле `InputAction.Priority` (0..65535) — разрешение конфликтов между перекрывающимися действиями (`B` и `Shift+B`). Обе настройки **по умолчанию выключены**; включать только если появятся сочетания клавиш.

## 8. Устройства: клавиатура, геймпад, руль

**Control Schemes** — именованные наборы привязок под тип устройства («Keyboard&Mouse», «Gamepad», «Wheel»); настраиваются в выпадающем списке слева вверху Actions Editor. Схема без добавленных типов устройств нерабочая — минимум одно устройство обязательно.

**Геймпад.** Класс `Gamepad`, привязки `<Gamepad>/leftStick`, `<Gamepad>/rightTrigger`, `<Gamepad>/buttonSouth`. Вибрация — `Gamepad.current.SetMotorSpeeds(low, high)`: на macOS работает для контроллеров PS4, на Windows — для Xbox и PS4. Останавливается автоматически примерно через 10 секунд после последнего вызова.

**Руль.** Ожидания стоит понизить заранее:

- Система «supports joysticks as generic HIDs only» — руль опознаётся как `Joystick` с автораскладкой из HID-дескриптора.
- Автораскладка — «best effort»: направления hat-switch и назначение кнопок угадываются, часто неверно. Документация сама советует: «These devices often work best when you allow the user to manually remap the controls».
- **Force feedback из коробки нет.** Есть только низкоуровневый путь: `InputDevice.ExecuteCommand` с командой типа `"HIDO"` (HID Output Report) и device-specific форматом данных. В первой итерации поддерживаем руль как источник осей, без отдачи.
- HID напрямую поддерживается на Windows, macOS и UWP; на Linux — только геймпады и джойстики через SDL.

Вывод: под руль **обязателен рантайм-ремаппинг** (раздел 9), иначе устройство работает «как повезёт».

**Частота опроса.** `InputSystem.pollingFrequency` с 1.13.1 инициализируется рекомендованным значением платформы на Unity новее 6000.3.0a1 (раньше — фиксированные 60 Гц). Повышение частоты «leads to less probability of input loss and reduces input latency» — первая ручка, если рулению не хватит точности.

## 9. Rebinding в рантайме

```csharp
private InputActionRebindingExtensions.RebindingOperation _rebind;

public void StartRebind(InputAction action, int bindingIndex)
{
    action.Disable();                                   // обязательно
    _rebind = action.PerformInteractiveRebinding(bindingIndex)
        .WithControlsExcluding("<Mouse>/position")
        .WithCancelingThrough("<Keyboard>/escape")
        // Dispose обязателен, иначе утечка unmanaged-памяти.
        .OnComplete(op => { op.Dispose(); _rebind = null; action.Enable(); })
        .Start();
}
```

- `PerformInteractiveRebinding()` ждёт ввода с любого устройства, подходящего по типу контрола, и пишет путь в `InputBinding.overridePath`. При нескольких одновременных контролах выбирается тот, у кого выше магнитуда.
- **`Dispose()` обязателен:** «You must dispose of `RebindingOperation` instances with `Dispose()`, so that they don't leak memory on the unmanaged memory heap».
- Настройки операции: `WithExpectedControlType()`, `WithControlsExcluding()`, `WithCancelingThrough()`, `WithTargetBinding()`, `WithBindingGroup()`, `WithBindingMask()`.
- В 1.15.0 добавлен `WithSuppressedActionPropagation()` — не даёт действиям срабатывать во время перепривязывания (иначе назначаемое нажатие тут же выполняет действие).

```csharp
PlayerPrefs.SetString("rebinds", InputSystem.actions.SaveBindingOverridesAsJson());
InputSystem.actions.LoadBindingOverridesFromJson(PlayerPrefs.GetString("rebinds"));
InputSystem.actions.RemoveAllBindingOverrides();   // сброс к дефолту
```

Для отображения текущей привязки в UI — `InputBinding.effectivePath` и `GetBindingDisplayString()`. Готовый пример: Package Manager → Input System → Samples → **Rebinding UI** (в 1.15–1.16 расширен состояниями «игра / меню», индикаторами срабатывания, слайдером чувствительности мыши и обменом привязок `RebindActionUI.SwapBinding`).

Смена привязок недёшева: «Modifying the binding mask or modifying any of the bindings … will lead to all enabled actions being temporarily disabled and then re-enabled and resumed». Значит — только в меню настроек, не в геймплее.

## 10. UI: uGUI и UI Toolkit

| UI-система | Совместимость | Нужен `InputSystemUIInputModule` |
|---|---|---|
| UI Toolkit (2023.2+, значит и 6.3) | Да | **Нет** |
| UI Toolkit (до 2023.2) | Да | Да |
| uGUI (Unity UI) | Да | **Да** |
| IMGUI (`OnGUI`) | **Нет** | — |

С 2023.2 карта `UI` из project-wide actions напрямую питает UI Toolkit. Отсюда жёсткое ограничение: **нельзя переименовывать карту `UI`, её действия и менять их Action Types** — иначе ввод в UI Toolkit сломается; добавлять и менять привязки внутри действий можно. Если в сцене есть `InputSystemUIInputModule`, его настройки **перекрывают** UI-настройки из project-wide actions.

Ограничения, о которые легко споткнуться:

- В UI Toolkit клик мышью и «submit» с геймпада — **разные события**: `button.RegisterCallback<ClickEvent>(...)` не сработает от геймпада, `button.clicked += ...` сработает от обоих.
- UI Toolkit не поддерживает XR-ввод и не использует `TrackedDeviceRaycaster`.
- uGUI: новый Input System **не умеет подавать текст** в `InputField` и TextMesh Pro — текст берётся напрямую из нативного рантайма.
- **UI не «съедает» ввод**: «The UI will not consume input such that it will not also trigger in-game actions». Клик по кнопке HUD одновременно выстрелит игровым действием, привязанным к `<Mouse>/leftButton`.

Документация предлагает три стратегии разведения UI и геймплея: весь ввод через UI-события; UI поверх сцены без прямого взаимодействия; **явное переключение режимов** — то, что нужно нам. Проверка «указатель над UI» — `EventSystem.IsPointerOverGameObject()`, но **только из `Update`**: вызов из колбэка `InputAction.performed` логирует предупреждение, потому что UI обновляется после обработки ввода. Полезный образец — sample **UI vs Game Input**.

## 11. Пауза, переключение карт и потеря фокуса

`Time.timeScale = 0` **не выключает ввод** — Input System живёт в player loop, а не в игровом времени.

```csharp
public sealed class GameModeSwitcher : MonoBehaviour
{
    private InputActionMap _player, _ui;

    private void Awake()
    {
        _player = InputSystem.actions.FindActionMap("Player");
        _ui = InputSystem.actions.FindActionMap("UI");
    }

    // Project-wide actions включают ВСЕ карты — наводим порядок явно.
    private void Start() { _ui.Enable(); _player.Enable(); }

    public void SetPaused(bool paused)
    {
        Time.timeScale = paused ? 0f : 1f;
        if (paused) _player.Disable(); else _player.Enable();
    }
}
```

Про `Enable()` / `Disable()`: включение запускает **binding resolution** — поиск подходящих контролов среди подключённых устройств; пока действие включено, менять его привязки нельзя. При выключении или сбросе устройства действия **отменяются** гарантированно, «even if an Action is set to trigger on button release» — машина не уедет с зажатой `W`.

**Потеря фокуса окна.** `Background Behavior` (`Project Settings > Input System Package`) по умолчанию `Reset And Disable Non Background Devices`: при потере фокуса устройства сбрасываются и отключаются, при возврате включаются и синхронизируются. Два нюанса: в **development-билдах** `Run In Background` включён принудительно, поэтому ввод обрабатывается даже в фоне и поведение дев-билда отличается от релиза; плюс известный баг — устройства, зависящие от фокуса, «will not automatically sync their current state when the app loses and subsequently regains focus», так что зажатая при возврате `W` не даст события, пока её не отпустить.

## 12. Input Debugger — первое, что открывать при странностях

`Window > Analysis > Input Debugger`:

- **Devices** — подключённые и нераспознанные устройства; для HID есть кнопка **HID Descriptor** (незаменима при подключении руля);
- **Layouts** — база поддерживаемого железа;
- **Actions** — только в Play mode и только если включено хотя бы одно действие: список активных действий и **реально привязанных контролов**. Главный инструмент для вопроса «почему действие не срабатывает»;
- **Users** — активные `InputUser` (сюда попадают действия, принадлежащие `PlayerInput`); **Settings**, **Metrics** — настройки и статистика.

В окно устройства добавлены (1.13.1) диагностики достижимой средней частоты опроса и задержек обработки (среднее/мин/макс). Дополнительно: `InputActionTrace` для записи всех изменений действий и sample **Visualizers** с наглядными графиками ввода — как раз в духе проекта-витрины.

## 13. Антипаттерны

- **`Input.GetAxis` / `Input.GetKey`.** Legacy Input Manager объявлен нерекомендуемым и будет удалён из Unity; плюс строковый поиск оси каждый кадр.
- **`FindAction("Move")` в `Update` / `FixedUpdate`.** Строковый поиск в горячем пути; документация советует находить ссылку в `Start()`. Лучше вообще без строк — через `InputActionReference`.
- **Update Mode = Fixed Update «потому что у нас физика».** Интуитивно, но неверно: fixed-режим добавляет лаг из-за необработанного «хвоста» времени. Документация рекомендует обратное.
- **`WasPressedThisFrame()` в `FixedUpdate` при dynamic-режиме.** Классическая причина «прыжок иногда не срабатывает» и «ручник дёргается дважды».
- **Умножение `<Mouse>/delta` на `Time.deltaTime`.** Чувствительность становится зависимой от FPS; на форумах Unity всплывает регулярно.
- **Движение `Rigidbody` прямо из колбэка `performed`.** Колбэк приходит при разборе очереди событий, вне физического шага; силы применяются только в `FixedUpdate`.
- **Сохранение `InputAction.CallbackContext` за пределы колбэка.** Структура валидна только внутри колбэка.
- **`PlayerInput` с `Behavior = Send Messages` в одиночной игре.** Рефлексия через `SendMessage`, опечатка в имени `OnJump` проваливается молча, связь «действие → метод» не видна в коде.
- **Одновременно `Generate C# Class` и `InputSystem.actions`.** Два параллельных API к одному ассету; документация явно называет генерацию класса альтернативой project-wide actions.
- **Надежда, что project-wide actions сами выключат ненужные карты.** Включены все — порядок наводим руками в `Start()`.
- **Пауза через `Time.timeScale = 0` без выключения карты.** Ввод не зависит от игрового времени.
- **`Active Input Handling = Both` «на всякий случай».** Ввод обрабатывается дважды; смысл есть только ради runtime-`OnGUI`, которого у нас не будет.
- **Опора на `pressPoint` у `Vector2Control` / `StickControl`.** В 1.20 поле удалено; порог задаётся интеракцией `Press` или `defaultButtonPressPoint`.
- **Ожидание, что заработает `OnMouseDown`.** В Unity 6.3 с новым Input System — нет, только с 6.4.
- **Расчёт на то, что руль «просто заработает».** Generic HID, угаданная раскладка, отсутствие force feedback: без рантайм-ремаппинга поддержка неполноценна.

## 14. Чек-лист

**Настройка проекта**
- [ ] `Active Input Handling` = `Input System Package (New)`.
- [ ] Создан и назначен project-wide ассет, лежит в `Assets/_Project/Settings/`.
- [ ] `Update Mode` = `Process Events in Dynamic Update`.
- [ ] `Background Behavior` = `Reset And Disable Non Background Devices` (дефолт) — проверить, что не менялось.
- [ ] `.asmdef` каждого модуля, читающего ввод, ссылается на `Unity.InputSystem`.

**Ассет действий**
- [ ] Карты разделены по режимам: `Player`, `UI`, при необходимости `Debug`.
- [ ] Карта `UI` и её действия **не переименованы** (иначе сломается UI Toolkit).
- [ ] Оси — тип `Value`, кнопки — тип `Button`; Control Type совпадает с `T` в `ReadValue<T>()`.
- [ ] На стиках `StickDeadzone`, на осях руля и педалей `AxisDeadzone`.
- [ ] Заведены Control Schemes (Keyboard&Mouse, Gamepad, Wheel), в каждой указано хотя бы одно устройство.

**Код**
- [ ] Ссылки на действия — `[SerializeField] InputActionReference`, не строки.
- [ ] Ни одного `FindAction` / `GetComponent` в `Update` и `FixedUpdate`.
- [ ] Ввод читается в `Update`, физика применяется в `FixedUpdate`.
- [ ] `WasPressedThisFrame` / `WasReleasedThisFrame` — только в `Update`; в физику события приходят через флаг, который гасится потребителем.
- [ ] Дельта мыши **не** умножается на `Time.deltaTime`; ось стика — умножается.
- [ ] В `Start()` явно выставлено, какие action maps включены; пауза выключает `Player`, оставляя `UI`.
- [ ] Каждый `RebindingOperation` завершается `Dispose()`; `CallbackContext` нигде не сохраняется.

**Проверка в редакторе**
- [ ] Input Debugger → Play Mode → Actions: у каждого действия перечислены ожидаемые контролы.
- [ ] Подключён геймпад — управление работает без правки кода.
- [ ] Alt-Tab во время движения: после возврата фокуса машина не едет сама.
- [ ] Пауза во время движения: газ не проходит, меню навигируется геймпадом.

## 15. Видео и доклады

Официальная серия Unity из 7 частей; ссылки взяты со страницы Video resources документации Input System 1.20, плейлист — `https://www.youtube.com/playlist?list=PLX2vGYjWbI0RpLvO3B7aH-ObfcOifMD20`.

- Unity Input System in Unity 6 (1/7): Input Action Editor — Unity — https://www.youtube.com/watch?v=TiTKAseu17A — установка пакета, Actions Editor, привязки, первое знакомство с интеракциями и процессорами.
- Unity Input System in Unity 6 (2/7): Input System Scripting — Unity — https://www.youtube.com/watch?v=Cd2Erk_bsRY — **самое полезное для нас**: скриптинг движения и прыжка, геймпад и клавиатура, пауза и динамическое переключение action maps.
- Unity Input System in Unity 6 (3/7): Input System Mobile controls — Unity — https://www.youtube.com/watch?v=aI-r7ILNDug — On-Screen Stick и On-Screen Button; пригодится, если демо поедет на Android/iOS.
- Unity Input System in Unity 6 (4/7): Input System and UI toolkit — Unity — https://www.youtube.com/watch?v=GdjP5pggaHw — навигация по UI Toolkit геймпадом и переключение фокуса между группами элементов.
- Unity Input System in Unity 6 (5/7): Rebinding Input System controls — Unity — https://www.youtube.com/watch?v=JfuqMaOiNPs — рантайм-ремаппинг и сохранение привязок в `PlayerPrefs`; наш сценарий поддержки руля.
- Unity Input System in Unity 6 (6/7): Player Input Component — Unity — https://www.youtube.com/watch?v=beDfIBLfx4c — что именно делает `PlayerInput`; смотреть, чтобы осознанно от него отказаться.
- Unity Input System in Unity 6 (7/7): Player Input Manager & local multiplayer — Unity — https://www.youtube.com/watch?v=lGxXQzE5Vu8 — локальный кооп и split-screen, на будущее.
- Демо-проект InputSystem_Warriors — Unity Technologies — https://github.com/UnityTechnologies/InputSystem_Warriors — референсная реализация, на которую ссылается официальная документация.

## 16. Источники

Все ссылки проверены 2026-08-19. База — документация пакета `com.unity.inputsystem@1.20` (версия, идущая с Unity 6.3), корень: https://docs.unity3d.com/Packages/com.unity.inputsystem@1.20/manual/index.html

Разделы руководства пакета (префикс `https://docs.unity3d.com/Packages/com.unity.inputsystem@1.20/manual/`):
`Workflows.html`, `using-actions-workflow.html`, `using-playerinput-workflow.html`, `using-direct-workflow.html`, `about-project-wide-actions.html`, `quick-start-guide.html`, `default-actions.html`, `action-type-reference.html`, `about-action-control-types.html`, `binding-initial-state-checks.html`, `polling-actions.html`, `set-callbacks-on-actions.html`, `about-responding-to-input.html`, `enable-actions.html`, `binding-resolution.html`, `timing-and-latency.html`, `timing-input-events-queue.html`, `timing-select-mode.html`, `timing-optimize-fixed-update.html`, `timing-optimize-dynamic-update.html`, `timing-missed-duplicate-events.html`, `timing-mixed-scenarios.html`, `update-mode.html`, `event-merging.html`, `introduction-to-processors.html`, `built-in-processors.html`, `introduction-interactions.html`, `built-in-interactions.html`, `composite-bindings.html`, `control-schemes.html`, `devices-joysticks.html`, `hid-specification-introduction.html`, `gamepad-haptics.html`, `user-rebinding-runtime.html`, `rebind-action-runtime.html`, `save-load-rebinds.html`, `restore-original-bindings.html`, `understand-ui-compatibility.html`, `introduction-ui-input-module.html`, `configure-ui-input-action-map.html`, `handling-input-target-ambiguity.html`, `background-behavior.html`, `the-input-debugger-window.html`, `debug-action.html`, `KnownLimitations.html`, `corresponding-old-new-api.html`, `generate-cs-api-from-actions.html`, `get-started-player-input-component.html`, `select-notification-behavior.html`, `optimize-controls.html`, `Installation.html`, `enable-correct-input-system.html`, `videos.html`.

Changelog пакета (версии, состав и даты релизов 1.8–1.20):
- https://docs.unity3d.com/Packages/com.unity.inputsystem@1.20/changelog/CHANGELOG.html

Scripting API (префикс `https://docs.unity3d.com/Packages/com.unity.inputsystem@1.20/api/`):
`UnityEngine.InputSystem.InputAction.html`, `UnityEngine.InputSystem.InputSettings.html`, `UnityEngine.InputSystem.InputActionReference.html`, `UnityEngine.InputSystem.InputActionRebindingExtensions.html`.

Руководство Unity 6.3 (6000.3):
- https://docs.unity3d.com/6000.3/Documentation/Manual/Input.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/input-introduction.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/InputLegacy.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.inputsystem.html

Прочее:
- https://github.com/Unity-Technologies/InputSystem — исходники пакета, имя сборки `Unity.InputSystem`
- https://github.com/UnityTechnologies/InputSystem_Warriors — демо-проект из документации
- https://unity.com/resources/input-system-video-tutorial-series — страница видеосерии
- https://discussions.unity.com/t/best-practices-for-using-input-system/1704430 — сравнение четырёх способов доступа к действиям, январь 2026
- https://discussions.unity.com/t/new-input-system-framerate-affecting-mouse-sensitivity/1610328 — разбор ошибки с `Time.deltaTime` и дельтой мыши, март 2025

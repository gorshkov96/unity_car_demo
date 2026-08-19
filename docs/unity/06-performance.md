# Производительность и профилирование в Unity 6

Как считать бюджет кадра, чем и в каком порядке замерять, и какие приёмы реально дают прирост в проекте «машина + трафик». Заглядывать сюда до того, как что-то «оптимизировать», и каждый раз, когда падает FPS.

---

## TL;DR — решения для нашего проекта

1. **Целевой бюджет: 60 FPS = 16.66 мс на кадр**, при этом CPU Active Time и GPU Time считаются **по отдельности** и каждый должен уложиться в 16.66 мс — Unity выполняет работу CPU и GPU параллельно ([Highlights module](https://docs.unity3d.com/6000.3/Documentation/Manual/ProfilerHighlights.html)). Меряем **миллисекунды, не FPS**.
2. **Включить Highlights-модуль профайлера сразу** (`Window > Analysis > Profiler`, модуль по умолчанию выключен) и выставить в нём Target Frame Time. Он единственный сразу отвечает на вопрос «мы CPU-bound или GPU-bound».
3. **URP-настройки для нашего рендера:** SRP Batcher — On, GPU Resident Drawer — Instanced Drawing, Rendering Path — Forward+, GPU Occlusion — On. При этом **выключить** Static Batching в Player Settings и **снять галку Enable GPU Instancing на материалах** — в URP они конфликтуют с GPU Resident Drawer и плодят лишние варианты шейдеров.
4. **Трафик — не через `Instantiate`/`Destroy`, а через `UnityEngine.Pool.ObjectPool<T>`** (встроенный, без сторонних пакетов). Пул также для партиклов и любых снарядов/эффектов.
5. **Массовое движение трафика — `IJobParallelForTransform` + Burst**, без перехода на ECS. Это даёт многопоточность на обычных `GameObject`-ах.
6. **Лучи для ИИ-водителей — `RaycastCommand`** (батч в Job System), а не сотня `Physics.Raycast` в `FixedUpdate`. Одиночные проверки — `Physics.RaycastNonAlloc`, не `RaycastAll`.
7. **В `Project Settings > Physics` включить `Reuse Collision Callbacks`** (иначе каждый `OnCollisionStay` аллоцирует объект `Collision`) и почистить Layer Collision Matrix.
8. **Ноль аллокаций в `Update`/`FixedUpdate`:** никакого LINQ, никакой конкатенации строк, никаких `mesh.vertices`/`Input.touches`-подобных свойств, возвращающих массив. Ссылки — кешировать в `Awake`, не звать `GetComponent`/`Camera.main` покадрово.
9. **На desktop управлять частотой кадров через `QualitySettings.vSyncCount`, а не `Application.targetFrameRate`** — второй даёт микрозаикания (прямая рекомендация документации Unity 6).
10. **Adaptive Performance не ставить** — пакет решает задачу теплового троттлинга мобильных устройств, для десктопного демо он бесполезен.

## 1. Бюджет кадра

Бюджет — это **время кадра в миллисекундах**, а не FPS. FPS — обманчивая метрика: разница между 900 и 450 FPS — те же 1.111 мс, что и между 60 и 56.25 FPS, но во втором случае она катастрофична. Формула: `1000 / целевой FPS`.

| Цель | Бюджет кадра |
|---|---|
| 30 FPS | 33.33 мс |
| 60 FPS | 16.66 мс |
| 120 FPS | 8.33 мс |

Ключевые уточнения из официальных материалов Unity:

- **Один-единственный кадр, вылезший за бюджет, уже виден игроку как рывок.** Средний FPS ничего не гарантирует.
- Превышать бюджет допустимо в неинтерактивных местах (загрузка сцены, меню), но не в геймплее.
- **CPU и GPU считаются раздельно.** «CPU Active Time» — это максимум из main thread и render thread за вычетом времени ожидания (`WaitForTargetFPS`, `Gfx.WaitForPresentOnGfxThread` на главном, `Gfx.WaitForGfxCommandsFromMainThread` на рендер-потоке). «GPU Time» — от отправки первой команды до завершения работы GPU.
- Правило приоритета: **проект ограничен тем потоком/чипом, который дольше всех**. Оптимизировать GPU, когда вы CPU-bound, — впустую потраченное время.
- Запас в ~35 % бюджета «на охлаждение» — это **мобильная** рекомендация (33.33 мс → фактические 22 мс). Для десктопа она не нужна.

Отдельная ловушка: **FPS в редакторе ≠ FPS в билде**. Профилировать надо development-билд, а не Play Mode, — в редакторе есть накладные расходы самого редактора.

## 2. Инструменты

### 2.1 Unity Profiler (`Window > Analysis > Profiler`)

Работает в Play Mode, по сети с development-билдом и над самим редактором.

- **Highlights module** — отвечает на вопрос «укладываемся ли в целевой FPS и кто виноват». Красные маркеры = CPU вышел за бюджет, жёлтые = GPU. Показывает Main thread utilization, Bottlenecks, Systems impact, Top markers, GC allocations и время GC.Collect. **По умолчанию выключен**, включается через Activating Profiler modules.
- **Timeline view в CPU-модуле** — вся картина потоков сразу, включая ожидание GPU. Серые/жёлтые маркеры — простой (хорошо), пурпурные вставки — аллокации в managed-куче.
- **Deep Profiling** (инструментирует *каждый* вызов метода) — тулбар профайлера или Build Profiles → Development Build → Deep Profiling. Даёт полный стек, но **сильно искажает относительные величины** и жрёт память; большой проект может уронить редактор по OOM. Точечно, не как режим по умолчанию.
- Без Deep Profiling профайлер видит только код, обёрнутый в **Profiler markers** — свои добавляются через `Unity.Profiling.ProfilerMarker`:

```csharp
using Unity.Profiling;

public sealed class TrafficSimulation
{
    static readonly ProfilerMarker s_StepMarker = new ProfilerMarker("Traffic.Step");

    public void Step(float dt)
    {
        using (s_StepMarker.Auto()) { /* ... */ }
    }
}
```

`ProfilerMarker.Begin/End` помечены `ConditionalAttribute` — в release-билдах компилируются в ничто, оверхеда нет. Работает и внутри Job-ов.

### 2.2 Profile Analyzer (пакет `com.unity.performance.profile-analyzer`)

Профайлер показывает один кадр; Profile Analyzer агрегирует **сотни кадров** и умеет **сравнивать два набора данных** (Compare view) — инструмент доказательства: «медиана маркера X была 4.1 мс, стала 1.3 мс». Показывает min/max/median/mean и квартили по диапазону кадров. `Window > Analysis > Profile Analyzer`.

### 2.3 Memory Profiler (пакет `com.unity.memoryprofiler`)

Отдельный пакет, не путать со встроенным Memory-модулем профайлера. Делает **снапшот** всей managed-кучи плюс данные Unity Memory Manager и ОС (включая «Untracked» память). Режим **Compare Snapshots** — основной способ ловить утечки: снапшот до и после цикла «загрузил уровень → выгрузил», смотрим, что не освободилось.

### 2.4 Frame Debugger (`Window > Analysis > Frame Debugger`)

Останавливает кадр и даёт пошагово пройти по событиям рендеринга; работает во всех пайплайнах, включая URP.

- Даёт: почему развалился батчинг, что рисуется зря, к какому именно `GameObject` относится draw call (внешние отладчики этого не умеют). Объекты GPU Resident Drawer видны как батчи **Hybrid Batch Group**.
- **Не** даёт: тайминги GPU и отдельные state changes — для этого нужен нативный GPU-профайлер.
- Ограничение: окно обновляется каждый кадр, поставить на паузу нельзя — при быстро меняющемся списке пассов читать тяжело.

### 2.5 Render Graph Viewer (`Window > Analysis > Render Graph Viewer`)

Появился вместе с Render Graph в URP (Unity 6). Таймлайн: слева ресурсы (текстуры, буферы), сверху рендер-пассы в порядке выполнения, на пересечении — блок доступа (зелёный = чтение, красный = запись, зелёно-красный = чтение-запись, серый = нет доступа, пунктир = ресурс ещё не создан). Синяя полоска под пассами = URP их **смёржил**, чёрные блоки = пасс **выкинут** (culled) как не влияющий на картинку. Подключается и к билду (Target Selection: Editor / Local / Remote).

Понадобится, когда начнём писать свои `ScriptableRenderPass` (эффект скорости, обводка, отражения): показывает, реально ли render graph сэкономил память и пассы.

### 2.6 Project Auditor (пакет `com.unity.project-auditor`)

Статический анализатор: находит проблемные настройки, ассеты и код, включая **все строки, делающие managed-аллокации**. Хорош как «первый прогон» до профилирования. Доступен как пакет с Unity 6.1.

### 2.7 Свой оверлей: `FrameTimingManager` и `ProfilerRecorder`

Для витрины технологий уместно показать FPS/мс прямо на экране. Правильные API:

```csharp
ProfilerRecorder _mainThreadTime;

void OnEnable() =>
    _mainThreadTime = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Main Thread Frame Time");
void OnDisable() => _mainThreadTime.Dispose();           // обязательно, иначе утечка
void Update() => _ms = _mainThreadTime.LastValue * 1e-6; // значения в наносекундах
```

`FrameTimingManager` даёт CPU- и GPU-время кадра с низким оверхедом и работает в release-билдах (галка `Player Settings > Frame Timing Stats`; при использовании `ProfilerRecorder` включается автоматически). Данные приходят **с задержкой в 4 кадра**. На нём же построен Dynamic Resolution.

### 2.8 macOS / Metal — что учитывать

- **Xcode Frame Debugger интегрирован с Unity на macOS**: можно захватить кадр не только из билда, но и **из самого редактора**. Работает только при графическом API Metal — на других интеграция отключается. Захват через UI Xcode или `FrameCapture` API.
- **Ловушка измерений на Metal:** `FrameTimingManager` на iOS и macOS может показать **GPU-время больше полного времени кадра** — следствие tile-based deferred rendering у GPU Apple, а не баг. Не делать из этого выводов о GPU-bound.
- Официально рекомендованные инструменты для macOS/iOS: **Instruments** (Xcode) и **Metal Debugger**. Дополнительно Unity умеет запускать **RenderDoc** прямо из редактора (полезнее на Windows).

## 3. Методика: как искать узкое место

Порядок, который рекомендует Unity: **сверху вниз** — сначала общая картина (рендер, скрипты, физика, GC), потом углубление в маркер. Плюс правило трёх замеров: базовая линия → изменения → доказательство эффекта.

- **В бюджете?** Признак здорового кадра: на главном потоке `WaitForTargetFPS` (ждём VSync), рендер-поток простаивает в `Gfx.WaitForGfxCommandsFromMainThread`, воркеры в основном свободны.
- **CPU-bound, главный поток.** Виновники по убыванию частоты: физика, `Update` у MonoBehaviour (маркер `BehaviourUpdate`), аллокации и GC, culling и рендеринг на главном потоке, плохой батчинг, UI-ребилды, анимация. Отдельная классика — **несколько активных камер**: каждая гоняет весь пайплайн. В разобранном Unity кейсе пять камер съедали 16.23 мс — больше целого бюджета.
- **CPU-bound, рендер-поток.** Признак: главный поток стоит в `Gfx.WaitForPresentOnGfxThread`. Причины: плохой батчинг, лишние камеры, слабый culling. Инструменты — Frame Debugger и Rendering-модуль (батчи, SetPass calls).
- **CPU-bound, воркеры.** Признак: `WaitForJobGroupID` на главном потоке. Причины: джобы без Burst; одна длинная джоба вместо параллельной; мало времени между планированием и получением результата; много sync points. Смотреть **Flow Events** в Timeline view.
- **GPU-bound.** Признак: главный поток долго в `Gfx.WaitForPresentOnGfxThread`, рендер-поток — в `Gfx.PresentFrame`. Проверять: полноэкранный пост-процессинг (Bloom, AO), дорогие фрагментные шейдеры, overdraw в прозрачной очереди (частицы, UI), высокое разрешение (4K/Retina), микротреугольники из-за плотной геометрии без LOD, промахи кеша из-за несжатых текстур и текстур без мипмапов.

**Важно про `GC.Alloc`:** маркер не измеряется как обычный сэмпл — Unity пишет только момент и размер аллокации, а длительность рисует искусственную. **Реальная стоимость выше показанных миллисекунд.** Чтобы увидеть правду, ставить свои `ProfilerMarker` вокруг подозрительного кода.

## 4. GC и аллокации в кадре

Unity использует **Boehm–Demers–Weiser** сборщик, **не поколенческий и не уплотняющий** (non-generational, non-compacting). Отсюда два следствия: мелкие частые аллокации он выметает плохо, а куча фрагментируется и не сжимается обратно.

**Incremental GC включён по умолчанию** (`Project Settings > Player > Configuration`). Он не ускоряет сборку, а размазывает её по кадрам, убирая пики. Важные детали:

- Если `vSyncCount != 0` или задан `Application.targetFrameRate`, Unity **отдаёт инкрементальному GC простаивающее время в конце кадра** — то есть VSync прямо помогает сборщику.
- Инкрементальный режим добавляет **write barriers** — накладные расходы на *каждое изменение ссылки*. Если ссылок меняется очень много, фаза маркировки может не успевать, и сборщик всё равно свалится в полную stop-the-world сборку.
- `GarbageCollector.GCMode` позволяет выключить GC вручную — но только для коротких критичных участков, иначе куча растёт до OOM.

### Что действительно аллоцирует (по документации Unity 6.3)

| Причина | Как чинить |
|---|---|
| **Боксинг** — value-тип автоматически превращается в reference-тип (передача `int`/`float` в метод, принимающий `object`). «Один из самых частых источников непреднамеренных аллокаций». IDE и компилятор об этом **не предупреждают**. | Обобщённые методы, `IEquatable<T>`, не сравнивать через `object.Equals` |
| **Замыкания (closures)** — анонимный метод, который читает переменную из внешней области видимости. Компилятор генерирует класс, и его экземпляр аллоцируется на куче | Передавать данные явным параметром, кешировать делегаты в статические поля |
| **Конкатенация строк** — `"Score: " + score` каждый кадр. Строки неизменяемы, каждая операция создаёт новую | `StringBuilder`; обновлять текст только при изменении значения; в TextMeshPro — `TMP_Text.SetText(char[])` |
| **Unity-API, возвращающие массив** — `mesh.vertices`, `Input.touches`, `Animator.parameters`, `Renderer.sharedMaterials`. **Каждое обращение к свойству создаёт новую копию массива** | Кешировать в локальную переменную вне цикла; лучше — не-аллоцирующие версии: `Mesh.GetVertices(list)`, `Physics.RaycastNonAlloc`, `Animator.GetParameter`, `Renderer.GetSharedMaterials` |
| **LINQ** в горячем пути — «избегайте использования LINQ в runtime-коде, особенно в `Update`/`FixedUpdate`»: боксинг, замыкания, лишние аллокации | Обычные циклы |
| **`Collision` в `OnCollisionEnter/Stay/Exit`** — экземпляр аллоцируется на куче | `Physics.reuseCollisionCallbacks = true` (галка в Project Settings) |
| **Yield-инструкции корутин** — `new WaitForSeconds(1f)` внутри цикла | Кешировать объект `WaitForSeconds` в поле |
| **Большие массивы (>10 000 элементов)** — GC эвристический и считает указателем всё, что размером с указатель; большие массивы замедляют сборку и фрагментируют кучу | `NativeArray<T>` / `Unity.Collections` — бонусом совместимо с Job System и Burst |
| **C# reflection** в рантайме | Отдельная страница документации «Avoid C# reflection overhead» |
| **C# finalizers** — сами по себе увеличивают нагрузку на GC, плюс работают на отдельном потоке, где Unity API недоступен | Не использовать в runtime-коде |

### Про `foreach`

Совет «`foreach` всегда аллоцирует, пиши `for`» — наследие старого Mono-компилятора Unity. В современном Unity `foreach` по массиву и по `List<T>` использует struct-энумератор и **не аллоцирует**. Аллокация возникает, когда перебор идёт **через интерфейс** (`IEnumerable<T>`, `IList<T>`) — тогда struct-энумератор боксится. В документации Unity 6 общего запрета на `foreach` нет (более того, официальный пример custom update manager сам написан на `foreach`). **Правило: не запрещать `foreach`, а проверить в профайлере конкретное место** — если коллекция объявлена интерфейсом, поменять тип поля на конкретный.

## 5. Пулинг: `UnityEngine.Pool`

Встроен в `UnityEngine.CoreModule`, сторонние библиотеки не нужны. `ObjectPool<T>` — стековый пул с четырьмя колбэками.

```csharp
using UnityEngine;
using UnityEngine.Pool;

public sealed class TrafficCarSpawner : MonoBehaviour
{
    [SerializeField] private TrafficCar _prefab;
    private ObjectPool<TrafficCar> _pool;

    private void Awake() => _pool = new ObjectPool<TrafficCar>(
        createFunc:      () => Instantiate(_prefab),
        actionOnGet:     car => car.gameObject.SetActive(true),
        actionOnRelease: car => car.gameObject.SetActive(false),
        actionOnDestroy: car => Destroy(car.gameObject),
        collectionCheck: true,   // ловит двойной Release; работает только в редакторе
        defaultCapacity: 32,
        maxSize:         128);

    public TrafficCar Spawn() => _pool.Get();
    public void Despawn(TrafficCar car) => _pool.Release(car);
}
```

- **Не потокобезопасен** — только главный поток, как и большинство Unity API.
- `collectionCheck: true` ловит двойной `Release`; работает **только в редакторе**, в билд не попадает — держать включённым.
- **Сброс состояния обязателен.** При `Release`: остановить корутины, отписаться от событий, обнулить `linearVelocity`/`angularVelocity`, сбросить аниматор, остановить партиклы, деактивировать `GameObject` (иначе он продолжит получать `Update`, лёжа в пуле).
- Если пул полон, `Release` **уничтожает** объект через `actionOnDestroy`; если пуст, `Get` создаёт новый через `createFunc`. Размеры подбирать по реальному пику трафика.
- Перегрузка `Get(out PooledObject<T> handle)` возвращает объект автоматически при `Dispose` — удобно в `using`.
- Пулить можно не только `GameObject`: в `UnityEngine.Pool` есть `ListPool<T>`, `DictionaryPool<K,V>` и др.

## 6. Кеширование ссылок и петля `Update`

Официальные рекомендации Unity 6.3 (страница Unity programming best practices):

- **`GetComponent` — в `Awake`, результат в поле.** Не в `Update`.
- **`Camera.main` не бесплатен** — обращение регистрируется как сэмпл `FindMainCamera` в профайлере. Кешировать.
- `FindObjectOfType` в Unity 6 заменён на **`FindFirstObjectByType<T>()` / `FindAnyObjectByType<T>()`** — но и они не для горячего пути. Правильный путь — `[SerializeField]` и прокидывание ссылок.
- **Сравнение с `null` у `UnityEngine.Object` медленнее стандартного C#** — оно дополнительно проверяет, не уничтожен ли нативный объект. В горячем пути заметно.
- **Каждый `Update`/`FixedUpdate`/`LateUpdate` стоит денег сам по себе:** вызов идёт из нативного кода в managed, Unity ведёт внутренние списки. При сотнях-тысячах компонентов расходы значимы. Тот же оверхед бьёт по времени `Instantiate` префабов с кучей MonoBehaviour (вызовы `Awake`/`OnEnable`).
- Лечение — **custom update manager**: один MonoBehaviour с `Update` рассылает `CustomUpdate(deltaTime)` подписчикам интерфейса `IUpdatable` (регистрация в `OnEnable`, снятие в `OnDisable`). Выгодно от сотен подписчиков; главный выигрыш — можно **отписаться**, когда объекту нечего делать.

Для нашего проекта: машина игрока — обычный `FixedUpdate`, трафик — через менеджер и/или Job System (раздел 9).

## 7. SRP Batcher vs GPU instancing vs static batching vs GPU Resident Drawer

Это самая частая тема путаницы, потому что советы из статей 2019–2022 годов в Unity 6 + URP уже вредны. Официальная таблица Unity 6.3:

| Механизм | Что делает | Многопоточность | Рекомендация для URP |
|---|---|---|---|
| **SRP Batcher** | Уменьшает смены render state; материалы живут в GPU-памяти постоянно. **Число draw call не уменьшает — делает каждый дешевле** | Только DX12/Vulkan/консольные API, при включённых Graphics Jobs | **Включить** |
| **GPU Resident Drawer** | Автоматически использует BatchRendererGroup + GPU instancing, режет draw calls | Да | **Включить** |
| **BatchRendererGroup (BRG) API** | То же вручную | Да | Не трогать, кроме продвинутых случаев |
| **Галка GPU Instancing на материале** | Аппаратный инстансинг | Нет | **Выключить** — плодит лишние варианты шейдеров |
| **Batching (static/dynamic)** | Склеивает меши | Нет | **Выключить.** Static batching несовместим с BRG/GPU Resident Drawer |

Порядок применения, если включено всё сразу: статика → static batching; динамика с совместимым шейдером → SRP Batcher + GPU Resident Drawer/BRG; остатки с совместимым шейдером → GPU instancing; остальное → dynamic batching.

**Dynamic batching в Unity 6 официально не рекомендуется** для большинства случаев: накладные расходы CPU могут превысить стоимость самого draw call. Оставлен только для слабых устройств; в HDRP не поддерживается вовсе. Ограничения: не более 900 вертексных атрибутов и 300 вершин на меш.

### Как получить максимум от любого из них

- **Одинаковые материалы** у как можно большего числа объектов; разные цвета одного шейдера — через **Material Variants**, а не плодя материалы.
- **Не использовать `MaterialPropertyBlock` в URP/HDRP** — прямая рекомендация документации; он же ломает совместимость с GPU Resident Drawer.
- Не трогать `Renderer.material` из скрипта: обращение **создаёт копию материала** и ломает батч. Читать `Renderer.sharedMaterial`.
- Держать **мало вариантов шейдеров** — SRP Batcher батчит по варианту шейдера, а не по материалу.

### GPU Resident Drawer: настройка и ограничения (Unity 6, URP)

Включение: (1) `Project Settings > Graphics > Shader Stripping > BatchRendererGroup Variants = Keep All`; (2) в активном URP Asset убедиться, что **SRP Batcher включён** (может быть скрыт — меню `⋮` → Show All Advanced Properties); (3) `GPU Resident Drawer = Instanced Drawing`; (4) в Universal Renderer `Rendering Path = Forward+`.

- Только **Forward+**, только API с поддержкой compute shaders (OpenGL ES не поддерживается), только объекты с **Mesh Renderer** — всё прочее рисуется по старинке.
- Объект совместим, если: Light Probes ≠ Use Proxy Volume; статическое GI (не realtime); шейдер поддерживает **DOTS Instancing**; объект не двигается между рендером двух камер; **не использует `MaterialPropertyBlock`**; нет скриптов с per-instance колбэками вроде `OnRenderObject`. Исключить объект точечно — компонент **Disallow GPU Driven Rendering** (есть опция Apply to Children Recursively).
- **Время сборки билда растёт** — Unity компилирует все варианты BRG-шейдеров.
- Выигрыш тем больше, чем крупнее сцена и чем больше объектов делят один меш. В Scene/Game view эффект меньше, чем в Play mode и билде. Дополнительно рекомендуется: выключить Static Batching в Player Settings, включить Fixed Lightmap Size и выключить Use Mipmap Limits в Lighting settings.
- Проверка эффекта: Frame Debugger (батчи `Hybrid Batch Group`), Rendering Debugger, Rendering Statistics (SetPass calls, CPU time), профайлер.

Наш трафик из одинаковых мешей машин плюс столбы, ограждения и деревья — идеальный кейс для GPU Resident Drawer.

## 8. Culling и LOD

**Frustum culling** работает всегда, автоматически и распараллелено по воркерам. **Culling выполняется на каждую камеру** — отсюда правило «одна активная камера», если это не сплит-скрин.

**`Camera.layerCullDistances`** — массив на 32 слоя, отсекает мелкие объекты раньше, чем `farClipPlane` (0 = использовать `farClipPlane`). Unity сначала отсеивает по слоям (битовая маска, дёшево), потом по фрустуму. Для нашей карты: мелкий декор, мусор, дорожная разметка — на отдельные слои с малой дистанцией.

**Occlusion culling** (классический, запекаемый): пометить объекты Occluder/Occludee Static, запечь через `Window > Rendering > Occlusion Culling`. Выигрыш заметный, но стоит места на диске, времени CPU и RAM (данные грузятся вместе со сценой).

**GPU Occlusion Culling** (Unity 6): считает на GPU по depth-буферам текущего и предыдущего кадров, **запекание не нужно**. Галка `GPU Occlusion` в Universal Renderer, **работает только вместе с GPU Resident Drawer** и наследует его ограничения. Каждый объект аппроксимируется **ограничивающей сферой** — тонкие и вытянутые объекты (столбы, ограждения, фонари) отсекаются хуже; используется даунсэмплированный depth-буфер, уровень выбирается по экранному размеру сферы. Выигрывает там, где много перекрытий, а перекрытые объекты тяжёлые по вершинам и мелкие на экране. **Если перекрытий мало, кадр может стать даже медленнее** из-за подготовки данных.

**LOD.** В Unity 6 два разных механизма:

| | LOD Group | Mesh LOD (с Unity 6.2) |
|---|---|---|
| Что оптимизирует | полигоны, материалы, число draw call, настройки Mesh Renderer | **только полигоны** |
| Создание | вручную во внешнем редакторе | **автоматически при импорте модели** |
| Хранение | отдельные GameObject-ы с Mesh Renderer | индексный буфер исходного меша |
| Память/оверхед | больше | меньше |
| Порог переключения | задаётся явно, в % экрана | неявно, через параметры |

Ограничения Mesh LOD: не поддерживается в Entities Graphics, Particle System, VFX Graph, **static batching и GPU instancing** (там всегда берётся LOD0); cross-fade требует включённого GPU Resident Drawer; только треугольная топология; **комбинировать с LOD Group не рекомендуется**; визуализации текущего LOD-индекса нет.

Для нашего проекта: Mesh LOD — быстрый способ получить LOD на всём трафике без ручной работы художника, что важно, раз мы начинаем с примитивов и процедурной генерации.

## 9. Jobs + Burst для трафика без перехода на ECS

Полный переход на ECS/DOTS для демо избыточен, но **Job System и Burst доступны и в обычном MonoBehaviour-проекте** — это прямо написано в best practices Unity 6.

**`IJobParallelForTransform`** — способ двигать сотни `Transform` параллельно. `Transform` — managed-тип и в джобу его не передать, поэтому используется `TransformAccessArray`, оборачивающий трансформы в unmanaged-структуру `TransformAccess`.

```csharp
using Unity.Burst; using Unity.Collections; using UnityEngine; using UnityEngine.Jobs;

[BurstCompile]
public struct MoveTrafficJob : IJobParallelForTransform
{
    [ReadOnly] public NativeArray<Vector3> Velocity;
    public float DeltaTime;

    public void Execute(int index, TransformAccess transform) =>
        transform.position += Velocity[index] * DeltaTime;
}
```

Планирование: `ScheduleByRef(transformAccessArray, dependency)` — чтение/запись; `ScheduleReadOnlyByRef` — только чтение и **лучшая параллелизация**; `RunReadOnlyByRef` — синхронно на главном потоке.

- **Всегда `[BurstCompile]`.** Джоба без Burst — одна из главных причин, по которым проект упирается в воркеры.
- **Не звать `Complete()` сразу после `Schedule()`** — теряется весь смысл. Планировать пораньше, забирать результат позже (в идеале в следующем кадре); иначе увидите `WaitForJobGroupID`.
- Трансформы одной иерархии обрабатываются одним воркером (защита от гонок при записи world-space) — плоская иерархия трафика параллелится лучше.

**`RaycastCommand`** — батч лучей через Job System, асинхронно и параллельно. Для ИИ-водителей («свободна ли полоса», датчики препятствий) это правильный API вместо цикла из `Physics.Raycast`. Результат команды N лежит в `results[N * maxHits]`; первый невалидный результат определяется по `collider == null`.

**`TransformHandle` (только с Unity 6.3)** — unmanaged struct-альтернатива `Transform`, совместимая с Burst; получается через `transform.GetTransformHandle()`. Так как это структура, свойства меняются через локальную переменную: `var h = transformHandle; h.position += Vector3.one;`. В документации 6.0–6.2 страницы нет — в нашей версии доступна, но материалов в сети пока мало.

## 10. Физика машины и трафика

Физика живёт в `FixedUpdate` и почти наверняка будет одной из главных статей расхода. Что настраивать (`Project Settings > Physics` и `Project Settings > Time`):

- **`Reuse Collision Callbacks` — включить.** Иначе каждый `OnCollisionEnter/Stay/Exit` аллоцирует объект `Collision`. Официальная рекомендация — «всегда включать»; выключать только в легаси-коде, который хранит отдельные экземпляры `Collision`.
- **Layer Collision Matrix — почистить**, убрать лишние пары слоёв; для триггеров особенно заметно.
- **Fixed Timestep** по умолчанию 0.02 с = 50 Гц. Если кадр занял 40 мс, на следующем выполнятся **два** шага физики — это может закрутить спираль деградации. Предохранитель — `Maximum Allowed Timestep`: уменьшение (напр. до 0.1 с) ограничивает число догоняющих шагов ценой точности.
- **Broadphase Type.** Дефолт `Sweep and Prune` даёт много ложных срабатываний на **плоских мирах с большим числом коллайдеров** — а наша карта именно такая. Переключить на `Automatic Box Pruning` или `Multibox Pruning`.
- **Solver Iterations:** низкий `Default Solver Iterations` глобально, высокий `Rigidbody.solverIterations` точечно — только на машине игрока.
- **`Physics.autoSyncTransforms`** по умолчанию выключен — так и оставить: при включении каждый физический вызов форсирует синхронизацию. Нужно точное состояние между изменением `Transform` и запросом — звать `Physics.SyncTransforms()` вручную.
- **Не-аллоцирующие запросы:** `RaycastNonAlloc`, `OverlapSphereNonAlloc`, `OverlapBoxNonAlloc` вместо `RaycastAll`/`OverlapSphere`. Буфер выделяется заранее и **не растёт** — размер закладывать с запасом.
- **Mesh Collider:** в `CookingOptions` можно снять `EnableMeshCleaning`, `WeldColocatedVertices`, `CookForFasterSimulation`, если меш заведомо корректный; на PC оставить `Use Fast Midphase`. В Player Settings включить `Prebake Collision Meshes`. Процедурные меши готовить через `Physics.BakeMesh` (можно из джобы), а не вешать `MeshCollider` на главном потоке.
- **Статические коллайдеры можно двигать** без `Rigidbody`; но для предсказуемого взаимодействия — kinematic `Rigidbody` и движение через `Rigidbody.position`/`Rigidbody.rotation`, а не через `Transform`.
- Отладка — `Window > Analysis > Physics Debugger`. И помним: в Unity 6 это **`Rigidbody.linearVelocity`**, а не `velocity`.

## 11. Загрузка ассетов и Addressables

Addressables (пакет `com.unity.addressables`, для Unity 6 — ветка 2.x) даёт асинхронную загрузку с подсчётом ссылок: каждый `LoadAssetAsync` увеличивает ref-count, `Addressables.Release` уменьшает; при нуле ассет готов к выгрузке, и рекурсивно уменьшаются счётчики зависимостей. Хендл завершается минимум через кадр даже для мгновенных операций.

**Честная оценка для нашего проекта:** пока карта статична, а объекты — примитивы Unity, Addressables — преждевременная сложность. Смысл появится, когда: (а) начнут грузиться реальные модели и текстуры и вырастет время старта; (б) появятся разные «уровни»/районы карты, которые надо стримить; (в) размер билда станет проблемой. До этого — обычные ссылки в префабах и `Resources`-free архитектура.

Что важно уже сейчас, независимо от Addressables: **не держать strong-ссылки на крупные ассеты в статических полях** — они переживают смену сцен и мешают выгрузке.

## 12. Настройки качества, VSync и Adaptive Performance

- **VSync против targetFrameRate.** Документация Unity 6 прямо рекомендует на десктопе управлять частотой через **`QualitySettings.vSyncCount`**, потому что это аппаратная синхронизация, а `Application.targetFrameRate` — программный таймер, дающий микрозаикания. При `vSyncCount != 0` значение `targetFrameRate` игнорируется. Бонус: VSync и `targetFrameRate` отдают простаивающее время инкрементальному GC.
- **Dynamic Resolution** (`Allow Dynamic Resolution` на камере) динамически масштабирует render target при просадках GPU. Опирается на `FrameTimingManager`. Уместно как демонстрационная фича, но список поддерживаемых платформ надо сверять по документации.
- **Adaptive Performance** — пакет про **термальное состояние и энергопотребление мобильных устройств**. Требует Unity 2021.2+. Для десктопного демо на Apple Silicon смысла не имеет; ставить не надо.
- **Уровни качества.** Практика Unity — заводить «тиры» железа и профилировать под нижнюю границу каждого тира, а не под свою машину.

## Антипаттерны

Что часто советуют — и почему здесь это плохо.

1. **«Включи GPU Instancing на всех материалах».** В URP/HDRP официальная рекомендация Unity 6 — **выключить** галку: она плодит варианты шейдеров, а её работу берут на себя SRP Batcher и GPU Resident Drawer. Более того, GPU instancing с кастомными шейдерами в URP/HDRP работает, **только если отключить SRP Batcher** или сделать шейдер несовместимым с ним. Совет актуален для Built-in RP, который мы не используем.
2. **«Помечай всё статикой ради static batching».** Static batching **несовместим** с BatchRendererGroup и GPU Resident Drawer; документация Unity 6.3 для URP говорит его **выключить**. Кроме того, Mesh LOD при static batching всегда берёт LOD0.
3. **«Включи dynamic batching, он бесплатный».** В Unity 6 он «no longer recommended»: CPU-оверхед поиска и трансформации мешей часто дороже сэкономленного draw call. В HDRP не поддерживается вообще.
4. **«`foreach` всегда аллоцирует, пиши `for`».** Наследие старого компилятора. Аллокация возникает при переборе через интерфейс, а не сама по себе. Не переписывать код вслепую — проверить в профайлере.
5. **«Оптимизируем шейдеры / уменьшаем текстуры», не зная, кто узкое место.** Если проект CPU-bound, работа с GPU не даст ничего. Порядок обратный: сначала Highlights-модуль → CPU или GPU → потом конкретный поток → потом конкретный маркер.
6. **«Смотрим FPS в редакторе».** In-Editor FPS не переносится на билд. Профилировать development-билд.
7. **«Держим Deep Profiling всегда включённым».** Он инструментирует каждый вызов, искажает относительные величины и может уронить редактор по памяти. Включать точечно.
8. **«`GC.Alloc` занял 0.33 мс — ерунда».** Профайлер рисует этому маркеру **искусственную** длительность: он пишет только момент и размер. Реальная цена выше — плюс вытеснение кеш-линий L1 и отложенный запуск сборщика.
9. **«Несколько камер — это удобно».** Каждая камера прогоняет весь пайплайн: culling, сортировку, батчинг. Реальный кейс из материалов Unity: пять камер = 16.23 мс на кадр.
10. **«`MaterialPropertyBlock` — правильный способ красить объекты по-разному».** В URP/HDRP документация Unity 6 советует его избегать, и он делает объект несовместимым с GPU Resident Drawer. Использовать Material Variants.
11. **«`Renderer.material.color = ...` для подсветки».** Обращение к `.material` создаёт копию материала и ломает батч. Читать `.sharedMaterial`.
12. **«Выключим Incremental GC, он тормозит».** Он не ускоряет сборку, но убирает пики; выключение возвращает stop-the-world паузы, которые «могут длиться сотни миллисекунд». Выключать имеет смысл только если профайлер показал, что write barriers дороже — это редкость.
13. **«Занулим `targetFrameRate` и снимем VSync, чтобы было больше FPS».** На десктопе получите микрозаикания и лишите инкрементальный GC простаивающего времени.
14. **«Возьмём Adaptive Performance для стабильного FPS на маке».** Пакет про тепловое состояние мобильных устройств.
15. **`FindObjectOfType` / `Object.FindObjectsOfType`** — устаревшие имена; в Unity 6 это `FindFirstObjectByType<T>()` / `FindAnyObjectByType<T>()` / `FindObjectsByType<T>()`. Но и они не для `Update` — ссылки прокидывать через `[SerializeField]`.

## Чек-лист

**Настройка проекта (один раз, при создании)**
- [ ] URP Asset: SRP Batcher — On, GPU Resident Drawer — Instanced Drawing
- [ ] Universal Renderer: Rendering Path — Forward+, GPU Occlusion — On
- [ ] `Project Settings > Graphics > Shader Stripping`: BatchRendererGroup Variants = Keep All
- [ ] `Project Settings > Player`: Static Batching — Off; Incremental GC — On (дефолт); Frame Timing Stats — On, если нужен оверлей в release
- [ ] `Project Settings > Physics`: Reuse Collision Callbacks — On; Layer Collision Matrix почищена; Broadphase = Automatic/Multibox Pruning для плоской карты
- [ ] `Project Settings > Time`: Fixed Timestep и Maximum Allowed Timestep осознанно выставлены
- [ ] Пакеты установлены: Memory Profiler, Profile Analyzer, Project Auditor

**Каждая сессия профилирования: диагностика по порядку**
- [ ] Профилирую development-билд, а не Play Mode; Highlights-модуль включён, Target Frame Time = 60 FPS; базовая линия снята в Profile Analyzer
- [ ] В бюджете? (`WaitForTargetFPS` на главном, простой на рендер-потоке и воркерах). Если нет — CPU или GPU (Highlights)
- [ ] Если CPU — какой поток (Timeline view): main / render (`Gfx.WaitForPresentOnGfxThread`) / worker (`WaitForJobGroupID`)
- [ ] Число активных камер = 1
- [ ] Batches и SetPass calls в Rendering-модуле; развалившиеся батчи — в Frame Debugger
- [ ] `GC.Alloc` в кадре = 0 в установившемся режиме (фильтр `GC.Alloc` в Hierarchy view, Allocation Call Stacks для источника)

**Код-ревью горячего пути**
- [ ] Нет `GetComponent`, `Find*`, `Camera.main` в `Update`/`FixedUpdate`
- [ ] Нет LINQ, конкатенации строк, `new` в апдейтах
- [ ] Нет обращений к массив-возвращающим свойствам Unity API внутри циклов
- [ ] Спавн массовых объектов — через `ObjectPool<T>`, состояние сбрасывается при `Release`
- [ ] Физические запросы — `NonAlloc`-версии или `RaycastCommand`
- [ ] Джобы помечены `[BurstCompile]`, `Complete()` не вызывается сразу после `Schedule()`
- [ ] Все `ProfilerRecorder` / `NativeArray` / `TransformAccessArray` освобождаются

**После оптимизации**
- [ ] Сравнение до/после в Profile Analyzer (Compare view, медиана — не среднее)
- [ ] Memory Profiler: снапшоты до/после цикла загрузки-выгрузки, утечек нет

## Видео и доклады

- Unity Profiler Walkthrough & Tutorial — Unity — https://www.youtube.com/watch?v=xjsqv8nj0cw — стартовая экскурсия по окну профайлера, модулям и подключению к билду.
- Profile Analyzer Walkthrough & Tutorial — Unity — https://www.youtube.com/watch?v=Ypg84Fr20Sw — как агрегировать сотни кадров и сравнивать «до/после».
- Memory Profiler Walkthrough & Tutorial — Unity — https://www.youtube.com/watch?v=Uuzd39AjFWQ — снапшоты, сравнение снапшотов, поиск утечек.
- Optimization for web, XR & mobile games in Unity 6 — Unity — https://youtu.be/2J0kDtUGlrY — 40-минутный практикум: профилирование неоптимизированного Unity 6 + URP проекта и последовательное исправление узких мест. Методика применима и к десктопу.
- How to profile and optimize a game | Unite Now 2020 — Unity — https://www.youtube.com/watch?v=epTPFamqkZo — полный цикл «нашли — исправили — доказали».
- Introduction to profiling in Unity | Unite Now 2020 — Unity — https://youtu.be/uXRURWwabF4 — базовые понятия: бюджет кадра, CPU-bound vs GPU-bound.
- Improve memory usage with the Memory Profiler in Unity — Unity — https://www.youtube.com/watch?v=I9wB4Cvgz5g — практический разбор работы с памятью.
- Optimize your game with the Profile Analyzer — Unite Copenhagen 2019 — Unity — https://www.youtube.com/watch?v=0lzqdDdE9Tc — статистика по кадрам (интерфейс изменился, методика — нет).
- Unite Berlin 2018 — Book of the Dead: Optimizing Performance for High End Consoles — Unity — https://www.youtube.com/watch?v=I5lzlGiJW0k — wavefront occupancy, async compute, GPU-профилирование. Устарел по API, актуален по мышлению.

## Источники

Дата обращения: 2026-08-19. Документация — версия Unity 6.3 LTS (6000.3), если не указано иное.

**Профилирование**
- https://docs.unity3d.com/6000.3/Documentation/Manual/Profiler.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/ProfilerHighlights.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/profiler-deep-profiling.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/profiler-profiling-applications.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/performance-profiling-tools.html
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Profiling.ProfilerMarker.html
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.html
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/FrameTimingManager.html
- https://docs.unity3d.com/Packages/com.unity.performance.profile-analyzer@1.2/manual/index.html
- https://docs.unity3d.com/Packages/com.unity.memoryprofiler@1.1/manual/index.html
- https://docs.unity3d.com/Packages/com.unity.project-auditor@1.1/manual/index.html
- https://unity.com/how-to/best-practices-for-profiling-game-performance
- https://unity.com/blog/use-unity-6-profiling-tools-smart-efficient

**Рендеринг и батчинг**
- https://docs.unity3d.com/6000.3/Documentation/Manual/optimizing-draw-calls-choose-method.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/optimizing-draw-calls.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/DrawCallBatching.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/SRPBatcher.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/GPUInstancing.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/gpu-resident-drawer.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/make-object-compatible-gpu-rendering.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/reduce-rendering-work-on-cpu.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/gpu-culling.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/render-graph-viewer-reference.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/render-graph-introduction.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/FrameDebugger.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/LevelOfDetail.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/lod/mesh-lod-introduction.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/OcclusionCulling.html
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Camera-layerCullDistances.html
- https://unity.com/how-to/gpu-optimization

**Память, GC, код**
- https://docs.unity3d.com/6000.3/Documentation/Manual/programming-best-practices.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/performance-reference-types.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/performance-optimizing-arrays.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/performance-reusable-code.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/performance-incremental-garbage-collection.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/performance-managed-memory.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/events-per-frame-optimization.html
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Pool.ObjectPool_1.html

**Jobs, Burst, физика**
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Jobs.IJobParallelForTransform.html
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/RaycastCommand.html
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Physics.RaycastNonAlloc.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/job-system.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/transformhandle-landing.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/transformhandle-examples.html
- https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/index.html
- https://unity.com/how-to/enhanced-physics-performance-smooth-gameplay

**Платформа, ассеты, качество**
- https://docs.unity3d.com/6000.3/Documentation/Manual/XcodeFrameDebuggerIntegration.html
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Application-targetFrameRate.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/runtime-performance-scaling.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/DynamicResolution.html
- https://docs.unity3d.com/Packages/com.unity.adaptiveperformance@5.1/manual/index.html
- https://docs.unity3d.com/Packages/com.unity.addressables@2.1/manual/load-assets-asynchronous.html
- https://unity.com/blog/unity-6-game-optimization-guides

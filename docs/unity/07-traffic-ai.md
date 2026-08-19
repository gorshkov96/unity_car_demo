# Движущийся мир: трафик, пешеходы, ИИ-агенты

Как заставить десятки и сотни объектов жить вокруг игрока в Unity 6.3 и не уронить кадр. Заглядывать сюда перед тем, как выбирать между NavMesh, сплайнами и ECS, и перед первой строчкой кода трафика.

---

## TL;DR — решения для нашего проекта

1. **Машины — по сплайнам, не по NavMesh.** Трафик едет по заранее заданным полосам (пакет Splines 2.9.0). NavMesh не знает про полосы, правую сторону и задний ход — машина на `NavMeshAgent` выглядит как ходячий шкаф на колёсах.
2. **Пешеходы — на `NavMeshAgent`** (пакет AI Navigation 2.0.14). Здесь как раз сильная сторона NavMesh: локальное расхождение агентов (RVO) и обход препятствий.
3. **Стартуем на уровне A: обычные `MonoBehaviour` + `ObjectPool<T>`.** До ~200–300 активных агентов этого хватает; сначала измеряем профайлером, потом оптимизируем.
4. **Потолок роста — уровень B: Jobs + Burst без ECS.** `NativeSpline` официально job-совместим, `IJobParallelForTransform` двигает сотни `Transform` параллельно. Переход с уровня A на B не ломает архитектуру, если логика уже отделена от `MonoBehaviour`.
5. **DOTS/ECS (Entities 1.4.8) в этом проекте не оправдан.** Он окупается на десятках тысяч сущностей; цена — второй набор инструментов, `Entities Graphics` только на URP Forward+ / HDRP, Web не поддерживается вовсе, на Android только Vulkan и OpenGL ES 3.1+, на iOS только Metal.
6. **Трафик — кинематический, без `Rigidbody`-симуляции.** Физика только у машины игрока. Столкновения трафика решаются моделью «следования за ведущим» и резервированием перекрёстков, а не физдвижком.
7. **Светофоры и перекрёстки — граф из данных.** Полоса = ребро, перекрёсток = узел с фазами в `ScriptableObject`. Одна система тикает фазы и рассылает событие; машины не опрашивают светофор каждый кадр.
8. **LOD агентов через `CullingGroup`:** близко — полная логика и анимация, средне — упрощённая, далеко — только позиция без `Animator` (`AnimatorCullingMode.CullCompletely`).
9. **Спавн кольцом вокруг игрока с гистерезисом** (спавн за `R_spawn`, деспавн за `R_despawn > R_spawn`), с бюджетом «не более N объектов за кадр» и всегда вне поля зрения камеры.
10. **Включить GPU Resident Drawer** (URP, Forward+): он сам гоняет `MeshRenderer` через `BatchRendererGroup` и сокращает draw call'ы — ровно то, что нужно сотне одинаковых машин.

---

## 0. Версии, от которых пляшем

Всё проверено по документации Unity 6.3 LTS (6000.3) на 2026‑08‑19.

| Пакет | Версия для 6000.3 | Зачем нам |
|---|---|---|
| `com.unity.ai.navigation` | 2.0.14 | NavMesh, `NavMeshSurface`, `NavMeshLink` — пешеходы |
| `com.unity.splines` | 2.9.0 | полосы движения, маршруты трафика |
| `com.unity.burst` | 1.8.30 | компиляция джобов в машинный код |
| `com.unity.collections` | 2.6.8 | `NativeArray`/`NativeList` для джобов |
| `com.unity.mathematics` | 1.3.3 | `float3`, `quaternion` — типы, дружественные Burst |
| `com.unity.entities` | 1.4.8 | ECS, если вдруг понадобится |
| `com.unity.entities.graphics` | 1.4.21 | рендер ECS-сущностей; **только URP Forward+ / HDRP**, Web не поддерживается |

Jobs System (система задач — способ раскидать вычисления по ядрам процессора) встроена в движок, отдельный пакет не нужен.

---

## 1. Три уровня масштаба: честное сравнение

| | A. MonoBehaviour + пул | B. Jobs + Burst без ECS | C. DOTS / ECS |
|---|---|---|---|
| Порядок агентов | до ~200–500 | ~1 000–10 000 | 10 000+ |
| Цена входа | нулевая | 1–2 дня на понимание джобов | недели: своя модель данных, свой рендер, свой отладчик |
| Инспектор, префабы, отладка | как обычно | как обычно | отдельный тулинг, часть привычного не работает |
| Совместимость с URP/тенями/партиклами | полная | полная | через `Entities Graphics`, с ограничениями |
| Риск для проекта-витрины | низкий | низкий | высокий: половина времени уйдёт на инфраструктуру, а не на демонстрацию |

**Уровень A.** Каждая машина — `GameObject` с компонентом, который двигает её в `Update`. Узкое место — не сама математика, а тысячи вызовов `Update` из движка и промахи кеша процессора. Спасают: пул (`UnityEngine.Pool.ObjectPool<T>`), отказ от `GetComponent`/`Camera.main` в горячем пути, отсутствие аллокаций в кадре.

**Уровень B — наш потолок.** Данные агентов лежат в `NativeArray` (неуправляемый массив — без сборщика мусора), одна Burst-джоба считает всех сразу, `IJobParallelForTransform` записывает результат в `Transform`. Официальная документация Unity 6.3 прямо описывает этот путь и приводит пример на 500 объектов.

**Уровень C — ECS.** Документация самого пакета Entities честно предупреждает: у планирования любой джобы есть постоянная CPU-цена (выделение памяти потока и копирование данных), и она *«может стать заметной в приложениях, которые планируют много коротких джоб»*. Совет из тех же docs — укрупнять джобы, а не дробить. ECS начинает окупаться там, где счёт идёт на десятки тысяч. Публичный пример уровня C — `OSMTrafficSim`: 25 000 машин и 10 000 пешеходов на 30 FPS на ноутбуке, но это Unity 2023.1 + Entities 1.0.6 и лицензия GPL‑3.0.

**Когда ECS в демо-проекте не оправдан:** когда цель — показать физику, свет, звук и партиклы, а не рекорд по числу сущностей. ECS отнимает именно те инструменты, которыми удобно «показывать»: инспектор, префабы, привычные компоненты. Возвращаемся к нему, только если профайлер покажет, что 300 агентов не влезают в бюджет кадра — а он этого не покажет.

---

## 2. Сплайны — позвоночник трафика

Пакет Splines (кривые и пути) даёт `SplineContainer` — компонент, хранящий одну или несколько кривых. Полоса движения = один сплайн. Перекрёсток = набор коротких сплайнов-«поворотов», соединяющих входящие полосы с исходящими.

### 2.1 Строим дорогу из скрипта

```csharp
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

var go = new GameObject("Lane_A_to_B");
var container = go.AddComponent<SplineContainer>();
Spline lane = container.Spline;              // первый сплайн создаётся автоматически
lane.Add(new float3(0f, 0f, 0f), TangentMode.AutoSmooth);
lane.Add(new float3(0f, 0f, 40f), TangentMode.AutoSmooth);
lane.Add(new float3(25f, 0f, 65f), TangentMode.AutoSmooth);
lane.Closed = true;                          // замкнутое кольцо — трафик ездит вечно
// вторую и последующие полосы: container.AddSpline();  (расширение SplineUtility)
```

`TangentMode.AutoSmooth` — Unity сам считает касательные, дорога получается плавной без ручной правки маркеров.

### 2.2 Движение: дистанция, а не «t»

Параметр `t` в сплайне **не равномерен по длине** — на крутом повороте одинаковый шаг `t` даёт другой шаг в метрах. Для трафика нужна дистанция в метрах:

- `spline.GetLength()` / `container.CalculateLength()` — длина;
- `SplineUtility.ConvertIndexUnit(spline, value, PathIndexUnit.Distance, PathIndexUnit.Normalized)` — перевод метров в `t`;
- `SplineUtility.GetPointAtLinearDistance(spline, fromT, relativeDistance, out float resultT)` — «сдвинуться на N метров вперёд».

### 2.3 Сотня машин в одной Burst-джобе

`NativeSpline` — версия сплайна только для чтения; документация явно говорит: *«NativeSpline is compatible with the job system»*. Значит, весь трафик можно двигать параллельно.

**Сначала — про пространство координат.** Документация `SplineContainer.Spline`: *«all methods and properties accessed on the spline are in the local space of the container»*. То есть `Evaluate` возвращает позицию **в локальных координатах контейнера**, а `TransformAccess.position` — это **мировая** позиция. Если контейнер полосы стоит не в начале координат, трафик уедет. Лечится при создании `NativeSpline`: у него есть конструктор, принимающий матрицу.

```csharp
// Один раз при загрузке карты, не в кадре.
// Allocator.Persistent, а не Temp по умолчанию: джоба переживает кадр.
_lane = new NativeSpline(
    container.Spline,
    container.transform.localToWorldMatrix,   // печём мировое пространство внутрь
    Allocator.Persistent);
_laneLength = _lane.GetLength();              // длина уже с учётом матрицы
// ...
_lane.Dispose();                              // NativeSpline : IDisposable — обязательно в OnDestroy
```

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Jobs;
using UnityEngine.Splines;

[BurstCompile]
public struct LaneTrafficJob : IJobParallelForTransform
{
    [ReadOnly] public NativeSpline Lane;
    [ReadOnly] public NativeArray<float> Speed;   // м/с
    public NativeArray<float> Distance;           // пройдено вдоль полосы, м
    public float LaneLength;
    public float DeltaTime;

    public void Execute(int index, TransformAccess transform)
    {
        float d = Distance[index] + Speed[index] * DeltaTime;
        if (d >= LaneLength) d -= LaneLength;     // замкнутая полоса
        Distance[index] = d;

        float t = Lane.ConvertIndexUnit(d, PathIndexUnit.Distance, PathIndexUnit.Normalized);
        Lane.Evaluate(t, out float3 pos, out float3 tangent, out float3 up);

        transform.position = pos;                                       // float3 -> Vector3 неявно
        transform.rotation = quaternion.LookRotationSafe(tangent, up);  // quaternion -> Quaternion неявно
    }
}
```

Планирование — в `Update` через `job.ScheduleByRef(transformAccessArray, dependency)` (в Unity 6 официальный пример использует именно `…ByRef`: структура джобы не копируется лишний раз). `Complete()` — как можно позже: в идеале следующая джоба получает `JobHandle` как зависимость, а не немедленный `Complete`.

**Ловушка «только свой индекс».** Перед каждым вызовом `Execute` движок сужает разрешённый диапазон всех обычных `NativeArray` в джобе до **одного элемента** — текущего `index`. Поэтому `Distance[index]` читать и писать можно, а `Distance[index - 1]` (посмотреть на ведущего, см. §5) — уже нет: в редакторе это `IndexOutOfRangeException` про «restricted IJobParallelFor range». Варианты: пометить массив `[NativeDisableParallelForRestriction]` (безопасно, пока каждый индекс пишется ровно один раз, а чужие читаются только на чтение) или разнести шаги — сначала обычная `IJobParallelFor` считает скорости и дистанции по всей полосе, потом `IJobParallelForTransform` только раскладывает результат по `Transform`. Второй вариант чище и обычно быстрее.

**Ловушка иерархии.** Документация `IJobParallelForTransform`: *«All transforms from the same transform hierarchy will be processed by a single worker thread»*. Если сложить весь трафик под один родительский `GameObject` «Traffic», параллельная джоба выродится в однопоточную. Пулу и спавнеру — не задавать общего родителя.

### 2.4 Почему сплайны, а не NavMesh, для машин

| | Сплайн-полоса | NavMeshAgent |
|---|---|---|
| Держит правую сторону дороги | да, полоса — это и есть путь | нет, агент срезает по центру полигона |
| Предсказуемость для демо | полная, машина едет ровно там, где нарисовано | зависит от триангуляции навмеша |
| Расхождение агентов | вручную (модель следования) | из коробки (RVO) |
| Задний ход, разворот в три приёма | вручную | не поддерживается |
| Стоимость на агента | ~ноль | поиск пути + локальное избегание |

`SplineAnimate` — готовый компонент «двигай объект по сплайну» с easing и loop-режимами. Хорош для одиночных объектов (поезд, лифт, камера-облёт), но это `MonoBehaviour` на объект — для сотни машин не годится.

---

## 3. NavMesh и пакет AI Navigation 2.0.14 — для пешеходов

### 3.1 Что изменилось по сравнению с советами 2019–2022

Навигация вынесена в пакет начиная с Unity 2022.2. Практические последствия:

- В окне `Window > AI > Navigation` **больше нет вкладок Bake и Object** — только Agents и Areas. Флага «Navigation Static» тоже нет: его роль играет компонент `NavMeshModifier`.
- Навмеш живёт в компоненте `NavMeshSurface`, а не «внутри сцены». Один `NavMeshSurface` = один тип агента.
- `OffMeshLink` **устарел**, его больше нельзя добавить через Add Component. Вместо него `NavMeshLink`. Документация перечисляет, что добавилось по сравнению с `OffMeshLink`: **тип агента**, **ширина** и две точки-концы, задаваемые прямо в компоненте (`startPoint`/`endPoint`), а не только через `Transform`. В скриптах поля переименованы: `biDirectional` → `bidirectional`, `costOverride` → `costModifier`, `autoUpdatePositions` → `autoUpdate` (старые имена оставлены как совместимые). Автозамена компонентов — `Window > AI > Navigation Updater`, но в **скриптах** она `OffMeshLink` не правит.
- Скрипты `NavMeshComponents` со старого GitHub Unity надо удалить перед установкой пакета — иначе конфликт имён.

### 3.2 Как устроено внутри (и что из этого следует)

Документация «Inner Workings of the Navigation System»:

- глобальный поиск пути — **A\*** по полигонам навмеша; результат — «коридор» полигонов;
- локальное расхождение — **RVO (reciprocal velocity obstacles)**, предсказание будущих столкновений;
- «выпекание» — вокселизация: чем меньше `Voxel Size`, тем точнее и **медленнее**. Дефолт — 3 вокселя на радиус агента; для больших открытых площадок хватает 1–2.

### 3.3 Цена на многих агентах и рычаги

- **`Quality` (obstacle avoidance).** Официально: *«If you have a high number of agents, you can reduce the obstacle avoidance quality to reduce performance costs»*. Полные имена значений `ObstacleAvoidanceType` (в коде пишутся целиком): `NoObstacleAvoidance` → `LowQualityObstacleAvoidance` → `MedQualityObstacleAvoidance` → `GoodQualityObstacleAvoidance` → `HighQualityObstacleAvoidance`. Для толпы на заднем плане ставим `LowQuality…` или вовсе `NoObstacleAvoidance` (столкновения при этом всё равно разрешаются, но активного обхода нет).
- **`avoidancePriority` (0–99, меньше = важнее, по умолчанию 50).** Агент избегает более приоритетных и **игнорирует менее приоритетных**. Машине игрока и «героям» — низкие числа, массовке — высокие: это и дешевле, и даёт нужную картинку. Документация советует ровно это для персонажа игрока — низкий номер, чтобы он «протискивался» сквозь толпу.
- **`NavMesh.pathfindingIterationsPerFrame`** — сколько узлов A\* обрабатывается за кадр при асинхронном поиске (то есть при `SetDestination`). По умолчанию 100, разумный диапазон 50–500. Это главный рычаг против спайков, когда сто пешеходов одновременно просят новый маршрут.
- **`NavMesh.avoidancePredictionTime`** — на сколько секунд вперёд агенты предсказывают столкновение. По умолчанию 2.0, диапазон 0.5–5.0. Меньше — дешевле и «нервнее».

Отдельно: **Jobs/Burst-API у NavMesh нет** — расчёт пути и симуляция агентов живут внутри движка, в джобу их не завернуть. На форуме Unity участник сообщества (прямо оговоривший, что он не сотрудник Unity) пишет, что avoidance и перепрокладка маршрутов многопоточны, а сам поиск пути идёт в основном потоке — синхронно через `NavMesh.CalculatePath` либо асинхронно через `SetDestination`. Официальной документацией это не подтверждено; практический вывод от этого не меняется: единственный доступный рычаг — `pathfindingIterationsPerFrame` и частота перезапросов пути.

```csharp
// Один раз на старте — глобальные настройки навигации.
NavMesh.pathfindingIterationsPerFrame = 150;
NavMesh.avoidancePredictionTime = 1.2f;

// Настройка агента массовки при выдаче из пула.
agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
agent.avoidancePriority = 60;   // игрок — 10, чтобы толпа расступалась
```

### 3.4 Runtime baking (пересборка навмеша на лету)

Два уровня API:

- `NavMeshSurface.BuildNavMesh()` — **синхронно**, блокирует кадр. Годится на загрузке уровня, не годится на лету.
- `NavMeshSurface.UpdateNavMesh(NavMeshData data)` → возвращает `AsyncOperation`, пересобирает **только изменившиеся области**. Это рабочий вариант для процедурной карты.
- Низкий уровень: `NavMeshBuilder.CollectSources` + `NavMeshBuilder.UpdateNavMeshDataAsync` — полный контроль над тем, какая геометрия и какой объём пересобираются.

Практика: включить **Override Tile Size** и уменьшить тайл (дефолт — 256 вокселей, для рантайма разумно 64–128). Документация: *«If you plan to bake the NavMesh at runtime, use a smaller tile size to keep the maximum memory use low»*. Мелкие тайлы также ускоряют carving. Плата за это названа там же: чем мельче тайлы, тем сильнее фрагментирован навмеш, и пути иногда получаются неоптимальными.

В пакет входят готовые сцены-примеры, две из которых прямо про нашу задачу: **Sliding Window Infinite** и **Sliding Window Terrain** — навмеш строится только в окне вокруг игрока и «едет» вместе с ним.

### 3.5 `NavMeshLink` и препятствия

- `NavMeshLink` — переход между несвязанными навмешами (пешеходный переход над дорогой, лестница, прыжок). Свойства: `Bidirectional`, `Cost Override`, `Width`, `Activated`, `Area Type`. Управляемый `activated` — готовый механизм «переход закрыт на красный».
- `NavMeshObstacle` **с Carve** прорезает дыру в навмеше — операция дорогая. Документация: для движущихся препятствий использовать локальное избегание, carve включать только для стоящих. Дефолтный режим `Carve Only Stationary` + `Carving Time To Stationary` — правильный.
- Если у препятствия есть `Rigidbody`, `NavMeshObstacle` автоматически берёт его скорость — агенты предсказывают движение. Это ровно то, что нужно, чтобы пешеходы уворачивались от машины игрока: вешаем `NavMeshObstacle` на машину игрока, а не `NavMeshAgent`.

---

## 4. Перекрёстки и светофоры

**Модель данных.** Дорожная сеть — направленный граф:

- `LaneAsset` (ScriptableObject или plain-класс): ссылка на `SplineContainer`, лимит скорости, список полос-наследников (куда можно уехать с конца).
- `IntersectionNode`: список входящих полос, список конфликтных пар «поворот × поворот», ссылка на фазовую программу.
- `TrafficLightProgram` (ScriptableObject — ассет-конфиг, правится без перекомпиляции): массив фаз `{ длительность, набор разрешённых групп }`.

**Тик фаз — один на всю карту.** Одна система в `Update` считает время текущей фазы и при смене шлёт событие/меняет флаг в общем `NativeArray<byte> LaneGreen`. Машины читают этот массив из джобы. Никаких `GetComponent<TrafficLight>()` в кадре и никаких `Update` на каждом светофоре.

**Разрешение конфликтов на перекрёстке — резервирование, а не физика.** Перед въездом машина запрашивает «слот» на конфликтную зону; если занят — тормозит на стоп-линии. Это детерминированно, дёшево и не даёт машинам «толкаться» бамперами. Физическое разрешение столкновений между сотней `Rigidbody` — прямая дорога к дрожанию и просадкам.

**Очередь на полосе — бесплатная детекция.** Если машины на полосе хранятся в списке, отсортированном по пройденной дистанции, то «кто передо мной» — это сосед по индексу, за O(1), без единого рейкаста. Пересортировка не нужна: обгонов внутри полосы нет, порядок сохраняется.

---

## 5. Детекция и избегание столкновений

**Основная модель — следование за ведущим (car-following).** Целевая скорость складывается из трёх ограничений: лимит полосы, дистанция до ведущего, состояние светофора/стоп-линии впереди. Ускорение ограничено, чтобы не было рывков:

```csharp
float gap = leaderDistance - myDistance - carLength;        // зазор в метрах
float safeSpeed = math.sqrt(math.max(0f, 2f * brake * (gap - minGap)));
float target = math.min(math.min(laneSpeedLimit, safeSpeed), stopLineSpeed);
speed = math.clamp(target, speed - brake * dt, speed + accel * dt);
```

**Когда нужны реальные лучи — пакетные запросы.** `RaycastCommand`, `SpherecastCommand`, `BoxcastCommand`, `OverlapSphereCommand` + `QueryParameters` выполняют пачку запросов в джобе параллельно. Один `ScheduleBatch` на весь трафик вместо сотни `Physics.Raycast` в `Update`. Результаты читаются только после `Complete()`.

**Машина игрока — единственный «настоящий» физический объект.** Трафик её видит через тот же граф: игрок проецируется на ближайшую полосу (`SplineUtility.GetNearestPoint`) и учитывается как ведущий/помеха. Для пешеходов игрок — `NavMeshObstacle` с `Rigidbody`.

---

## 6. LOD агентов и «дальний дешёвый режим»

`CullingGroup` — недооценённый API: движок сам считает видимость и дистанционные «полосы» (distance bands) для набора сфер и дёргает колбэк **только при смене состояния**. Никаких `Vector3.Distance` в `Update`.

```csharp
_group = new CullingGroup { targetCamera = _camera };
_group.SetBoundingSpheres(_spheres);              // по сфере на агента
_group.SetBoundingSphereCount(_activeCount);
_group.SetBoundingDistances(new[] { 40f, 120f, 300f });
_group.SetDistanceReferencePoint(_playerTransform);
_group.onStateChanged += OnAgentLodChanged;       // вызывается только при переходе
```

Три режима:

| Режим | Дистанция | Логика | Визуал |
|---|---|---|---|
| Near | < 40 м | полная: следование, повороты головы, звук | `Animator` работает, LOD0, тени |
| Mid | 40–120 м | упрощённая: только скорость и позиция | `AnimatorCullingMode.CullUpdateTransforms`, LOD1 |
| Far | > 120 м | «виртуальный агент»: позиция обновляется в общей джобе, `GameObject` может отсутствовать | `CullCompletely` или прокси-инстанс |

Дополнительно:

- `LODGroup` на префабах машин и пешеходов — обязательный минимум.
- `Animator.cullingMode = AnimatorCullingMode.CullCompletely` для невидимых пешеходов: анимация полностью выключается, когда рендереры не видны.
- Самый дешёвый «дальний режим» — вообще без `GameObject`: агент существует только как строка в `NativeArray`, а материализуется при входе в ближнюю зону. Для витрины это ещё и красивый пункт «десятки тысяч сущностей в симуляции».

---

## 7. Спавн вокруг игрока и деспавн

**Пул — `UnityEngine.Pool.ObjectPool<T>`.** Стандартный API движка, не надо писать свой.

```csharp
_pool = new ObjectPool<TrafficCar>(
    createFunc:      () => Instantiate(_prefab),        // без родителя: см. ловушку иерархии
    actionOnGet:     car => car.gameObject.SetActive(true),
    actionOnRelease: car => car.gameObject.SetActive(false),
    actionOnDestroy: car => Destroy(car.gameObject),
    collectionCheck: true,      // ловит двойной Release; работает только в редакторе
    defaultCapacity: 64,
    maxSize: 256);
```

Документация подчёркивает: `ObjectPool<T>` **не потокобезопасен** и вызывается только из главного потока; при возврате объект надо *сбрасывать в исходное состояние* — остановить корутины, отписаться от событий, обнулить физику, остановить партиклы, выключить `GameObject`.

**Правила кольца:**

- Спавн на дистанции `R_spawn`, деспавн на `R_despawn = R_spawn * 1.3…1.5`. Без этого зазора (гистерезиса) объекты на границе начнут мигать «появился–исчез».
- Спавнить **только вне усечённой пирамиды камеры** и только на свободном участке полосы (зазор до соседей больше минимального).
- Бюджет: не более N объектов за кадр (N ≈ 2–5). Массовое `Instantiate` в один кадр — гарантированный фриз даже с пулом, потому что первый прогрев пула всё равно создаёт объекты.
- Прогревать пул на загрузке: `Get()` × capacity, затем `Release()` — чтобы в геймплее `createFunc` не вызывался.
- Плотность трафика — параметр `ScriptableObject`, а не константа: «машин на километр полосы», отдельно для дня/ночи/зон.

---

## 8. Детерминизм и FixedUpdate

- **Физика — только в `FixedUpdate`.** Интервал задаётся `Time.fixedDeltaTime` (Project Settings > Time > Fixed Timestep). Документация напоминает: интервал «фиксированный» лишь логически — при низком FPS за один кадр выполняется несколько `FixedUpdate` подряд, есть потолок `Maximum Allowed Timestep`.
- **Трафик по сплайнам физикой не является.** Его можно двигать в `Update` с `Time.deltaTime` — это дешевле и даёт гладкую картинку на любом FPS. Но если нужен воспроизводимый прогон (запись/повтор демо, тесты), считаем симуляцию фиксированным шагом (аккумулятор + `Time.fixedDeltaTime`), а визуальные `Transform` интерполируем между шагами.
- **Случайность — с явным зерном.** `Unity.Mathematics.Random` (структура, работает в Burst, своё зерно на систему) вместо статического `UnityEngine.Random`. Статический генератор — общий на весь процесс, любой чужой вызов ломает воспроизводимость.
- **Кинематические `Rigidbody`** (если они всё же нужны на трафике ради триггеров) двигать через `MovePosition`/`MoveRotation` в `FixedUpdate`, а не присваиванием `transform.position`.
- **Интерполяция.** `Rigidbody.interpolation = RigidbodyInterpolation.Interpolate` — только машине игрока и только одному-двум объектам: это не бесплатно.
- **Межплатформенный детерминизм на float не гарантирован** (разные CPU/компиляторы дают разный результат). Внутри одной машины повторяемость достижима, «сетевой» детерминизм — нет; для этого существует Unity Physics/Havok в DOTS.

---

## 9. Рендеринг сотен объектов

- **GPU Resident Drawer** (URP, Unity 6): автоматически прогоняет `MeshRenderer` через `BatchRendererGroup` и режет draw call'ы. Требования: **Rendering Path = Forward+**, графическое API с compute-шейдерами (кроме OpenGL ES), у объекта есть `MeshRenderer`. Включение: Project Settings > Graphics > Shader Stripping > **BatchRendererGroup Variants = Keep All**; в URP Asset — SRP Batcher включён, **GPU Resident Drawer = Instanced Drawing**; в Universal Renderer — **Forward+**.
  Побочные эффекты: дольше сборка билда; рекомендуется **выключить Static Batching** в Player Settings. В Frame Debugger батчи называются `Hybrid Batch Group`. Есть встроенный GPU occlusion culling.
- **Не путать с галочкой «GPU Instancing» на материале.** Документация Unity 6.3: в URP/HDRP рекомендуется SRP Batcher, а *«GPU instancing works only if you disable the SRP Batcher»*. То есть старый совет «поставьте галку Enable GPU Instancing на материале» в URP по умолчанию не работает.
- **Пешеходы дороже машин.** `SkinnedMeshRenderer` (скиннинг — деформация меша по скелету) — самая дорогая часть толпы. Меры: мало уникальных силуэтов, `Animator.cullingMode`, агрессивный `LODGroup`, для дальних — не-скиннованные прокси. Полноценная альтернатива (запечённая в текстуру анимация, VAT) — отдельная большая тема, в демо оправдана только если толпа станет главным номером программы.

---

## 10. Готовые решения и разборы

| Проект | Что показывает | Статус и лицензия |
|---|---|---|
| [Unity AI Navigation Samples](https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/Samples.html) | Sliding Window Infinite/Terrain, Dungeon, Cornering Speed Control | ставится из Package Manager, самый актуальный источник |
| [Unity-Technologies/EntityComponentSystemSamples](https://github.com/Unity-Technologies/EntityComponentSystemSamples) | эталонные примеры ECS | официальный, поддерживается |
| [Unity-Technologies/megacity-metro](https://github.com/Unity-Technologies/megacity-metro) | DOTS-город с трафиком и 128+ игроками | активен (апрель 2026), лицензия нестандартная — читать перед копированием |
| [maajor/OSMTrafficSim](https://github.com/maajor/OSMTrafficSim) | 25k машин + 10k пешеходов на ECS, BVH для «общения» машин, генерация графа дорог из OpenStreetMap | Unity 2023.1.20, Entities 1.0.6 — **не Unity 6**. **GPL‑3.0**: код нельзя копировать в проект с другой лицензией, только читать как разбор |
| [Kink3d/SimpleTraffic](https://github.com/Kink3d/SimpleTraffic) | трафик на NavMesh Components | MIT, но 2019 год: построен на устаревших скриптах `NavMeshComponents` |
| [mchrbn/unity-traffic-simulation](https://github.com/mchrbn/unity-traffic-simulation) | сегменты дорог, перекрёстки со стопами и светофорами, смена полос | 2022 год, **лицензия не указана** → использовать код нельзя, только идеи |

---

## Антипаттерны

1. **`NavMeshAgent` на машинах.** Самый частый совет в туториалах и самый вредный для нашей задачи: агент не знает полос, срезает углы, не умеет задний ход, а сотня агентов с avoidance стоит дороже сотни точек на сплайне.
2. **`NavMeshAgent` + не-кинематический `Rigidbody` на одном объекте.** Документация Unity: это гонка — оба компонента двигают объект. Нужен `Rigidbody` ради триггеров? Ставим `Is Kinematic = true`.
3. **`NavMeshAgent` + `NavMeshObstacle` на одном объекте.** Агент начинает избегать сам себя; с включённым carve — постоянно «выпрыгивает» из прорезанной дыры.
4. **`Animator` с Root Motion поверх `NavMeshAgent`.** Обратная связь, которую невозможно отладить. Либо агент ведёт, анимация следует (`NavMeshAgent.velocity` → параметры аниматора), либо наоборот через `updatePosition = false` + `nextPosition`.
5. **`SetDestination` каждый кадр для каждого агента.** Каждый вызов ставит запрос на асинхронный поиск пути. Пересчитывать по событию или раз в N кадров, разнося агентов по кадрам.
6. **Carve у движущихся препятствий.** Прорезание навмеша — дорогая операция с задержкой в один кадр. Для движущегося — только локальное избегание.
7. **Весь трафик под одним родительским `GameObject`.** Красиво в Hierarchy, но `IJobParallelForTransform` обрабатывает одну иерархию одним потоком — параллелизм исчезает.
8. **`Instantiate`/`Destroy` в кадре вместо пула.** Аллокации → работа сборщика мусора → фризы. При этом «прогрев» пула на старте так же обязателен, как сам пул.
9. **«Сразу на ECS, чтобы потом не переписывать».** Для витрины возможностей Unity это обмен наглядности и скорости итераций на производительность, которая не нужна.
10. **Устаревшие имена API из туториалов 2019–2022:** `Rigidbody.velocity` → `linearVelocity`; `FindObjectOfType<T>()` → `FindFirstObjectByType<T>()`; `OffMeshLink` → `NavMeshLink`; вкладка Bake в окне Navigation → компонент `NavMeshSurface`; флаг «Navigation Static» → компонент `NavMeshModifier`.
11. **Галочка «Enable GPU Instancing» на материале в URP.** Не даёт эффекта, пока включён SRP Batcher (а он включён по умолчанию и нужен). Правильный путь в Unity 6 — GPU Resident Drawer.
12. **Static Batching вместе с GPU Resident Drawer.** Документация Unity 6.3 прямо советует выключить Static Batching для ускорения GRD.
13. **Свой «Update Manager» как первая оптимизация.** Он снимает накладные расходы на вызовы `Update`, но не решает главного — данные всё равно разбросаны по куче. В Unity 6 тот же бюджет времени лучше вложить в Burst-джобу.
14. **Логика трафика в `FixedUpdate` «чтобы было точнее».** Трафик — не физика; лишние прогоны при просадке FPS только удорожают кадр.

---

## Чек-лист

- [ ] Дорожная сеть описана данными (полосы-сплайны + граф перекрёстков), а не расставлена руками в сцене.
- [ ] Параметры трафика (плотность, лимиты скорости, дистанции, фазы светофоров) вынесены в `ScriptableObject`.
- [ ] Логика движения — обычные C#-классы/структуры, `MonoBehaviour` только как «оболочка»: это и EditMode-тесты, и путь к джобам без переписывания.
- [ ] Ни одного `GetComponent`, `Find*`, `Camera.main`, LINQ и `new` в `Update`/`FixedUpdate`.
- [ ] Спавн и деспавн — через `ObjectPool<T>`, пул прогрет на загрузке, бюджет спавна на кадр ограничен.
- [ ] Кольцо спавна с гистерезисом, спавн только вне поля зрения камеры.
- [ ] Трафик кинематический; `Rigidbody`-симуляция — только у машины игрока.
- [ ] Пешеходы: `obstacleAvoidanceType` понижен для массовки, `avoidancePriority` расставлен (игрок — самый приоритетный).
- [ ] `NavMesh.pathfindingIterationsPerFrame` и `avoidancePredictionTime` выставлены осознанно, а не по умолчанию.
- [ ] Runtime-выпекание идёт через `UpdateNavMesh(...)`/`UpdateNavMeshDataAsync`, `Override Tile Size` уменьшен.
- [ ] `CullingGroup` разводит агентов по режимам near/mid/far; `Animator.cullingMode` настроен; на префабах есть `LODGroup`.
- [ ] Светофоры тикает одна система, машины читают общий массив состояний.
- [ ] Перекрёстки разруливаются резервированием слотов, а не столкновениями коллайдеров.
- [ ] Если понадобились рейкасты — они пакетные (`RaycastCommand`/`SpherecastCommand`), а не поштучные.
- [ ] GPU Resident Drawer включён: Forward+, BatchRendererGroup Variants = Keep All, Static Batching выключен.
- [ ] Замер до и после: CPU Usage в Profiler, кадры Frame Debugger (`Hybrid Batch Group`), число draw call'ов.
- [ ] Модули `Traffic`/`Pedestrians` изолированы своими `.asmdef` и не зависят от `UI`.

---

## Видео и доклады

- AI Navigation 2.0 — NavMesh basics — Unity (официальный) — https://www.youtube.com/watch?v=SMWxCpLvrcc — базовая настройка `NavMeshSurface` в Unity 6, с нуля.
- AI Navigation 2.0 — NavMesh links and obstacles — Unity — https://www.youtube.com/watch?v=vU6fCMC_IXA — `NavMeshLink`, препятствия, связывание нескольких поверхностей.
- AI Navigation 2.0 — Runtime NavMesh surfaces — Unity — https://www.youtube.com/watch?v=UGh4VSeKPNA — динамическое создание навмеша при спавне уровня, стоимости областей.
- Tapping the Entity Component System for Cities: Skylines II | Unite 2024 — Unity — https://www.youtube.com/watch?v=nEkIyWhvq3o — как городской симулятор с массовым трафиком устроен на ECS; лучший ориентир по масштабу.
- Using DOTS to optimize GameObject gameplay: A case study from Survival Kids | Unite 2025 — Unity — https://www.youtube.com/watch?v=ZkvK0mX-id4 — гибрид: обычные `GameObject` + джобы, ровно наш «уровень B».
- Connecting the DOTS: Let's make a game with Entities | Unite 2024 — Unity — https://www.youtube.com/watch?v=gqJlQJn0N2g — актуальный обзор Entities 1.x, чтобы принять решение «нужен/не нужен».
- Unity ECS for mobile: Metropolis Traffic Simulation — Unite Copenhagen — Unity — https://www.youtube.com/watch?v=iCnYm7kRC1g — разбор именно трафика на ECS: пути, детекция столкновений. Старый (2019), API устарел, ценна архитектура.
- How to use the Job System + Burst with Game Objects in Unity! (2020 — Unity 6) — Dev Dunk — https://www.youtube.com/watch?v=1VZaW4_quzI — джобы поверх обычных `GameObject`, без ECS.
- Don't Let Your Nav Mesh Update Strategy RUIN Your Game's Performance — git-amend — https://www.youtube.com/watch?v=vNDMwXNfmrw — стратегии обновления навмеша в рантайме и их цена.
- Runtime NavMesh Updates in Performance Critical Contexts — LlamAcademy — https://www.youtube.com/watch?v=l1rROE06BRw — практические замеры пересборки навмеша.
- How to get started with the splines package — Unity — https://www.youtube.com/watch?v=IJbH5OZa_is — официальное введение в Splines.
- How To Build Roads Procedurally In Unity with the Splines Package — Game Dev Guide — https://www.youtube.com/watch?v=ZiHH_BvjoGk — генерация дорожной геометрии по сплайну (Splines 1.x, идея актуальна).
- Graphics rendering: Getting the best performance with Unity 6 | Unite 2024 — Unity — https://www.youtube.com/watch?v=Oc6T4hh5gaI — GPU Resident Drawer и occlusion culling из первых рук.
- Optimizing smarter, not harder with Unity's performance tools | Unite 2025 — Unity — https://www.youtube.com/watch?v=S4xF-eE1pBg — как мерить, прежде чем оптимизировать.
- Level up your code with game programming design patterns: Object pool — Unity — https://www.youtube.com/watch?v=U08ScgT3RVM — паттерн пула в изложении Unity.

---

## Источники

Все ссылки проверены 2026‑08‑19.

- https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.ai.navigation.html — AI Navigation 2.0.14 для 6000.3
- https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/NavInnerWorkings.html — A\*, RVO, вокселизация, размер вокселя
- https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/NavMeshAgent.html — свойства агента, цена avoidance quality
- https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/NavMeshSurface.html — Object Collection, Advanced Settings, tile/voxel size
- https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/NavMeshLink.html — свойства `NavMeshLink`
- https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/AboutObstacles.html — carving, `Carve Only Stationary`
- https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/MixingComponents.html — сочетание агента с Rigidbody/Animator/Obstacle
- https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/AreasAndCosts.html — области и стоимости пути
- https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/UpgradeGuide.html — переход с legacy-навигации, устаревание `OffMeshLink`
- https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/NavigationWindow.html — окно Navigation в 2.x (только Agents/Areas)
- https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/Samples.html — сцены-примеры, включая Sliding Window
- https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/api/Unity.AI.Navigation.NavMeshSurface.html — `BuildNavMesh()`, `UpdateNavMesh()` → `AsyncOperation`
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AI.NavMesh.html — статические свойства и методы NavMesh
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AI.NavMesh-pathfindingIterationsPerFrame.html — дефолт 100, диапазон 50–500
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AI.NavMesh-avoidancePredictionTime.html — дефолт 2.0, диапазон 0.5–5.0
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AI.ObstacleAvoidanceType.html — уровни качества избегания
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AI.NavMeshAgent.html — API агента
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AI.NavMeshBuilder.html — `UpdateNavMeshDataAsync`, `CollectSources`
- https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.splines.html — Splines 2.9.0 для 6000.3
- https://docs.unity3d.com/Packages/com.unity.splines@2.9/api/UnityEngine.Splines.NativeSpline.html — job-совместимость
- https://docs.unity3d.com/Packages/com.unity.splines@2.9/api/UnityEngine.Splines.SplineContainer.html — Evaluate/CalculateLength
- https://docs.unity3d.com/Packages/com.unity.splines@2.9/api/UnityEngine.Splines.SplineUtility.html — `ConvertIndexUnit`, `GetNearestPoint`, `GetPointAtLinearDistance`, `AddSpline`
- https://docs.unity3d.com/Packages/com.unity.splines@2.9/api/UnityEngine.Splines.PathIndexUnit.html — Distance / Knot / Normalized
- https://docs.unity3d.com/Packages/com.unity.splines@2.9/manual/animate-component.html — `SplineAnimate`
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Jobs.IJobParallelForTransform.html — пример, `ScheduleReadOnlyByRef`, оговорка про иерархию
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/RaycastCommand.html — пакетные рейкасты в джобе
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Pool.ObjectPool_1.html — `ObjectPool<T>`, потоконебезопасность
- https://docs.unity3d.com/6000.3/Documentation/Manual/performance-reusable-code.html — пулинг и сброс состояния
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/CullingGroup.html — distance bands и колбэк смены состояния
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AnimatorCullingMode.html — `CullUpdateTransforms`, `CullCompletely`
- https://docs.unity3d.com/6000.3/Documentation/Manual/fixed-updates.html — `FixedUpdate` и фиксированный шаг
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rigidbody.html — `linearVelocity`, `interpolation`, `MovePosition`
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/gpu-resident-drawer.html — требования и настройка GPU Resident Drawer
- https://docs.unity3d.com/6000.3/Documentation/Manual/gpu-instancing-enable.html — GPU Instancing работает только при выключенном SRP Batcher
- https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.entities.html — Entities 1.4.8 для 6000.3
- https://docs.unity3d.com/Packages/com.unity.entities@1.4/manual/job-overhead.html — накладные расходы планирования джобов
- https://docs.unity3d.com/Packages/com.unity.entities@1.4/manual/ecs-packages.html — состав DOTS
- https://docs.unity3d.com/Packages/com.unity.entities.graphics@1.4/manual/requirements-and-compatibility.html — только URP Forward+/HDRP, без Web
- https://unity.com/ecs — позиционирование ECS от Unity
- https://discussions.unity.com/t/new-ai-navigation-2-0-video-tutorials-series/1565073 — официальная серия видео + реплики о Jobs/Burst и NavMesh (форум, не документация)
- https://github.com/maajor/OSMTrafficSim — разбор ECS-трафика, 25k машин, Unity 2023.1, GPL‑3.0
- https://github.com/Unity-Technologies/megacity-metro — DOTS-город от Unity
- https://github.com/Kink3d/SimpleTraffic — трафик на NavMesh Components (MIT, 2019)
- https://github.com/mchrbn/unity-traffic-simulation — сегменты, перекрёстки, светофоры (лицензия не указана)

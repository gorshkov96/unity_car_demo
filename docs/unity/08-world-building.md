# Построение 3D-мира: дороги, окружение, процедурная сборка сцены

Что использовать для карты, дорог и окружения в Unity 6.3, что делать кодом, а что руками, и какие советы 2019–2022 годов сегодня уже вредны. Заглядывать сюда перед тем, как строить карту, писать генератор мира или трогать настройки статики и батчинга.

---

## TL;DR — решения для нашего проекта

1. **Дороги — пакет Splines (`com.unity.splines`, в Unity 6.3 версия 2.9).** Не писать свой сплайн-редактор. С версии 2.7.1 (октябрь 2024) у `SplineExtrude` есть встроенный профиль сечения **Road** — плоское полотно с «юбками» по краям. Все туториалы, где сначала объясняют «Unity умеет только трубу, поэтому пишем свой экструдер», описывают состояние пакета до осени 2024.
2. **Меш из сплайна строим кодом через `SplineMesh.Extrude(spline, mesh, new ExtrudeSettings<ExtrusionShapes.Road>(...))`**, а не компонентом в инспекторе. Это даёт нам ту самую «сцена как код», о которой договорились: дорога описывается ScriptableObject-конфигом плюс списком точек, а не ручным перетаскиванием.
3. **Блокаут окружения — ProBuilder 6.1** (моделирование прямо в редакторе Unity). **ProGrids и Polybrush не ставить**: их нет в списке поддерживаемых пакетов Unity 6.3, а сетка и привязка (snap — «прилипание» к узлам сетки) давно встроены в Scene view.
4. **Terrain (ландшафт) нам не нужен.** Unity официально заявила, что Terrain будет заменён новой Worldbuilding System, которой нет в Unity 6 и которая появится только в следующем поколении движка. Городская карта из мешей и сплайнов и наглядней, и дешевле.
5. **В URP на Unity 6 включить SRP Batcher + GPU Resident Drawer и ВЫКЛЮЧИТЬ Static Batching** (Project Settings > Player > Other Settings). Это прямая рекомендация документации: статический батчинг несовместим с GPU Resident Drawer. Совет «пометь всё Static, чтобы работал батчинг» — из эпохи Built-in RP.
6. **Флаг `Static` всё равно ставим — но ради другого:** запекание света (ContributeGI), окклюжн-куллинг (Occluder/Occludee) и отражения. Ставить из кода через `GameObjectUtility.SetStaticEditorFlags`.
7. **Физматериал теперь `PhysicsMaterial`** (с буквой «s»), меню `Assets > Create > Physics Material`. Старое имя `PhysicMaterial` помечено obsolete. Важная оговорка: **`WheelCollider` полностью игнорирует физматериал** — у него собственная модель трения на кривых.
8. **Коллайдер дороги — `MeshCollider` без Convex** (статичный, произвольная геометрия), бордюры — невидимые `BoxCollider`. Convex-меш ограничен 255 треугольниками, для дороги это не вариант.
9. **Масштаб: 1 юнит = 1 метр.** Модульная сетка 0.25 / 1 / 2 / 4 м, полоса дороги 3.5 м, легковая машина ≈ 1.8 × 4.5 м. Встроенный Cube — ровно 1×1×1, Plane — 10×10 и 200 треугольников.
10. **Addressables (система адресуемых ассетов, 4.0 в Unity 6.3) сейчас не подключать.** Для одной статичной карты хватит одной сцены плюс двух-трёх аддитивных подсцен. Addressables добавляет build-пайплайн, который нечем окупить.

---

## 1. Карта инструментов Unity 6.3

| Задача | Инструмент | Версия в Unity 6.3 | Комментарий |
|---|---|---|---|
| Дороги, бордюры, пути ИИ | Splines `com.unity.splines` | 2.9 | Профили сечения Circle / Square / Road / Spline Profile |
| Блокаут зданий, тротуаров, рампы | ProBuilder `com.unity.probuilder` | 6.1 | Моделирование в редакторе, экспорт в OBJ/FBX |
| Ландшафт | Terrain (встроенный) + `com.unity.terrain-tools` | 5.3 | Легаси-система, будет заменена |
| Навигация ИИ-трафика | `com.unity.ai.navigation` | 2.0 | `NavMeshSurface`, запекание из иерархии |
| Стриминг контента | `com.unity.addressables` | 4.0 | Не сейчас |
| Разметка на дороге | URP Decal Renderer Feature | входит в URP | Проекция материала на геометрию |

Ссылки на все пакеты — в разделе «Источники».

---

## 2. Splines: дороги и пути

### 2.1 Что есть в пакете

- `SplineContainer` — MonoBehaviour-контейнер, держит один или несколько `Spline`. **Все методы и свойства сплайна работают в локальном пространстве контейнера**, а методы самого `SplineContainer` (`EvaluatePosition`, `CalculateLength`) — в мировом. Это регулярный источник багов «объекты уехали».
- `Spline` — чистая C#-структура данных из `BezierKnot` (узел кривой Безье: позиция + два касательных вектора).
- `SplineExtrude` — компонент, генерирующий меш вдоль сплайна.
- `SplineInstantiate` — компонент, расставляющий префабы вдоль сплайна.
- `SplineAnimate` — двигает объект вдоль сплайна (пригодится для движущихся объектов вокруг машины).
- `SplineMesh` + `ExtrudeSettings<T>` + `ExtrusionShapes.*` — низкоуровневый API экструзии, доступный из кода.

### 2.2 Профили сечения (важно, это новое)

`SplineExtrude` в инспекторе даёт **Shape Extrude > Type**: `Circle`, `Square`, `Road`, `Spline Profile`. Добавлено в Splines 2.7.1 (октябрь 2024). `Road` документирован как «плоское сечение с небольшим бортиком» — буквально то, что нужно для дороги, которая должна аккуратно ложиться на неровную поверхность. `Spline Profile` позволяет взять **другой сплайн как шаблон сечения** — так делается тротуар с бордюром, отбойник или рельс.

Отдельный полезный приём из документации: один сплайн можно назначить источником (`Source Spline Container`) сразу нескольким объектам. Правим осевую линию дороги — тротуар и отбойник перестраиваются сами.

### 2.3 Генерация дороги из C#

```csharp
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class RoadMeshBuilder : MonoBehaviour
{
    [SerializeField] private SplineContainer _source;
    [SerializeField] private float _halfWidth = 3.5f;   // метры от осевой до края
    [SerializeField] private int _segments = 256;       // колец вдоль сплайна

    private Mesh _mesh;

    private void Awake()
    {
        // 16-битный индексный буфер держит только 65535 вершин — длинной дороге мало
        _mesh = new Mesh { name = "Road", indexFormat = IndexFormat.UInt32 };
        GetComponent<MeshFilter>().sharedMesh = _mesh;
    }

    public void Rebuild()
    {
        var settings = new ExtrudeSettings<ExtrusionShapes.Road>(
            segments: _segments,
            capped: false,
            range: new float2(0f, 1f),   // нормализованный диапазон, НЕ проценты
            radius: _halfWidth,
            shape: new ExtrusionShapes.Road());

        SplineMesh.Extrude(_source.Spline, _mesh, settings);
    }
}
```

Компонентный вариант (когда меш нужен в редакторе) — `SplineExtrude` со свойствами `Container`, `Radius`, `SegmentsPerUnit`, `Sides`, `Capped`, `FlipNormals`, `Range`, `RebuildFrequency`, `RebuildOnSplineChange`, `targetMesh` и методом `Rebuild()`.

### 2.4 Равномерная расстановка объектов вдоль дороги

Параметр `t` в `EvaluatePosition(t)` — это **не** доля длины: кривая Безье проходится неравномерно, и на повороте один и тот же шаг `t` даёт меньший путь. Фонари, расставленные «в лоб» по `t`, собьются в кучу на поворотах. Правильно — перевести метры в нормализованный `t`:

```csharp
var spline = container.Spline;
float length = spline.GetLength();                 // локальные единицы контейнера

for (float d = 0f; d < length; d += 25f)           // фонарь каждые 25 м
{
    float t = SplineUtility.ConvertIndexUnit(
        spline, d, PathIndexUnit.Distance, PathIndexUnit.Normalized);

    float3 position = container.EvaluatePosition(t);   // мировые координаты
    float3 tangent  = container.EvaluateTangent(t);
    // ... спавн префаба
}
```

`PathIndexUnit` имеет ровно три значения: `Distance` (единицы мира), `Knot` (индекс узла + дробная часть), `Normalized` (0…1). Для последовательного обхода есть `SplineUtility.GetPointAtLinearDistance`, для поиска ближайшей точки трассы к машине (респавн, ИИ-водитель, прогресс по кругу) — `SplineUtility.GetNearestPoint`.

### 2.5 Подводные камни Splines

- **Переполнение 16-битного индекса.** До версии 2.9.0 `SplineExtrude` строил неправильный меш, если число индексов превышало `UInt16.MaxValue`. Симптом — длинная дорога с высоким `SegmentsPerUnit` внезапно рвётся. Лечение: Splines 2.9+ и `indexFormat = IndexFormat.UInt32` на своём меше.
- **`Range`: документация противоречит сама себе.** Справочник компонента говорит «0 — первая точка, 100 — последняя», API-документация `SplineExtrude.Range` и `ExtrudeSettings.range` — «нормализованные значения от 0 до 1», а пример «Extrude a spline at runtime» передаёт `new float2(0, 100)`. Считать авторитетным API-описание (0…1) и проверить в редакторе.
- **Меш из `new Mesh()` не является ассетом** — живёт в памяти и исчезнет при перезагрузке сцены. Сохранять через `AssetDatabase.CreateAsset(mesh, path)`.
- **`RebuildOnSplineChange` (Auto Refresh Generation) в рантайме — перестройка меша каждый кадр.** Для статичной карты выключать.
- **Локальное против мирового.** `SplineMesh.Extrude` работает в пространстве сплайна: при ненулевом Transform контейнера меш надо класть на тот же GameObject, иначе дорога уедет вдвое.
- **`SplineInstantiate` — не пул объектов**, он инстанцирует GameObject'ы. Для десятков фонарей нормально, для леса — нет.

---

## 3. ProBuilder: блокаут уровня

**Блокаут (blockout, greybox, whitebox)** — черновая геометрия из простых форм, по которой проверяют масштаб и «читаемость» уровня до того, как появится настоящий арт. Для нашего проекта это основной способ сделать здания, тротуары, эстакады и препятствия без внешнего 3D-редактора.

Практика, подтверждённая официальным e-book Unity по левел-дизайну:

- **Осмысленные имена с размерами**: `P_GenericWall_200x300cm` (префикс `P_` — префаб, дальше что это, дальше габариты). Через месяц в иерархии из 200 кубов иначе ничего не найти.
- **Цветные материалы как семантика**: играбельная зона / непроходимое / интерактивное. Цвет читается быстрее, чем имя.
- **При установке ProBuilder импортировать support-файлы для URP**, иначе объекты могут не отрисоваться (материалы идут под Built-in RP).
- **Auto UV Mode > Fill Mode = Tile** — текстура тайлится по мировому размеру грани и не растягивается при масштабировании блока. Для блокаута это единственный вменяемый режим: растянутая клетка на стене сразу врёт про масштаб.
- **`ProBuilderize`** превращает обычный меш в редактируемый ProBuilder-меш, **`Export`** выгружает обратно в OBJ/FBX, **Lightmap UVs** генерирует недостающие UV2 для запекания света.
- В ProBuilder 6.0.6 специально запретили «пробилдеризовать» объект с `isPartOfStaticBatch = true` — раньше это роняло редактор.

**Сетка и привязка встроены**: Scene view grid snapping, оверлей Grid and Snap, инкрементальные move/rotate/scale. Пакета ProGrids, который раньше советовали для этого, нет в списке released packages Unity 6.3, и его документация недоступна. Polybrush там же.

---

## 4. Terrain: почему мы его не берём

Terrain — встроенная система ландшафта: карта высот, слои текстур, кисти для травы и деревьев, LOD «из коробки». Terrain Tools 5.3 добавляет эрозию, скульптинг и Terrain Toolbox (пакетное создание тайлов, импорт/экспорт карт высот). Но:

- В сентябре 2024 продакт-менеджер Unity по worldbuilding официально написал: разрабатывается **новая Worldbuilding System, которая заменит Terrain**; в Unity 6 её нет, ориентир — следующее поколение движка. Причина — «в системе было много технических решений, которые нельзя было модернизировать».
- Наша карта — город с дорогами. Дорога, вписанная в Terrain, требует деформации карты высот под сплайн (`TerrainData.SetHeights`) и борьбы с Z-файтингом на стыке меша и ландшафта. Меш-карта этого не требует вообще.
- Terrain плохо ложится на принцип «сцена как код»: `TerrainData` — бинарный ассет.

Если Terrain всё же понадобится (холмы на фоне): включать **Draw Instanced** в Terrain Settings; **Detail Scatter Mode** — `Coverage` (по плотности) или `Instance Count` (по разрешению карты деталей), **Detail Resolution Per Patch** рекомендовано 16; **Heightmap Resolution** обязана быть степенью двойки плюс единица (513, 1025). В Unity 6.3 появилась кастомизация террейна через Shader Graph: типы материала **URP/HDRP Terrain Lit**, нода **Terrain Texture** и шаблон Terrain в новом браузере шаблонов.

---

## 5. Процедурная сборка сцены из C# и Editor-скриптов

Это ключевой раздел для нашего проекта.

### 5.1 Две модели генерации — выбрать осознанно

| | Рантайм-генерация | Editor-time «запекание» в сцену |
|---|---|---|
| Когда работает | в Play Mode, каждый запуск | один раз, результат лежит в `.unity` |
| Запечённый свет и occlusion culling | невозможны — оба пекутся в редакторе | да |
| Правка руками потом | нет | да |
| Git-diff | пустой | огромный YAML |

**Рекомендация для нас: editor-time.** Пишем `[MenuItem]`-инструмент, который строит мир в открытой сцене, и держим генерацию детерминированной, чтобы результат можно было пересобрать. Документация по окклюжн-куллингу подтверждает прямо: «если ваш проект генерирует геометрию сцены в рантайме, встроенный occlusion culling для него не подходит».

### 5.2 Правильный набор Editor-API

```csharp
#if UNITY_EDITOR
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WorldBaker
{
    [MenuItem("Tools/World/Bake Street Props")]
    private static void Bake()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/StreetLamp.prefab");

        var root = new GameObject("StreetProps");
        Undo.RegisterCreatedObjectUndo(root, "Bake Street Props");

        var rng = new Random(0xC0FFEEu);   // Unity.Mathematics.Random, seed обязан быть != 0

        for (int i = 0; i < 64; i++)
        {
            // именно InstantiatePrefab, а не Object.Instantiate:
            // сохраняется связь с префабом, правки префаба долетают до сцены
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.scene);
            go.transform.SetParent(root.transform, worldPositionStays: false);
            go.transform.position = new Vector3(rng.NextFloat(-50f, 50f), 0f, rng.NextFloat(-50f, 50f));

            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.ContributeGI |
                StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.ReflectionProbeStatic);

            Undo.RegisterCreatedObjectUndo(go, "Bake Street Props");
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
    }
}
#endif
```

Почему именно так:

- **`PrefabUtility.InstantiatePrefab`** сохраняет связь с префабом; `Object.Instantiate` в редакторе даёт отвязанную копию, и правка префаба до неё не дойдёт.
- **`Undo.RegisterCreatedObjectUndo`** — без него Ctrl+Z не откатит генерацию. Документация просит порядок: создать объект → зарегистрировать → модифицировать.
- **`EditorSceneManager.MarkSceneDirty`** — иначе Unity не считает сцену изменённой и не предложит сохранить.
- **`ObjectFactory.CreateGameObject(name, types...)`** — альтернатива `new GameObject()`, применяет пресеты компонентов, как при создании через меню.
- **Детерминизм.** `Unity.Mathematics.Random` — xorshift с 32-битным состоянием, **сид не может быть нулём**. Сид держим в ScriptableObject-конфиге.
- **Массовые операции с ассетами** оборачиваем в `AssetDatabase.StartAssetEditing()` / `StopAssetEditing()` в `try/finally` — иначе каждый созданный ассет вызывает отдельный импорт.

### 5.3 Префабы и процедурные меши кодом

```csharp
// префаб из объекта в сцене
string path = AssetDatabase.GenerateUniqueAssetPath("Assets/_Project/Prefabs/RoadSegment.prefab");
PrefabUtility.SaveAsPrefabAsset(instanceInScene, path);

// меш дороги: UV2 для лайтмапа + сохранение как ассета
Unwrapping.GenerateSecondaryUVSet(mesh);
AssetDatabase.CreateAsset(mesh, "Assets/_Project/Meshes/Road.asset");
AssetDatabase.SaveAssets();
```

`PrefabUtility.CreatePrefab` устарел; используем `SaveAsPrefabAsset` либо `SaveAsPrefabAssetAndConnect` (сохранить и связать объект в сцене с новым префабом). При перезаписи существующего префаба Unity сопоставляет объекты **по именам** — все GameObject внутри должны иметь уникальные имена, иначе ссылки поедут.

`Unwrapping.GenerateSecondaryUVSet` — редакторный аналог галки **Generate Lightmap UVs** в настройках импорта модели: считает `Mesh.uv2`. Возвращает `false`, если развёртка потребовала больше вершин, чем влезает в 16-битный индекс, — ещё одна причина заранее ставить `IndexFormat.UInt32`. Требования к хорошей развёртке из документации: в пределах [0,1]×[0,1], без пересечений граней, с зазором между «островами», без сильных перекосов углов и относительных площадей.

---

## 6. Масштаб, единицы и модульность

**Базовое правило: 1 юнит Unity = 1 метр.** Документация импорта моделей формулирует это прямо: «физическая система Unity ожидает, что 1 м в игровом мире — это 1 юнит в импортируемом файле». Если модель из Blender/Maya приходит в сантиметрах — `Scale Factor` или `Convert Units` в Model Import Settings, а не масштаб на Transform.

Точные размеры встроенных примитивов (полезно как линейка):

| Примитив | Габарит | Нюанс |
|---|---|---|
| Cube | 1 × 1 × 1 | Идеальный эталон масштаба рядом с импортом |
| Sphere | диаметр 1 | |
| Cylinder | высота 2, диаметр 1 | **Коллайдер — капсула**, цилиндрического примитива-коллайдера в Unity нет |
| Capsule | высота 2, диаметр 1 | |
| Plane | 10 × 10, 200 треугольников | 200 треугольников на плоский пол — расточительство |
| Quad | 1 × 1, 2 треугольника | Односторонний |

Рабочие числа для автомобильной сцены: полоса 3.5 м, тротуар 2 м, легковой автомобиль ≈ 1.8 × 4.5 м, высота этажа 3 м, столб освещения 8–10 м. Модульная сетка — 0.25 м для мелочей, 1 / 2 / 4 м для зданий. Grid Snapping в Scene view настраивается под этот шаг.

**Тайлинг.** Для модульных наборов текстура должна тайлиться по мировому размеру, а не по UV объекта. В ProBuilder — Auto UV `Fill Mode: Tile`. Для обычных мешей — Tiling в материале URP Lit, а для процедурной геометрии без UV — triplanar-проекция в Shader Graph.

**Разметка на дороге — декалями, а не геометрией.** URP Decal Renderer Feature проецирует материал на существующую геометрию. Ограничения из документации: не работает по прозрачным поверхностям; декали **не поддерживают SRP Batcher** (используют material property blocks), поэтому все декали делаем одним материалом с атласом и включённым GPU Instancing на нём — тогда они схлопываются в один инстансированный draw call.

---

## 7. Коллайдеры и физматериалы

**Имя класса — `PhysicsMaterial`**, а не `PhysicMaterial`. Старое имя и `PhysicMaterialCombine` помечены obsolete и автоматически апгрейдятся в редакторах новее 2023.3. Меню: `Assets > Create > Physics Material`. Свойства: `dynamicFriction`, `staticFriction`, `bounciness`, `frictionCombine`, `bounceCombine`; по умолчанию оба трения 0.6, bounciness 0, комбинирование Average.

Что вешать на дорогу:

- **Полотно** — `MeshCollider`, **Convex выключен**. Non-convex работает только для статики, но нам того и надо; convex ограничен 255 треугольниками.
- **Бордюры, отбойники, невидимые стены** — `BoxCollider` вдоль сегментов: дешевле меша и предсказуемее в контакте.
- **Cooking Options** у `MeshCollider` по умолчанию все включены (Cook for Faster Simulation, Enable Mesh Cleaning, Weld Colocated Vertices, Use Fast Midphase). Если менять — меш обязан иметь `isReadable = true`, а это лишняя копия в памяти.

**Ловушка с WheelCollider.** Документация Unity 6.3 прямым текстом: `WheelCollider` считает трение колеса отдельно от физического движка и **игнорирует стандартные настройки Physics Material**; он использует slip-based модель с кривыми `Forward/Sideways Friction` (`WheelFrictionCurve`: `extremumSlip`, `extremumValue`, `asymptoteSlip`, `asymptoteValue`). Практический вывод: физматериал на дороге не сделает лёд скользким. Нужен либо raycast-подвеска с ручным применением сил (аркадная модель, рекомендованная в `CLAUDE.md`), либо код, подменяющий `WheelFrictionCurve` по типу покрытия под колесом.

---

## 8. Организация статики: флаги, окклюжн, лайтмапы, батчинг

### 8.1 Static Editor Flags

`StaticEditorFlags` в Unity 6.3 содержит ровно пять флагов: `ContributeGI`, `OccluderStatic`, `OccludeeStatic`, `BatchingStatic`, `ReflectionProbeStatic`. Устанавливать из кода — `GameObjectUtility.SetStaticEditorFlags(go, flags)`. В рантайме менять бессмысленно: «Setting StaticEditorFlags at runtime has no effect on these systems». Навигационной статики в перечислении нет — запекание NavMesh переехало в пакет AI Navigation и работает через `NavMeshSurface` и иерархию объектов.

### 8.2 Батчинг в URP на Unity 6 — здесь всё поменялось

Официальная таблица «Choose a method for optimizing draw calls» для URP: **SRP Batcher — включить**; **GPU Resident Drawer — включить**; BatchRendererGroup API напрямую не трогать; **галку GPU Instancing в материалах — выключить** (плодит варианты шейдера); **Static и Dynamic Batching — выключить**, статический батчинг несовместим с BRG API и GPU Resident Drawer.

Как включить GPU Resident Drawer (инстансированная отрисовка GameObject'ов силами GPU):

1. Project Settings > Graphics > Shader Stripping > **BatchRendererGroup Variants = Keep All**.
2. В URP Asset — включён **SRP Batcher**, там же **GPU Resident Drawer = Instanced Drawing**.
3. В Universal Renderer — **Rendering Path = Forward+**.
4. Project Settings > Player > Other Settings — **выключить Static Batching**.

Ограничения GRD: только Forward+, только графические API с compute-шейдерами (не OpenGL ES), только `MeshRenderer`. Объект несовместим, если использует `MaterialPropertyBlock`, Light Probe Proxy Volume, realtime GI, per-instance колбэки вроде `OnRenderObject`, или двигается между рендерами двух камер. Исключить точечно — компонент **Disallow GPU Driven Rendering**. Побочный эффект: билд собирается дольше, компилируются все BRG-варианты шейдеров.

### 8.3 Окклюжн-куллинг: два разных механизма

- **Классический (запекаемый)**: Window > Rendering > Occlusion Culling, данные пекутся заранее, из кода — `StaticOcclusionCulling.Compute()` / `GenerateInBackground()`. Работает лучше всего там, где мир поделён стенами на «комнаты и коридоры». **Городская сцена с длинными прямыми улицами — не тот случай.** Динамические объекты могут быть скрыты, но сами ничего не закрывают.
- **GPU Occlusion Culling (Unity 6, URP)**: включается галкой **GPU Occlusion** в Universal Renderer и **требует включённого GPU Resident Drawer**. Ничего не пекётся, работает по depth-буферу текущего и предыдущего кадра. Ограничения честно описаны: объект аппроксимируется **ограничивающей сферой**, поэтому длинные тонкие объекты (тот же отбойник вдоль дороги) отсекаются плохо; если окклюзии в сцене мало, накладные расходы могут превысить выигрыш.

Для нашей карты разумно начать с GPU Occlusion Culling и вообще не пеки́ классический.

### 8.4 Лайтмапы и UV

- Запечённые лайтмап-UV лежат в `Mesh.uv2` (шейдерная семантика `TEXCOORD1`, в обиходе «UV1»). Realtime GI берёт `Mesh.uv3`, а при его отсутствии откатывается на `uv2`.
- Для импортированных моделей — галка **Generate Lightmap UVs** в Model Import Settings. Для процедурных мешей — `Unwrapping.GenerateSecondaryUVSet`.
- **Unity 6.3: GPU Lightmapper стал бэкендом запекания по умолчанию** для новых проектов и новых Lighting Settings.
- **Unity 6.3: новый упаковщик лайтмапов на базе xAtlas**, по умолчанию для новых сцен. Пакует реальные формы UV-островов вместо ограничивающих прямоугольников — атлас плотнее. Существующие сцены остаются на старом упаковщике, чтобы не поехала раскладка. Переключается в Lighting window или через `LightingSettings` API. Оговорка из документации: упаковка может быть медленнее и время растёт с размером сцены.

### 8.5 Комбинирование мешей и LOD

`Mesh.CombineMeshes` и `StaticBatchingUtility.Combine` склеивают меши в один. Документация предупреждает: **склеенные меши нельзя отсечь по отдельности** — виден уголок, рисуется вся склейка. Под GPU Resident Drawer ручное комбинирование обычно вредно: ломает и инстансинг, и GPU-окклюжн. Применять только к плотным группам мелочи, которые всегда видны целиком.

LOD в Unity 6 — два независимых механизма. **LOD Group** — классический: отдельные меши-рендереры на каждый уровень, можно упрощать и материалы, и число draw call'ов. **Mesh LOD** (появился в Unity 6.2) — автоматическая генерация уровней **при импорте модели**, все LOD'ы в индексном буфере одного меша; меньше памяти, но не поддерживается Entities Graphics, Particle System, VFX Graph, статическим батчингом и GPU Instancing (везде берётся LOD0), cross-fade требует GRD, сочетать с LOD Group не рекомендуется. Для примитивов и процедурных мешей Mesh LOD не применим — он про импортированные модели.

---

## 9. Сцены и стриминг

**Наш масштаб не требует стриминга.** Разумная раскладка на старте — три сцены: `Boot` (точка входа и менеджеры), `Environment` (карта, дороги, свет — генерируется инструментом), `Gameplay` (машина, трафик, триггеры).

Аддитивная загрузка: `SceneManager.LoadSceneAsync(name, LoadSceneMode.Additive)`. В редакторе — правый клик по сцене в Project > **Open Scene Additive** либо перетаскивание в Hierarchy; Alt/Option + перетаскивание добавляет сцену **без загрузки**. Активная сцена (куда попадают объекты, созданные скриптом) назначается через More-меню > **Set Active Scene** или `SceneManager.SetActiveScene`.

**Список сцен в билде теперь в Build Profiles** (`File > Build Profiles`), а не в старом Build Settings. Профили сохраняются как ассеты и коммитятся в git; каждому можно назначить свой набор сцен.

**Addressables 4.0** знать стоит, подключать сейчас — нет. Он умеет уже не только AssetBundles, но и новую систему **Content Directories**; из свежего — `await handle` напрямую на `AsyncOperationHandle` и `ToAwaitable(CancellationToken)`. Правило из блога Unity, если дело всё же дойдёт: **сделал адресуемой одну сцену — делай адресуемыми все**, иначе получишь дублирование ассетов между бандлами.

---

## 10. Ассеты: почему на старте хватит примитивов

Аргументы по существу, а не «так проще»:

1. **Один материал на всё → инстансинг работает.** GPU Resident Drawer группирует объекты с одинаковым мешем и материалом. Десять типов примитивов с тремя материалами дают почти идеальный батчинг; сорок бесплатных моделей с сорока материалами — не дают ничего.
2. **Наглядность.** Проект — витрина технологий. На сером блокауте видно, что демонстрируется физика, свет или партиклы. На детализированном арте — видно арт.
3. **Нет проблем с лайтмап-UV, LOD, лицензиями и Git LFS.**

Когда примитивов перестанет хватать — бесплатные источники с пригодными лицензиями: **Kenney** (kenney.nl, CC0, есть городские и дорожные наборы), **Poly Haven** (polyhaven.com, CC0 — HDRI, PBR-текстуры, модели), **ambientCG** (PBR-материалы, включая асфальт и бетон), **Quaternius** (low-poly модели), **Unity Asset Store** с фильтром Free — в e-book Unity по левел-дизайну отдельно упомянут **POLYGON Prototype Pack** от Synty с маркерами под блокаут.

Правило: **один стилистический источник на проект**. Реалистичный PBR-асфальт рядом с low-poly домиками — гарантированная визуальная каша.

---

## Антипаттерны

**«Пометь всё как Static — заработает батчинг».** В URP на Unity 6 статический батчинг официально рекомендуется **выключить**: он несовместим с GPU Resident Drawer и BatchRendererGroup. Флаг Static ставим ради GI, окклюжна и отражений, а не ради батчинга.

**«Включи GPU Instancing на всех материалах».** Та же таблица документации: в URP и HDRP галку **Disable**, чтобы не плодить варианты шейдера. Инстансинг обеспечивают SRP Batcher и GRD.

**«Splines умеет только трубу, пиши свой экструдер».** Верно до Splines 2.7.1 (октябрь 2024). Сейчас есть `ExtrusionShapes.Road`, `Square`, `SplineShape` и публичный `SplineMesh.Extrude` с `IExtrudeShape`. Свой экструдер оправдан только под требования, которых нет в `IExtrudeShape`.

**«Ставь ProGrids для сетки и Polybrush для покраски».** Ни того, ни другого нет в списке released packages Unity 6.3. Сетка и снап встроены в Scene view.

**`Object.Instantiate` в Editor-скрипте генерации.** Ломает связь с префабом. Нужен `PrefabUtility.InstantiatePrefab`.

**Генерировать мир в `Start()` и ждать запечённого света и окклюжна.** И лайтмапы, и occlusion culling — предрасчёт в редакторе; документация по occlusion culling говорит про рантайм-геометрию прямым текстом.

**Convex MeshCollider на дороге.** 255 треугольников — жёсткий потолок, и выпуклая оболочка дороги это вообще не дорога. Non-convex `MeshCollider` плюс `BoxCollider` на бордюры.

**Ждать, что физматериал изменит поведение колёс.** `WheelCollider` игнорирует Physics Material.

**`Graphics.DrawMeshInstanced` для растительности.** Метод obsolete, лимит 1023 инстанса за вызов. Актуально: `Graphics.RenderMeshInstanced` / `RenderMeshIndirect` с `RenderParams`, а для обычных GameObject'ов — просто GPU Resident Drawer.

**`Mesh.CombineMeshes` «для оптимизации» по всей сцене.** Убивает поштучное отсечение и инстансинг: склеенное рисуется целиком, даже если видно уголок.

**Ручная правка `.unity` и `.prefab`.** Это YAML с GUID и file ID. Любая генерация — через Editor API, любая ручная работа — в редакторе по чек-листу.

**Параметры карты в полях инспектора отдельных объектов.** Ширина полосы, шаг фонарей, сид — в ScriptableObject, иначе детерминированно пересобрать карту невозможно.

---

## Чек-лист

**Перед началом работ над миром**

- [ ] URP, Rendering Path = **Forward+**; SRP Batcher включён; **GPU Resident Drawer = Instanced Drawing**; BatchRendererGroup Variants = Keep All.
- [ ] Static Batching в Player Settings **выключен**; галка GPU Instancing на материалах **выключена**.
- [ ] Установлены пакеты Splines и ProBuilder. Terrain Tools, Addressables, ProGrids, Polybrush — **нет**.
- [ ] Зафиксированы единицы: 1 юнит = 1 м, шаг сетки 0.25 / 1 / 2 / 4 м, полоса 3.5 м; Grid Snapping настроен на этот шаг.

**Во время генерации мира**

- [ ] Генератор — Editor-скрипт под `[MenuItem]`, а не рантайм.
- [ ] Все параметры (включая сид `Unity.Mathematics.Random`, ≠ 0) — в ScriptableObject.
- [ ] Префабы — через `PrefabUtility.InstantiatePrefab`; каждое создание — `Undo.RegisterCreatedObjectUndo`; в конце — `EditorSceneManager.MarkSceneDirty`.
- [ ] Массовые операции с ассетами обёрнуты в `StartAssetEditing` / `StopAssetEditing` внутри `try/finally`.
- [ ] Процедурные меши: `IndexFormat.UInt32`, `Unwrapping.GenerateSecondaryUVSet`, сохранены как `.asset`.

**После генерации**

- [ ] Статике выставлены `ContributeGI | OccluderStatic | OccludeeStatic | ReflectionProbeStatic`.
- [ ] Дорога — `MeshCollider` без Convex, бордюры — `BoxCollider`; физматериалы созданы как **Physics Material**.
- [ ] Свет запечён (GPU Lightmapper + xAtlas по умолчанию в 6.3), UV2 у статичных мешей есть.
- [ ] Включён **GPU Occlusion** в Universal Renderer; классический occlusion culling **не** пекли.
- [ ] Frame Debugger показывает **Hybrid Batch Group**; SetPass calls в Rendering Statistics измерены до и после.

---

## Видео и доклады

- Fast worldbuilding with Unity's updated Terrain System & ProBuilder — Unity (Unite LA) — https://www.youtube.com/watch?v=XhYHuju5n6M — официальный доклад про связку Terrain + ProBuilder; полезен как обзор философии инструментов, но снят до Unity 6.
- Creating EASY Pathways with Splines | Free Pro Training Session — Unity — https://www.youtube.com/watch?v=6xZs_aeplhA — официальная тренировочная сессия Unity (сентябрь 2025) по путям на сплайнах, самое свежее официальное видео по теме.
- How to get started with the splines package — Unity — https://www.youtube.com/watch?v=IJbH5OZa_is — официальное введение в пакет Splines: узлы, касательные, инструменты.
- Build With SPLINES in UNITY 6 — Make a RACE TRACK in MINUTES! — Synty Studios — https://www.youtube.com/watch?v=XDjmzHPdYBQ — трасса на сплайнах именно в Unity 6; ближайший к нашей задаче кейс.
- How To Build Roads Procedurally In Unity with the Splines Package — Game Dev Guide — https://www.youtube.com/watch?v=ZiHH_BvjoGk — подробный разбор своего инструмента дорог на Spline API; учитывать, что снято до появления профиля Road.
- Make ROADS with Unity's Splines! — AnanDEV — https://www.youtube.com/watch?v=FbSvEieELXo — дорога с кастомным сечением меша вдоль сплайна.
- Procedural Mesh & Animation with the Official Unity Spline Package — LlamAcademy — https://www.youtube.com/watch?v=tZ-eR11cXUg — генерация процедурного меша по сплайну из кода.
- Procedurally Generate Entire Buildings With Unity Splines — Game Dev Guide — https://www.youtube.com/watch?v=FYmudT12NcI — сплайны не только для дорог; тот же паттерн Editor-инструмента.
- [Unity] 2D Curve Editor (E06: road mesh) — Sebastian Lague — https://www.youtube.com/watch?v=Q12sb-sOhdI — как устроен экструдер дороги изнутри (собственный сплайн, не пакет Unity).
- ProBuilder 6 for Unity 6 (плейлист) — Gabriel-Polysketch — https://www.youtube.com/playlist?list=PLrJfHfcFkLM_a0jRCedjV2vJMcCSUHgb- — самый актуальный набор уроков именно по ProBuilder 6.
- Master ProBuilder In Unity 6 With This Easy Beginner's Tutorial! — Cam Ayres — https://www.youtube.com/watch?v=Bfl-V-39JlU — введение с нуля, январь 2025.
- The Fastest Way to Design Levels in Unity 6? (Pro Builder Explained) — DebugDevin — https://www.youtube.com/watch?v=qV2o1D1uWgE — блокаут уровня от установки пакета до готовой геометрии.

---

## Источники

Дата обращения ко всем ссылкам — 2026-08-19.

Мануал Unity 6.3 (6000.3), префикс `https://docs.unity3d.com/6000.3/Documentation/Manual/`:

- `WhatsNewUnity63.html` — xAtlas, GPU Lightmapper по умолчанию, Terrain в Shader Graph; `WhatsNewUnity62.html` — Mesh LOD
- `optimizing-draw-calls-choose-method.html` — таблица выбора метода оптимизации draw call'ов (ключевой источник по батчингу)
- `urp/gpu-resident-drawer.html`, `urp/make-object-compatible-gpu-rendering.html`, `urp/gpu-culling.html` — GPU Resident Drawer и GPU occlusion culling
- `OcclusionCulling.html`, `static-batching.html`, `combining-meshes.html` — классический окклюжн, батчинг, склейка мешей
- `LightingGiUvs.html`, `LightingGiUvs-GeneratingLightmappingUVs.html` — лайтмап-UV
- `LevelOfDetail.html`, `lod/mesh-lod-introduction.html` — LOD Group против Mesh LOD и ограничения Mesh LOD
- `class-MeshCollider.html`, `class-PhysicsMaterial.html`, `wheel-colliders-friction.html` — коллайдеры и физматериалы
- `PrimitiveObjects.html`, `FBXImporter-Model.html`, `GridSnapping.html` — размеры примитивов, масштаб импорта, сетка
- `setupmultiplescenes.html`, `build-profiles.html` — несколько сцен и Build Profiles
- `script-Terrain.html`, `terrain-OtherSettings.html` — Terrain и его настройки
- `urp/renderer-feature-decal.html` — декали в URP; `pack-safe.html` — список released-пакетов 6.3

Scripting API Unity 6.3, префикс `https://docs.unity3d.com/6000.3/Documentation/ScriptReference/`:

- `PhysicsMaterial.html`, `StaticEditorFlags.html`, `GameObjectUtility.SetStaticEditorFlags.html`
- `PrefabUtility.InstantiatePrefab.html`, `PrefabUtility.SaveAsPrefabAsset.html`, `ObjectFactory.CreateGameObject.html`
- `Undo.RegisterCreatedObjectUndo.html`, `SceneManagement.EditorSceneManager.MarkSceneDirty.html`, `AssetDatabase.StartAssetEditing.html`
- `Unwrapping.GenerateSecondaryUVSet.html`, `Mesh-indexFormat.html`, `StaticBatchingUtility.Combine.html`, `StaticOcclusionCulling.Compute.html`
- `Graphics.RenderMeshInstanced.html` и `Graphics.DrawMeshInstanced.html` (последний помечен obsolete)
- `GameObject.CreatePrimitive.html`, `TerrainData.SetHeights.html`

Документация пакетов:

- https://docs.unity3d.com/Packages/com.unity.splines@2.9/manual/extrude.html и `.../manual/instantiate-component.html`
- https://docs.unity3d.com/Packages/com.unity.splines@2.8/manual/extrude-component.html и `.../manual/extrude-runtime.html`
- API Splines 2.9 (`https://docs.unity3d.com/Packages/com.unity.splines@2.9/api/`): `UnityEngine.Splines.SplineExtrude.html`, `SplineMesh.html`, `ExtrudeSettings-1.html`, `ExtrusionShapes.Road.html`, `SplineUtility.html`, `PathIndexUnit.html`; `SplineContainer.html` — в ветке 2.8
- https://github.com/needle-mirror/com.unity.splines/blob/master/CHANGELOG.md — профили Road (2.7.1), фикс UInt16 (2.9.0)
- https://docs.unity3d.com/Packages/com.unity.probuilder@6.1/manual/index.html, `.../manual/auto-uvs-actions.html`, `.../manual/Object_LightmapUVs.html`, `.../changelog/CHANGELOG.html`
- https://docs.unity3d.com/Packages/com.unity.terrain-tools@5.3/manual/index.html
- https://docs.unity3d.com/Packages/com.unity.addressables@4.0/manual/index.html и `.../changelog/CHANGELOG.html`
- https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/NavMeshSurface.html
- https://docs.unity3d.com/Packages/com.unity.mathematics@1.3/api/Unity.Mathematics.Random.html

Блоги, форумы, книги:

- https://discussions.unity.com/t/new-worldbuilding-update-q3-2024-info-revealed-at-unite/1519292 — официальное заявление Unity: новая Worldbuilding System заменит Terrain, в Unity 6 её нет
- https://unity.com/blog/games/e-book-for-level-designers и https://unity.com/resources/introduction-to-level-design-in-game-development-and-in-unity — e-book по левел-дизайну
- https://unity.com/blog/engine-platform/addressables-planning-and-best-practices — планирование Addressables
- https://unity.com/blog/unity-6-3-lts-is-now-available — анонс Unity 6.3 LTS

Бесплатные ассеты: https://kenney.nl/assets (CC0), https://polyhaven.com/license (CC0), https://ambientcg.com/, https://quaternius.com/, https://assetstore.unity.com/

# Партиклы, эффекты и шейдеры (Unity 6.3 / URP)

Что выбрать для эффектов машины и мира, чем платит кадр за красоту и где Unity 6 сломал советы из туториалов 2019–2022. Заглядывать сюда перед тем, как добавлять любой визуальный эффект.

---

## TL;DR — решения для нашего проекта

1. **Базовая система эффектов — встроенный `ParticleSystem` (Shuriken).** VFX Graph в URP до сих пор официально «не вышел из preview» и требует compute-шейдеров; для пыли, искр и выхлопа на десктопе Shuriken достаточен и даёт полный C#-доступ к частицам.
2. **VFX Graph подключаем точечно и позже** — там, где реально нужны десятки тысяч частиц (дождь на всю сцену, пыльная буря, «вау»-эффект для витрины). На macOS (Metal) он работает; на Android с OpenGL ES — нет вообще.
3. **Следы шин — процедурный меш-стрип, а не `TrailRenderer` и не декали.** Один меш, один draw call, вершины кладутся по нормали дороги. Декали через `DecalProjector` — только для разовых отметок (пятно от бурн-аута на старте), потому что каждый проектор это отдельный объект с посчётной ценой.
4. **Кузов — URP Lit Shader Graph с включённым Clear Coat** в Graph Settings (галка добавляет блоки `Coat Mask` и `Coat Smoothness`). Это «второй лаковый слой» — то, что визуально отличает автомобильную краску от крашеного металла.
5. **Фары и стоп-сигналы — эмиссия с HDR-интенсивностью выше 1** плюс Bloom с `Threshold` около 1. Менять яркость в рантайме через `MaterialPropertyBlock`, не через `renderer.material` (иначе Unity клонирует материал и ломает батчинг).
6. **Постобработка — Volume-фреймворк URP.** Пакет Post Processing Stack v2 с URP несовместим (прямая цитата из документации), все туториалы с ним — мусор.
7. **Ощущение скорости даёт связка Vignette + Chromatic Aberration + Motion Blur (`Camera Only`)**, привязанная к скорости из скрипта. Motion Blur в режиме `Camera and Objects` включает проход motion vectors — дороже, но у нас камера следует за машиной, и объектный режим тут почти не даёт эффекта.
8. **Bloom в Unity 6.3 получил свойство `Filter` (Gaussian / Dual / Kawase)** — это новое, в 6.2 его не было. Плюс `High Quality Filtering` выключить, `Downscale = Quarter` — самый дешёвый рабочий bloom.
9. **Пулинг через `UnityEngine.Pool.ObjectPool<T>`** (встроенный, без сторонних пакетов) + `Stop Action = Callback` и `OnParticleSystemStopped()` для возврата эффекта в пул. `Instantiate`/`Destroy` на каждое попадание — гарантированные фризы от сборщика мусора.
10. **Профилируем не «на глаз»: Rendering Debugger → Overdraw** (Window → Analysis → Rendering Debugger) и Profiling-панель VFX Graph. Overdraw — главная и почти единственная причина, по которой партиклы съедают кадр.

---

## 1. Shuriken против VFX Graph

**Shuriken** — это компонент `ParticleSystem`, модульная система, симуляция считается на CPU.
**VFX Graph** (пакет `com.unity.visualeffectgraph`, компонент `VisualEffect`) — нодовый редактор, симуляция считается на GPU через compute-шейдеры (программы, выполняющиеся на видеокарте вне обычного рендеринга).

Официальное сравнение из документации Unity 6.3:

| | Built-in Particle System | Visual Effect Graph |
|---|---|---|
| Пайплайны | Built-in, URP, HDRP | URP, HDRP |
| Реалистичное число частиц | тысячи | миллионы |
| Физика | взаимодействует с физической системой Unity | взаимодействует только с тем, что задано в графе (например, с буфером глубины) |
| C#-доступ | полный: чтение/запись каждой частицы, реакция на события столкновений | только exposed-свойства графа и события |

### Требования VFX Graph и что это значит для нас

Из «Requirements and compatibility» пакета 17.3:

- Нужна поддержка compute-шейдеров (`SystemInfo.supportsComputeShaders == true`) и SSBO (`SystemInfo.maxComputeBufferInputsVertex > 0`).
- «The Visual Effect Graph isn't out of preview for URP, which means it only supports some of the platforms that URP supports»; отдельно — «isn't out of preview for mobile platforms» и «does not support Open GL ES».
- В URP не поддерживается gamma color space (у нас всё равно должен быть linear — это дефолт для URP).

Практика: macOS/Metal, Windows/DX12, консоли — работает. Android на OpenGL ES — нет (нужен Vulkan). WebGL 2 — нет; в Unity 6.1 появился путь через WebGPU.

### Что из VFX Graph работает именно в URP

Из таблицы «Render pipeline feature comparison» (Unity 6.3 Manual), колонка URP:

| Возможность | URP |
|---|---|
| Lit / Unlit частицы, Soft Particles, Flipbook blending, Motion Vectors | да |
| Instancing, Skinned Mesh Sampling, Custom HLSL, 6-way lighting | да |
| Camera Buffer (коллизии по глубине, чтение цвета) | да |
| Decal-частицы (`Output Particle URP Lit Decal`) | да, требует Decal Renderer Feature |
| Trail / Particle Strips | да, помечено как Experimental |
| Simple Lit частицы | нет |
| **Distortion output (тепловое марево)**, Volumetric Fog Output | **нет — только HDRP** |

**Вывод для нас.** Стартуем на Shuriken. VFX Graph держим как второй эшелон для эффектов-витрин. Тепловое марево от выхлопа через VFX Graph в URP не сделать — только шейдером, читающим `_CameraOpaqueTexture`, или готовым URP Particles Lit с включённым `Distortion`.

---

## 2. Эффекты машины

### Пыль и дым из-под колёс

Shuriken, **Simulation Space = World** (частицы остаются там, где родились, а не едут вместе с машиной). Мировое пространство ломает «процедурный режим» системы (см. §6) — это неизбежно и нормально.

Плотность привязываем к пробуксовке колеса. Канонический паттерн работы с модулями (модуль возвращается как структура-обёртка, писать в свойство напрямую нельзя — компилятор выдаст CS1612):

```csharp
using UnityEngine;

/// <summary>Drives wheel dust density from the wheel slip value.</summary>
public sealed class WheelDustEmitter : MonoBehaviour
{
    [SerializeField] private ParticleSystem _dust;
    [SerializeField] private float _maxRate = 60f;

    /// <param name="slip01">Normalized wheel slip, 0..1.</param>
    public void SetSlip(float slip01)
    {
        var emission = _dust.emission;              // копия структуры — так надо
        emission.rateOverTimeMultiplier = _maxRate * Mathf.Clamp01(slip01);
    }
}
```

Материал: **URP Particles Unlit** для пыли (дёшево) или **Particles Lit**, если пыль должна ловить свет фар. `Soft Particles` (плавное затухание у земли, чтобы биллборд не резал геометрию) требует включённого **Depth Texture** в URP Asset.

Для «настоящего» объёмного дыма в Unity 6 есть **шеститочечное освещение (six-way lighting)**: шесть направлений света запечены в две RGBA-текстуры, шейдер смешивает их по фактическому свету сцены. В URP это готовый префаб-шейдер **Six Way Shader Graph** (появился в Unity 6.0) и выход `Six-Way Smoke Lit` в VFX Graph. Требует запечённых лайтмап-текстур дыма — это заготовка на будущее, не на первую итерацию.

### Искры при ударе

Два рабочих способа.

**А. Sub Emitters.** В модуле Collision системы-«снаряда» включить `Send Collision Messages`, в модуле Sub Emitters повесить дочернюю систему на событие Collision. Всё внутри Shuriken, без скриптов.

**Б. Из C# по событию физики** — точнее и дешевле, когда искры нужны от удара кузова:

```csharp
using UnityEngine;

public sealed class ImpactSparks : MonoBehaviour
{
    [SerializeField] private ParticleSystem _sparks;
    [SerializeField] private float _minImpulse = 2f;

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.impulse.magnitude < _minImpulse) return;

        var contact = collision.GetContact(0);
        var emitParams = new ParticleSystem.EmitParams
        {
            position = contact.point,
            applyShapeToPosition = false,
            velocity = Vector3.Reflect(-collision.relativeVelocity, contact.normal) * 0.3f
        };
        _sparks.Emit(emitParams, 12);
    }
}
```

`ParticleSystem.Emit(EmitParams, int)` — актуальная сигнатура Unity 6. Одна система искр на всю сцену, вызываемая из разных мест, дешевле, чем инстанс на каждый удар.

Настройки Collision-модуля, если искры должны отскакивать от геометрии: `Collision Quality` = `High` использует физическую систему (точно, дорого), `Medium`/`Low (Static Colliders)` кешируют коллизии в воксельной сетке и подходят **только для неподвижных коллайдеров**. Карта у нас статичная — значит `Medium` уместен. `Max Collision Shapes` ограничивает число учитываемых форм.

### Выхлоп

Маленькая система, Simulation Space = World, короткая жизнь, `Start Size` растёт по времени жизни. Тепловое марево — свойство `Distortion` в материале **URP Particles Lit / Unlit** (`Strength`, `Blend`). По документации URP оно требует включённого Depth Texture; само искажение читает `_CameraOpaqueTexture`, поэтому в URP Asset нужен и **Opaque Texture** (описание в документации URP Asset: «You can use this in transparent Shaders to create effects like frosted glass, water refraction, or heat waves»).

Важно: включение Opaque Texture на мобильных платформах, не поддерживающих store action `StoreAndResolve`, заставляет Unity игнорировать MSAA. На десктопе это не проблема, но знать стоит.

### Брызги и дождь

**Брызги из-под колёс** — та же система, что и пыль, другой материал и меньшее время жизни; переключение «пыль ↔ брызги» логично делать через два префаба и ScriptableObject-конфиг покрытия, а не ветвлением в коде. **Дождь** — здесь Shuriken начинает проигрывать: дождь на всю сцену это десятки тысяч мировых частиц без процедурного режима, ровно тот случай, ради которого стоит подключать VFX Graph. Если остаёмся на Shuriken — дождь делается «коробкой» вокруг камеры с локальной симуляцией, а не покрытием всей карты.

---

## 3. Следы шин: четыре варианта

| Вариант | Плюсы | Минусы | Когда брать |
|---|---|---|---|
| **`TrailRenderer` на колесо** | 5 минут работы, `Alignment = TransformZ` кладёт ленту плашмя | Отдельный прозрачный рендерер на колесо, длина ограничена `Time`, лента не облегает рельеф — на неровностях висит в воздухе или проваливается | Прототип, черновик |
| **Процедурный меш-стрип** | Один меш и один draw call на все следы, вершины кладём по нормали дороги (raycast), полный контроль над длиной и затуханием | Надо написать генератор меша и следить за лимитом вершин | **Наш выбор для постоянных следов** |
| **`DecalProjector` (URP Decal Renderer Feature)** | Правильно облегает геометрию, влияет на normal / metallic / smoothness — след выглядит как реальная резина на асфальте | Каждый проектор — отдельный объект; **декали не поддерживают SRP Batcher by design** (используют MaterialPropertyBlock); не работают на прозрачных поверхностях; в URP **нет эмиссивных декалей** | Разовые отметки: бурн-аут на старте, следы юза при резком торможении |
| **Запекание в текстуру (splat-маска)** | Практически бесплатно в рантайме: рисуем в `RenderTexture`, шейдер дороги её сэмплит | Нужен свой шейдер дороги и рендер-таргет; разрешение маски ограничивает чёткость следа | Если карта статичная и небольшая — очень сильный вариант |

### Практика по URP-декалям

Порядок: **URP Renderer → Add Renderer Feature → Decal**. Материал проектора должен использовать шейдер `Shader Graphs/Decal`.

Свойства Decal Renderer Feature (Unity 6.3):

- **Technique**: `DBuffer` — качественнее (смешивает albedo, нормали, MAOS), но **не работает на OpenGL/OpenGL ES**, требует DepthNormal-прохода и «не работает на партиклах и деталях террейна». `Screen Space` — рекомендован для мобильных с tile-based GPU, поддерживает только смешивание нормалей; параметр `Normal Blend` (Low/Medium/High) задаёт 1/3/5 сэмплов буфера глубины. `Automatic` выбирает сам по платформе.
- **Max Draw Distance** — дистанция отсечения. Обязательно уменьшить: следы за спиной рисовать не нужно.
- **Use Rendering Layers** — включение создаёт DepthNormal prepass, что делает декали дороже на tile-based GPU. Не включать без нужды.

Оптимизация из документации: собрать все текстуры следов в один атлас, включить `Enable GPU Instancing` на материале — тогда все декали с этим материалом рисуются одним instanced draw call.

Скриптовый пул проекторов (`DecalProjector` живёт в `UnityEngine.Rendering.Universal`, для asmdef нужна ссылка на `Unity.RenderPipelines.Universal.Runtime`):

```csharp
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Rendering.Universal;

public sealed class SkidDecalPool : MonoBehaviour
{
    [SerializeField] private DecalProjector _prefab;
    [SerializeField] private int _maxAlive = 64;

    private ObjectPool<DecalProjector> _pool;

    private void Awake()
    {
        _pool = new ObjectPool<DecalProjector>(
            createFunc: () => Instantiate(_prefab, transform),
            actionOnGet: p => p.gameObject.SetActive(true),
            actionOnRelease: p => p.gameObject.SetActive(false),
            actionOnDestroy: p => Destroy(p.gameObject),
            collectionCheck: false,
            defaultCapacity: _maxAlive,
            maxSize: _maxAlive);
    }

    public DecalProjector Spawn(Vector3 position, Quaternion rotation)
    {
        var projector = _pool.Get();
        projector.transform.SetPositionAndRotation(position, rotation);
        projector.fadeFactor = 1f;       // прозрачность декали, гасим со временем
        projector.drawDistance = 80f;    // индивидуальная дистанция отсечения
        return projector;
    }

    public void Despawn(DecalProjector projector) => _pool.Release(projector);
}
```

`ObjectPool<T>` живёт в `UnityEngine.Pool` и встроен в движок — сторонние пул-пакеты не нужны. Из документации: пул не потокобезопасен и не гарантирует непрерывность объектов в памяти.

---

## 4. Shader Graph: минимум, который нужен

**Shader Graph** — нодовый редактор шейдеров (шейдер = программа, определяющая, как поверхность выглядит под светом). Для URP это основной способ делать материалы: hand-coded шейдеры документация прямо называет «supported, but not recommended».

Полезное в Unity 6: **Color Mode → Heatmap** раскрашивает ноды по относительной стоимости на GPU (тёмные почти бесплатны, яркие требуют вычислений) — быстрый способ увидеть, где граф жирный. **Template browser** (новое в 6.3): `Create > Shader Graph > From Template` — готовые шаблоны для lit/unlit поверхностей, декалей, постобработки, UI, спрайтов, партиклов и 6-way lighting. **Support VFX Graph** в Graph Settings — галка, без которой Shader Graph не появится в списке выбора внутри VFX Graph; на рантайм не влияет, но такие графы дольше компилируются.

### Кузов: metallic + clearcoat

Два пути, оба валидны:

1. **Готовый шейдер `Complex Lit`.** Свойство **Clear Coat** — «Adds an additional transparent, reflective layer over the base material… Enable this property to simulate surfaces like car paint or varnished wood. The material takes longer to render, because Unity calculates lighting for both the base layer and the clear coat.» Подсвойства: `Mask` (интенсивность, красный канал текстуры) и `Smoothness` (чёткость отражений, зелёный канал).
2. **Свой Lit Shader Graph** с включённым `Clear Coat` в Graph Settings — добавляет блоки `Coat Mask` и `Coat Smoothness` во Fragment Context. Этот путь нужен, если хотим двухтонную краску, флейк, ливрею или маску грязи.

Важно про батчинг: в документации Complex Lit прямо помечено, что `Workflow Mode`, `Surface Type`, `Alpha Clipping`, `Receive Shadows`, `Emission` **«affects batching performance»** — каждая включённая опция это отдельный вариант шейдера и лишний SRP Batcher batch. Не включать то, что не используется.

### Фары и стоп-сигналы

Эмиссия — это свойство материала «светиться самому». Чтобы фара действительно «зажглась» и дала bloom, интенсивность цвета в HDR-пикере надо поднять выше 1 (в документации Bloom: «For effects like lava that glow brighter than white… increase the Intensity value in the color picker»), а `Threshold` Bloom-эффекта держать около 1 — тогда светятся только фары, а не весь кузов.

Переключение стоп-сигналов в рантайме:

```csharp
using UnityEngine;

public sealed class BrakeLights : MonoBehaviour
{
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [SerializeField] private Renderer _lightsRenderer;
    [SerializeField] private Color _onColor = new Color(4f, 0.15f, 0.1f); // HDR: > 1
    [SerializeField] private Color _offColor = Color.black;

    private MaterialPropertyBlock _block;

    private void Awake() => _block = new MaterialPropertyBlock();

    public void SetBraking(bool braking)
    {
        _lightsRenderer.GetPropertyBlock(_block);
        _block.SetColor(EmissionColorId, braking ? _onColor : _offColor);
        _lightsRenderer.SetPropertyBlock(_block);
    }
}
```

`MaterialPropertyBlock` меняет свойство без создания копии материала. Обращение к `renderer.material` в рантайме молча клонирует материал — это утечка и лишние draw call'ы.

Сама лампочка ≠ освещённая дорога: для светового пятна перед машиной нужен реальный Spot Light, эмиссия его не заменяет (Unity 6 URP по умолчанию Forward+, число дополнительных источников света уже не ограничено восемью на объект, как было в старом Forward).

---

## 5. Постобработка через Volume

**Volume** — компонент-«зона», хранящий профиль (`VolumeProfile`) с набором эффектов-оверрайдов. Global Volume действует везде, Local Volume — внутри коллайдера, между ними идёт блендинг по весу и приоритету.

**URP не совместим с пакетом Post Processing Stack v2** — цитата из документации. Всё, что находится в туториалах про «PostProcessVolume» и «PostProcessLayer», к нашему проекту неприменимо.

Список оверрайдов URP 6.3: Bloom, Channel Mixer, Chromatic Aberration, Color Adjustments, Color Curves, Color Lookup, Depth of Field, Film Grain, Lens Distortion, Lift Gamma Gain, Motion Blur, Panini Projection, Screen Space Lens Flare, Shadows Midtones Highlights, Split Toning, Tonemapping, Vignette, White Balance.

Самые дешёвые по документации Unity: Bloom (с выключенным `High Quality Filtering`), Chromatic Aberration, Color Grading, Lens Distortion, Vignette.

### Что реально усиливает ощущение скорости

| Эффект | Вклад в скорость | Цена |
|---|---|---|
| **Motion Blur, `Camera Only`** | высокий: смазывает окружение при повороте камеры | средняя; не использует motion vectors, режим `Camera and Objects` дороже (нужен проход motion vectors) |
| **Vignette** | средний: сужает «туннель» обзора | очень низкая |
| **Chromatic Aberration** | средний: цветная кайма по краям кадра, «перегрузка оптики» | низкая |
| **Bloom** | средний, косвенный: фары и блики размазываются в шлейф | зависит от настроек, см. ниже |
| **Lens Distortion** | низкий-средний: «рыбий глаз» на разгоне | низкая |
| **Depth of Field** | почти нулевой для гонки, при этом дорогой | высокая — не использовать |
| **Радиальный blur / speed lines** | самый высокий, но в URP нет готового оверрайда | делается через **Full Screen Pass Renderer Feature** + Fullscreen Shader Graph (появились в 2022.2, есть в Unity 6) |

Bloom в Unity 6.3 получил свойство **`Filter`**: `Gaussian` (лучшее качество, дефолт), `Dual` (быстрее, для мобильных), `Kawase` (экономит память, самый быстрый на низком разрешении). В Unity 6.2 этого свойства ещё не было. Дополнительно: `Downscale = Quarter` («For best performance»), `Max Iterations` меньше 6, `High Quality Filtering` выключен.

Motion Blur имеет параметр `Clamp` (по умолчанию 0.05) — «limits the blur at high velocity, to avoid excessive performance costs». Задирать его в погоне за смазом — прямой путь к просадке.

### Привязка эффектов к скорости

```csharp
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Ramps speed-related post-processing with the car velocity.</summary>
public sealed class SpeedPostFx : MonoBehaviour
{
    [SerializeField] private Volume _speedVolume;   // отдельный Volume только под эти эффекты
    [SerializeField] private Rigidbody _carBody;
    [SerializeField] private float _fullEffectSpeed = 40f; // м/с

    private Vignette _vignette;
    private ChromaticAberration _aberration;

    private void Awake()
    {
        // profile создаёт инстанс профиля для этого Volume; sharedProfile правил бы ассет на диске
        _speedVolume.profile.TryGet(out _vignette);
        _speedVolume.profile.TryGet(out _aberration);
    }

    private void Update()
    {
        // Unity 6: Rigidbody.velocity переименован в linearVelocity
        float t = Mathf.Clamp01(_carBody.linearVelocity.magnitude / _fullEffectSpeed);

        _speedVolume.weight = t;                       // самый дешёвый способ — гнать общий вес
        _vignette.intensity.value = Mathf.Lerp(0.15f, 0.45f, t);
        _aberration.intensity.value = Mathf.Lerp(0f, 0.6f, t);
    }
}
```

`VolumeProfile.TryGet<T>(out T)` — актуальный API из `com.unity.render-pipelines.core`. Разница `profile` / `sharedProfile` документирована прямо: `profile` клонирует профиль и правит только этот Volume, `sharedProfile` меняет ассет в проекте и все Volume, которые его используют.

---

## 6. Оптимизация партиклов

### Overdraw — причина №1

Overdraw = один и тот же пиксель закрашивается многократно полупрозрачными квадами. Десять слоёв полупрозрачного дыма на весь экран стоят десяти полноэкранных проходов. Лечится:

- меньше частиц крупнее вместо кучи мелких — либо наоборот, в зависимости от того, что даёт нужную плотность при меньшей суммарной площади;
- альфа ближе к нулю по краям текстуры и обрезанный прозрачный бордюр (частица-квад с непрозрачным содержимым в центре и полным прозрачным кольцом по краю — это чистый overdraw);
- `Max Particle Size` в Renderer-модуле ограничивает размер частицы как долю вьюпорта — страховка от «дым на весь экран, когда камера влетела внутрь»;
- проверять глазами: **Window → Analysis → Rendering Debugger → Rendering → Overdraw** («useful to check where Unity draws pixels over one other»).

### Culling и «процедурный режим»

Внутри у Shuriken два режима. В **процедурном** состояние системы предсказуемо в любой момент времени, поэтому за кадром её можно не считать вообще и мгновенно «перемотать» при возврате в кадр. В **непроцедурном** система обязана считаться всегда, даже невидимая.

Процедурный режим ломают (список из инженерного блога Unity; механизм и иконка-подсказка в инспекторе живы до сих пор): Simulation Space = World, gravity modifier кривой, Rate over Distance ≠ 0, External Forces, Clamp Velocity, Rotation by Speed, Collision, Trigger, Sub Emitters, Noise, Trails, а также **любое изменение свойств из скрипта в рантайме**.

Для нас это значит: система пыли из-под колёс (world space + скриптовая правка emission) непроцедурна by design. Компенсируем `Culling Mode` в Main-модуле: `Automatic` (зацикленные ставятся на паузу, остальные считаются всегда), `Pause` (не считать за кадром — то, что нужно для пыли), `Pause And Catch-up` («In complex systems, this option can cause performance spikes» — избегать), `Always Simulate` (только для разовых эффектов вроде фейерверка). Если эффект нельзя ставить на паузу, но он локален, есть ручной вариант через `CullingGroup` — систему bounding-сфер, уведомляющую о входе и выходе из кадра.

### Пулинг

`Instantiate` + `Destroy` на каждое попадание = мусор для GC = микрофризы. Схема:

1. `Main → Stop Action = Callback`.
2. На объекте эффекта — `OnParticleSystemStopped()`, внутри возврат в `ObjectPool<T>`.
3. Пул создаётся в `Awake`, размер задаётся через ScriptableObject-конфиг.

Эмиттеры, которые работают постоянно (пыль, выхлоп), не пулятся вообще — они висят на префабе машины и просто меняют rate.

### VFX Graph: специфика

Если/когда подключим:

- **Bounds** задаются в Initialize Context. Режим `Automatic` документация не рекомендует («can have a negative impact on performances»), потому что он принудительно ставит флаг «always recompute bounds and simulate» — то есть отключает культинг. Правильный путь — `Recorded`: записать реальные границы через VFX Control panel и применить.
- **Instancing** включён по умолчанию: эффекты с одним и тем же VFX-ассетом батчатся в общие буферы. Не поддерживают инстансинг `Output Mesh` и Output Event Handlers. В Unity 6.3 добавлена поддержка инстансинга для GPU Events.
- **Capacity** в Initialize Context — это выделенная память под частицы. Панель Particle System Info показывает `Alive/Capacity`; «Optimizing the capacity to fit the maximum number of particles alive saves memory allocation space».
- **Profiling / Debug panel** (иконка в правом верхнем углу окна VFX Graph при подключённом GameObject) даёт CPU Full Update, GPU Time, GPU Memory, Texture Usage и heatmap-порог в миллисекундах.

---

## 7. Антипаттерны

**«Ставь VFX Graph, он же на GPU и быстрее».** VFX Graph выигрывает на масштабе. На эффекте из 30 искр он проиграет: у него выше фиксированная стоимость на систему, он не «выходил из preview» в URP, требует compute-шейдеров и не даёт нормального C#-доступа к отдельным частицам. Для реактивных эффектов машины Shuriken точнее и предсказуемее.

**«Следы шин — это Projector».** Компонент `Projector` в URP **не поддерживается вообще** (в таблице сравнения пайплайнов прямо: «No. Use the Decal Renderer Feature instead»). Все туториалы 2016–2019 по скидмаркам через Projector нерабочие.

**«Post Processing Stack v2 для эффектов».** Прямая цитата документации: «URP is not compatible with the Post Processing Stack v2 package». Только Volume-фреймворк.

**«Motion Blur = ощущение скорости, ставим на максимум».** URP-документация отдельно предупреждает про `Clamp` и про то, что режим `Camera and Objects` перезаписывает камерные motion vectors объектными. Плюс для VR Unity рекомендует Motion Blur, Chromatic Aberration и Lens Distortion **не использовать** — если когда-нибудь захотим VR-режим витрины, эту связку придётся отключать.

**«Depth of Field добавит кинематографичности».** В гонке от третьего лица размытие дали не читается как красота, читается как «пропала детализация», при этом это один из самых дорогих оверрайдов. Bokeh DoF — для консолей/десктопа, Gaussian — для слабых устройств; нам не нужен ни тот, ни другой.

**«`renderer.material.SetColor` в Update».** Клонирует материал при первом обращении, ломает SRP Batcher, плодит мусор. Только `MaterialPropertyBlock`.

**«Пусть частицы летят, они дешёвые».** Дорого не количество частиц, а закрашенные пиксели: 50 частиц во весь экран дороже 5000 мелких искр. Сортировка между системами при этом делается только через `Sorting Fudge` — грубый bias по системе целиком, не по отдельным частицам.

**`ps.emission.rateOverTime = x;`** — не компилируется (CS1612, структура возвращается по значению). Правильно: `var emission = ps.emission; emission.rateOverTime = x;`.

**«В URP декаль может светиться».** Не может: у Decal Shader Graph в URP нет блока Emission (только Base Color, Alpha, Normal, Normal Alpha, Metallic, AO, Smoothness, MAOS Alpha), и в таблице сравнения пайплайнов у URP явно указано «no emissive decals».

**«Включим все галки в материале, вдруг пригодится».** В документации URP у Lit/Complex Lit опции помечены «This property affects batching performance» — каждая создаёт вариант шейдера и отдельный SRP Batcher batch.

---

## 8. Чек-лист

- [ ] В URP Asset включены **Depth Texture** (soft particles, decals) и **Opaque Texture** (тепловое марево, искажения). Проверить, что не включено «просто так» — оба стоят кадра.
- [ ] Материалы частиц — `Universal Render Pipeline/Particles/Unlit` или `.../Lit`, не Standard Particles из Built-in.
- [ ] У каждой системы выставлен осознанный `Culling Mode` (по умолчанию `Automatic`; для пыли — `Pause`).
- [ ] `Max Particles` и `Max Particle Size` не оставлены дефолтными «на всякий случай».
- [ ] Все спавнящиеся эффекты идут через `ObjectPool<T>` + `Stop Action = Callback`.
- [ ] В Rendering Debugger включён Overdraw и просмотрена самая «дымная» сцена.
- [ ] Decal Renderer Feature добавлен в URP Renderer; `Max Draw Distance` уменьшен; `Use Rendering Layers` выключен, если не нужен; на материале декали включён GPU Instancing.
- [ ] Материал кузова — Lit Shader Graph с `Clear Coat`; лишние опции (Alpha Clipping, Specular workflow) выключены.
- [ ] Эмиссия фар в HDR > 1, Bloom `Threshold` ≈ 1, `High Quality Filtering` выключен, `Filter` = `Kawase` или `Dual` при просадках (Unity 6.3+).
- [ ] Постобработка — один Global Volume + один «скоростной» Volume с меняющимся `weight`; никаких PPSv2.
- [ ] `Volume.profile` (не `sharedProfile`) при правках из скрипта, иначе изменится ассет в проекте.
- [ ] Если подключён VFX Graph: bounds записаны в режиме `Recorded`, capacity подогнан под реальный `Alive`, instancing не отключён.
- [ ] Замер до/после: Profiler → GPU, и Profiling panel VFX Graph, а не «на глаз стало плавнее».

---

## 9. Видео и доклады

- Graphics rendering: Getting the best performance with Unity 6 | Unite 2024 — Unity — https://www.youtube.com/watch?v=Oc6T4hh5gaI — официальный разбор, что в Unity 6 стало дешевле в рендере и где искать бутылочные горлышки.
- Boosting your game performance with Unity 6 Profiling tools | Unite 2024 — Unity — https://www.youtube.com/watch?v=_cV1B2hqXGI — как читать Profiler и Rendering Debugger; нужно, чтобы измерять цену эффектов, а не гадать.
- VFX Graph Learning Templates | Tutorial — Unity — https://www.youtube.com/watch?v=DKVdg8DsIVY — обзор официального набора учебных графов (совместим с URP и HDRP, Unity 6+), включая декаль-частицы и strips.
- The Complete Beginner's Guide to VFX Graph in Unity 6 — Cam Ayres — https://www.youtube.com/watch?v=TJGYXUoTmSU — вводный проход по интерфейсу VFX Graph именно в Unity 6.
- VFX Graph: Six-way lighting workflow | Unity at GDC 2023 — Unity — https://www.youtube.com/watch?v=uNzLQjpg6UE — полный разбор шеститочечного освещения дыма; техника доступна и в URP.
- Unity VFX Graph VS Particle System - Comparing — https://www.youtube.com/watch?v=XSdXvhLjVEk — наглядное сравнение двух систем «в лоб».
- Understanding URP settings and essentials — Unity — https://www.youtube.com/watch?v=HCXCmHgV7Sk — настройки URP Asset и Renderer, включая Depth/Opaque Texture и Renderer Features.
- Unity 6 Tips: Decal Renderer Feature — Unity — https://www.youtube.com/watch?v=-HrYyWU736k — короткая официальная демонстрация подключения декалей.
- Car Paint Shader - Advanced Materials - Episode 9 — Ben Cloward — https://www.youtube.com/watch?v=dtc3WmL5OTU — теория автомобильной краски (база + флейк + лак) на уровне нод; переносится на URP Clear Coat.
- Post Processing in Unity 6 URP — ithappy — https://www.youtube.com/watch?v=UsRoPHZR0RM — практическая настройка Volume в Unity 6.
- Unity Car Controller Tutorial (Off-Road, Skid Marks, Suspension) — Simon Lee — https://www.youtube.com/watch?v=7PYREq2OmHA — рабочий пример следов шин через trail; отправная точка, не эталон.

---

## 10. Источники

Все ссылки проверены 2026-08-19.

**Официальная документация Unity 6.3 (6000.3), сборка от 2026-08-17/19:**

- Choosing your particle system solution — https://docs.unity3d.com/6000.3/Documentation/Manual/ChoosingYourParticleSystem.html
- Main module (Culling Mode, Stop Action) — https://docs.unity3d.com/6000.3/Documentation/Manual/PartSysMainModule.html · Renderer module — https://docs.unity3d.com/6000.3/Documentation/Manual/PartSysRendererModule.html · Collision module — https://docs.unity3d.com/6000.3/Documentation/Manual/PartSysCollisionModule.html
- Introduction to decals in URP — https://docs.unity3d.com/6000.3/Documentation/Manual/urp/renderer-feature-decal.html · Decal Renderer Feature reference — https://docs.unity3d.com/6000.3/Documentation/Manual/urp/renderer-feature-decal-reference.html · Decal shader graph reference — https://docs.unity3d.com/6000.3/Documentation/Manual/urp/prebuilt-shader-graphs-urp-decal.html
- Lit shader graph reference for URP (Clear Coat, Support VFX Graph) — https://docs.unity3d.com/6000.3/Documentation/Manual/urp/prebuilt-shader-graphs-urp-lit.html · Complex Lit — https://docs.unity3d.com/6000.3/Documentation/Manual/urp/shader-complex-lit.html · Six Way — https://docs.unity3d.com/6000.3/Documentation/Manual/urp/prebuilt-shader-graphs-urp-sixway.html
- Particles Lit shader (Soft Particles, Distortion) — https://docs.unity3d.com/6000.3/Documentation/Manual/urp/particles-lit-shader.html
- Introduction to post-processing in URP — https://docs.unity3d.com/6000.3/Documentation/Manual/urp/integration-with-post-processing.html · Volume Overrides list — https://docs.unity3d.com/6000.3/Documentation/Manual/urp/EffectList.html · Bloom — https://docs.unity3d.com/6000.3/Documentation/Manual/urp/post-processing-bloom.html · Motion Blur — https://docs.unity3d.com/6000.3/Documentation/Manual/urp/Post-Processing-Motion-Blur.html
- URP asset reference (Depth/Opaque Texture) — https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html · Rendering Debugger reference (Overdraw) — https://docs.unity3d.com/6000.3/Documentation/Manual/urp/features/rendering-debugger-reference.html
- Render pipeline feature comparison — https://docs.unity3d.com/6000.3/Documentation/Manual/render-pipelines-feature-comparison.html · Trail Renderer — https://docs.unity3d.com/6000.3/Documentation/Manual/class-TrailRenderer.html
- ScriptReference: ObjectPool<T0> — https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Pool.ObjectPool_1.html · ParticleSystem.Emit — https://docs.unity3d.com/6000.3/Documentation/ScriptReference/ParticleSystem.Emit.html · EmissionModule.rateOverTime — https://docs.unity3d.com/6000.3/Documentation/ScriptReference/ParticleSystem.EmissionModule-rateOverTime.html

**Документация пакетов (VFX Graph 17.3, Shader Graph 17.3, SRP Core 17.3, URP 17.3):**

- Requirements and compatibility — https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.3/manual/System-Requirements.html · Working with Shader Graph in VFX Graph — https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.3/manual/sg-working-with.html
- Lit Output Settings — https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.3/manual/Context-OutputLitSettings.html · Output Particle URP Lit Decal — https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.3/manual/Context-OutputParticleURPLitDecal.html · Output Distortion (HDRP-only) — https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.3/manual/Context-OutputDistortion.html
- Six-way lighting — https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.3/manual/six-way-lighting.html · Visual Effect Bounds — https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.3/manual/visual-effect-bounds.html · Instancing — https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.3/manual/Instancing.html
- Performance and optimization — https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.3/manual/performance-debug-panel.html · Visual Effect component API — https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.3/manual/ComponentAPI.html
- Shader Graph Color Modes (Heatmap) — https://docs.unity3d.com/Packages/com.unity.shadergraph@17.3/manual/Color-Modes.html · Template browser — https://docs.unity3d.com/Packages/com.unity.shadergraph@17.3/manual/template-browser.html
- API Volume — https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.3/api/UnityEngine.Rendering.Volume.html · VolumeProfile — https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.3/api/UnityEngine.Rendering.VolumeProfile.html · DecalProjector — https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.3/api/UnityEngine.Rendering.Universal.DecalProjector.html

**Блоги и обсуждения Unity:**

- Unity 6.3 LTS is Now Available (Bloom Kawase/Dual, шаблоны Shader Graph, инстансинг GPU Events) — https://unity.com/blog/unity-6-3-lts-is-now-available
- Get the most out of the VFX Graph in Unity 6 (e-book) — https://unity.com/blog/unity-6-vfx-graph-ebook · Realistic smoke lighting with 6-way lighting — https://unity.com/blog/engine-platform/realistic-smoke-with-6-way-lighting-in-vfx-graph · #UnityTips: ParticleSystem Performance – Culling (процедурный режим и таблица «что его ломает»; пост 2016 года, механизм актуален) — https://unity.com/blog/engine-platform/particlesystem-performance-culling-tips
- Which URP platforms does Visual Effect Graph support? (ответы инженеров Unity; последняя активность январь 2026 — CPU-симуляция так и не выпущена) — https://discussions.unity.com/t/which-urp-platforms-does-visual-effect-graph-support/874154

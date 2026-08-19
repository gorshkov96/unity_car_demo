# URP и рендеринг в Unity 6.3

Как устроен рендеринг в нашем проекте, что включать и почему. Заглядывать сюда при создании проекта, при настройке URP-ассета, при добавлении визуального эффекта и при просадках FPS.

> Версии: Unity **6000.3.21f1** (6.3 LTS), пакет URP **17.3**. Документация Unity 6.3 собрана 2026-08-17. Дата обращения к источникам — 2026-08-19.

---

## TL;DR — решения для нашего проекта

1. **URP + Render Graph, без вариантов.** Compatibility Mode (старый способ писать кастомные пассы) **удалён в Unity 6.3**. Любой туториал, где `ScriptableRenderPass` переопределяет `Execute()`, — для нас мусор.
2. **Rendering Path = Forward+.** Обязательное условие для GPU Resident Drawer, снимает лимит «8 доп. источников на объект», позволяет смешивать больше 2 Reflection Probe. Deferred не берём: он ломает MSAA и **не поддерживает GPU Resident Drawer**.
3. **GPU Resident Drawer = Instanced Drawing + GPU Occlusion Culling вкл.** Статичная карта из повторяющихся мешей — идеальный для них сценарий. Цена: `MaterialPropertyBlock` под запретом, Static Batching выключаем.
4. **HDRP не берём.** Он физически не собирается под iOS/Android/Switch/WebGL, а у нас эти модули стоят. Ничего нужного демо про машину он не даёт.
5. **Три URP-ассета — Low / Medium / High**, привязанные к Quality Levels, с **одинаковым** Rendering Path: разные пути в одном билде удваивают шейдерные варианты.
6. **Постобработка — только через Volume.** Один Global Volume в сцене (Tonemapping, Bloom, Color Adjustments, Motion Blur) плюс локальные Volume под зоны. Post Processing Stack v2 не ставим — legacy.
7. **AA на десктопе: STP** (Spatial-Temporal Post-processing — программный апскейлер + TAA), работает даже при Render Scale 1.0. Запасной вариант при «шлейфах» на колёсах — MSAA 4x. **TAA и MSAA вместе не работают**, как и TAA + Camera Stacking.
8. **Свет — Adaptive Probe Volumes**, не Light Probe Groups (legacy): едущая машина корректно освещается запечённой картой без ручной расстановки пробников.
9. **Depth Texture — вкл.** (нужен SSAO, декалям, софт-партиклам). **Opaque Texture — выкл.**, включать точечно под стекло/воду с преломлением.
10. **Camera Stacking не используем** — несовместим с TAA и мешает оптимизации порядка отрисовки. Одна камера на сцену, UI в Screen Space — Overlay.

---

## 1. Почему URP, а не Built-in и не HDRP

**SRP** (Scriptable Render Pipeline) — семейство конвейеров, где логика кадра задаётся C#-кодом, а не зашита в движок. URP и HDRP — две готовые реализации.

| | Built-in | URP | HDRP |
|---|---|---|---|
| iOS / Android / Switch / WebGL | да | да | **нет** |
| SRP Batcher, Render Graph, GPU Resident Drawer | нет | да | да |

Built-in отпадает: без SRP Batcher и Render Graph, новые фичи туда не приезжают. HDRP отпадает по платформам — плюс он требует физически корректной настройки света (люмены, экспозиция, физические камеры), а это пласт работы, никак не приближающий нас к «машина ездит по карте».

С Unity 6.3 у URP и HDRP **общий бэкенд Render Graph** (один компилятор и API), так что навык переносится между конвейерами. Аргумент «начнём на URP, потом переползём на HDRP» стал ещё слабее.

---

## 2. Render Graph — единственный способ расширять URP

**Render Graph** — система, где ты описываешь, *что* пасс читает и *куда* пишет, а Unity сама решает, когда выделять текстуры, какие пассы слить и какие выбросить. Автоматически: не выделяет неиспользуемые ресурсы; выбрасывает пассы, не влияющие на картинку; переиспользует память под текстуры с одинаковыми параметрами; синхронизирует compute- и graphics-очереди; на мобильных TBDR-чипах (tile-based deferred rendering — Apple/ARM GPU) сливает пассы в один native render pass, чтобы данные не уезжали из tile-памяти.

### Compatibility Mode удалён

Объявлен устаревшим в Unity 6.0, **удалён в 6.3**:

- `RenderGraphSettings.enableRenderCompatibilityMode` теперь **read-only и всегда `false`**;
- код вырезан из сборки по умолчанию (быстрее компиляция, меньше билд);
- вернуть можно только дефайном `URP_COMPATIBILITY_MODE` в Player Settings, **и только чтобы доконвертировать проект** — в Unity 6.4 дефайн перестанет работать;
- шипить проект с Compatibility Mode Unity не поддерживает.

### Скелет кастомного пасса на актуальном API

```csharp
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

sealed class CopyColorPass : ScriptableRenderPass
{
    // Только реально нужные поля: лишние замедляют граф.
    class PassData { public TextureHandle source; }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        using (var builder = renderGraph.AddRasterRenderPass<PassData>("Copy Color", out var passData))
        {
            UniversalResourceData resourceData = frameContext.Get<UniversalResourceData>();
            passData.source = resourceData.activeColorTexture;

            TextureDesc desc = renderGraph.GetTextureDesc(passData.source);
            desc.name = "MyCopyTexture";
            TextureHandle destination = renderGraph.CreateTexture(desc);

            builder.UseTexture(passData.source);           // читаем
            builder.SetRenderAttachment(destination, 0);   // пишем (аналог SetRenderTarget)

            // static-лямбда обязательна: замыкающая аллоцирует каждый кадр.
            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx)
                => Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1, 1, 0, 0), 0, false));
        }
    }
}
```

Подключение через `ScriptableRendererFeature`: `renderer.EnqueuePass(m_CopyPass)` внутри `AddRenderPasses`.

### Типы пассов

| API | Когда |
|---|---|
| `AddRasterRenderPass` | **по умолчанию**; только такие пассы Unity умеет сливать друг с другом |
| `AddComputePass` | compute-шейдеры |
| `AddBlitPass` | копирование через материал. В 6.3 **возвращает builder**, умеет блитить в backbuffer и блитить depth |
| `AddCopyPass` | простое копирование текстуры |
| `AddUnsafePass` | последнее средство: полный `CommandBuffer` и `SetRenderTarget` |

`AddUnsafePass` — не «продвинутый режим», а отказ от оптимизаций: граф перестаёт видеть зависимости, не может слить пасс с соседями, не настраивает render target. Чем больше unsafe-пассов, тем больше трафика между GPU и памятью.

### Как не плодить пассы

- нужна копия цвета или глубины — **не делай свою**: URP уже создаёт `cameraOpaqueTexture` и `cameraDepthTexture`, запроси их через `ConfigureInput(ScriptableRenderPassInput.Depth | ...Normal | ...Color)`;
- не дроби логику на пассы «для читаемости» — каждый пасс стоит CPU-времени;
- чтобы не блитить туда-обратно, перенаправь `resourceData.cameraColor` на свою текстуру.

Текстуры из frame data: `activeColorTexture`, `activeDepthTexture`, `cameraOpaqueTexture`, `cameraDepthTexture`, `cameraNormalsTexture`, `mainShadowsTexture`, `motionVectorColor`, `ssaoTexture`, `dBuffer`, `gBuffer`, `overlayUITexture`.

---

## 3. Пути рендеринга: Forward / Forward+ / Deferred / Deferred+

**Rendering Path** — способ, которым конвейер считает освещение. Ставится в Universal Renderer, **не** в URP-ассете: `Project Settings > Graphics` (или `Quality`) → двойной клик по Render Pipeline Asset → в секции Rendering двойной клик по рендереру → **Rendering Path**.

| | Forward | Forward+ | Deferred |
|---|---|---|---|
| Realtime-света на объект | **9** (1 main + 8 additional) | без лимита | без лимита для opaque, **9** для transparent |
| Realtime-света на камеру | до 257 (зависит от платформы) | до 256 (Main Light считается наравне, поэтому на один меньше) | до 257 |
| Per-vertex света | да | **нет** | нет |
| Отключить Main Light | да | **нет** | нет |
| MSAA | да | да | **нет** |
| Camera Stacking | да | да | да, но Base — Deferred, Overlay — Forward |
| Нормали | точные | точные | закодированные (или точнее, но медленнее) |

Лимиты на камеру для Forward: **десктоп/консоли 256**, **мобильные 32**, **OpenGL ES 3.0 и старше 16**.

**Как работает Forward+:** экран режется на тайлы, для каждого считается список влияющих источников, объект шейдится только светом своего тайла. Настройки `Main Light`, `Additional Lights` и `Per Object Limit` в URP-ассете при Forward+ **игнорируются**.

**Deferred+** (появился в **Unity 6.1**) — Deferred для непрозрачных + Forward+ для прозрачных и forward-only объектов.

### Почему Forward+

1. GPU Resident Drawer работает **только** с Forward+ — а это наш главный CPU-выигрыш.
2. Будет много мелких источников (фары, стопы, фонари, неон) — лимит «8 на объект» из Forward упрётся мгновенно.
3. Deferred убивает MSAA — наш запасной план по антиалиасингу.
4. Deferred требует лишний G-buffer target при Rendering Layers и хуже блендит нормали в декалях (при Accurate G-buffer normals блендинг нормалей не поддерживается вовсе).

Цена Forward+ — **время сборки шейдеров**: лимит видимых источников (256 на десктопе) прямо влияет на компиляцию каждого варианта Lit/Complex Lit. Если билды станут невыносимыми: скопировать `/Library/PackageCache/com.unity.render-pipelines.universal-config` в `/Packages/`, поправить `MAX_VISIBLE_LIGHT_COUNT_DESKTOP (32)` в `Runtime/ShaderConfig.cs.hlsl` и синхронно `k_MaxVisibleLightCountDesktop = 32;` в `Runtime/ShaderConfig.cs`, перезапустить редактор. Для Forward+ значение **включает** Main Light, для Forward — нет. После копирования пакет становится встроенным: при апгрейде Unity правки придётся вносить заново.

---

## 4. GPU Resident Drawer и GPU Occlusion Culling

**GPU Resident Drawer (GRD)** держит данные объектов в памяти GPU и рисует их через `BatchRendererGroup` с GPU-инстансингом, снимая с CPU подготовку draw call'ов.

**Требования:** Rendering Path = **Forward+** (жёстко); API с поддержкой compute-шейдеров, **OpenGL ES исключён**; у объекта есть **Mesh Renderer**; шейдер поддерживает DOTS Instancing (встроенные URP-шейдеры поддерживают). Иначе объект просто рисуется обычным путём.

**Включение:**
1. `Project Settings > Graphics > Shader Stripping > BatchRendererGroup Variants = Keep All`.
2. В URP-ассете **SRP Batcher** включён (если поля не видно — меню ⋮ → **Show All Advanced Properties**).
3. **GPU Resident Drawer = Instanced Drawing**.
4. В Universal Renderer **Rendering Path = Forward+**, для окклюжена — **GPU Occlusion**.

### Совместимость объекта — тут закапывается собака

Объект **не попадёт** в GRD, если он: использует `MaterialPropertyBlock`; имеет Light Probes = **Use Proxy Volume**; использует realtime GI вместо статичной; меняет позицию между рендером одной камеры и началом другой; имеет per-instance колбэк вроде `OnRenderObject`. Исключить принудительно — компонент **Disallow GPU Driven Rendering** (с Apply to Children Recursively).

Замена `MaterialPropertyBlock` в Unity 6.3 — **Renderer Shader User Value**: `MeshRenderer.SetShaderUserValue(...)` кладёт на рендерер 32-битное целое, доступное в шейдере как `unity_RendererUserValue` (в Shader Graph — через Custom Function node). Туда влезает упакованный RGBA32-цвет или индекс в атлас. **Работает вместе с GRD** — то есть это и есть правильный способ красить трафик в разные цвета.

**Побочные эффекты:** сборка идёт дольше (компилируются все BatchRendererGroup-варианты); при связке Forward+ и GRD по умолчанию включается **Probe Atlas Blending**; GRD разгружает CPU, но слегка **добавляет** работы GPU; в Scene/Game view ускорение заметно слабее, чем в Play mode и билде.

**Помогает GRD:** выключить **Static Batching** в `Project Settings > Player > Other Settings`; в `Window > Rendering > Lighting > Lightmapping Settings` включить **Fixed Lightmap Size** и выключить **Use Mipmap Limits**.

### GPU Occlusion Culling

Unity строит depth-текстуры с точки зрения камеры и отсекает скрытое силами GPU, используя глубину **текущего и предыдущего кадра** (объект рисуется, если не отсечён хотя бы в одном). Механика консервативная:

- объект аппроксимируется **ограничивающей сферой** — тонкие и вытянутые объекты (столбы, отбойники, провода) описываются грубо, и Unity реже понимает, что они скрыты;
- тест идёт по **downsampled depth buffer**, уровень выбирается по экранному размеру сферы.

Эффективно при обилии перекрытий, высокополигональных перекрытых объектах и малом экранном радиусе. Если перекрытий мало — **станет медленнее**: подготовка окклюжена сама стоит GPU-времени. Проверять замером, а не верой.

В URP-ассете рядом лежит **Small-Mesh Screen-Percentage** — процент экрана, ниже которого мелкие объекты выбрасываются (`0` отключает). Может конфликтовать с собственными LOD-мешами; отдельный объект защищается компонентом **Disallow Small Mesh Culling**.

---

## 5. SRP Batcher — что он на самом деле делает

Частое заблуждение: «SRP Batcher уменьшает количество draw call'ов». **Нет.** Он уменьшает **смену состояний рендера между draw call'ами**: данные материалов постоянно лежат в GPU-памяти, и если содержимое материала не менялось, менять состояние не нужно.

Отсюда правило, противоположное интуиции из Built-in: **много разных материалов на одном шейдере — это хорошо**, много разных *шейдеров* — плохо. Экономить надо шейдерные варианты, а не материалы.

Если включены и SRP Batcher, и Dynamic Batching, приоритет у SRP Batcher (при совместимом шейдере). Dynamic Batching нужен только там, где нет GPU-инстансинга — у нас выключен. На слабом железе с неоптимизированными шейдерами выключенный SRP Batcher иногда быстрее; проверять замером.

Порядок оптимизаций для нас: **GPU Resident Drawer → SRP Batcher → остальное**, Static и Dynamic Batching выключены.

---

## 6. URP Asset и Universal Renderer

Два разных ассета, их постоянно путают. **URP Asset** — «что рисуем и с каким качеством». **Universal Renderer** — «как рисуем кадр» и список Renderer Features.

Ключевое в URP-ассете:

| Поле | Комментарий |
|---|---|
| `Depth Texture` | создаёт `_CameraDepthTexture`. **Вкл.** — нужен SSAO, декалям, софт-партиклам |
| `Opaque Texture` | создаёт `_CameraOpaqueTexture`, аналог GrabPass. **Выкл.**, пока нет преломлений |
| `Store Actions` | Auto / Discard / Store. На мобильных Auto или Discard, иначе жрёт bandwidth |
| `HDR Precision` | 32 bit по умолчанию; 64 bit убирает бандинг, но дороже |
| `Anti Aliasing (MSAA)` | на мобильных без StoreAndResolve MSAA **игнорируется**, если включён Opaque Texture |
| `Render Scale` | масштаб рендер-таргета; UI всегда рисуется в нативном разрешении |
| `LOD Cross Fade Dithering Type` | Bayer Matrix (быстрее) / Blue Noise (красивее) / **2x2 Stencil** (сильно меньше вариантов) |
| `Light Probe System` | **Adaptive Probe Volumes**, не Light Probe Groups (Legacy) |
| `Terrain Holes` | выкл., если террейна с дырками нет — меньше вариантов |

Ключевое в Universal Renderer:

| Поле | Комментарий |
|---|---|
| `Prepass Layer Mask` | **добавлено в Unity 6.2**: слои, попадающие в prepass, чтобы SSAO и декали работали с объектами из Render Objects Feature |
| `Depth Priming Mode` | PC/консоли — Auto или Forced, мобильные — Disabled. **Не работает** с Deferred, с MSAA и на мобильных TBDR |
| `Depth Texture Mode` | After Opaques / After Transparents / Force Prepass. **After Transparents** заметно экономит bandwidth на мобильных |
| `Native RenderPass` | включать на Vulkan / Metal / DX12; на OpenGL ES эффекта нет |
| `Intermediate Texture` | **Auto**. `Always` — костыль для Renderer Feature, не объявляющих входы через `ConfigureInput` |
| `Stencil` | нам доступны биты 0–3, то есть индексы 0–15 |

### Уровни качества (Quality tiers)

`Create > Rendering > URP Asset (with Universal Renderer)` на каждый уровень → `Edit > Project Settings > Quality` → уровень → `Rendering > Render Pipeline Asset`. В рантайме:

```csharp
// Индекс = позиция уровня в списке Quality, начиная с 0.
QualitySettings.SetQualityLevel(SystemInfo.graphicsMemorySize <= 4096 ? 1 : 0);
```

Два правила: переключать качество **только на загрузочных экранах или в статичном меню** (смена даёт заметный временный провал производительности); **не смешивать разные Rendering Path** в ассетах одного билда — Unity сгенерирует два набора вариантов на каждый keyword.

---

## 7. Renderer Features и порядок пассов

**Renderer Feature** — подключаемый блок, добавляющий в кадр свои пассы; добавляется в Universal Renderer через **Add Renderer Feature**. Встроенные: **Render Objects**, **Decal**, **Screen Space Ambient Occlusion**, **Screen Space Shadows**, **Full Screen Pass**.

Точка вставки задаётся `ScriptableRenderPass.renderPassEvent`. Порядок `RenderPassEvent`:

```
BeforeRendering
  BeforeRenderingShadows      → AfterRenderingShadows
  BeforeRenderingPrePasses    → AfterRenderingPrePasses
  BeforeRenderingGbuffer      → AfterRenderingGbuffer          (только Deferred)
  BeforeRenderingDeferredLights → AfterRenderingDeferredLights
  BeforeRenderingOpaques      → AfterRenderingOpaques
  BeforeRenderingSkybox       → AfterRenderingSkybox
  BeforeRenderingTransparents → AfterRenderingTransparents
  BeforeRenderingPostProcessing → AfterRenderingPostProcessing
AfterRendering
```

- на `BeforeRendering`, `BeforeRenderingShadows` и `AfterRenderingShadows` **матрицы камеры ещё не настроены**; начиная с `BeforeRenderingPrePasses` — уже готовы;
- `AfterRenderingPostProcessing` — после постобработки, но **до** финального блита, FXAA/SMAA и цветокоррекции;
- значения enum — целые с зазорами (по исходникам URP: `BeforeRendering = 0` … `AfterRendering = 1000`), поэтому допустим приём `renderPassEvent = RenderPassEvent.AfterRenderingOpaques + 1`. *(Нумерация — из исходников пакета на GitHub, не из Manual.)*

---

## 8. Volume-система и постобработка

**Volume** — компонент, задающий настройки сцены (прежде всего постобработку) в зависимости от положения камеры. Типы объектов: Global / Box / Sphere / Convex Mesh Volume; Volume можно повесить на любой GameObject. **Global** влияет везде, **Local** — когда камера рядом с границами коллайдера родителя. Каждый Volume ссылается на **Volume Profile** с набором **Volume Override**. Каждый кадр URP обходит активные Volume, считает вклад каждого и **интерполирует** финальные значения.

### Два дефолтных Volume, про которые все забывают

В каждой URP-сцене неявно есть два глобальных Volume: **Default Volume** проекта (`Project Settings > Graphics > URP > Default Volume Profile`) и глобальный Volume **активного уровня качества** (URP Asset → `Volumes > Volume Profile`). Оба имеют **наименьший приоритет** и вычисляются **только при загрузке сцены и смене качества**, а не каждый кадр — если других Volume в сцене нет, это заметно дешевле.

**Ловушка:** значения этих двух профилей **кешируются**, правка их из скрипта не даёт эффекта. Решения: положить в сцену обычный **Global Volume** и переопределять через него (его свойства не кешируются) — рекомендуемый путь; либо форсировать пересчёт через `VolumeManager.instance.OnVolumeProfileChanged(volumeProfile)`, но это дополнительная работа и просадка интерполяции.

Для CPU-экономии в URP-ассете есть **Volume Update Mode = Via Scripting**: стек обновляется вручную через `UpdateVolumeStack`, а не каждый кадр.

### SSAO — не постобработка

**Screen Space Ambient Occlusion** реализован как **Renderer Feature**, а не Volume Override, и **не зависит от Volume и не взаимодействует с ними**. `Source = Normals` использует `_CameraNormalsTexture` из пасса `DepthNormals` (по умолчанию); `Source = Depth` восстанавливает нормали из глубины, и тогда доступен `Normal Quality`: Low = 1 сэмпл, Medium = 5, High = 9.

---

## 9. Декали

Декаль — проецируемая на геометрию наклейка: следы шин, лужи, разметка. Нужны **Decal Renderer Feature** + компонент **Decal Projector**. Свойство **Technique**:

| Техника | Суть | Ограничения |
|---|---|---|
| **Automatic** | Unity выбирает по платформе (учитывает Accurate G-buffer normals) | — |
| **DBuffer** | декали рисуются в отдельный буфер, накладываемый поверх непрозрачных при их отрисовке | **не поддерживает OpenGL и OpenGL ES**; требует **DepthNormal prepass** (плохо для мобильных TBDR); **не работает на партиклах и terrain details** |
| **Screen Space** | рисуются после непрозрачных, нормали восстанавливаются из depth (или из G-buffer в Deferred) | только блендинг нормалей; в Deferred с Accurate G-buffer normals блендинг нормалей даёт неверный результат |

При DBuffer доступен `Surface Data`: `Albedo` / `Albedo Normal` / `Albedo Normal MAOS` (последний влияет ещё на metallic, smoothness, AO). При Screen Space — `Normal Blend`: Low = 1 сэмпл глубины, Medium = 3, High = 5. Общее: **Max Draw Distance** (Adaptive Performance может её понизить) и галка **Use Rendering Layers**, которая принудительно создаёт DepthNormal prepass — включать только при реальной нужде.

**Для нас:** десктоп — DBuffer (лучше блендинг следов шин по асфальту), мобильные — Screen Space. Помнить: Decal Renderer Feature добавляет отдельный render pass, документация прямо советует минимизировать его при борьбе за производительность.

---

## 10. Антиалиасинг и апскейлеры

Три метода AA живут **на камере** (`Camera > Rendering > Anti-aliasing`), MSAA — **в URP-ассете**.

- **FXAA** — полноэкранный пасс, самый дешёвый; документация рекомендует его для мобильных.
- **SMAA** — ищет паттерны на границах, заметно резче FXAA.
- **TAA** — накапливает историю кадров, использует **motion vectors**, даёт «шлейфы» (ghosting) на быстром движении.
- **MSAA** — аппаратный, решает только геометрический алиасинг; против бликов и алиасинга текстур не помогает.

Несовместимости наизусть: **TAA + MSAA — нельзя**, **TAA + Camera Stacking — нельзя**, **TAA + Dynamic Resolution — нельзя**. MSAA сочетается с FXAA/SMAA (они постобработка), но не с TAA.

**Upscaling Filter** (URP Asset → Quality) применяется при **Render Scale < 1.0**, кроме двух случаев:

| Фильтр | Требования | Замечания |
|---|---|---|
| `Automatic` / `Bilinear` / `Nearest-Neighbor` | — | Nearest-Neighbor **не работает при включённой постобработке** |
| **FSR 1.0** | Shader Model 4.5 | пространственный, **активен даже при Render Scale 1.0**; есть `Override FSR Sharpness` / `FSR Sharpness` (0 — без резкости, 1 — максимум); на неподдерживаемом железе откат на Automatic |
| **STP 1.0** | **compute-шейдеры, Shader Model 5.0**, не OpenGL ES | пространственно-временной, **принудительно включает TAA**, активен и при Render Scale 1.0, **несовместим с Dynamic Resolution** (только Render Scale); сам подбирает качество под платформу — на PC/консолях с деринг-логикой, на мобильных дешевле |

**Motion vectors** (попиксельное смещение между кадрами) нужны TAA и Motion Blur; пасс запускается **только если фича его запросила**. Важно: URP считает их **только для непрозрачных материалов** (включая alpha-clip), для прозрачных — **нет**. То есть на стёклах и прозрачных эффектах TAA и Motion Blur отработают неверно.

**Для нашего демо:** десктоп — STP при Render Scale 1.0 (получаем TAA плюс запас на снижение разрешения без смены настроек); если шлейфы на вращающихся колёсах окажутся заметны — MSAA 4x + SMAA. Мобильные — FXAA и Render Scale 0.7–0.8.

---

## 11. Инструменты отладки

**Render Graph Viewer** — `Window > Analysis > Render Graph Viewer`. Таймлайн: слева ресурсы, сверху пассы, на пересечении блок доступа. Зелёный — чтение, красный — запись, зелёно-красный — оба, серый — нет доступа, пунктир — ресурс ещё не создан, пусто — уже уничтожен. **Синяя merge bar** под пассами = Unity слила их в один native render pass. **Pass break reasoning** в Pass List прямым текстом объясняет, **почему** пасс не слился со следующим — это главный инструмент оптимизации. View Options → **Load Store Actions** показывает load/store: синий Clear, зелёный Load, красный Store, серый Don't Care.

**Новое в Unity 6.3:** Target Selection умеет подключаться к билду (Editor / Local / Remote / Direct Connection), то есть граф можно смотреть **на реальном устройстве**. Нужен Development Build (для WebGL/UWP ещё и Autoconnect Profiler).

**Frame Debugger** — пошаговый разбор draw call'ов; объекты, собранные GPU Resident Drawer, видны как **Hybrid Batch Group**. Для подробных тегов в URP-ассете `Debug Level = Profiling`.

**Rendering Debugger** — `Window > Analysis > Rendering Debugger`; в Play mode и development-билде `LeftCtrl + Backspace` (**на macOS `LeftCtrl + Delete`**), на мобильных двойной тап тремя пальцами, на консолях L3 + R3. Чтобы в билде были все секции, снять **Strip Debug Variants** в `Project Settings > Graphics > URP Global Settings`. Для Forward+ особенно полезен режим **Lighting Complexity** — сетка тайлов с числом источников на каждый.

---

## 12. Десктоп против мобильных

| Настройка | Десктоп (наша цель) | Мобильные |
|---|---|---|
| Rendering Path | Forward+ | Forward+ или Forward |
| GPU Resident Drawer / Occlusion | вкл. | по замеру: добавляет работы GPU |
| Depth Priming Mode | Auto / Forced | **Disabled** (не поддерживается на TBDR) |
| Depth Texture Mode | After Opaques | **After Transparents** (экономит bandwidth) |
| Native RenderPass / Store Actions | вкл. (Vulkan/Metal/DX12) / Store-Auto | вкл. (Vulkan/Metal) / **Auto-Discard** |
| AA | STP (TAA) либо MSAA 4x | FXAA, Render Scale 0.7–0.8 |
| Decal Technique | DBuffer | **Screen Space** |
| Additional Lights | Per Pixel | Per Vertex или Disabled |
| Soft Shadows / LOD Cross Fade / HDR | вкл. | выкл. или пониженное качество |
| Шейдеры | любые, включая Complex Lit | Baked Lit для статики, Simple Lit для динамики; Complex Lit избегать |

---

## 13. Что включаем в демо про машину

**Включить:** Forward+; GPU Resident Drawer = Instanced Drawing; GPU Occlusion Culling; SRP Batcher; Depth Texture; Depth Priming Mode = Auto; Native RenderPass; Adaptive Probe Volumes; SSAO Renderer Feature; Decal Renderer Feature (DBuffer) под следы шин и разметку; один Global Volume с Tonemapping, Bloom, Color Adjustments, Vignette и **Motion Blur** (для машины он и есть главный «вау»-эффект); локальные Volume под тоннель и ночную зону — заодно демонстрация самой Volume-системы; STP как Upscaling Filter; три URP-ассета по уровням качества.

**Выключить / не трогать:** Compatibility Mode (его больше нет); Opaque Texture, пока нет стекла и воды с преломлением; Static и Dynamic Batching; Camera Stacking; Terrain Holes; Deferred и Deferred+; Post Processing Stack v2.

**Держать в голове при написании кода:**

- цвет машин трафика — через `MeshRenderer.SetShaderUserValue` (`unity_RendererUserValue`), **не** через `MaterialPropertyBlock`, иначе трафик выпадет из GRD;
- фары и стопы — обычные Additional Lights, лимиты Forward+ их не ограничивают;
- прозрачные объекты (стёкла) не дают motion vectors — не строить на них эффекты, зависящие от TAA/Motion Blur.

---

## 14. Миграция с Built-in RP

Актуально, только если появятся ассеты из магазина под Built-in. `Window > Rendering > Render Pipeline Converter`, тип **Built-In Render Pipeline to URP**. Конвертеры: Rendering Settings (создаёт URP Asset и Universal Renderer), Material Upgrade, Read-only Material Converter, Animation Clip Converter, Post-Processing Stack v2 Converter. Порядок: выбрать тип → отметить конвертеры → **Initialize Converters** → просмотреть список → **Convert Assets**.

**Процесс необратим — бэкап обязателен.** Material Upgrade **не поддерживает кастомные шейдеры**: их переписывают вручную либо через свой `MaterialUpgrader` + `IMaterialUpgradersProvider`. Конверсию можно гонять из CLI: `Converters.RunInBatchMode(...)` под `-batchmode -executeMethod`.

---

## Антипаттерны

1. **Писать пасс через `Execute()` / `OnCameraSetup()` / `Configure()`.** Это API Compatibility Mode, удалённого в 6.3. Единственная точка входа — `RecordRenderGraph`.
2. **Включать `URP_COMPATIBILITY_MODE`, «чтобы заработал ассет из Asset Store».** Дефайн существует только для доконвертации и умрёт в Unity 6.4. Правильно — искать версию ассета под Render Graph.
3. **`AddUnsafePass` как основной инструмент** («там есть `SetRenderTarget`, так проще»). Граф перестаёт сливать пассы, не настраивает render target и не отслеживает зависимости. Дефолт — `AddRasterRenderPass`.
4. **Копировать цвет/глубину своим пассом.** URP уже делает `Copy Color` и `Copy Depth`; результат берётся из frame data через `ConfigureInput`. Свой блит — лишний пасс и лишняя память.
5. **Брать Deferred «потому что там больше света».** У Forward+ тоже нет лимита на объект, при этом Deferred отбирает MSAA, per-vertex света и точные нормали, ломает блендинг нормалей в декалях и **отключает GPU Resident Drawer**.
6. **Совет 2019–2022 «жми Static Batching».** С GPU Resident Drawer он мешает, документация прямо советует его выключить.
7. **`MaterialPropertyBlock` для покраски однотипных объектов.** Классика Built-in, ломающая в Unity 6 и SRP Batcher, и GRD. Замена — `SetShaderUserValue` / `unity_RendererUserValue` (6.3) или отдельные материалы на одном шейдере.
8. **«SRP Batcher уменьшает draw calls».** Он уменьшает смену состояний; экономить надо шейдерные варианты, а не материалы.
9. **Включать Depth Texture и Opaque Texture «на всякий случай».** Каждая стоит памяти и bandwidth. Включать по факту потребности и проверять во Frame Debugger.
10. **Править Default Volume Profile из скрипта** и удивляться, что ничего не меняется: значения кешируются. Класть в сцену Global Volume.
11. **Пытаться включить TAA вместе с MSAA** (а также TAA + Camera Stacking или TAA + Dynamic Resolution). Не поддерживается.
12. **Camera Stacking «для UI и миникарты».** Ломает оптимизацию порядка отрисовки, требует постобработки только на последней камере, несовместим с TAA, не даёт смешивать 2D- и 3D-рендереры.
13. **Разные Rendering Path в разных URP-ассетах одного билда.** Удвоение шейдерных вариантов и времени сборки на ровном месте.
14. **HDRP «для красивой картинки».** Не собирается под Android/iOS/Switch/WebGL и требует физически корректной настройки света.
15. **Ставить Post Processing Stack v2.** В URP постобработка — это Volume; PPv2 является legacy, и отдельный конвертер существует именно чтобы от него уйти.
16. **`Intermediate Texture = Always`.** Костыль совместимости для Renderer Feature без `ConfigureInput`; на части платформ бьёт по производительности заметно.
17. **Верить, что GPU Occlusion Culling всегда ускоряет.** Без реальных перекрытий он только добавляет работы GPU. Мерить.

---

## Чек-лист

**При создании проекта**
- [ ] шаблон Universal 3D; URP-ассет назначен в `Project Settings > Graphics` и в `Quality`
- [ ] Rendering Path = **Forward+** во всех Universal Renderer проекта
- [ ] `Graphics > Shader Stripping > BatchRendererGroup Variants = Keep All`
- [ ] URP Asset: SRP Batcher вкл., GPU Resident Drawer = **Instanced Drawing**; Universal Renderer: **GPU Occlusion** вкл.
- [ ] `Player > Other Settings`: **Static Batching выкл.**, Dynamic Batching выкл.
- [ ] Light Probe System = **Adaptive Probe Volumes**; Depth Texture вкл.; Opaque Texture выкл.
- [ ] три URP-ассета (Low/Medium/High) привязаны к Quality Levels, **Rendering Path одинаковый**

**При добавлении визуальной механики**
- [ ] эффект правда требует нового пасса? (может хватить Volume Override или Render Objects Feature)
- [ ] пасс написан через `RecordRenderGraph` + `AddRasterRenderPass`
- [ ] `SetRenderFunc` принимает **static**-лямбду; в `PassData` только используемые поля
- [ ] текстуры запрошены через `ConfigureInput`, а не скопированы вручную
- [ ] в Render Graph Viewer проверено **Pass break reasoning** — пасс сливается с соседями

**Перед тем как сказать «работает»**
- [ ] Frame Debugger: нет неожиданных пассов, GRD-объекты видны как **Hybrid Batch Group**
- [ ] Render Graph Viewer: нет лишних Store-действий и неожиданно живых пассов
- [ ] Rendering Debugger → Lighting Complexity: нет тайлов с аномальным числом источников
- [ ] Rendering Statistics: SetPass calls и CPU-время после включения GRD упали, а не выросли
- [ ] новые объекты совместимы с GRD (нет `MaterialPropertyBlock`, Proxy Volume, `OnRenderObject`)

**Перед билдом**
- [ ] `Graphics > Additional Shader Stripping Settings > Strip Unused Variants` и `Strip Unused Post Processing Variants` вкл.
- [ ] выключены неиспользуемые фичи (Terrain Holes, LOD Cross Fade, Light Cookies) — это стрипает варианты
- [ ] при долгой сборке — снизить `MAX_VISIBLE_LIGHT_COUNT_*` через URP Config package
- [ ] для отладки на устройстве: Development Build + Render Graph Viewer через Target Selection

---

## Видео и доклады

- **Introduction to the Render Graph in Unity 6** — Unity — https://www.youtube.com/watch?v=U8PygjYAF7A — официальный старт по Render Graph: Renderer Feature с Full Screen Shader Graph материалом.
- **Migrating to Render Graph: Understanding the Render Graph Samples — Part 1** — Unity — https://www.youtube.com/watch?v=7L_8-H4W-bY — разбор официальных сэмплов пакета URP (лучший источник шаблонов кода).
- **Migrating to Render Graph: Understanding the Render Graph Samples — Part 2** — Unity — https://www.youtube.com/watch?v=D7YBpOg6DZY — продолжение, более сложные случаи.
- **Migrating to Render Graph: the Render Graph viewer** — Unity — https://www.youtube.com/watch?v=2qJGlu4Thlw — как читать таймлайн пассов и находить причины, по которым они не сливаются.
- **Understanding URP settings and essentials** — Unity — https://www.youtube.com/watch?v=HCXCmHgV7Sk — обзор полей URP Asset и Universal Renderer; полезно перед первой настройкой.
- **Graphics rendering: Getting the best performance with Unity 6 | Unite 2024** — Unity — https://www.youtube.com/watch?v=Oc6T4hh5gaI — главный доклад по оптимизации рендера в Unity 6: GRD, окклюжен, bandwidth.
- **GPU Resident Drawer In Unity 6 (Improve CPU performance)** — SpeedTutor — https://www.youtube.com/watch?v=x7HgRwEynFw — практическое пошаговое включение GRD.
- **Unity Tip Tuesday: GPU Resident Drawer & GPU occlusion culling** — Unity — https://www.youtube.com/shorts/0PenzdQFlv0 — минутная выжимка по обеим фичам.
- **What is SPATIAL-TEMPORAL POST PROCESSING? (Unity 6 URP Tutorial)** — SpeedTutor — https://www.youtube.com/watch?v=V_LWaueim_E — что даёт STP и как он выглядит в сравнении.
- **Forward+ (Plus) Rendering in Unity URP 14+ — Shine Your Lights!** — The Gamedev Guru — https://www.youtube.com/watch?v=AIUyNLc0Boc — механика тайлового отсечения света, с замерами.
- **018. Forward vs Deferred Rendering: The Decision That Defines Your Graphics Budget** — The Gamedev Guru — https://www.youtube.com/watch?v=Tlp62NfQS_4 — как выбирать путь рендеринга под бюджет.

---

## Источники

Дата обращения — 2026-08-19. Страницы Unity Manual ниже даны относительно базы `https://docs.unity3d.com/6000.3/Documentation/Manual/`.

- **Версии:** `UpgradeGuideUnity63.html` (удаление Compatibility Mode, read-only `enableRenderCompatibilityMode`, дефайн `URP_COMPATIBILITY_MODE`) · `WhatsNewUnity63.html` (общий Render Graph бэкенд URP+HDRP, Viewer на устройствах, `unity_RendererUserValue`, Bloom Kawase/Dual) · `WhatsNewUnity62.html` (Prepass Layer Mask) · `WhatsNewUnity61.html` (появление Deferred+) · `render-pipelines-feature-comparison.html` (Built-in / URP / HDRP, платформы и лимиты света)
- **Пути рендеринга и свет:** `urp/rendering-paths-comparison.html` · `urp/rendering/forward-rendering-paths.html` · `urp/rendering/deferred-rendering-path-landing.html` · `urp/rendering-paths-set.html` · `urp/rendering/forward-plus-rendering-path-limitations.html` (URP Config package, `MAX_VISIBLE_LIGHT_COUNT`) · `urp/lighting/light-limits-in-urp.html`
- **Render Graph:** `urp/render-graph.html` · `urp/render-graph-introduction.html` · `urp/render-graph-write-render-pass.html` · `urp/render-graph-optimize.html` · `urp/render-graph-unsafe-pass.html` · `urp/render-graph-view.html` · `urp/render-graph-viewer-reference.html` · `urp/render-graph-frame-data-reference.html`
- **Производительность и батчинг:** `urp/gpu-resident-drawer.html` · `urp/make-object-compatible-gpu-rendering.html` · `urp/gpu-culling.html` · `SRPBatcher.html` · `urp/configure-for-better-performance.html` · `urp/shader-stripping-features.html`
- **Настройки и фичи:** `urp/universalrp-asset.html` · `urp/urp-universal-renderer.html` · `urp/urp-renderer-feature.html` · `urp/urp-quality-settings-landing.html` · `urp/quality/quality-settings-through-code.html` · `urp/Volumes.html` · `urp/volumes-troubleshooting.html` · `urp/post-processing-ssao.html` · `urp/renderer-feature-decal-reference.html` · `urp/anti-aliasing.html` · `urp/stp/stp-upscaler.html` · `urp/stp/stp-enable.html` · `urp/features/motion-vectors.html` · `urp/cameras/camera-stacking-concepts.html` · `urp/features/rendering-debugger-use.html` · `urp/features/rp-converter.html`

**Пакеты и исходники**
- https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.3/manual/index.html — URP 17.3 = Unity 6.3 LTS
- https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.3/api/UnityEngine.Rendering.Universal.RenderPassEvent.html — состав enum
- https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.universal/Runtime/Passes/ScriptableRenderPass.cs — числовые значения `RenderPassEvent`

**Учебные материалы Unity**
- https://unity.com/blog/unity-6-graphics-learning-resources — подборка сэмплов, докладов и e-book по графике Unity 6
- https://unity.com/resources/introduction-to-urp-advanced-creators-unity-6 — бесплатный e-book «Introduction to the URP for advanced creators (Unity 6 edition)»

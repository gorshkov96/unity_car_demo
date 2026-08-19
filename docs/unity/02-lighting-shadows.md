# Свет и тени в URP (Unity 6.3)

Рабочая справка по освещению для нашего проекта: чем светить статичную карту, как осветить движущуюся машину, как настроить тени и чем измерять их цену. Заглядывать сюда перед созданием сцены, при настройке URP Asset и когда «тени выглядят плохо».

---

## TL;DR — решения для нашего проекта

1. **GI (глобальное освещение — учёт отражённого от поверхностей света) делаем на Adaptive Probe Volumes.** В URP Asset: `Lighting > Light Probe Lighting > Light Probe System = Adaptive Probe Volumes`, затем `GameObject > Light > Adaptive Probe Volume` с `Mode = Global`. Ручные Light Probe Groups в Unity 6 — legacy, конвертации из них в APV нет.
2. **Карта статична → запекаем лайтмапы для карты + APV для всего движущегося.** `Lighting Mode = Shadowmask` (на десктопе можно `Distance Shadowmask`). Каждый статичный меш — `Contribute Global Illumination` включён, машина и трафик — выключен.
3. **Лайтмаппер (запекатель освещения) — только Progressive GPU.** Мы на Apple Silicon, а CPU-лайтмаппер с Apple-silicon-редактором несовместим. В Unity 6.3 GPU-бэкенд и так стал дефолтным для новых проектов.
4. **Солнце — `Mode = Mixed`.** Только тогда его непрямой свет попадёт в APV и лайтмапы, а прямой останется реалтаймовым. `Realtime`-свет в APV не пишется вообще — типичная ошибка «запекли, а машина не подсвечивается».
5. **Тени солнца:** `Max Distance` держим в районе 60–90 м (не 150+), `Cascade Count = 4`, `Shadow Resolution = 2048…4096`, `Soft Shadows = Medium`, `Conservative Enclosing Sphere` включён.
6. **Bias правим на самом источнике (`Light > Shadows > Bias = Custom`), а не глобально в URP Asset.** Depth Bias лечит acne (полосатую самозатенённость), Normal Bias — тоже, но пережатый делает тень уже объекта.
7. **Отражения кузова — только Reflection Probes.** В URP нет ни SSR, ни планарных отражений (это HDRP). Ставим `Box Projection` + `Probe Blending`; для «зеркального» кузова — отдельный Realtime-проб с Time Slicing, едущий вместе с машиной.
8. **SSAO Renderer Feature включаем** — это дешёвые контактные тени под колёсами и в стыках. `Downsample` вкл., `Blur Quality = Medium`, `Samples = Medium`, `Falloff Distance` 30–50 м.
9. **Туман — обычный `RenderSettings` fog (Exponential Squared).** Объёмного света/god rays в URP из коробки нет — только свой ScriptableRenderPass или ассет.
10. **Смена времени суток — Sky Occlusion (один бейк) либо 2–3 Lighting Scenario с блендом.** Sky Occlusion требует GPU-лайтмаппера и ручной анимации ambient-цвета (в URP небо само ambient probe не обновляет).
11. **Мерить тени: Rendering Debugger → `Display Stats > Detailed Stats`** (мс на каждый проход) плюс Frame Debugger (проходы `MainLightShadow`, `AdditionalLightsShadow`, `ScreenSpaceShadows`).

---

## 1. Общая карта решений в URP 6.3

| Что | Значение для нас |
|---|---|
| Rendering Path | **Forward+** (дефолт Unity 6). Снимает лимит «9 источников на объект»; лимит на камеру — до 256 источников на десктопе. |
| Forward (обычный) | 1 Main Light + 8 Additional на объект. Нам мало: фары, фонари, стоп-сигналы быстро упрутся. |
| Deferred / Deferred+ | Нам не нужен: дороже на мобилках, нет MSAA, а туман по документации Lighting window в Deferred недоступен. |
| Light Probe System | Adaptive Probe Volumes (см. ниже) |
| Enlighten Realtime GI | **Не использовать.** Unity 6 — последний релиз с поддержкой, дальше удаляют. |

Термины одной строкой: *Main Light* — самый яркий directional-источник (наше солнце); *Additional Lights* — все остальные (point/spot); *Renderer Feature* — подключаемый в URP Renderer дополнительный проход рендера; *Volume* — компонент-«область», внутри которой действуют переопределения эффектов.

---

## 2. Adaptive Probe Volumes (APV) — основной GI в Unity 6

**Что это.** Сетка light probes (зондов освещения — точек, хранящих информацию о падающем со всех сторон свете), которую Unity расставляет автоматически по плотности геометрии. URP заполняет объём «кирпичами» (bricks): один кирпич = 64 зонда сеткой 4×4×4. Шаг зондов по умолчанию 1, 3, 9 или 27 м — гуще там, где больше геометрии.

**Главное отличие от старых Light Probe Groups: выборка идёт per-pixel, а не per-GameObject.** В документации это иллюстрируют ровно нашим кейсом — машиной, собранной из нескольких GameObject: со старыми зондами каждая деталь получала одно усреднённое значение, с APV каждый пиксель берёт свои восемь ближайших зондов. Для составного автомобиля это принципиально.

**Минимальная настройка:**

1. URP Asset → `Lighting > Light Probe Lighting > Light Probe System = Adaptive Probe Volumes`.
2. `GameObject > Light > Adaptive Probe Volume`, в инспекторе `Mode = Global` (накроет всю сцену).
3. Источники света: `Light > General > Mode = Mixed` или `Baked` — иначе они в APV не попадут.
4. Статичная геометрия: `Mesh Renderer > Lighting > Contribute Global Illumination` включить.
5. `Window > Rendering > Lighting` → вкладка `Scene`: `Mixed Lighting > Baked Global Illumination` вкл.; вкладка `Adaptive Probe Volumes`: `Baking = Single Scene` → `Generate Lighting` (или `Bake Probe Volumes`, если нужен только APV).

**Как режим света влияет на APV** (формулировка инженера графики Unity на форуме, подтверждается документацией):

- `Realtime` — свет **не влияет на APV вообще**;
- `Mixed` — в APV пишется только непрямой вклад, прямой остаётся реалтаймовым;
- `Baked` — в APV пишется всё, и прямой, и непрямой.

Смешивать realtime-источники с APV можно свободно — это ортогональные системы.

**Размер и плотность.** Размер APV меняется только ручками бокс-гизмо в Scene view (масштаб трансформа не работает). Плотность: `Override Probe Spacing` в инспекторе тома, но значения не могут выйти за `Min Probe Spacing` / `Max Probe Spacing` в панели `Adaptive Probe Volumes` окна Lighting. Для крупных пустых зон (наша земля/фон) — отдельный локальный том с большим шагом; `Override Renderer Filters` + Layer Mask позволяет исключить, например, ландшафт из расчёта позиций зондов.

**Отладка.** Rendering Debugger → вкладка `Probe Volume`: `Display Probes` (сами зонды и их свет), `Display Bricks`, `Display Cells`, `Debug Probe Sampling` (показывает веса восьми зондов для выбранной точки — лучший инструмент при разборе протечек).

**Что нам не нужно.** Disk/GPU streaming (для огромных открытых миров), загрузка APV из AssetBundles. Наша карта в память влезет целиком.

---

## 3. Запекание: лайтмаппер, профили, ловушки на macOS

**Выбор бэкенда:** `Window > Rendering > Lighting > Lightmapping Settings > Lightmapper` — `Progressive CPU` или `Progressive GPU`.

- **Apple-silicon-версия редактора несовместима с Progressive CPU Lightmapper**, совместима с Progressive GPU. То есть для нас выбора нет.
- В **Unity 6.3** GPU-лайтмаппер объявлен дефолтным бэкендом для новых проектов и новых Lighting Settings-ассетов (вышел из preview). Учтите расхождение: справочная страница `Lightmapping settings` всё ещё пишет «The default value is Progressive CPU» — доверять релиз-ноутам 6.3.
- Требования GPU-бэкенда: OpenCL 1.2, ≥2 ГБ VRAM, CPU с SSE4.1. На macOS Unity хуже определяет доступную память, поэтому откат на CPU вероятнее — а на Apple Silicon откатываться некуда. Практический вывод: держать лайтмапы небольшими.
- **Baking Profile** (новинка архитектуры Unity 6) — выбор между `lowest memory usage` (редактор остаётся отзывчивым) и `highest performance` (бейк быстрее, но редактор занят).
- **Auto Generate больше нет.** Его заменил *Interactive GI Debug Preview Mode* — недеструктивный предпросмотр в Scene view Draw Modes. Бейк теперь делает «снимок» состояния сцены в момент нажатия Generate.

**Настройки лайтмапов, которые реально влияют:**

- `Lightmap Packing = Auto` — в 6.3 новые сцены пакуют UV алгоритмом **xAtlas** (упаковывает реальные силуэты чартов, а не bounding box). Старые сцены остаются на прежнем упаковщике, чтобы не ломать раскладку. Пакует медленнее и время растёт со сценой.
- `Lightmap Resolution` — главный рычаг времени бейка: удвоение = ×4 текселей. Отдельная ловушка из документации: **слишком высокое разрешение лайтмапа может привести к ошибкам GPU-буфера при запекании APV**.
- `Use Bicubic Lightmap Sampling` (URP Graphics Settings, появилось в **Unity 6.1**) — сглаживает ступеньки на низкополигональных лайтмапах, особенно на краях теней. Требует запаса между UV-чартами: минимум 4 текселя для бикубика (для билинейного — 2), иначе получите подтекание соседних чартов.
- `Directional Mode = Directional` — второй лайтмап с направлением доминирующего света, нужен для нормалмапов; ×2 видеопамяти.
- Мелкий реквизит (мусор, знаки, конусы) не запекать: выключить `Contribute GI`, освещать зондами. Это и время бейка, и место в атласе.

**Известная проблема 6.3 (не наша платформа, но знать полезно):** на Windows в 6000.3.1/6000.3.2 сообщали о стабильных крашах GPU-бейка, связанных с DirectX 12; помогала смена порядка Graphics API. На macOS/Metal это не воспроизводится.

---

## 4. Mixed Lighting и режимы теней

`Lighting Settings Asset > Mixed Lighting > Lighting Mode` управляет всеми источниками с `Mode = Mixed`. Четыре режима по убыванию качества:

| | Baked Indirect | Shadowmask | Distance Shadowmask | Subtractive |
|---|---|---|---|---|
| Прямой свет | realtime | realtime | realtime | статика — запечён |
| Непрямой | запечён | запечён | запечён | запечён |
| Тени динамики (до Shadow Distance) | realtime | realtime | realtime | realtime, только от одного directional |
| Тени статики (до Shadow Distance) | realtime | **запечены**, до 4 источников | realtime | запечены, до 4 источников |
| Тени статики (дальше Shadow Distance) | нет | запечены | запечены | нет |

**Наш выбор — Shadowmask или Distance Shadowmask.** Distance Shadowmask даёт лучшую картинку вблизи (реалтайм-тени статики) и запечённые тени вдали — то, что нужно для города вокруг машины. Оговорка: таблица сравнения пайплайнов помечает Distance Shadowmask в URP как «Forward Rendering Path only» — если в Forward+ он поведёт себя не так, откатываемся на Shadowmask. Проверять на сцене.

Shadowmask-текстура хранит окклюзию **до четырёх источников на тексель** (RGBA). Если в точке перекрывается больше четырёх mixed-источников, лишние Unity переводит в baked. Смотреть перекрытие — Draw Mode `Light Overlap`, а плотность источников — `Lighting Complexity` в Rendering Debugger.

**Subtractive не берём:** прямой свет статики запекается, реалтайм- и запечённые тени плохо смешиваются (лечится только глобальным `Realtime Shadow Color`), спекуляр только у динамики. Это режим для мобильного лоу-энда и стилизованной графики.

---

## 5. Каскады теней

Каскады (несколько шадоумапов разного масштаба вдоль луча зрения) работают **только для directional light** и борются с перспективным алиасингом — «пикселями» у тени под ногами.

Настройки: `URP Asset > Shadows`.

- `Max Distance` — дальше этого расстояния теней нет. Шадоумап растягивается на всю эту дистанцию, поэтому уменьшение `Max Distance` вдвое эквивалентно удвоению разрешения. В документации прямой пример: 40 м / 2048 px хуже, чем 10 м / 1024 px.
- `Working Unit` — единицы, в которых задаются границы каскадов (метры или проценты). `Max Distance` всегда в метрах.
- `Cascade Count` + `Split 1..3` — границы. Больше каскадов = больше draw call'ов в теневом проходе.
- `Last Border` — зона затухания перед `Max Distance`, чтобы тени не обрывались скачком.
- `Conservative Enclosing Sphere` (в меню ⋮ → Show All Advanced Properties) — **включать**. Улучшает отсечение теневого фрустума, снижает перекрытие каскадов и число лишних статичных кастеров, то есть обычно ускоряет. Выключать только ради совместимости со старыми проектами; при включении в старом проекте придётся переподобрать дистанции каскадов.
- `Soft Shadows` + `Quality`: Low — 4 PCF-тапа, Medium — тент-фильтр 5×5 (дефолт), High — 7×7.

Визуализация: Rendering Debugger → `Lighting > Lighting Debug Mode = Shadow Cascades`.

**Практика для нашей сцены.** Камера следует за машиной на 5–15 м. Тени нужны детальные в радиусе ~30 м и «есть какие-то» до ~80 м. Стартовая точка: `Max Distance 80`, 4 каскада со сплитами примерно 7 / 18 / 40 %, `Shadow Resolution 4096`, `Soft Shadows Medium`. Дальше подрезать по замерам.

---

## 6. Bias, shadow acne, peter-panning, протечки

**Что это.** *Shadow acne* — ложная полосатая самозатенённость на освещённых поверхностях; возникает из-за конечной точности шадоумапа. *Peter-panning* — тень «отклеилась» от объекта, он словно парит. Оба лечатся bias, и оба им же вызываются, если перекрутить.

- Глобальные значения: `URP Asset > Shadows > Depth Bias`, `Normal Bias`.
- Персонально: `Light > Shadows > Bias = Custom` → `Depth` и `Normal`. **Так и делать** — глобальные значения ломают другие источники.
- `Depth` отодвигает тень от источника, `Normal` сжимает кастер вдоль нормали. Слишком большой Depth → peter-panning; слишком большой Normal → тень уже объекта плюс подтекание света из соседней геометрии.
- Паллиатив против подтекания через тонкие стены: `Mesh Renderer > Cast Shadows = Two Sided` (дороже).
- `Near Plane` (per-light, 0.1…10, дефолт 0.2) — ближняя плоскость при рендере тени. Низкое значение даёт «дырки» в тенях.
- **Shadow pancaking**: Unity поджимает ближнюю плоскость светового пространства к видимым кастерам ради точности. Побочный эффект — артефакты на очень больших треугольниках, пересекающих near plane (наша земля/дорога — как раз такой случай!). Лечится `Shadow Near Plane Offset` в Quality-настройках или тесселяцией проблемных мешей.
- **Мерцание теней вдали:** `Project Settings > Graphics > Culling Settings > Camera-Relative Culling` → включить `Shadows`. Unity начнёт считать тени относительно камеры, а не мирового нуля. Для машины, уезжающей далеко от origin, — обязательный пункт.
- Мерцание другого рода: в URP есть лимит видимых источников на камеру; при его превышении Unity гасит часть источников, и если погас теневой — тени мигают. Считать через `renderingData.cullResults.visibleLights.Length`.

Порядок настройки: сначала выставить `Max Distance` и разрешение, потом каскады, и только потом трогать bias — половина «acne» лечится дистанцией и разрешением.

---

## 7. Отражения: кузов машины

**Чего в URP нет** (по официальной таблице сравнения пайплайнов): Screen Space Reflections — нет; **Planar Reflections — нет** (только HDRP); Screen Space GI — нет; PCSS и Contact Shadows — нет. Всё, что у нас есть, — Reflection Probes и куб-мапа окружения.

**Как URP выбирает пробы.** У каждого проба есть бокс-объём; пиксель вне всех объёмов берёт отражение из скайбокса. Влиять на объект могут **максимум два проба**, выбор по `Importance`, затем по меньшему объёму, затем по площади пересечения. `Blend Distance` — расстояние от грани бокса внутрь, на котором вклад проба растёт с 0 % до 100 %. Если `Blend Distance` больше половины бокса, проб никогда не даст 100 %. В отличие от Built-in, URP считает вклад **на пиксель**, а не на объект — швов на длинном кузове не будет.

**Настройка под наш проект:**

- `URP Asset > Lighting > Reflection Probes`: `Probe Blending` вкл. (иначе отражение будет скачком «включаться» при въезде в бокс), `Box Projection` вкл. (иначе отражение не учитывает форму помещения/улицы). `Box Projection` нужно включить и в самом пробе, и в URP Asset.
- `Probe Atlas Blending` — актуально при Forward+ (по умолчанию используется, когда включён и Forward+, и GPU Resident Drawer).
- **Unity 6.3:** объём влияния reflection probe в URP теперь можно **вращать** инструментом Rotate. Для города с улицами не по осям — заметное улучшение; для новых проектов включено по умолчанию.
- Статичное окружение → пробы `Type = Baked`, расставить по перекрёсткам и под мостами.
- «Зеркальность» кузова → отдельный `Type = Realtime` проб, привязанный к машине. Обязательно `Time Slicing`: `All Faces At Once` (обновление размазано на 9 кадров) или `Individual Faces` (14 кадров). `No Time Slicing` — весь рендер шести граней в одном кадре, это гарантированный фриз. Также у проба есть свой `Shadow Distance`, перекрывающий качественные настройки на время захвата — ставить маленьким.
- Обновлять вручную: `Refresh Mode = Via Scripting` + `ReflectionProbe.RenderProbe()` раз в N кадров.

**Если нужны настоящие планарные отражения** (мокрый асфальт, зеркальный пол салона) — это ручная работа: вторая камера с зеркально отражённой матрицей рендерит в `RenderTexture`, шейдер кузова/асфальта её сэмплит по экранным координатам. В URP это делается через `ScriptableRenderPass` или отдельную камеру; готового компонента нет.

---

## 8. SSAO и screen-space тени

**SSAO** (Screen Space Ambient Occlusion — затемнение щелей и стыков) в URP — это **Renderer Feature**, а не Volume-эффект: он не зависит от Volume и не настраивается по областям.

Ключевые параметры и их цена (по документации):

| Параметр | Влияние на производительность |
|---|---|
| `Radius` | **высокое** — меньше радиус, лучше кеш-локальность |
| `Samples` (Low 4 / Medium 8 / High 12) | **высокое** — 4→8 удваивает нагрузку |
| `Blur Quality` (Low Kawase 1 проход / Medium Gaussian 2 / High Bilateral 3) | **очень высокое** |
| `Downsample` | **очень высокое** (в хорошую сторону: пикселей вчетверо меньше) |
| `Falloff Distance` | зависит от сцены — обрезает дальние объекты |
| `Source` (`Depth Normals` / `Depth`) | зависит; `Depth Normals` обычно красивее |
| `After Opaque` | средне; ускоряет, но **пересветляет/пережигает** места, где уже есть запечённая окклюзия |
| `Intensity`, `Direct Lighting Strength`, `Method` | незначительное |

Для нас: `Downsample` вкл., `Samples = Medium`, `Blur Quality = Medium`, `Falloff Distance` 30–50, `After Opaque` — **выключить**, потому что у нас есть запечённая AO в лайтмапах и мы получим двойное затемнение.

**Screen Space Shadows Renderer Feature.** Считает тени главного directional-источника из одной экранной текстуры вместо обращения к нескольким каскадным. Вид теней **не меняет**. В Forward может ускорить, но: добавляет depth prepass (лишний проход, больно для tile-based GPU) и держит дополнительную текстуру в памяти. Прозрачные объекты всё равно идут через шадоумапы. Проверять A/B, не включать «на всякий случай».

---

## 9. Туман и объёмный свет

**Туман в URP — это классический `RenderSettings`-туман**, а не post-process: `Window > Rendering > Lighting > Environment > Other Settings > Fog`. Режимы: `Linear` (Start/End), `Exponential` (Density), `Exponential Squared`. Ключевые слова шейдеров — `FOG_LINEAR`, `FOG_EXP`, `FOG_EXP2`; в Unity 6.1+ их можно перевести на динамическое ветвление (`Project Settings > Graphics > Shader Build Settings`, `Type Override = dynamic_branch` для набора `_ FOG_LINEAR FOG_EXP FOG_EXP2`) — меньше вариантов шейдеров и время сборки. То же доступно для `_REFLECTION_PROBE_BLENDING` и `_REFLECTION_PROBE_BOX_PROJECTION`.

По документации Lighting window туман **недоступен в Deferred rendering path** — ещё один довод за Forward+.

**Объёмного тумана и light shafts в URP нет.** Local Volumetric Fog — фича HDRP (в 6.2 ей добавили режим `Scale` и снизили алиасинг от punctual-источников, но это не про нас). Варианты для демонстрации «столбов света»:

- свой `ScriptableRenderPass` с raymarching (в сообществе есть открытые реализации под render graph Unity 6);
- готовые ассеты (BEAM, HAZE и аналоги);
- дешёвый фейк: конусы с аддитивным шейдером у фонарей + light cookies.

Приятный бонус APV: партиклы VFX Graph в URP получают непрямое освещение из APV — дым за машиной будет подсвечен окружением бесплатно.

---

## 10. Rendering Layers (бывшие Light Layers)

В Unity 6 это называется **Rendering Layers**; «Light Layers» — старое имя, в новой документации не используется.

- Включение: `URP Asset > Lighting`, меню ⋮ → `Advanced Properties`, затем `Use Rendering Layers`.
- Имена слоёв: `Project Settings > Tags and Layers > Rendering Layers`.
- На источнике: `Light > Rendering > Rendering Layers`. На объекте: `Mesh Renderer > Additional Settings > Rendering Layer Mask`. Свет действует на объект, если маски пересекаются.
- `Light > Shadows > Custom Shadow Layers` — объект не освещается этим источником, но **тень от него отбрасывает**. Полезно, когда нужно «выключить» свет для объекта, не потеряв его тень на дороге.
- Цена: для света в Forward — незначительна; для декалей растёт. Скачки производительности при переходе через кратные 8 (9, 17, 25 слоёв) — URP добавляет ещё один канал текстуры.

**Rendering Layer Masks для APV** (появились в 6000.1) — отдельная механика: в Baking Set задаётся **до четырёх** масок, при бейке Unity сам приписывает каждому зонду маску по окружающей геометрии, а в рантайме объект приоритетно сэмплит зонды своей маски. Классический рецепт «Interior / Exterior», который убирает протечку света сквозь стены даже при низкой плотности зондов. Требует включённого `Use Rendering Layers` в URP Asset; увеличивает время бейка и память. Локальные исключения — через `Probe Adjustment Volume` с `Mode = Override Rendering Layer Mask` (операции Override / Add / Remove).

---

## 11. Динамическая машина на статичной запечённой карте

Пошагово, что должно быть настроено:

1. **Машина и её части:** `Contribute Global Illumination` — выключено, объект не static. Тогда она освещается из APV автоматически, per-pixel.
2. `Mesh Renderer > Cast Shadows = On`, `Receive Shadows = On`.
3. Если кузов — высокополигональная модель, сделать **упрощённый теневой прокси**: копия low-poly в той же позиции с `Cast Shadows = Shadows Only`, а у оригинала `Cast Shadows = Off`. Материал прокси не важен — он не рендерится.
4. **Статичная карта:** `Contribute GI` включён, `Receive Global Illumination = Lightmaps`. Мелочёвка — `Receive Global Illumination = Light Probes`.
5. **Фары/стопы** — additional lights. Тени от них дороги: point light = шесть шадоумапов (эквивалент шести spot). Фары делать `Spot`, тени включать максимум у двух передних. Габариты и стопы — без теней вообще, лучше emissive-материал.
6. Гасить далёкие источники скриптом (`Light.enabled`), плавно уводя `intensity`, чтобы не было «щелчка». Пример такого контроллера есть в официальном сэмпле Universal 3D (сцена Garden, скрипт `DynamicLightController.cs`).
7. Шадоу-атлас additional lights: `URP Asset > Lighting > Additional Lights > Shadow Atlas Resolution`. Атлас 1024×1024 вмещает 16 карт 256×256 или 4 карты 512×512. Считать по формуле «сколько теневых источников одновременно в кадре».

```csharp
// Тени только у ближних источников. Ссылки прокинуты через [SerializeField], поиска в кадре нет.
private void Update()
{
    Vector3 carPos = _car.position;
    float r2 = _shadowRange * _shadowRange;
    for (int i = 0; i < _lights.Length; i++)
    {
        bool near = (_lights[i].transform.position - carPos).sqrMagnitude < r2;
        _lights[i].shadows = near ? LightShadows.Soft : LightShadows.None;
    }
}
```

---

## 12. Смена времени суток

Три варианта, от дешёвого к точному.

**А. Полный realtime.** Просто крутим directional light, GI не меняется вообще. Дёшево, но непрямой свет останется «дневным» — в тенях будет вечно голубой отсвет полудня.

**Б. Sky Occlusion (рекомендую как основной).** Один бейк; в каждый зонд APV дополнительно пишется, сколько света он получает с неба. В рантайме цвет неба берётся из ambient probe и умножается на эту статическую окклюзию.

- Включается в `Lighting > Adaptive Probe Volumes > Sky Occlusion Settings`. Параметры: `Samples` (дефолт 2048), `Bounces` (2), `Albedo Override` (0.6 — Unity считает все поверхности серыми, этим значением правится яркость).
- **Требует Progressive GPU Lightmapper** — с CPU не поддерживается. Нам это не мешает.
- `Sky Direction` — точнее (учитывает, откуда реально пришёл свет), но дольше бейк, больше памяти и возможны швы: направление между зондами не интерполируется.
- **Важная особенность URP:** при `Environment > Source = Skybox` ambient probe **сам не обновляется** при смене неба. Нужно ставить `Gradient` или `Color` и анимировать цвета вручную. Вариант с `Skybox` + `DynamicGI.UpdateEnvironment()` документация прямо называет очень дорогим.
- Прозрачные объекты (стёкла, листва) при расчёте окклюзии считаются непрозрачными — у них лучше снять `Contribute GI`.

```csharp
// Анимация «неба» для Sky Occlusion: ambient-градиент, туман, отражения.
// В Start(): RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight; // = Gradient
public void SetTime(float t01)                            // 0..1 — сутки
{
    _sun.transform.rotation = Quaternion.Euler(t01 * 360f - 90f, 30f, 0f);
    RenderSettings.ambientSkyColor     = _skyColor.Evaluate(t01);
    RenderSettings.ambientEquatorColor = _horizonColor.Evaluate(t01);
    RenderSettings.ambientGroundColor  = _groundColor.Evaluate(t01);
    RenderSettings.fogColor            = _fogColor.Evaluate(t01);
    _skyProbe.RenderProbe();                              // отражения сами не обновятся
}
```

**В. Lighting Scenarios.** Несколько полных бейков APV (утро/день/ночь), переключение и блендинг в рантайме. Точнее всего: учитывается и цвет неба, и цвет отражающих поверхностей, и свет ламп.

- Включить: `URP Asset > Lighting > Light Probe Lighting > Lighting Scenarios`, для блендинга дополнительно `Scenario Blending`.
- Сценарии создаются в панели `Adaptive Probe Volumes` окна Lighting (кнопка `+`), бейк идёт в активный сценарий.
- **Критично:** чтобы бленд работал, позиции зондов во всех сценариях должны совпадать. Перед вторым бейком поставить `Probe Placement > Probe Positions = Don't Recalculate`.
- Сценарии меняют **только непрямой свет в зондах**. Небо, направление солнца, туман, отражения — обновлять скриптом.

```csharp
using UnityEngine.Rendering;   // ProbeReferenceVolume

private ProbeReferenceVolume _apv;

private void Start()
{
    _apv = ProbeReferenceVolume.instance;
    _apv.lightingScenario = _day;                        // string — имя сценария
    _apv.numberOfCellsBlendedPerFrame = _cellsPerFrame;  // размазываем стоимость по кадрам
}

private void Update() => _apv.BlendLightingScenario(_night, _blend); // _blend: 0..1
```

Предпросмотр перехода без запуска игры: Rendering Debugger → `Scenario Blend Target` + `Scenario Blending Factor`.

Sky Occlusion и Lighting Scenarios комбинируются: небо — через окклюзию, включение уличных фонарей — через сценарий.

---

## 13. Цена теней в кадре и как её мерить

**Что определяет стоимость** (по документации URP):

- число видимых объектов, отбрасывающих тени (зависит от `Max Distance`);
- число объектов, принимающих тени;
- число теневых источников (point = 6 рендеров сцены);
- `Cascade Count` и размеры сплитов;
- разрешение шадоумапов main и additional;
- `Soft Shadows` (особенно больно на tile-based GPU).

**Инструменты, в порядке применения:**

1. **Rendering Debugger → `Display Stats`** (только в Play Mode). `Frame Stats`: GPU Frame, CPU Main/Render Thread. `Bottlenecks`: доля кадров, ограниченных CPU / GPU / презентацией — сначала понять, во что упёрлись. `Detailed Stats`: **время каждого шага рендера в мс отдельно по CPU и GPU** — это и есть прямой ответ «сколько стоят тени».
2. **Frame Debugger** (`Window > Analysis > Frame Debugger`) — увидеть проходы `MainLightShadow`, `AdditionalLightsShadow`, `ScreenSpaceShadows`, их размеры текстур и число draw call'ов в теневом проходе.
3. **Render Graph Viewer** — какие ресурсы кадра создаются и живут; помогает понять, что тени тянут за собой лишние текстуры.
4. **GPU Usage Profiler module** + Highlights module (в 6.3 у Highlights появилась детальная панель с разбивкой CPU/GPU по категориям).
5. **A/B без пересборки:** Rendering Debugger → `Lighting > Lighting Features` — флажками отключать `Main Light`, `Additional Lights`, `Emission` и смотреть дельту. Плюс `Lighting Debug Mode = Shadow Cascades` для проверки, что каскады покрывают ровно то, что нужно.
6. Rendering Debugger → `Rendering > LightingComplexity` — тепловая карта числа источников на пиксель. `RenderingLayerMasks` — проверка, что маски слоёв расставлены как задумано.

**Методика измерения одной настройки:** зафиксировать позицию камеры (лучше — воспроизводимый прогон по треку), выключить VSync (иначе Bottlenecks всегда покажет Present Limited), снять `Detailed Stats` по 30 кадрам, изменить один параметр, снять снова. Менять по одному.

---

## Антипаттерны

- **Расставлять Light Probe Group руками.** В Unity 6 это legacy-система: хуже качество (per-object), нет стриминга, нет блендинга бейков, и конвертации в APV не существует. Ставить APV сразу.
- **Включать Enlighten Realtime GI «чтобы свет был динамическим».** Unity 6 — последний релиз с его поддержкой. Динамика — через Sky Occlusion / Lighting Scenarios.
- **Оставить солнце `Realtime` и удивляться, что APV пустой.** В APV попадает только `Mixed`/`Baked`.
- **Крутить `Depth Bias` в URP Asset, пока не исчезнет acne.** Так получают peter-panning на всей сцене. Правильный порядок: `Max Distance` → разрешение → каскады → и только потом per-light `Bias = Custom`.
- **`Max Distance = 500`, «чтобы тени были везде».** Шадоумап растянется, вблизи всё станет мылом, а число кастеров вырастет линейно. Тени вдали — задача Shadowmask, а не каскадов.
- **Много point light с тенями.** Каждый — шесть рендеров сцены. Для фонарей вдоль дороги — spot с cookie без теней, либо запечённые тени.
- **Искать в URP «Screen Space Reflections» и «Planar Reflection Probe».** Их там нет, это HDRP. Советы из HDRP-туториалов сюда не переносятся.
- **Ждать `Auto Generate` в окне Lighting.** Его убрали в Unity 6, заменив Interactive GI Debug Preview. И настраивать тени в `Project Settings > Quality`, как в Built-in: в URP почти всё это живёт в URP Asset — старые гайды 2019–2021 ведут не туда.
- **Ставить `Min Probe Spacing` крохотным «для качества».** Растёт время бейка и размер данных, а протечки от этого не проходят — их лечат Rendering Layer Masks, Virtual Offset, Dilation и Probe Adjustment Volume. На форумах регулярно наоборот: увеличение шага убирало пятна и протечки.
- **SSAO с `After Opaque` поверх запечённой AO.** Документация прямо предупреждает о пересветлении таких зон. Там же: **`No Time Slicing` у realtime reflection probe** — шесть граней куб-мапы в одном кадре — гарантированный фриз.
- **Считать `Subtractive` «быстрым режимом для всего».** Он лишает статику спекуляра и корректного смешивания теней.

---

## Чек-лист

Настройка проекта:

- [ ] Rendering Path = Forward+ в URP Renderer.
- [ ] `Light Probe System = Adaptive Probe Volumes` в URP Asset.
- [ ] `Lightmapper = Progressive GPU` (на Apple Silicon альтернативы нет).
- [ ] `Use Rendering Layers` включён (нужен и для APV-масок).
- [ ] `Conservative Enclosing Sphere` включён.
- [ ] `Camera-Relative Culling > Shadows` включён.
- [ ] `Use Bicubic Lightmap Sampling` включён (URP Graphics Settings).

Сцена:

- [ ] Один Adaptive Probe Volume с `Mode = Global`; локальные тома с большим шагом для пустых зон.
- [ ] Солнце: `Mode = Mixed`, `Shadow Type = Soft`, `Bias = Custom`.
- [ ] `Lighting Mode = Shadowmask` (или Distance Shadowmask, если подтвердится в Forward+).
- [ ] Статика: `Contribute GI` вкл.; мелочёвка — `Receive GI = Light Probes`.
- [ ] Машина и трафик: не static, `Contribute GI` выкл., `Cast/Receive Shadows` вкл.
- [ ] Теневые прокси для сложных мешей (`Shadows Only`).
- [ ] Reflection probes: Baked по карте + Realtime с Time Slicing на машине; `Probe Blending` и `Box Projection` вкл.
- [ ] SSAO Renderer Feature добавлен, `After Opaque` выкл.
- [ ] Туман включён (`Exponential Squared`), цвет анимируется вместе с временем суток.

Проверка:

- [ ] Rendering Debugger → `Probe Volume > Display Probes`: нет чёрных (невалидных) зондов внутри геометрии.
- [ ] `Lighting Debug Mode = Shadow Cascades`: каскады покрывают зону, где реально нужны детальные тени.
- [ ] `Display Stats > Detailed Stats`: известна цена теневых проходов в мс.
- [ ] Frame Debugger: нет неожиданного `ScreenSpaceShadows` / лишнего depth prepass.
- [ ] Сборка (не только редактор) выглядит так же — расхождения «редактор vs билд» по свету встречаются регулярно.

---

## Видео и доклады

- New lighting features and workflows in Unity 6 — Unity (Maxime Grange, David Llewelyn, Rémi Chapelain) — https://www.youtube.com/watch?v=IpVuIZYFRg4 — официальный разбор APV в Unity 6: настройка, превью, борьба с протечками, Scenario Blending и стриминг. Самое полезное видео по теме.
- Efficient and impactful lighting with Adaptive Probe Volumes | Unity at GDC 2023 — Unity — https://www.youtube.com/watch?v=iU7X5xICkc8 — концепция APV, отличия от старых зондов, reflection probe normalization и инструменты отладки протечек.
- Glow up your graphics with Unity 6.3 LTS and beyond | Unite 2025 — Unity — https://www.youtube.com/watch?v=K3-wPnhmDi4 — что нового в графике 6.3: улучшения APV, повёрнутые reflection probes в URP, оптимизации; плюс анонс Surface Cache.
- How to: Excellently Light a Scene with Unity's Universal Render Pipeline — Unity — https://www.youtube.com/watch?v=mjputyz5Mok — практический стрим по постановке света именно в URP.
- 👉 Why URP Shadows Look Worse in Unity 6 (And How to Improve Them) — Ketra Games — https://www.youtube.com/watch?v=JJUQZSnvK80 — сравнение дефолтных настроек теней URP в 2022 и Unity 6 и что крутить, чтобы вернуть качество. Полезно ровно потому, что мы стартуем с шаблона.
- Fix Light Leaking in Unity 6 (Adaptive Probe Volumes Explained) — Cam Ayres — https://www.youtube.com/watch?v=7_TU4TK9Q0I — разбор причин протечек в APV: плотность зондов, размер кирпичей, размещение тома.
- Adaptive Probe Volumes: Scenario Blending for Time of Day Sequences — Cam Ayres — https://www.youtube.com/watch?v=qxXf0jeQnX0 — практика Lighting Scenarios под смену времени суток, включая управление из рантайма.
- Adaptive Probe Volumes vs Lightmaps — Quad Art — https://www.youtube.com/watch?v=IIzH4EiDd94 — наглядное сравнение: что запекать в лайтмапы, а что отдать зондам.
- Unity 6 Lightmapping Tutorial: Modular Room Workflow — Mythmatic — https://www.youtube.com/watch?v=-nqZfFUzAL8 — рабочий процесс запекания лайтмапов в Unity 6, UV-чарты и разрешение.
- How to use Reflection Probes well | Unity Environment Art (URP & HDRP) — Thiago Klafke — https://www.youtube.com/watch?v=iGw5auyCvwc — расстановка reflection probes глазами энвайронмент-художника; напрямую применимо к отражениям на кузове.
- Introduction to the Render Graph in Unity 6 — Unity — https://www.youtube.com/watch?v=U8PygjYAF7A — нужно, если будем писать свой проход (объёмный свет, планарные отражения).
- Boosting your game performance with Unity 6 Profiling tools | Unite 2024 — Unity — https://www.youtube.com/watch?v=_cV1B2hqXGI — методика профилирования, применимая к замеру цены теней.

---

## Источники

Дата обращения — 2026-08-19. Документация Unity — версия 6.3 LTS (6000.3), если не указано иное.

Документация Unity (Manual, 6000.3):

- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/probevolumes.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/probevolumes-concept.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/probevolumes-use.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/probevolumes-changedensity.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/probevolumes-options-override-reference.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/probevolumes-adjustment-volume-component-reference.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/probevolumes-troubleshoot-light-leaks.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/probevolumes-bakedifferentlightingsetups.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/probevolumes-skyocclusion.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/probevolumes-streaming.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/features/rendering-layer-masks-apv-prevent-light-leaks.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/features/rendering-layers-introduction.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/features/rendering-layers-lights.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/shadow-resolution-urp.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/shadows-troubleshooting-urp.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/shadows-optimization.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/shadow-cascades-visualize.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/shadow-cascades-performance.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/ShadowPerformance.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/renderer-feature-screen-space-shadows.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/ssao-renderer-feature-reference.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/lighting/reflection-probes-introduction.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/class-ReflectionProbe.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/lighting/lightmapping-improve-visual-fidelity.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/lighting/light-limits-in-urp.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/rendering-paths-comparison.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/light-component.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/features/rendering-debugger-reference.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/shader-stripping-fog.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/whats-new/urp-whats-new.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/lighting-mode.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/lighting-window.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/progressive-lightmapper.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/GPUProgressiveLightmapper.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/Lightmaps-reference.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/class-MeshRenderer.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/render-pipelines-feature-comparison.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/WhatsNewUnity63.html

Scripting API:

- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rendering.AmbientMode.html
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/ReflectionProbe.RenderProbe.html
- https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.4/api/UnityEngine.Rendering.ProbeReferenceVolume.html

Блог и обсуждения:

- https://unity.com/blog/engine-platform/new-ways-of-applying-global-illumination-in-unity-6 — обзорная статья Unity по GI в Unity 6: APV, Leak Reduction Modes, Baking Profile, отказ от Auto Generate, депрекация Enlighten Realtime GI.
- https://discussions.unity.com/t/hdrp-6-adaptive-probe-volumes-causing-performance-regression-vs-realtime-lighting/1672413 — тред с ответами инженера Unity: как Realtime/Mixed/Baked влияют на APV, когда нужен Sky Occlusion, когда — Lighting Scenarios.
- https://discussions.unity.com/t/unity-light-baking-crash-on-unity-6000-3-2f1/1701124 — краши GPU-бейка в 6000.3.1/6000.3.2 на Windows/DirectX 12.
- https://discussions.unity.com/t/persistent-glitches-light-leaks-with-adaptive-probe-volumes/1705764 — типичные жалобы на протечки APV; https://discussions.unity.com/t/apv-out-of-memory-at-bake/1718789 — нехватка памяти при бейке APV.
- https://discussions.unity.com/t/surface-cache-gi-preview/1720494 — превью Surface Cache GI, нового динамического GI Unity. В 6.3 его нет, релиз анонсирован в 6.7 LTS — учитывать при планировании, но не рассчитывать на него сейчас.

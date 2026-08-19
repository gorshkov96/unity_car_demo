# Камеры: Cinemachine 3.x для автомобиля

Как настроить камеру машины в Unity 6.3 на Cinemachine 3.1: что переименовали относительно 2.x, чем следовать за машиной, почему дрожит картинка и как трясти камеру при ударах. Заглядывать сюда перед тем, как ставить пакет, перед копированием любого туториала из интернета и каждый раз, когда камера ведёт себя не так.

## TL;DR — решения для нашего проекта

1. **Ставим `com.unity.cinemachine` 3.1.7** — это версия, выпущенная для Unity 6000.3. Cinemachine не входит в шаблон, его надо доустановить через Package Manager. Версии 3.0.x — предрелизные ветки, 2.10.7 — легаси.
2. **Любой туториал старше конца 2023 года — под Cinemachine 2.x и здесь не работает.** `CinemachineVirtualCamera`, вкладки Body/Aim, скрытый дочерний объект `cm`, `using Cinemachine;` — всё это мертво. Таблица соответствий — ниже, сверяться с ней обязательно.
3. **Основная камера машины: `CinemachineCamera` + `CinemachineOrbitalFollow` (Position Control) + `CinemachineRotationComposer` (Rotation Control).** Binding Mode = `Lock To Target With World Up`, Recentering Target = `Tracking Target`, Recentering включён по горизонтали (Wait ≈ 1 c, Time ≈ 1.5 c). Это даёт классическую аркадную камеру за машиной, которую игрок может покрутить мышью, а она сама возвращается за корму.
4. **Камера НЕ должна быть дочерним объектом машины.** Отдельный GameObject в корне сцены, машина указывается в поле Tracking Target. Иначе получите двойную трансформацию и гарантированное дрожание.
5. **Brain Update Method = `Smart Update` (дефолт), Rigidbody машины = `Interpolate`, и 100 % движения и поворота машины — только в `FixedUpdate`.** Это и есть весь рецепт от дрожания. Смешивание физики и `Update` — источник примерно всех жалоб на «Cinemachine дёргается».
6. **Столкновения камеры с миром: `CinemachineDecollider`, а не `CinemachineDeoccluder`.** Машина едет по земле, камера низко — нам нужно вытолкнуть камеру из геометрии и не дать провалиться под террейн, а не сохранять линию взгляда любой ценой. Deoccluder добавляем позже, если появятся тоннели и мосты.
7. **Тряска при ударах — только через Impulse.** `CinemachineCollisionImpulseSource` на кузове (Impulse Type = Dissipating, Shape = Bump) + `CinemachineImpulseListener` на камере. Двигать трансформ камеры руками нельзя — Cinemachine его перезапишет.
8. **FOV от скорости встроенного компонента не имеет.** `CinemachineFollowZoom` — это про другое (держит постоянный экранный размер объекта). Пишем свой `CinemachineExtension` на 20 строк, код ниже.
9. **Кинематографичные пролёты — `CinemachineSplineDolly` на нативном `SplineContainer`** + `CinemachineSplineSmoother` на сплайне. Старые `CinemachinePath` / `CinemachineSmoothPath` / `CinemachineDollyCart` не удалены, но объявлены deprecated — в новом коде не использовать.
10. **Переключение камер: `Priority` / `Prioritize()` + Custom Blends asset.** Учесть: в ассете блендов камеры указываются **по имени строкой**, а не ссылкой — переименование камеры молча ломает бленд.

## 1. Версии и установка

| Что | Значение | Источник |
|-----|----------|----------|
| Unity 6.3 LTS (6000.3) | Cinemachine **3.1.7** (released) | Unity Manual 6000.3 |
| Доступно также в 6000.3 | 3.1.0–3.1.7, 3.0.x, 2.10.7 | там же |
| Минимальный редактор для CM 3.1 | 2022.3 LTS и новее | Installation and upgrade |
| Namespace | `Unity.Cinemachine` (было `Cinemachine`) | What's new |

Cinemachine — отдельный пакет, ставится через **Window → Package Manager → Unity Registry → Cinemachine**. После установки появляется меню `GameObject → Cinemachine`.

Отдельно: в changelog есть версия **6.6.0 от 2026-05-08** — это не «Cinemachine 6», а следствие того, что в свежих редакторах (6.6) Cinemachine стал core-пакетом с версией, совпадающей с версией редактора; архитектурно это по-прежнему Cinemachine 3. **Нас это не касается: под 6000.3 ставится 3.1.7.**

Стоит сразу импортировать сэмплы (Package Manager → Cinemachine → Samples): для нас релевантны **Brain Update Modes** (наглядно про дрожание), **Mixing Camera** (там как раз бленд группы камер в зависимости от скорости машины), **Split Screen Car**, **Impulse Wave**, **FreeLook Deoccluder**.

## 2. Cinemachine 2.x → 3.x: таблица соответствий

Главная ловушка темы: подавляющее большинство роликов и статей в выдаче написано под 2.x, а там другие имена компонентов, другая структура объекта и другой namespace.

### 2.1. Заменённые компоненты (старые остались, но deprecated)

| Cinemachine 2.x | Cinemachine 3.x |
|---|---|
| `CinemachineVirtualCamera` | `CinemachineCamera` |
| `CinemachineFreeLook` | `CinemachineCamera` (+ `CinemachineOrbitalFollow`) |
| `CinemachineTransposer` | `CinemachineFollow` |
| `CinemachineOrbitalTransposer` | `CinemachineOrbitalFollow` |
| `CinemachineFramingTransposer` | `CinemachinePositionComposer` |
| `CinemachineComposer` | `CinemachineRotationComposer` |
| `CinemachinePOV` | `CinemachinePanTilt` |
| `CinemachineTrackedDolly` | `CinemachineSplineDolly` |
| `CinemachineGroupComposer` | `CinemachineGroupFraming` + `CinemachineRotationComposer` |
| `CinemachineCollider` | `CinemachineDeoccluder` |
| `CinemachineConfiner` | `CinemachineConfiner2D` / `CinemachineConfiner3D` |
| `Cinemachine3rdPersonFollow` | `CinemachineThirdPersonFollow` |
| `CinemachineSameAsFollowTarget` | `CinemachineRotateWithFollowTarget` |
| `CinemachinePath`, `CinemachineSmoothPath` | `SplineContainer` (нативные Unity Splines) |
| `CinemachineDollyCart` | `CinemachineSplineCart` |

Просто переименованы: `Cinemachine3rdPersonAim` → `CinemachineThirdPersonAim`, `CinemachineBlendListCamera` → `CinemachineSequencerCamera`.

### 2.2. Что ещё сломается в чужом коде

- `using Cinemachine;` → `using Unity.Cinemachine;` (редакторный — `Unity.Cinemachine.Editor`; `Cinemachine.Utility` растворился в основном).
- **Префикс `m_` убран у всех полей.** `m_Lens` → `Lens`, `m_FollowOffset` → `FollowOffset`. В 90 % случаев починка = удалить `m_`.
- `SimpleFollowWithWorldUp` (binding mode) переименован в **`LazyFollow`**; `MoveToTopOfPrioritySubqueue()` → `Prioritize()`.
- **`CinemachineCore.Instance` удалён** — всё стало статическими членами `CinemachineCore`; исключения `ActiveBrainCount` и `GetActiveBrain()` переехали в `CinemachineBrain`.
- **`GetCinemachineComponent<T>()` больше не нужен**: в 3.x нет скрытого дочернего объекта `cm`, процедурные компоненты висят прямо на GameObject камеры и берутся обычным `GetComponent<T>()`.
- **Фильтрация камер по Unity Layers заменена на Cinemachine Channels** (нужно только для split-screen, у нас — `Default`).
- **События переехали:** глобальные шлёт `CinemachineCore`; для событий конкретной камеры или Brain добавляются компоненты `Cinemachine Camera Events` / `Cinemachine Brain Events`.
- **`Lens Mode Override`** (перспектива/орто/физическая камера) по умолчанию выключен и требует включения в `CinemachineBrain` + выбора Default Mode.

### 2.3. Терминология инспектора

Вкладки **Body / Aim** переименованы в **Position Control / Rotation Control**. Важная деталь для скриптов: **внутренний enum не менялся** — это по-прежнему `CinemachineCore.Stage.Body`, `.Aim`, `.Noise`, `.Finalize`. UI говорит «Position Control», а код — `Stage.Body`.

Ещё упрощение: у камеры теперь **одна цель `Tracking Target`** (и «за кем ехать», и «на что смотреть»); отдельный `Look At Target` включается кнопкой справа от поля, только когда реально нужен.

## 3. Анатомия камеры в Cinemachine 3

- **`CinemachineBrain`** — на настоящей Unity-камере (`Camera`). Выбирает, какая CM-камера Live, и выполняет переход. Одна на сцену (больше — только для split-screen).
- **`CinemachineCamera`** — пустой GameObject-«режиссёрский указатель», сам по себе пассивен.
- **Position Control** (один компонент): `Follow`, `Orbital Follow`, `Third Person Follow`, `Position Composer`, `Hard Lock to Target`, `Spline Dolly`.
- **Rotation Control** (один компонент): `Rotation Composer`, `Hard Look At`, `Pan Tilt`, `Rotate With Follow Target`.
- **Noise** — «дрожание ручной камеры»; считается отдельным каналом и не влияет на damping.
- **Extensions** — надстройки поверх результата: `Deoccluder`, `Decollider`, `Impulse Listener`, `Follow Zoom`, `Volume Settings`, `Recomposer`, `Confiner3D`, `Group Framing`.

Порядок стадий пайплайна: Body (позиция) → Aim (поворот) → Noise → Finalize (стадия extensions).

## 4. Follow-камера для машины

### 4.1. Какой Position Control выбрать

**`CinemachinePositionComposer` для погонной камеры машины брать не надо**, хотя в подборках он часто мелькает: он держит цель в заданной точке экрана и **не поворачивает камеру вовсе**, а документация позиционирует его как решение для 2D и ортографии. Его дед-зоны и look-ahead пригодятся разве что для вида сверху. Реальные варианты:

| Компонент | Что делает | Когда для машины |
|---|---|---|
| `CinemachineFollow` | Держит фиксированный офсет от цели + damping | Простейшая «камера на палке». Минимум настроек, годится как первый шаг |
| `CinemachineOrbitalFollow` | Позиция на сфере или на поверхности из трёх орбит вокруг цели; оси Horizontal/Vertical/Radius можно крутить вводом; есть **Recentering** | **Рекомендация.** Игрок может осмотреться, камера сама возвращается за корму |
| `CinemachineThirdPersonFollow` | Жёсткий риг, вращающийся вместе с целью, с плечевым офсетом | Для персонажа-шутера; для машины даёт слишком «приклеенное» ощущение |
| `CinemachineHardLockToTarget` + `CinemachineRotateWithFollowTarget` | Камера = позиция и поворот цели | Камера из кокпита / с капота, крепится на пустышку внутри машины |
| `CinemachineSplineDolly` | Камера едет по сплайну | Кинематографичные пролёты, реплеи |

### 4.2. Binding Mode — самая важная настройка

Binding Mode определяет систему координат, в которой интерпретируются офсет и damping. Есть у `Follow` и `OrbitalFollow`:

| Режим | Поведение | Для машины |
|---|---|---|
| `World Space` | Офсет в мировых координатах; поворот цели камеру не двигает | Камера-«стойка», машина крутится перед ней |
| `Lock To Target` | Полная локальная система цели, включая крен и тангаж | Плохо: машина клюнет носом — камеру кувыркнёт |
| `Lock To Target No Roll` | То же, но крен обнулён | Компромисс для трамплинов |
| `Lock To Target With World Up` | Локальная система цели, но tilt и roll обнулены — учитывается **только рыскание (yaw)** | **Наш выбор.** Классическая аркадная камера: заезжает за корму при повороте, но не переворачивается на кочках |
| `Lock To Target On Assign` | Ориентация берётся один раз при активации камеры | Для фиксированных кинематографичных ракурсов |
| `Lazy Follow` | Офсет и damping в системе координат камеры; камера двигается минимально, сохраняя дистанцию и высоту, но не пытается встать за корму | Отлично для дрифта: в заносе видно машину боком |

Быстрый рецепт вкуса: `Lock To Target With World Up` = «гоночная аркада», `Lazy Follow` = «камера оператора, которому лень бегать».

### 4.3. Damping и ощущение скорости

Damping в Cinemachine — это **приблизительное время в секундах, за которое камера догоняет желаемую позицию**. Меньше = отзывчивее, больше = «тяжелее». Позиционный damping задаётся отдельно по трём осям, и именно это даёт настройку ощущения:

- **Z (вдоль движения)** — главный рычаг «ощущения скорости»: при разгоне машина визуально отрывается от камеры, при торможении наезжает. 0.4–0.8 читается как ускорение; выше 1.2 машина начинает уезжать из кадра.
- **X** — гасит рывки при перекладках руля; 0.3–0.6, иначе на слаломе камеру мотает. **Y** — гасит кочки и работу подвески, обычно самый большой: 0.6–1.0.
- **Rotation Damping (Yaw)** — насколько лениво камера заезжает за корму в повороте; второй по важности параметр «характера». 0.2 = приклеена к оси машины (укачивает), 1.0–2.0 = машина эффектно вписывается в поворот перед камерой.
- **Angular Damping Mode**: `Euler` даёт раздельные Pitch/Roll/Yaw, но подвержен gimbal lock; `Quaternion` — один параметр без gimbal lock, для машины с трамплинами безопаснее.

Важное предупреждение мейнтейнера Cinemachine (Gregoryl, форум Unity): **damping очень чувствителен к неравномерному фреймрейту и усиливает его**. Отсюда два следствия: нулевой damping — не «максимальная отзывчивость», а усилитель микродрожания физики; а оценивать плавность надо **в билде**, потому что редактор сам по себе даёт неровный кадр.

### 4.4. Look-ahead (предсказание движения)

Look-ahead есть **только у `PositionComposer` и `RotationComposer`** — у `Follow`/`OrbitalFollow` его нет. Он сдвигает кадр в точку, где цель окажется через N секунд.

Документация прямо предупреждает: **фича чувствительна к «шумной» анимации и усиливает шум, вызывая дрожание камеры**. Машина на физике — как раз шумный источник. Поэтому `Lookahead Time` держим маленьким (0.15–0.3 с) и только на `RotationComposer` — упреждение взгляда «в поворот», не позиции; `Lookahead Smoothing` поднимаем до 3–10 (сглаживает предсказание ценой задержки); `Lookahead Ignore Y` включаем. Если дрожит — первым делом выключить look-ahead, а не крутить damping.

### 4.5. Rotation Control для машины

- `CinemachineRotationComposer` — рабочий выбор. `Screen Position` по Y смещаем немного вниз (машина ниже центра кадра, видно больше дороги). `Dead Zone` небольшой (0.05–0.15): камера не реагирует на микродвижения, но не отстаёт.
- `CinemachineHardLookAt` — жёстко центрирует цель, без damping и композиции. Годится для отладки и для камер-«штативов» у трассы.
- `CinemachineRotateWithFollowTarget` — для камеры из кокпита в паре с `HardLockToTarget`.

## 5. Дрожание картинки: FixedUpdate против LateUpdate

Это тема номер один во всех тредах про Cinemachine, и в проекте с физической машиной она встанет обязательно.

### 5.1. Откуда берётся дрожание

Физика считается в `FixedUpdate` с фиксированным шагом (0.02 с = 50 Гц по умолчанию), а картинка рисуется в произвольном темпе (60/120/144 Гц). Если камера читает позицию машины в момент, не совпадающий с физическим шагом, она видит «устаревшую» позицию и потом резко догоняет — это и есть джиттер, а damping его усиливает.

### 5.2. Update Method у CinemachineBrain

| Режим | Что делает |
|---|---|
| `Fixed Update` | Обновлять CM-камеры синхронно с физикой |
| `Late Update` | Обновлять в `LateUpdate` |
| **`Smart Update`** | Обновлять каждую камеру так, как обновляется её цель. **Рекомендованное значение и дефолт** |
| `Manual Update` | Ничего не обновляется, вы сами зовёте `brain.ManualUpdate()` |

Отдельная настройка **Blend Update Method**: `Late Update` — рекомендованное; `Fixed Update` — только если Update Method = FixedUpdate и при блендах видно рывки.

### 5.3. Правила (формулировка мейнтейнера Cinemachine)

Для объекта с `Rigidbody`: **никогда не трогать его трансформ напрямую** (ни position, ни rotation, ни scale); менять Rigidbody **только его собственными методами и только в `FixedUpdate`**; всё остальное — в `Update`.

Чтобы Cinemachine следил за таким объектом плавно: Brain в `Smart Update`, на Rigidbody включена **Interpolation = Interpolate**. При правильной настройке CM-камера будет обновляться в **LateUpdate** — как раз благодаря интерполяции.

### 5.4. Как отладить (готовый рецепт)

Инспектор `CinemachineCamera` в Play Mode показывает свой режим обновления рядом с кнопкой Solo. Дальше:

1. Временно **выключите** Interpolation на Rigidbody машины.
2. Войдите в Play Mode, смотрите на инспектор камеры во время движения. Должно быть написано **FixedUpdate**.
3. Если написано **LateUpdate** — значит машина двигается или поворачивается не только в `FixedUpdate`. Ищите в скриптах, кто трогает transform вне физического шага. **Поворот считается тоже.**
4. Починили → включите Interpolation обратно. Теперь должно писать **LateUpdate**, и картинка должна быть гладкой.

В реальных тредах именно шаг 3 решал 90 % случаев: люди двигали Rigidbody в `FixedUpdate`, а поворачивали объект (или дочернюю пустышку-цель камеры) в `Update`.

Для нас это прямое архитектурное требование: **цель камеры (пустышка на машине) не должна двигаться или поворачиваться в `Update`.** Если хочется сглаженной цели — сглаживайте её в `FixedUpdate`.

### 5.5. Дополнительные меры

- Уменьшить `Time.fixedDeltaTime` (Project Settings → Time): 0.0166 (60 Гц) или 0.01 (100 Гц) заметно снижает алиасинг. Но мейнтейнер честно называет это **маскировкой** проблемы — сперва почините порядок обновления.
- Сэмпл-сцена **Brain Update Modes** специально показывает взаимодействие интерполяции Rigidbody и Update Method: посмотреть быстрее, чем читать.
- Не подтверждено: часть пользователей в тредах 2025–2026 сообщает, что им помогло, наоборот, **выключение** Interpolation. Официального объяснения нет; относиться как к «попробуйте оба варианта, если всё остальное сделано правильно».

## 6. Столкновения камеры с миром

Два разных extension, их часто путают:

| | `CinemachineDeoccluder` | `CinemachineDecollider` |
|---|---|---|
| Задача | Сохранить **линию взгляда** на цель: отодвинуть камеру от препятствий или выставить перед ними | Просто **вытолкнуть камеру из геометрии** и поставить поверх террейна; линию взгляда не гарантирует |
| Появился | Переименование `CinemachineCollider` из 2.x | **Cinemachine 3.1.0** (2024-04-01) |
| Оценка качества кадра | Да (используется ClearShot) | Нет |
| Стратегии | `Pull Camera Forward`, `Preserve Camera Height`, `Preserve Camera Distance` | Terrain Resolution + Obstacle Resolution |

**Для машины по статичной карте берём `Decollider`**: он решает ровно нашу проблему (камера низко за машиной, задевает бордюры, заборы, склоны), и он дешевле — не пытается искать альтернативную точку обзора.

Что помнить в обоих случаях:

- Оба работают через **Physics-рейкасты**: препятствия обязаны иметь коллайдеры, и это стоит производительности.
- `Collide Against` / `Obstacle Layers` **никогда не ставим Everything** — камера начнёт цепляться за саму машину, триггеры и трафик. Заводим слой `CameraObstacle` и кладём туда только статичную геометрию карты; у Deoccluder дополнительно есть `Ignore Tag`, куда документация советует класть тег цели.
- `Camera Radius` держать маленьким; увеличивать, только если камера начинает заглядывать внутрь объектов.
- `Damping When Occluded` (как быстро уходить от препятствия) и `Damping` (как быстро возвращаться) — разные параметры; возврат делаем медленнее ухода. `Minimum Occlusion Time` и `Smoothing Time` спасают от «мигания» камеры среди мелкой геометрии.
- У Decollider слой, указанный и в Terrain Layers, и в Obstacle Layers, обрабатывается **только** алгоритмом террейна.

## 7. Переключение камер и Blends

### 7.1. Кто становится Live

Brain выбирает **активную CM-камеру с наибольшим `Priority`**; при равенстве — ту, что активировали последней. Timeline полностью перебивает эту логику, пока играет клип. Три состояния камеры: **Live** (управляет), **Standby** (GameObject активен, приоритет не выше — камера всё равно считает свои цели), **Disabled** (GameObject выключен, ресурсы не тратит). `Standby Update` (`Never` / `Always` / `Round Robin`) задаёт, как часто пересчитывать неактивные камеры: `Never` подходит почти всегда, кроме участников оценки качества кадра (ClearShot).

Из скрипта:

```csharp
using Unity.Cinemachine;
using UnityEngine;

public sealed class CarCameraSwitcher : MonoBehaviour
{
    [SerializeField] private CinemachineCamera _chaseCamera;
    [SerializeField] private CinemachineCamera _cockpitCamera;
    private CinemachineCamera _active;

    private void Awake() => _active = _chaseCamera;

    /// <summary>Switches the view. Call from input, never from FixedUpdate.</summary>
    public void ToggleView()
    {
        _active = _active == _chaseCamera ? _cockpitCamera : _chaseCamera;
        _active.Prioritize();   // push to the top of its priority peers
    }
}
```


`Prioritize()` — актуальный метод; `MoveToTopOfPrioritySubqueue()` помечен `[Obsolete]`. Поле `Priority` имеет тип `PrioritySettings` (структура с `Enabled` и `Value`, есть неявные преобразования в `int`) — фиксированные приоритеты удобнее задавать в инспекторе, включив галочку Priority And Channel.

### 7.2. Настройка блендов

- **Default Blend** в `CinemachineBrain` — стиль и длительность по умолчанию: `Cut`, `Ease In Out`, `Ease In`, `Ease Out`, `Hard In`, `Hard Out`, `Linear`, `Custom` (рисуемая кривая).
- **Custom Blends** — ассет `Cinemachine Blender Settings` со списком правил «из камеры A в камеру B». **Ловушка: From и To — это строки-имена, а не ссылки на объекты.** Переименовали камеру — правило молча перестало применяться (поле подсветится жёлтым, если имя не находится в сцене). Есть зарезервированное имя `**ANY CAMERA**`. При совпадении нескольких правил выигрывает более специфичное.
- **Blend Hint** на самой камере влияет не на тайминг, а на **алгоритм**: `Spherical Position` / `Cylindrical Position` (камера облетает цель по дуге, а не режет угол — очень полезно при переключении ракурсов вокруг машины), `Inherit Position` (стартовать с текущей позиции Unity-камеры), `Screen Space Aim When Targets Differ`, `Ignore Target`, `Freeze When Blending Out`.
- Бленд — это **не** fade/wipe: Cinemachine интерполирует позицию, поворот и настройки линзы, стараясь сохранить цель в кадре.
- Для нашей витрины интересен **`CinemachineMixingCamera`**: он держит до 8 дочерних камер и смешивает их по весам. Официальный сэмпл «Mixing Camera» делает ровно то, что нам нужно — **непрерывный бленд группы камер как функцию скорости машины**.

## 8. Cinemachine Impulse: тряска при ударах

Импульс состоит из двух частей: **Impulse Source** (излучает сигнал из точки пространства) и **Impulse Listener** (extension на камере, который этот сигнал «слышит» и трясёт камеру).

Настройка для машины:

1. На кузов (объект с `Collider` и `Rigidbody`) вешаем **`CinemachineCollisionImpulseSource`** — он сам генерирует импульс при столкновении или входе в триггер.
   - `Impulse Type` = `Dissipating` (сила падает с расстоянием) или `Propagating` (ещё и распространяется со скоростью, дефолт 343 м/с — скорость звука); `Uniform` — мгновенно и одинаково везде.
   - `Impulse Shape` = `Bump` для удара, `Explosion` для взрыва, `Rumble` для длительной вибрации; длительность задаётся полем в секундах.
   - `Scale Impact With Mass` и `Scale Impact With Speed` включить — чирк о бордюр и лобовое столкновение будут ощущаться по-разному. `Layer Mask` / `Ignore Tag` — фильтр, чтобы не трясло от каждого камешка.
2. На `CinemachineCamera` добавляем **`CinemachineImpulseListener`** (Add Extension).
   - `Gain` — множитель силы («жёсткость крепления камеры»); `Use Camera Space` — трясти по локальным осям камеры.
   - `Signal Combination Mode` (`Additive` по умолчанию либо `Use Largest`) добавлен в **Cinemachine 3.1.4**.
   - `Reaction Settings` — вторичный случайный шум «на пружинах» после удара; именно он делает тряску живой.
3. `Impulse Channel` — фильтр «какой источник какой слушатель слышит»; нужен, когда камер несколько.

Для событий, не являющихся коллизиями (взрыв, нитро, приземление после трамплина) — обычный `CinemachineImpulseSource` и вызов из кода:

```csharp
using Unity.Cinemachine;
using UnityEngine;

[RequireComponent(typeof(CinemachineImpulseSource))]
public sealed class CarCrashShake : MonoBehaviour
{
    [SerializeField] private float _minImpactSpeed = 3f;
    [SerializeField] private float _velocityScale = 0.05f;
    private CinemachineImpulseSource _source;

    private void Awake() => _source = GetComponent<CinemachineImpulseSource>();

    private void OnCollisionEnter(Collision collision)
    {
        Vector3 impact = collision.relativeVelocity;
        if (impact.sqrMagnitude < _minImpactSpeed * _minImpactSpeed)
            return;
        // GetContact(0) avoids the array allocation of collision.contacts.
        _source.GenerateImpulseAtPositionWithVelocity(
            collision.GetContact(0).point, impact * _velocityScale);
    }
}
```

Актуальные методы: `GenerateImpulse()`, `GenerateImpulseWithForce(float)`, `GenerateImpulseWithVelocity(Vector3)`, `GenerateImpulseAtPositionWithVelocity(Vector3, Vector3)`. Старые `GenerateImpulse(float)`, `GenerateImpulse(Vector3)`, `GenerateImpulseAt(...)` помечены в документации как Legacy API.

## 9. Spline Dolly: кинематографичные пролёты

В Cinemachine 3 пути — это **нативные Unity Splines** (`SplineContainer`), собственных путей у Cinemachine больше нет.

- `GameObject → Cinemachine → Dolly Camera with Spline` создаёт камеру со `CinemachineSplineDolly` и сплайн сразу.
- `Position Units`: `Knot` (индекс узла), `Distance` (метры), `Normalized` (0..1); для анимации удобнее `Normalized`. `Camera Rotation`: `Default` / `Path` / `Path No Roll` / `Follow Target` / `Follow Target No Roll`.
- **Automatic Dolly**: `None` (позицию ведёте вы), `Fixed Speed` (равномерный пролёт — то, что нужно для интро), `Nearest Point To Target` (камера едет вдоль сплайна за машиной, эффект «рельсы вдоль трассы»). Документация предупреждает: `Nearest Point To Target` неустойчив на сплайнах, огибающих цель дугой — в вырожденном случае (круговой сплайн, цель в центре) малое смещение цели швыряет камеру далеко по сплайну.
- **`CinemachineSplineSmoother`** (добавлен в 3.1.2) — вешается на объект со `SplineContainer` и автоматически правит касательные узлов, обеспечивая гладкость второго порядка. Это замена `CinemachineSmoothPath` из 2.x. **Ручные касательные при этом трогать нельзя — смузер их перезапишет.**
- **`CinemachineSplineRoll`** — крен вдоль пути (наклон камеры в вираже). Если повесить на сам сплайн — крен видят все, кто по нему едет; если на камеру — только она.
- **`CinemachineSplineDollyLookAtTargets`** (добавлен в 3.1.1) — Rotation Control, позволяющий задать, на что смотреть в конкретных точках сплайна. Идеально для пролёта «мимо машины — на здание — обратно на машину».
- `CinemachineSplineCart` — не камера, а способ двигать любой GameObject по сплайну (цель для камеры, объект окружения).

## 10. FOV от скорости

Встроенного компонента «FOV зависит от скорости» **нет**: `CinemachineFollowZoom` решает другую задачу — держит **постоянный экранный размер** объекта, подгоняя FOV под дистанцию, что для машины даст обратный эффект. Правильный путь — свой `CinemachineExtension`, правящий `state.Lens.FieldOfView` на стадии `Finalize`: так изменение корректно участвует в блендах (Cinemachine интерполирует линзу вместе с позицией) и не конфликтует с пайплайном.

```csharp
using Unity.Cinemachine;
using UnityEngine;

/// <summary>Widens the lens as the tracked Rigidbody speeds up, to sell the sense of speed.</summary>
[ExecuteAlways]
public sealed class SpeedFovExtension : CinemachineExtension
{
    [SerializeField] private Rigidbody _body;
    [SerializeField] private float _baseFov = 55f;
    [SerializeField] private float _maxFov = 78f;
    [SerializeField] private float _speedAtMaxFov = 40f;  // m/s
    [SerializeField] private float _smoothing = 0.35f;    // seconds
    private float _currentFov;

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage,
        ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finalize || _body == null)
            return;
        // Unity 6: Rigidbody.velocity is gone, it is linearVelocity now.
        float t = Mathf.Clamp01(_body.linearVelocity.magnitude / _speedAtMaxFov);
        float target = Mathf.Lerp(_baseFov, _maxFov, t);
        // deltaTime < 0 means "reset, no damping" by Cinemachine convention.
        _currentFov = (deltaTime < 0f || _smoothing <= 0f) ? target
            : Mathf.Lerp(_currentFov, target, 1f - Mathf.Exp(-deltaTime / _smoothing));
        state.Lens.FieldOfView = _currentFov;
    }
}
```

Замечания: скрипт, унаследованный от `CinemachineExtension`, сам появляется в списке **Add Extension** на инспекторе камеры; ссылка на `Rigidbody` — через `[SerializeField]`, никаких `FindFirstObjectByType` в кадре. Менять `Lens.FieldOfView` прямо на `CinemachineCamera` из `Update` тоже работает, но при блендах поведение менее предсказуемо.

Смежное: постобработка (motion blur, vignette, DOF) навешивается на конкретную камеру через extension **`CinemachineVolumeSettings`** с URP Volume Profile — при бленде вес профиля интерполируется вместе с камерой. Включать/выключать глобальные `Volume` руками не надо.

## 11. Ввод: Orbital Follow + Input System

Cinemachine 3 **не читает ввод сам** — компоненты выставляют оси, которые кто-то должен крутить. Штатный драйвер — **`CinemachineInputAxisController`**: добавляется на CM-камеру, сам находит доступные оси (Horizontal / Vertical / Radial у `OrbitalFollow`) и позволяет назначить на каждую `InputActionReference` из нового Input System. Полезные поля: `Gain`, `Accel Time` / `Decel Time` (инерция ввода), `Suppress Input While Blending`, `Ignore Time Scale`, `Cancel Delta Time` (включать для мышиных дельт — они и так пропорциональны длине кадра).

Для нашей камеры в `OrbitalFollow`: `Recentering Target` = **`Tracking Target`** (центр горизонтальной оси динамически ставится за корму машины); `Recentering` по горизонтали включён с `Wait` ≈ 1 с и `Time` ≈ 1.5 с; `Horizontal Axis` — Wrap = true, диапазон −180..180; `Vertical Axis` — ограниченный, чтобы камера не уходила под землю.

Для split-screen (две машины, два игрока) есть готовый сэмпл **Split Screen Multiplayer** с кастомным `InputAxisControllerBase`, читающим `PlayerInput`, и **Split Screen Car** — прямо про две гоночные машины. Разделение камер по игрокам делается **Cinemachine Channels**, не Unity Layers.

## 12. Миграция с 2.x (если вдруг понадобится)

Нам это не грозит — проект начинается с нуля, — но пригодится, если будем тащить ассет со Store, написанный под 2.x.

1. **Бэкап проекта.** Undo для апгрейда всего проекта не поддерживается.
2. Обновить пакет, починить скрипты: namespace, убрать `m_`, заменить типы по таблице из раздела 2.
3. **До апгрейда** данных заменить в своём коде типы `CinemachineVirtualCamera` / `CinemachineFreeLook` на базовый `CinemachineVirtualCameraBase` — тогда апгрейдер сохранит существующие ссылки на объекты.
4. Запустить **Cinemachine Upgrader** из инспектора любой старой виртуальной камеры. Три режима: один объект / одна сцена / **весь проект**. С префабами и Timeline годится только «весь проект» — остальные не трогают ассеты вне сцены и рвут ссылки.
5. После: проверить порванные ссылки, удалить оставшиеся дочерние объекты `cm` (в 3.x они больше не скрыты), заново включить `Lens Mode Override` в Brain, если он использовался.
6. Deprecated-классы 2.x пока остаются в пакете, но могут быть удалены. Вырезать из сборки — дефайн `CINEMACHINE_NO_CM2_SUPPORT`.

Документация Unity при этом честно предупреждает: если в проекте много кастомных Cinemachine-скриптов, **иногда разумнее остаться на 2.x**. Нам, начиная с чистого листа, — однозначно 3.1.

## 13. Антипаттерны

- **Брать туториал по «Cinemachine» без проверки года.** Всё, что показывает `CinemachineVirtualCamera`, вкладки Body/Aim или дочерний объект `cm`, — это 2.x. Компоненты в 3.x называются иначе, и скрипты оттуда не скомпилируются.
- **`using Cinemachine;`, `GetCinemachineComponent<T>()`, `CinemachineCore.Instance`** — namespace теперь `Unity.Cinemachine`, компоненты берутся обычным `GetComponent<T>()`, синглтона больше нет (статические члены `CinemachineCore`).
- **Делать камеру дочерним объектом машины.** Cinemachine и так двигает камеру; родительский трансформ добавит вторую трансформацию и джиттер.
- **Ставить Brain Update Method = Fixed Update «потому что машина на физике».** Правильный дефолт — `Smart Update` плюс `Interpolate` на Rigidbody. FixedUpdate-режим нужен в редких случаях и обычно маскирует настоящую проблему.
- **Двигать машину в `FixedUpdate`, а поворачивать (или двигать цель камеры) в `Update`.** Это причина большинства жалоб на дрожание. Проверяется за 30 секунд по индикатору режима обновления в инспекторе камеры.
- **Трогать `transform` объекта с `Rigidbody` напрямую.** Ломает интерполяцию, а с ней и плавность камеры.
- **Ставить damping в 0 «чтобы было отзывчивее»** и **судить о плавности по редактору.** Нулевой damping копирует микродрожание решателя, а редактор сам по себе даёт неровный кадр — смотреть надо билд.
- **Крутить look-ahead на машине.** Документация прямо пишет, что look-ahead усиливает шум движения; на физической машине это почти гарантированное дрожание.
- **`Collide Against = Everything` у Deoccluder/Decollider.** Камера начнёт цепляться за саму машину, трафик и триггеры; плюс лишние рейкасты каждый кадр. Отдельный слой препятствий + `Ignore Tag`.
- **Трясти камеру, двигая её `transform` из своего скрипта.** Cinemachine перезапишет позицию в `LateUpdate`. Тряска — только через Impulse (или Noise).
- **Считать `CinemachineFollowZoom` компонентом «FOV от скорости».** Он держит постоянный экранный размер объекта — это противоположный эффект.
- **Забыть, что Custom Blends ассет ссылается на камеры по имени.** Переименовали камеру — бленд тихо перестал работать.
- **Использовать deprecated `CinemachinePath` / `CinemachineSmoothPath` / `CinemachineDollyCart`** (заменены на нативные Splines и `CinemachineSplineCart`) или **делить камеры по Unity Layers для split-screen** (для этого теперь есть Cinemachine Channels).
- **Искать компонент `CinemachineFreeLook`.** Отдельного класса больше нет — FreeLook это обычная `CinemachineCamera` с `OrbitalFollow`; создаётся через `GameObject → Cinemachine → Targeted Cameras → FreeLook Camera`.
- **Ставить `Standby Update = Always` всем камерам «на всякий случай».** Это лишний счёт каждый кадр для каждой неактивной камеры.
- **Ожидать, что `Mode Override` (орто/перспектива/физическая) заработает сам.** В 3.x его надо сначала разрешить в `CinemachineBrain` (`Lens Mode Override` + Default Mode).

## 14. Чек-лист

**Установка и сцена**
- [ ] Пакет `com.unity.cinemachine` **3.1.7** поставлен; импортированы 3D Samples (минимум Brain Update Modes, Mixing Camera, Split Screen Car, Impulse Wave).
- [ ] На Unity-камере есть `CinemachineBrain`: Update Method = **Smart Update**, Blend Update Method = **Late Update**.
- [ ] CM-камера — отдельный GameObject в корне сцены, **не** ребёнок машины.

**Камера машины**
- [ ] `CinemachineCamera` + `CinemachineOrbitalFollow` + `CinemachineRotationComposer`.
- [ ] `Tracking Target` = пустышка на машине примерно на уровне крыши, а не корневой Rigidbody-объект.
- [ ] Binding Mode = `Lock To Target With World Up` (или `Lazy Follow` для дрифт-камеры); `Recentering Target` = `Tracking Target`, горизонтальный Recentering включён.
- [ ] Angular Damping Mode = `Quaternion`, если машина умеет переворачиваться.
- [ ] Position Damping разный по осям (Z < X < Y); look-ahead выключен или ≤ 0.3 с с `Lookahead Ignore Y`.

**Плавность**
- [ ] Rigidbody машины: `Interpolation = Interpolate`.
- [ ] 100 % изменений позиции и поворота машины — в `FixedUpdate`, ничего в `Update` / `LateUpdate`.
- [ ] Проверено: с выключенной интерполяцией инспектор CM-камеры пишет **FixedUpdate** во время движения; с включённой — **LateUpdate**.
- [ ] Плавность оценена **в билде**; `Time.fixedDeltaTime` выбран осознанно (0.02 по умолчанию).

**Столкновения, эффекты, переключение**
- [ ] `CinemachineDecollider` на камере, слой препятствий — отдельный, не Everything.
- [ ] `CinemachineCollisionImpulseSource` на кузове + `CinemachineImpulseListener` на камере; `Scale Impact With Mass/Speed` включены; тряска нигде не делается изменением `transform` камеры.
- [ ] FOV от скорости — через собственный `CinemachineExtension` на стадии `Finalize`.
- [ ] Камеры переключаются через `Prioritize()` / `Priority`; задан Default Blend, имена камер в Custom Blends сверены со сценой; для облёта — Blend Hint = `Spherical Position`.
- [ ] Постобработка на камеру — через `CinemachineVolumeSettings`, а не переключением глобальных Volume.

**Код**
- [ ] `using Unity.Cinemachine;`, ни одного `m_`-поля из старого API.
- [ ] Ссылки на камеры и Rigidbody — через `[SerializeField]`; никаких `Find*` и `Camera.main` в `Update`.
- [ ] Cinemachine-скрипты лежат в своём модуле со своим `.asmdef`.

## 15. Видео и доклады

Официальная серия Unity по Cinemachine 3.1 (сентябрь 2025), ссылки со страницы документации «Samples and tutorials»; плейлист целиком — https://www.youtube.com/playlist?list=PLX2vGYjWbI0QiMBrmyzbxZeHepAbhVOJa

- Types of Cinemachine cameras — Unity — https://www.youtube.com/watch?v=XTVzs4B1d7I — обзор типов камер в 3.1: Follow, FreeLook, Spline Dolly, Sequencer; правильная отправная точка вместо любого стороннего «введения».
- Cinemachine player controller cameras — Unity — https://www.youtube.com/watch?v=u0a1F6BlczE — переключение типов камер, обход препятствий, Deoccluder и ClearShot на практике.
- Cinemachine tips and tricks — Unity — https://www.youtube.com/watch?v=AFU9hsxPLZU — **самое релевантное нам**: разбор «как чинить джиттер движения», FreeLook Modifier, Target Group.
- Cinemachine and Timeline — Unity — https://www.youtube.com/watch?v=Px_H1oyZgGY — постановка кат-сцен, camera events, DOF-блёры.
- Cinemachine 2D cameras — Unity — https://www.youtube.com/watch?v=-tUd-bLmoO8 — про 2D; нам нужен только фрагмент про camera shake и события.

Прочее (найдено поиском по YouTube, версии проверять по дате):

- Cinemachine 3 | Updates for 2023.2 — Unity — https://www.youtube.com/watch?v=znOii5cz0RU — часовой доклад команды Cinemachine о том, что и зачем изменили в 3.x; лучший источник по мотивации переименований.
- Essential Elements of Cinemachine 3.1 — git-amend — https://www.youtube.com/watch?v=4xd37R1spKw — сжатый разбор архитектуры 3.1 с точки зрения программиста, а не левел-дизайнера.
- What has Changed in Cinemachine 3 (Intro/Overview) | Unity 6 — Omar Balfaqih — https://www.youtube.com/watch?v=X6_v9rhySPs — короткое видео именно про отличия от 2.x.
- Camera Follow using Cinemachine 3 & Timeline | Unity 6 — Omar Balfaqih — https://www.youtube.com/watch?v=wB-EQH7jvFY — практическая настройка follow-камеры в 3.x.
- Dolly Camera using Cinemachine 3 | Unity 6 — Omar Balfaqih — https://www.youtube.com/watch?v=hsf7VR8f3Bc — Spline Dolly в 3.x, пригодится для интро-пролёта.
- Orbit Target with Cinemachine 3 | Unity Tutorial — LlamAcademy — https://www.youtube.com/watch?v=Aeeh5OxWVrM — Orbital Follow и оси ввода.
- Free Look Camera Follow for Car in Unity | Cinemachine — The Game Guy — https://www.youtube.com/watch?v=wEZfW22dliU — **эпоха Cinemachine 2.x**: идеи про камеру для машины полезны, но все имена компонентов устарели; смотреть только как источник идей.

## 16. Источники

Все ссылки проверены 2026-08-19. `{CM}` ниже — сокращение общего префикса документации пакета `https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/`.

- https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.cinemachine.html — какие версии пакета выпущены для Unity 6000.3 (3.1.7).
- `{CM}manual/whats-new.html` — таблицы переименований 2.x → 3.x, namespace, каналы, события, отсутствие объекта `cm`.
- `{CM}manual/CinemachineUpgradeFrom2.html`, `{CM}manual/InstallationAndUpgrade.html` — Cinemachine Upgrader, три режима апгрейда, `CINEMACHINE_NO_CM2_SUPPORT`, требования к редактору.
- `{CM}manual/CinemachineBrain.html` — Update Method (Fixed/Late/Smart/Manual), Blend Update Method, Default Blend, Lens Mode Override, Channel Mask.
- `{CM}manual/CinemachineCamera.html` — Priority, Standby Update, Blend Hint, Lens, полный список Position/Rotation Control.
- `{CM}manual/concept-procedural-motion.html`, `{CM}manual/concept-camera-control-transitions.html` — устройство пайплайна, список extensions, состояния Live/Standby/Disabled.
- `{CM}manual/CinemachineFollow.html`, `{CM}manual/CinemachineOrbitalFollow.html` — binding modes, damping, орбиты, recentering.
- `{CM}manual/CinemachinePositionComposer.html`, `{CM}manual/CinemachineRotationComposer.html` — экранная композиция, dead zone, look-ahead и предупреждение о шуме.
- `{CM}manual/CinemachineHardLockToTarget.html`, `{CM}manual/CinemachineRotateWithFollowTarget.html` — камера из кокпита; `{CM}manual/CinemachineDeoccluder.html`, `{CM}manual/CinemachineDecollider.html` — два разных способа не влезать камерой в геометрию.
- `{CM}manual/CinemachineImpulse.html`, `{CM}manual/CinemachineCollisionImpulseSource.html`, `{CM}manual/CinemachineImpulseSource.html`, `{CM}manual/CinemachineImpulseListener.html` — вся система тряски.
- `{CM}api/Unity.Cinemachine.CinemachineImpulseSource.html` — актуальные и legacy методы `GenerateImpulse*`.
- `{CM}manual/CinemachineSplineDolly.html`, `{CM}manual/CinemachineSplineSmoother.html`, `{CM}manual/CinemachineSplineRoll.html` — движение по сплайну, сглаживание, крен, ловушка Nearest Point To Target.
- `{CM}manual/CinemachineFollowZoom.html` — что Follow Zoom делает на самом деле; `{CM}manual/CinemachineBlending.html`, `{CM}manual/ControllingAndCustomizingBlends.html`, `{CM}manual/CinemachineMixingCamera.html` — бленды, сопоставление камер по именам, Blend Hints, смешивание по весам.
- `{CM}manual/CinemachineInputAxisController.html`, `{CM}manual/InputSystemComponents.html` — оси ввода и интеграция с `PlayerInput`.
- `{CM}manual/CinemachineVolumeSettings.html`, `{CM}manual/CinemachineEvents.html`, `{CM}manual/ui-ref-pre-built-cameras.html` — постобработка на камеру, события, меню GameObject → Cinemachine.
- `{CM}manual/samples-tutorials.html` — сэмплы (Mixing Camera «по скорости машины», Split Screen Car, Brain Update Modes) и официальные видео.
- `{CM}manual/KnownIssues.html` — Accumulation AA + cut ломает FOV.
- `{CM}changelog/CHANGELOG.html` — версии появления: Decollider 3.1.0, SplineDollyLookAtTargets 3.1.1, SplineSmoother 3.1.2, Signal Combination Mode 3.1.4; сам файл фиксирует переход нумерации пакета к 6.6.0 (2026-05-08).
- `{CM}api/Unity.Cinemachine.CinemachineExtension.html`, `{CM}api/Unity.Cinemachine.CinemachineCore.Stage.html`, `{CM}api/Unity.Cinemachine.PrioritySettings.html` — `PostPipelineStageCallback`, enum Body/Aim/Noise/Finalize, тип поля Priority.
- https://discussions.unity.com/t/cinemachine-rigidbody-stutter-as-usual/942206 — канонический ответ мейнтейнера Cinemachine: правила работы с Rigidbody и рецепт отладки джиттера.
- https://discussions.unity.com/t/cinemachine-stutters-when-following-a-rigidbody/773853 — почему damping усиливает неровный фреймрейт и почему тестировать надо в билде.
- https://discussions.unity.com/t/rigid-body-stuttering-when-using-cinemachine/1695775 — свежий (ноябрь 2025, Unity 6.3 beta) разбор того же: поворот в `Update` при движении в `FixedUpdate`.
- https://discussions.unity.com/t/cinemachine-camera-stuttering-issue/1639690 — длинный тред 2025–2026; там же неподтверждённый случай, когда помогло выключение интерполяции.
- https://discussions.unity.com/t/new-5-part-cinemachine-3-1-youtube-tutorial-series-available/1685256 — анонс официальной серии видео (сентябрь 2025).

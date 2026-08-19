# Звук: двигатель, окружение, микшер

Как собрать звуковую часть демо на встроенном аудио Unity 6.3: мотор по оборотам, окружение, AudioMixer, 3D-затухание, бюджет голосов.
Читать перед тем, как класть в проект первый `.wav`, и каждый раз, когда звук «работает, но звучит дёшево».

---

## TL;DR — решения для нашего проекта

1. **Никакого middleware.** Встроенный аудиодвижок Unity. FMOD/Wwise в витрине возможностей Unity — демонстрация чужой технологии плюс лицензия и вторая точка отказа (раздел 8).
2. **Мотор — минимум 4 AudioSource, не один.** Два соседних RPM-лупа (кросс-фейд) + idle + слой впуска. Один луп с плавающим `pitch` — главная причина «пылесосного» звука.
3. **Сэмплы двигателя через каждые 500 об/мин**, в нижней части диапазона — через 250. Растяжение больше 500 об/мин слышно как «резина».
4. **RPM симулируем сами** от `Rigidbody.linearVelocity` и номера передачи. Физическая модель трансмиссии не нужна — нужна кривая с провалом при переключении и отсечкой.
5. **AudioMixer с первого дня:** `Master → Music / SFX (→ Vehicle, World, UI)`. Снапшоты `Exterior` / `Interior` / `Paused` вместо ручного дёргания громкостей из кода.
6. **Exposed parameters и snapshots не смешивать.** После `AudioMixer.SetFloat` параметр заперт и снапшотами не управляется. Exposed — только слайдеры настроек, всё игровое — снапшоты.
7. **Audio Random Container (Unity 6) — для ударов, скрипов, мелочи.** Для мотора не годится: `PlayOneShot` его не принимает, класс внутренний.
8. **Форматы:** короткое — PCM/ADPCM + Decompress On Load, длинное — Vorbis + Streaming. Лупы мотора только Decompress On Load: распаковка в кадре даст щелчки.
9. **`Doppler Level = 0` почти везде.** На дрожащей камере доплер даёт паразитный вой. Оставить ~0.3 только проезжающему мимо трафику.
10. **Бюджет голосов — 32 real / 512 virtual (дефолт).** Мотор игрока занимает 5–6. `priority`: музыка 0, мотор игрока 32, трафик 200+.

---

## 1. Карта аудиосистемы Unity 6

- **AudioListener** — «уши», один на сцену, обычно на камере. **AudioSource** — излучатель; считает 3D-затухание, панораму, доплер.
- **AudioClip / AudioResource** — данные. В Unity 6 `AudioSource.resource` имеет тип `UnityEngine.Audio.AudioResource`; от него наследуются и `AudioClip`, и `AudioRandomContainer`. Старое `AudioSource.clip` осталось и работает.
- **AudioMixer** — маршрутизация, группы, эффекты, снапшоты.

**Важный порядок обработки:** затухание по расстоянию и доплер применяются *на AudioSource, до входа в микшер* — они относятся к сцене, а не к категории звука ([Introduction to the Audio Mixer](https://docs.unity3d.com/6000.3/Documentation/Manual/AudioMixerOverview.html)).
Следствие: lowpass в группе микшера не «знает» про расстояние, а `AudioLowPassFilter` на самом источнике — знает и управляется кривой по расстоянию.

**Что нового в 6.3** (по официальным статус-постам аудиокоманды Unity, [Q3 2025][st25] и [Q2 2026][st26]) и почему нас это пока не касается:

- **Scriptable Audio Pipeline** (6.3) — расширение аудиоконвейера Burst-компилируемым C# (`IAudioGenerator`, `AudioSource.generator`, `AudioSource.generatorInstance`). Замена `OnAudioFilterRead` без GC-фризов. Для нашей задачи избыточно; на Web не работает.
- **Enhanced Audio Foundation** (6.3, Windows/macOS) — новый слой абстракции над железом, включается запуском редактора с флагом `-showAudioFoundationUI`. По умолчанию выключен.
- Unity пишет, что `AudioSource.generator` «со временем, вероятно, вытеснит `.clip` и `.resource`». Пишем на `.resource`/`.clip`, но знаем направление.

---

## 2. Звук двигателя

### 2.1 Почему один зациклённый сэмпл с меняющимся pitch звучит плохо

1. **Растяжение тембра.** `pitch` в Unity — это скорость воспроизведения: вместе с основным тоном едут вверх все форманты и шумы. Удвоение оборотов = октава ([BOOM Library][boom]). Луп на 2000 об/мин, растянутый до 6000, звучит как мультяшный писк.
2. **Тембр мотора нелинеен по оборотам.** На 1500 доминирует выхлоп, на 6000 — впуск и механический шум. Питч-шифтом этого не получить в принципе.
3. **Нагрузка не моделируется.** Мотор под газом и на торможении двигателем на тех же оборотах — два разных звука. У одного лупа второго состояния просто нет.

### 2.2 Набор сэмплов по оборотам

Практика гоночных игр ([BOOM Library][boom], автор — Mike Caviezel, Forza / Gran Turismo): лупы **через 500 об/мин**, а в диапазоне до ~2500 — через 250, потому что свыше 500 об/мин растяжения слышны артефакты.
Отдельный idle-луп 3–5 с, остальные 1–2 с. Для мотора с отсечкой 5500 это ~13 лупов на одну «перспективу» (выхлоп / моторный отсек / впуск / салон).

Для демо это перебор. **Наш минимум: 4–5 лупов** (idle, low, mid, high, redline) в одной перспективе плюс слой впуска. Если сэмплы из готового пака — выяснить их **реальные** обороты записи: питч считается от них, а не от круглого числа в имени файла.

### 2.3 Симуляция RPM от скорости и передачи

```csharp
// EngineRpmSimulator.cs — чистая логика вне MonoBehaviour, покрывается EditMode-тестами.
public sealed class EngineRpmSimulator
{
    readonly EngineProfile _profile;   // ScriptableObject: idleRpm, maxRpm, gearRatios[]
    float _rpm;
    int _gear;

    public float Rpm => _rpm;

    /// <param name="speed">Модуль горизонтальной скорости, м/с.</param>
    /// <param name="throttle">0..1, из Input System.</param>
    public void Tick(float speed, float throttle, float deltaTime)
    {
        _gear = _profile.SelectGear(speed, _gear);            // с гистерезисом
        float t = _profile.NormalizedInGear(speed, _gear);    // позиция внутри полосы передачи
        float target = Mathf.Lerp(_profile.IdleRpm, _profile.MaxRpm, t);

        if (throttle <= 0.01f)                                // без газа мотор проседает
            target = Mathf.Lerp(_profile.IdleRpm, target, _profile.OffThrottleSag);

        float pull = throttle > 0.01f ? _profile.RevUpSpeed : _profile.RevDownSpeed;
        _rpm = Mathf.MoveTowards(_rpm, target, pull * deltaTime);
    }
}
```

Два штриха дают почти весь эффект «настоящей коробки»: **провал при переключении** (при смене `_gear` вверх на 0.15–0.25 с уронить `throttle` в 0 и подмешать сэмпл переключения — иначе разгон звучит одной бесконечной нотой) и **отсечка** (у `MaxRpm` держать обороты и добавить прерывистый rev-limiter слой; самый узнаваемый «дорогой» признак).

`speed` берём из `Rigidbody.linearVelocity` (в Unity 6 `velocity` переименован в `linearVelocity`), проецируя на горизонталь. Считаем в `FixedUpdate`, применяем к AudioSource в `Update` — аудиопараметры не физика.

### 2.4 Кросс-фейд по оборотам

Работаем с двумя соседними лупами. Каждый играет с `pitch = currentRpm / clipRecordedRpm` — так оба слоя в унисон и на стыке нет биений. Громкости — **constant power** (сумма квадратов = 1), иначе в середине перехода провал −3 дБ.

```csharp
float angle = t * Mathf.PI * 0.5f;             // t: 0..1 между lower и upper по шкале RPM
lower.volume = Mathf.Cos(angle) * layerGain;   // cos² + sin² = 1
upper.volume = Mathf.Sin(angle) * layerGain;
lower.pitch  = rpm / lowerClipRpm;
upper.pitch  = rpm / upperClipRpm;
```

Ширина зоны кросс-фейда — примерно половина шага между сэмплами: 250 об/мин для лупов, разнесённых на 500 ([BOOM Library][boom]).
Лупы **не переключаем через `Play()`** — все источники играют постоянно с `loop = true`, меняются только `volume` и `pitch`. Рестарт лупа = слышимый щелчок. Источники в нулевой громкости Unity виртуализирует сам (5.3).

### 2.5 On-load / off-load слои

В идеале — отдельные сэмплы под газом и на сбросе. Записать их сложно, поэтому в луповых схемах off-load часто **симулируют DSP-обработкой on-load лупов** ([BOOM Library][boom]):

| Слой   | Под газом (throttle → 1)         | Сброс газа (throttle → 0)                          |
|--------|----------------------------------|----------------------------------------------------|
| Выхлоп | полная громкость, чуть дисторшна | −3…−6 дБ, срезать ~2 кГц и ~10 кГц                 |
| Мотор  | полная громкость                 | −2…−4 дБ, убрать нижнюю середину                   |
| Впуск  | слышен, добавляет «злости»       | **выключить полностью** — впуск не шумит на сбросе |

Последняя строка — самая дешёвая и самая заметная. Отдельный слой впуска с громкостью, привязанной к `throttle`, стоит один AudioSource и мгновенно читается как «машина реагирует на педаль».

### 2.6 Раскладка источников для демо

| AudioSource      | Клип                | Управление                      |
|------------------|---------------------|---------------------------------|
| `EngineLow`      | соседний луп снизу  | volume ← crossfade, pitch ← RPM |
| `EngineHigh`     | соседний луп сверху | volume ← crossfade, pitch ← RPM |
| `EngineIntake`   | луп впуска          | volume ← throttle × RPM-кривая  |
| `EngineIdle`     | холостые            | volume ← 1 − RPM-нормаль        |
| `TireSquealLoop` | скрип шин           | volume/pitch ← slip             |
| `WindLoop`       | набегающий поток    | volume + lowpass ← скорость     |

Шесть источников на машину игрока, все — дети одного GameObject на корпусе, `spatialBlend = 1`.

---

## 3. Audio Random Container

Ассет Unity 6 (появился в потоке 2023.2): плейлист аудиоклипов с правилами воспроизведения, который кладётся в AudioSource **вместо** клипа ([fundamentals][arcf], [reference][arcui]).
Настраивается в самом ассете: `Volume` и `Pitch` с рандомизацией (мин/макс), список клипов с индивидуальной громкостью и галкой активности, `Trigger` (Manual / Automatic), `Playback Mode` (Sequential / Shuffle / Random + `Avoid Repeating Last`), `Automatic Trigger Mode` (Pulse / Offset) с рандомизацией времени, `Loop` (Infinite / Clips / Cycles).

**Зачем нам:** избавляет от самописного «выбери случайный клип, подкрути pitch на ±0.1, не повторяй прошлый». Столкновения, скрипы кузова, точечная городская мелочь — прямо сюда. Один ассет = вся вариативность, без единой строчки кода.

**Ограничения — важные:**

- `AudioSource.PlayOneShot(AudioClip clip, float volumeScale)` принимает **только `AudioClip`** ([API][pos]). Контейнер через one-shot не сыграть; в Unity Discussions это оформлено как отдельный запрос фичи, ссылка на него есть в [Q2 2026][st26].
- Класс `AudioRandomContainer` внутренний — из скрипта не достать список клипов, а значит нельзя точечно вызвать `AudioClip.UnloadAudioData()` по его содержимому ([разбор с замерами][gt]).
- Поведение `Play()` зависит от `Trigger`: при **Manual** каждый вызов накладывает новый экземпляр (как one-shot), при **Automatic** — перезапускает плейлист с начала ([arcf], [gt]).
- В профайлере голоса контейнера идут с **пустым именем объекта**, и в момент стыка клипов счётчик Audio Voices кратковременно удваивается ([gt]).

Для двигателя контейнер не годится: там нужен непрерывный контроль `volume`/`pitch` каждый кадр, а не «сыграй случайный из списка».

---

## 4. AudioMixer: группы, снапшоты, эффекты

**Иерархия:** `Master → Music`, `Master → SFX → {Vehicle, World, UI}`. Правило: группа = категория, которой хочется управлять целиком. Больше пяти-семи групп на старте — избыточно.

### 4.1 Снапшоты против exposed parameters — не смешивать

Самая частая ошибка. Документация формулирует прямо: **exposed parameter обходит систему снапшотов, и после `AudioMixer.SetFloat` этот параметр заперт на выставленном значении и снапшотами больше не управляется** ([AudioGroup Inspector][mixin], [AudioMixer.SetFloat][setf]).

- **Exposed** (`SetFloat`): только пользовательские регуляторы — `MasterVolume`, `MusicVolume`, `SfxVolume`.
- **Snapshots** (`TransitionTo`): всё игровое — `Exterior`, `Interior`, `Paused`, `SlowMotion`.

Ещё две ловушки из документации: `AudioMixer.SetFloat` **нельзя вызывать в `Awake`, `OnEnable` и `RuntimeInitializeLoadType.AfterSceneLoad`** — поведение непредсказуемо, только `Start` и позже ([setf]).
И шкала громкости микшера — **децибелы, от −80 до +20**, а не 0..1: слайдер 0..1 переводится как `Mathf.Log10(Mathf.Max(value, 0.0001f)) * 20f`, линейная привязка даёт «всё выключено до 90 % хода ручки».

```csharp
[SerializeField] private AudioMixerSnapshot _exterior;
[SerializeField] private AudioMixerSnapshot _interior;

// Один переход интерполирует ВСЕ параметры микшера сразу.
public void SetCameraInside(bool inside) => (inside ? _interior : _exterior).TransitionTo(0.35f);
```

Для непрерывных состояний есть `AudioMixer.TransitionToSnapshots(snapshots, weights, timeToReach)` — смесь снапшотов с весами ([API][tts]). Пример: доля «закрытости» при въезде в тоннель как вес между `Exterior` и `Tunnel`.

### 4.2 Эффекты, ducking, реверберация

Встроенные эффекты микшера в 6.3: Low Pass, High Pass, Echo, Flange, Distortion, Normalize, Parametric EQ, Pitch Shifter, Chorus, Compressor, SFX Reverb, плюс Low Pass Simple и High Pass Simple ([audio-effects][fx]).

**Салон = снапшот `Interior`** на группе `Vehicle`: Low Pass со срезом ~800–1500 Гц, −3 дБ на `World`, чуть больше на `Music`. Не «включить компонент фильтра по триггеру» — снапшот делает это одним плавным переходом.

**Ducking** в Unity — связка `Send` + `Duck Volume` ([mixin]): на группе-источнике `Send` с уровнем, на группе-жертве `Duck Volume`, принимающий сигнал. Понадобится, только если появится закадровый голос.

**Reverb Zones против reverb в микшере.** `AudioReverbZone` — компонент со сферой `Min Distance` / `Max Distance` и пресетом: внутри Min — полный эффект, между Min и Max — плавное нарастание ([Reverb Zones][rz]).
Доля сигнала, уходящая в зоны, задаётся на источнике параметром `Reverb Zone Mix` и может вестись кривой по расстоянию (`AudioSourceCurveType.ReverbZoneMix`).
Рекомендация: **тоннели — Reverb Zone** (эффект привязан к месту в сцене), **салон — снапшот микшера** (эффект привязан к состоянию игры). Смешивать эти два механизма в одном месте нельзя: получится двойная реверберация, которую невозможно отстроить.

---

## 5. 3D-звук: кривые, доплер, приоритеты, голоса

### 5.1 Настройки источника

| Параметр          | Что делает                                                | Наше значение               |
|-------------------|-----------------------------------------------------------|-----------------------------|
| `Spatial Blend`   | 0 = 2D, 1 = полное 3D                                     | 1 для мира, 0 для UI/музыки |
| `Min Distance`    | радиус, внутри которого громкость максимальна             | мотор 3–5 м                 |
| `Max Distance`    | в Linear — точка тишины; при Logarithmic **игнорируется** | 80–150 м                    |
| `Volume Rolloff`  | Logarithmic / Linear / Custom                             | Custom                      |
| `Doppler Level`   | сила эффекта Доплера                                      | 0 (см. 5.2)                 |
| `Spread`          | угол разброса по спикерам, 0–360°                         | 0 вдали, 40–60° вблизи      |
| `Reverb Zone Mix` | доля сигнала в reverb-зоны, 0–1.1                         | 1                           |

Все 3D-параметры применяются пропорционально `Spatial Blend` ([AudioSource reference][asr]).

**Про rolloff.** Logarithmic физически корректнее, но никогда не доходит до нуля — звук тянется бесконечно, занимая голос. Linear доходит до нуля, но из-за логарифмичности слуха воспринимается размазанно. Практичнее **Custom Rolloff**: логарифмическая форма вблизи и явный ноль на `Max Distance`.

```csharp
var curve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
source.maxDistance = 120f;
source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, curve);  // rolloffMode станет Custom
```

Кривая нормализуется в 0..1 по `maxDistance`, и `GetCustomCurve` вернёт не то же самое, что вы положили ([SetCustomCurve][scc]). Глобальный `Volume Rolloff Scale` в Project Settings → Audio влияет только на логарифмический режим ([Audio settings][am]).

### 5.2 Доплер

`Doppler Factor` глобально = 1 по умолчанию, `Doppler Level` на источнике — тоже. Проблема: доплер считается от относительной скорости источника и слушателя, а камера в машине двигается рывками (сглаживание, тряска, телепорт при респавне). Результат — паразитный вой и щелчки.

Решение: **`Doppler Level = 0` для всего, что едет вместе с камерой** (мотор, шины, ветер игрока и так не движутся относительно слушателя). Оставить 0.2–0.4 только проезжающему мимо трафику, где эффект реально нагляден. Ровно тот случай, когда «физически правильно» и «звучит хорошо» расходятся.

### 5.3 Голоса, приоритеты, виртуализация

Дефолты в `ProjectSettings/AudioManager.asset` (сверено с шаблонными проектами Unity): `m_RealVoiceCount: 32`, `m_VirtualVoiceCount: 512`, `m_VirtualizeEffects: 1`, `m_DSPBufferSize: 1024`.

Механика ([AudioSource.priority][pri]): когда активных источников больше, чем Max Real Voices, Unity **виртуализирует** лишние — глушит, но продолжает вести позицию воспроизведения, чтобы вернуть их, когда приоритет или громкость вырастут.
Первыми уходят источники с **наименьшим** приоритетом; при равном — с меньшей громкостью. `priority`: 0 наивысший, 256 наинизший, дефолт 128.
Наша раскладка: музыка 0, мотор и шины игрока 32, столкновения игрока 64, ambience 128, трафик и дальние объекты 200. `Virtualize Effect` (отключает эффекты у отсечённых источников) — оставить включённым.

---

## 6. Шины, столкновения, ветер, эмбиенс

**Скрип шин.** На `WheelCollider` данные уже есть: `WheelCollider.GetGroundHit(out WheelHit hit)` даёт `hit.forwardSlip` и `hit.sidewaysSlip`; документация прямо называет это штатным применением — звук проскальзывания играется, когда slip превышает порог ([WheelHit][wh]).
При аркадной модели на raycast-подвеске slip считаем сами: проекция скорости на боковую ось колеса, нормированная на порог сцепления. Реализация — **один зациклённый источник**, а не поток one-shot'ов: `volume = Mathf.SmoothStep(0, 1, (slip - threshold) / range)`, `pitch` слегка растёт со slip.

**Столкновения.** `OnCollisionEnter` → громкость от `collision.relativeVelocity.magnitude`, ниже порога (~2 м/с) не играть вовсе. Клипы — через Audio Random Container. Обязателен кулдаун на объект: при скольжении вдоль стены `OnCollisionEnter` сыплется пачками.

**Ветер.** Один луп на машине: громкость и срез `AudioLowPassFilter` растут со скоростью. Тот редкий случай, когда фильтр нужен на источнике, а не в микшере — он зависит от состояния конкретной машины.

**Ambience города.** Двухслойно: базовый 2D-луп (`spatialBlend = 0`) на группе `World` — ровный гул, и точечные 3D-эмиттеры (стройка, вентиляция, светофор) с небольшим `Max Distance`. Точечные — через Audio Random Container с `Trigger: Automatic`, `Automatic Trigger Mode: Offset` и рандомизацией времени: нерегулярные неповторяющиеся события без кода.

---

## 7. Оптимизация: форматы, загрузка, пул

### 7.1 Import Settings

Опции `Load Type` ([Audio Clip Import Settings][cai]): **Decompress On Load** — распаковка при загрузке, Vorbis в распакованном виде занимает **примерно в 10 раз больше** памяти, ADPCM — примерно в 3.5 раза.
**Compressed In Memory** — держать сжатым, распаковывать на лету; распаковка идёт на микшерном потоке и видна в профайлере как **DSP CPU**.
**Streaming** — читать с диска порциями, накладные расходы **~200 КБ на клип** даже без загруженных данных; замеры показывают, что каждый одновременно играющий экземпляр требует своего буфера ([gt]).

| Звук                      | Compression Format | Load Type            | Force To Mono |
|---------------------------|--------------------|----------------------|---------------|
| Лупы двигателя, впуск     | PCM или ADPCM      | Decompress On Load   | да            |
| Скрип шин, ветер          | ADPCM              | Decompress On Load   | да            |
| Столкновения, UI          | ADPCM              | Decompress On Load   | да (для 3D)   |
| Ambience города (длинный) | Vorbis             | Compressed In Memory | нет           |
| Музыка                    | Vorbis             | Streaming            | нет           |

`Force To Mono` для 3D-звуков обязателен: 3D-источник всё равно сводится в моно перед пространственной обработкой, стерео-версия — вдвое больше памяти впустую (при сведении Unity делает peak-нормализацию).
`Preload Audio Data` включён по умолчанию; `Load In Background` выключен — включать для длинных клипов, чтобы не стопорить главный поток ([cai]).

### 7.2 AudioSource и пул

Не создавать `AudioSource` через `AddComponent` в момент события — аллокация в горячем пути. Пул из 12–16 источников на группу `SFX`, выдача по кругу с проверкой `isPlaying`; если все заняты — забирать самый старый.

Для одиночных ударов `PlayOneShot` предпочтительнее `Play`: он **не обрывает** уже играющие one-shot'ы и клип самого источника ([API][pos]), в профайлере создаёт новые Audio Voices без роста Total Audio Sources ([gt]). Обратная сторона — легко наложить десятки голосов; отсюда пороги и кулдауны из раздела 6.

Для стыков сэмпл-в-сэмпл — `PlayScheduled(double time)` по шкале `AudioSettings.dspTime`, планируя на ~100–200 мс вперёд ([PlayScheduled][psh]). Кросс-фейду мотора это не нужно: там источники не перезапускаются.

### 7.3 Профайлер

Window → Analysis → Profiler → Audio. В режиме **Detailed** видно по каждому голосу: `Audibility`, `Virtual` (да/нет), `3D`, `OneShot`, `Distance`, `Plays` ([Audio Profiler][pra]).
Смотреть три числа: **Audio Voices** (не упирается ли в 32), **DSP CPU** (не съедает ли распаковка Compressed In Memory) и **Total Audio Memory**.
Колонка `Plays` — недооценённый инструмент отладки: если счётчик растёт там, где событий быть не должно, логика триггерит звук вхолостую. Память аудиосистемы **пулится и не отдаётся обратно** — растёт до насыщения и переиспользуется внутри ([pra]); не пугаться, что цифра не падает.

---

## 8. FMOD / Wwise — когда и почему не сейчас

**Когда middleware действительно нужен:** в команде есть звуковой дизайнер, который должен итерировать без программиста; нужен Live Update — правка звука в работающем билде; нужны сложные системы (гранулярный синтез мотора, адаптивная музыка с такт-синхронными переходами).

**Почему не наш случай:**

1. Проект — витрина возможностей Unity; демонстрировать в ней чужой аудиодвижок противоречит цели.
2. Звуковой команды нет, а основной выигрыш middleware — рабочий процесс для дизайнера, а не техническое превосходство. Этот выигрыш мы не получим.
3. Всё из этого документа — кросс-фейд по оборотам, снапшоты, ducking, reverb-зоны, вариативность через Audio Random Container — встроенное аудио Unity 6 делает штатно.
4. Цена и интеграция: платная лицензия выше indie-порога плюс вторая точка отказа в сборке.

**Когда пересмотреть:** если понадобится гранулярный движок мотора (типа Crankcase REV) — это единственное из раздела 2, чего у Unity штатно нет. Но и оно с 6.3 теоретически реализуемо через Scriptable Audio Pipeline на Burst.

---

## Антипаттерны

- **Один луп мотора с `pitch = rpm / 3000`.** Самый частый совет в туториалах и главная причина «пылесосного» звука (2.1).
- **Линейный кросс-фейд громкостей.** `a.volume = 1-t; b.volume = t` даёт провал −3 дБ в середине. Нужен constant power (2.4).
- **Переключение лупов через `Stop()`/`Play()`.** Щелчок на каждом стыке; источники должны играть всегда.
- **Смешивание exposed parameters и снапшотов на одном параметре.** После `SetFloat` снапшот его не тронет — «сломанный микшер» отлаживается часами (4.1).
- **Линейная привязка UI-слайдера 0..1 к громкости микшера.** Шкала в дБ, нужен `Mathf.Log10(v) * 20`.
- **`AudioMixer.SetFloat` в `Awake`/`OnEnable`.** Документация прямо запрещает (4.1).
- **`Doppler Level = 1` по умолчанию на всём.** Паразитный вой на дрожащей камере (5.2).
- **`AddComponent<AudioSource>()` на каждое столкновение.** Аллокация в горячем пути; нужен пул (7.2).
- **Vorbis + Decompress On Load для длинного эмбиенса.** ×10 по памяти (7.1).
- **Стерео-клипы на 3D-источниках без `Force To Mono`.** Вдвое больше памяти за то, что всё равно схлопнется в моно.
- **Совет «дождись DOTS Audio / DSPGraph»** (типичный для статей 2019–2022). Unity в [Q3 2025][st25] называет DSPGraph прошлым подходом; актуальное направление — Scriptable Audio Pipeline с 6.3.
- **Reverb Zone плюс reverb в микшере на одном звуке.** Двойная реверберация, неотстраиваемая (4.2).
- **Попытка сыграть Audio Random Container через `PlayOneShot`.** Метод принимает только `AudioClip` (раздел 3).
- **`Debug.Log` в цикле подбора кросс-фейда.** Аллокация строк каждый кадр.

---

## Чек-лист

Настройки проекта и микшер:

- [ ] Max Real Voices 32 / Max Virtual Voices 512 оставлены; `Virtualize Effect` включён.
- [ ] Создан `MainMixer` с группами `Music`, `SFX/Vehicle`, `SFX/World`, `SFX/UI`; снапшоты `Exterior`, `Interior`, `Paused`, `Exterior` помечен как Start Snapshot.
- [ ] Exposed только `MasterVolume`, `MusicVolume`, `SfxVolume`; конвертация в дБ есть; `SetFloat` не вызывается раньше `Start`.
- [ ] У каждого AudioSource выставлен `outputAudioMixerGroup` — ни один звук не идёт мимо микшера.

Двигатель и импорт:

- [ ] ≥ 2 RPM-лупов + idle + впуск; все с `loop = true`, `playOnAwake = false`.
- [ ] Кросс-фейд constant power; `pitch` считается от реальных оборотов записи сэмпла.
- [ ] Есть провал газа при переключении передачи и отсечка на `MaxRpm`; слой впуска гаснет при `throttle → 0`.
- [ ] Логика RPM вынесена в обычный C#-класс и покрыта EditMode-тестами.
- [ ] Все 3D-клипы — `Force To Mono`; короткие PCM/ADPCM + Decompress On Load, длинные Vorbis + Streaming.

Проверка в Play Mode:

- [ ] Профайлер: Audio Voices не упирается в 32 при полном трафике; DSP CPU без пиков на спавне.
- [ ] Разгон с места до отсечки — нет щелчков на стыках лупов и провалов громкости; сброс газа слышимо меняет тембр.
- [ ] Переключение камеры в салон даёт плавный переход снапшота, а не скачок.
- [ ] Скольжение вдоль стены не порождает очередь звуков удара.

---

## Видео и доклады

- Vehicle Recordings for Modern Games — GDC Festival of Gaming — https://www.youtube.com/watch?v=x6Tz8AFd9po — как записывают исходники для машин; объясняет, откуда берутся лупы и рампы из раздела 2.
- Adaptive Engines in FMOD Demo, Part 3 — Rory Walker — https://www.youtube.com/watch?v=qQPGAqxjtHU — наглядная демонстрация слоёв on/off-load и кросс-фейда по оборотам; идея переносится на Unity один в один.
- How Do NASCAR Video Games Create Authentic Engine Sounds? — Pit Stop Chronicles — https://www.youtube.com/watch?v=9Va4DLIJ6S0 — обзорно про подход к звуку мотора в гоночных играх.
- Signal Processing For Sound Design — GDC Festival of Gaming — https://www.youtube.com/watch?v=jVac5IFXpFo — фундамент по DSP: зачем EQ и фильтры там, где нет отдельных сэмплов.
- How To Randomize Pitch Of Audio In Unity 6 (Audio Random Container) — Unity Unlocked — https://www.youtube.com/watch?v=G9EEio1BahY — короткий разбор ARC именно на Unity 6.
- Using Random Containers & Triggering Audio with Animation, Audio in Unity Ep. 2 — SwishSwoosh — https://www.youtube.com/watch?v=3fHe2wPBooY — ARC в связке с анимацией.
- Better Game Sound Effects with Audio Random Container — Sunny Valley Studio — https://www.youtube.com/watch?v=g4d0wZCU3AQ — быстрый обзор UI ассета.
- Audio Mixers and Snapshots in Unity — Anthony Romrell — https://www.youtube.com/watch?v=_Dp5O071XyE — практика по снапшотам и переходам.
- Unity 3D Tutorial Part 5: Audio Mixer, Audio Snapshot, and Audio Scripting — Studica News — https://www.youtube.com/watch?v=9Sa2Zns9CRo — микшер плюс скриптовая часть, включая exposed parameters.
- Boosting your game performance with Unity 6 Profiling tools, Unite 2024 — Unity — https://www.youtube.com/watch?v=_cV1B2hqXGI — официальный доклад по профайлеру Unity 6; аудиомодуль в общем контексте.
- Realistic Car Sounds in Unity: Engine, Brake & Crash Sound Effects — Sunny Gamedev — https://www.youtube.com/watch?v=IHY3sAPz7Pk — практический разбор набора звуков машины в Unity.
- Unity Car: Engine, Gears, Traction Control + RPM UI (Part 2) — Simon Lee — https://www.youtube.com/watch?v=zFcfh09YZ4Y — симуляция передач и RPM; ориентир для 2.3.

---

## Источники

Проверено 2026-08-19. Документация — ветка Unity 6.3 LTS (6000.3), кроме двух страниц Audio Random Container, которые полнее в 6000.2.

Руководство Unity, `https://docs.unity3d.com/6000.3/Documentation/Manual/` + имя страницы: `Audio.html`, `AudioOverview.html`, [`AudioSource-reference.html`][asr], [`class-AudioClip.html`][cai], `AudioFiles-compression.html`, [`class-AudioManager.html`][am], [`AudioMixerOverview.html`](https://docs.unity3d.com/6000.3/Documentation/Manual/AudioMixerOverview.html), `AudioMixerSpecifics.html`, [`AudioMixerInspectors.html`][mixin], [`audio-effects.html`][fx], `audio-filters.html`, [`class-AudioReverbZone.html`][rz], `AudioRandomContainer.html`, `Create-randomized-playlist.html`, [`ProfilerAudio.html`][pra], `audio-scriptable-processors.html`. Плюс [Audio Random Container fundamentals][arcf] и [reference][arcui] (6000.2).

Scripting API, `https://docs.unity3d.com/6000.3/Documentation/ScriptReference/` + имя страницы: `AudioSource-resource.html`, `Audio.AudioResource.html`, `AudioSource-generator.html`, [`AudioSource-priority.html`][pri], [`AudioSource.PlayOneShot.html`][pos], [`AudioSource.PlayScheduled.html`][psh], [`AudioSource.SetCustomCurve.html`][scc], `AudioSourceCurveType.html`, `AudioSource-outputAudioMixerGroup.html`, [`Audio.AudioMixer.SetFloat.html`][setf], `Audio.AudioMixerSnapshot.TransitionTo.html`, [`Audio.AudioMixer.TransitionToSnapshots.html`][tts], [`WheelHit.html`][wh], `Rigidbody-linearVelocity.html`.

Официальные посты и репозитории Unity:

- Audio status update Q3 2025 — https://discussions.unity.com/t/audio-status-update-q3-2025/1681867
- Audio status update Q2 2026 — https://discussions.unity.com/t/audio-status-update-q2-2026/1723396
- Unity-Technologies/audio-examples — https://github.com/Unity-Technologies/audio-examples
- The Basics of the Audio Random Container (Unity Learn) — https://learn.unity.com/tutorial/the-basics-of-the-audio-random-container

Отраслевые и сторонние источники:

- Integrating Interactive Car Engine Sounds in Games — BOOM Library, Mike Caviezel (Forza / Gran Turismo) — https://www.boomlibrary.com/blog/the-car-engine-sound-primer-mike-caviezel/
- AudioResource, AudioClip, AudioRandomContainer Interactions — Sirawat Pitaksarit — https://gametorrahod.com/audio-random-container/
- 10 Unity Audio Optimisation Tips — Game Dev Beginner — https://gamedevbeginner.com/unity-audio-optimisation-tips/

[am]: https://docs.unity3d.com/6000.3/Documentation/Manual/class-AudioManager.html
[arcf]: https://docs.unity3d.com/6000.2/Documentation/Manual/AudioRandomContainer-fundamentals.html
[arcui]: https://docs.unity3d.com/6000.2/Documentation/Manual/AudioRandomContainer-UI.html
[asr]: https://docs.unity3d.com/6000.3/Documentation/Manual/AudioSource-reference.html
[boom]: https://www.boomlibrary.com/blog/the-car-engine-sound-primer-mike-caviezel/
[cai]: https://docs.unity3d.com/6000.3/Documentation/Manual/class-AudioClip.html
[fx]: https://docs.unity3d.com/6000.3/Documentation/Manual/audio-effects.html
[gt]: https://gametorrahod.com/audio-random-container/
[mixin]: https://docs.unity3d.com/6000.3/Documentation/Manual/AudioMixerInspectors.html
[pos]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AudioSource.PlayOneShot.html
[pra]: https://docs.unity3d.com/6000.3/Documentation/Manual/ProfilerAudio.html
[pri]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AudioSource-priority.html
[psh]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AudioSource.PlayScheduled.html
[rz]: https://docs.unity3d.com/6000.3/Documentation/Manual/class-AudioReverbZone.html
[scc]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AudioSource.SetCustomCurve.html
[setf]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Audio.AudioMixer.SetFloat.html
[st25]: https://discussions.unity.com/t/audio-status-update-q3-2025/1681867
[st26]: https://discussions.unity.com/t/audio-status-update-q2-2026/1723396
[tts]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Audio.AudioMixer.TransitionToSnapshots.html
[wh]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/WheelHit.html

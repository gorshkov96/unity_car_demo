# Unity Car Demo

3D-демо на Unity 6: управляемый автомобиль на процедурно сгенерированной карте.
Проект — витрина возможностей Unity: физика, рендеринг, эффекты, UI, ИИ-агенты.

Сцена, карта, машина, материалы и настройки рендера **создаются кодом** — руками
в редакторе ничего собирать не нужно, всё пересоздаётся одной командой.

## Требования

- macOS (собиралось на Apple Silicon) или Windows
- Unity **6000.3.21f1** с модулем Mac Build Support
- Render Pipeline: **URP 17.3**, ввод: **Input System** (project-wide actions)

## Управление

| Клавиша | Действие |
|---|---|
| `W` / `S` (или ↑ / ↓) | газ / тормоз и задний ход |
| `A` / `D` (или ← / →) | руль |
| `Space` | ручник — задние колёса срываются в занос |
| `R` | вернуть машину на старт |
| `Cmd+Q` | выход |

Геймпад: левый стик — руль, триггеры — газ и тормоз, A — ручник, Y — рестарт.

Слева внизу — спидометр и счётчик колёс на земле, справа вверху — кадровый бюджет.

## Запуск

Собрать и запустить standalone-плеер (редактор открывать не нужно):

```bash
"/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity" -batchmode -nographics -quit -projectPath "$PWD" -executeMethod CarDemo.EditorTools.PlayerBuilder.BuildMac -logFile -
```

```bash
open Builds/CarDemo.app
```

Открыть проект в редакторе:

```bash
"/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity" -projectPath "$PWD"
```

## Пересоздать сцену

Вся сцена строится [SceneBootstrapper](Assets/_Project/Scripts/Editor/SceneBootstrapper.cs).
Правим код или конфиги — и пересобираем:

```bash
"/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity" -batchmode -nographics -quit -projectPath "$PWD" -executeMethod CarDemo.EditorTools.SceneBootstrapper.RebuildDemoScene -logFile -
```

Мир детерминирован: один и тот же сид в `WorldConfig` всегда даёт одинаковую карту.

Через меню редактора: **CarDemo ▸ Rebuild Demo Scene**. Там же — применение настроек
рендера, проекта, слоёв и Input Actions.

## Проверки

Смоук-тест ходовых качеств — прогоняет физику без Play Mode, ~40 секунд:

```bash
"/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity" -batchmode -nographics -projectPath "$PWD" -executeMethod CarDemo.EditorTools.DriveSmokeTest.Run -logFile - | grep SMOKE
```

Эталон последнего прогона:

```
[SMOKE] settle: grounded=True height=0.55m -> OK
[SMOKE] accelerate: distance=84.8m speed=102km/h -> OK
[SMOKE] straight: lateral drift=0.04m over 84.8m -> OK
[SMOKE] steer: yaw turned=175.6 deg to the right -> OK
[SMOKE] brake: 105 -> 28 km/h, 10.6 m/s^2 (1.08g) -> OK
[SMOKE] handbrake: slip angle=17.1 deg -> OK
```

Юнит-тесты (EditMode, 30 тестов):

```bash
"/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity" -runTests -batchmode -projectPath "$PWD" -testPlatform EditMode -testResults /tmp/results.xml -logFile -
```

## Замер производительности

Бенчмарк: машина 35 секунд едет по кругу на автопилоте, пробник пишет перцентили
кадрового времени, draw calls и мусор GC.

```bash
"/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity" -batchmode -nographics -quit -projectPath "$PWD" -executeMethod CarDemo.EditorTools.SceneBootstrapper.RebuildBenchmarkScene -logFile -
```

```bash
"/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity" -batchmode -nographics -projectPath "$PWD" -executeMethod CarDemo.EditorTools.PlayerBuilder.BuildBenchmark -logFile - && open Builds/CarDemoBenchmark.app
```

Результат — в `~/Library/Logs/Unity Technologies/CarDemo/Player.log`, строки `[PERF]`.

Замеренный эффект настройки рендера (M-серия, 3840×2160, автопилот по кругу):

| | было | стало |
|---|---|---|
| кадр, медиана | 15.4 мс | 7.4 мс |
| draw calls | 149–376 | 22–44 |
| кадров вне бюджета 16.66 мс | 12% | 0% |

## Структура

```
Assets/_Project/
  Scripts/
    Core/          слои, детерминированный генератор случайных чисел
    Vehicle/       физика машины, подвеска, ввод, вращение колёс, камера
    World/         процедурная карта, движущиеся объекты
    Game/          сборка машины из примитивов, респавн
    UI/            HUD на UI Toolkit, спидометр на Painter2D
    Diagnostics/   замер кадрового бюджета
    Editor/        генераторы сцены и настроек, смоук-тест, сборка плеера
    Tests/         EditMode и PlayMode
  Scenes/          сгенерированные сцены
  Settings/        конфиги машины и мира, Input Actions
```

Модули разделены через `.asmdef`, зависимости — только вниз: `Core` → `Vehicle`,
`World` → `Game` → `UI`.

## Документация

- [CLAUDE.md](CLAUDE.md) — правила работы над проектом
- [docs/unity/](docs/unity/) — база знаний по Unity 6.3, на которую опирается код

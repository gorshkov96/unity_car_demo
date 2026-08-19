# База знаний по Unity 6.3 для этого проекта

Семнадцать файлов с гайдлайнами, лучшими практиками и подводными камнями,
собранные из документации Unity 6.3, блога Unity, форумов, докладов и видео.
Всё привязано к нашему проекту: машина на статичной карте, живой мир вокруг,
витрина возможностей движка.

Материал собран **19 августа 2026** под Unity **6000.3.21f1**. Unity выпускает
патчи каждые пару недель — если файл расходится с документацией, прав документ.

## С чего начать

| Ситуация | Куда смотреть |
|---|---|
| Создаём проект с нуля | [14 — создание проекта, структура, git](14-git-project-setup.md) → [01 — URP](01-render-urp.md) |
| Пишем первый скрипт | [05 — архитектура](05-architecture.md) + [15 — стиль кода](15-code-style.md) |
| Делаем машину | [03 — физика](03-vehicle-physics.md) → [04 — ввод](04-input-system.md) → [09 — камера](09-cinemachine.md) → [10 — звук](10-audio.md) |
| Строим карту | [08 — мир и дороги](08-world-building.md) → [02 — свет](02-lighting-shadows.md) |
| Оживляем мир | [07 — трафик и ИИ](07-traffic-ai.md) |
| Проверяем, что не сломалось | [13 — тесты и компиляция](13-testing-ci.md) |
| Тормозит | [06 — производительность](06-performance.md) |
| Надо что-то изучить | [17 — каналы, видео, книги](17-learning.md) |

## Все файлы

| № | Файл | О чём |
|---|---|---|
| 01 | [render-urp](01-render-urp.md) | URP, Render Graph, Forward+, GPU Resident Drawer, качество, постобработка |
| 02 | [lighting-shadows](02-lighting-shadows.md) | Adaptive Probe Volumes, запекание, тени, отражения, освещение движущейся машины |
| 03 | [vehicle-physics](03-vehicle-physics.md) | Raycast-подвеска против WheelCollider, настройка, дрифт, ощущение веса |
| 04 | [input-system](04-input-system.md) | Input Actions, project-wide actions, буферизация ввода под FixedUpdate |
| 05 | [architecture](05-architecture.md) | Композиция, ScriptableObject, event channels, asmdef, границы модулей |
| 06 | [performance](06-performance.md) | Бюджет кадра, профайлер, GC, пулы, батчинг, LOD, Jobs и Burst |
| 07 | [traffic-ai](07-traffic-ai.md) | Трафик по сплайнам, пешеходы на NavMesh, уровни масштабирования до ECS |
| 08 | [world-building](08-world-building.md) | Splines, дороги мешем из кода, ProBuilder, процедурная сборка сцены |
| 09 | [cinemachine](09-cinemachine.md) | Cinemachine 3.x, таблица переименований с 2.x, камера за машиной |
| 10 | [audio](10-audio.md) | Звук двигателя по оборотам, микшер, 3D-звук, шины и окружение |
| 11 | [vfx](11-vfx.md) | ParticleSystem и VFX Graph, следы шин, Shader Graph, постобработка скорости |
| 12 | [ui](12-ui.md) | UI Toolkit против uGUI, спидометр на Painter2D, HUD без аллокаций |
| 13 | [testing-ci](13-testing-ci.md) | EditMode-тесты, проверка компиляции из терминала, коды возврата, CI |
| 14 | [git-project-setup](14-git-project-setup.md) | Создание проекта из CLI, .gitignore, meta-файлы, LFS, раскладка папок |
| 15 | [code-style](15-code-style.md) | Стиль C# в Unity, .editorconfig, анализаторы, ловушки сериализации |
| 16 | [ai-tooling](16-ai-tooling.md) | Сцена как код, автоматизация редактора, batch-режим, MCP для Unity |
| 17 | [learning](17-learning.md) | Каналы, конкретные видео, e-book Unity, доклады — с пометками актуальности |

## Версии пакетов для Unity 6000.3.21f1

Проверены при сборе материала, сверяться при создании проекта:

| Пакет | Версия |
|---|---|
| com.unity.render-pipelines.universal | 17.3.0 |
| com.unity.inputsystem | 1.20.0 |
| com.unity.cinemachine | 3.1.7 |
| com.unity.splines | 2.9.0 |
| com.unity.ai.navigation | 2.0.14 |
| com.unity.test-framework | 1.6.0 |
| com.unity.burst | 1.8.30 |
| com.unity.entities | 1.4.8 |

## Как этим пользуется агент

Поверх этой базы лежат скиллы в `.claude/skills/`: они держат в памяти жёсткие
правила и отправляют за деталями сюда. Правило одно — решения меняются здесь,
а не в скиллах.

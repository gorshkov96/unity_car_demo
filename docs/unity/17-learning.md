# 17. Обучающие материалы: каналы, видео, курсы, книги

Каталог проверенных источников по темам проекта. Заглядывать сюда, когда нужно разобраться в незнакомой подсистеме Unity 6, и до того, как открывать случайный туториал из поиска.

---

## TL;DR — решения для нашего проекта

1. **Базовый набор для чтения — четыре официальных e-book Unity 6**, скачивать именно редакции «Unity 6», не 2022 LTS: C# Code Style Guide, Design patterns and SOLID, ScriptableObjects, URP for advanced creators (ссылки в разделе «E-book»).
2. **Официальные видеосерии Unity точно совпадают с версиями пакетов в 6.3.** Проверено по мануалу 6000.3: Cinemachine `3.1.7`, Input System `1.20.0`, AI Navigation `2.0.14`. Серии «Cinemachine 3.1», «Input System в Unity 6 (1–7)» и «AI Navigation 2.0» — не «примерно подходят», а ровно наши.
3. **Физику машины учить по Toyful Games**, а не по туториалам с `WheelCollider`. Их разбор raycast-подвески (лучи вниз из углов кузова вместо колёсных коллайдеров) — ровно та аркадная модель, которую CLAUDE.md рекомендует по умолчанию.
4. **Живые каналы на 2026:** Unity, Code Monkey, Tarodev, git-amend, Game Dev Guide, Sasquatch B Studios, Sunny Valley Studio, LlamAcademy, Sebastian Lague. Каналы Infallible Code (последнее видео 07.2022), Dave / GameDevelopment (03.2023), iHeartGameDev (12.2024) — архив: смотреть можно, API сверять обязательно.
5. **Brackeys больше не про Unity.** Канал вернулся в 2024 с туториалами по Godot; вся его Unity-библиотека — эпоха 2019–2020, как инструкция под Unity 6 непригодна.
6. **git-amend — главный «взрослый» канал по архитектуре под Unity 6.** Assembly Definition (`.asmdef` — единица компиляции, задающая границы модулей) — 02.2026; ScriptableObject-архитектура — 02.2025; Event Bus; Cinemachine 3.1.
7. **Оптимизацию смотреть с Unite 2024/2025, а не с роликов «10 tips».** Тему закрывают три доклада: «Graphics rendering: best performance with Unity 6», «Advanced performance tips and tricks from a Unity consultant», «Optimizing smarter, not harder».
8. **Трафик и ИИ:** старт с официальной серии «AI Navigation 2.0» (4 видео, `NavMeshSurface` вместо старого запекания навмеша в настройках сцены), затем git-amend «Boids» для стайного поведения. Доклады Unite про ECS-трафик (Metropolis, Spline Based AI Agents) — 2018–2019 годов: брать идеи, не код.
9. **Catlike Coding — только раздел Custom SRP.** Автор сам предупреждает, что большинство серий сделаны до Unity 2019 LTS; при этом Custom SRP имеет отдельные обновления «4.0.0 Unity 6», «6.0.0 Unity 6.3», «7.0.0 Unity 6.5».
10. **Правило фильтрации любого туториала:** если в коде есть `rigidbody.velocity`, `FindObjectOfType<T>()`, `Input.GetAxis` или `CinemachineVirtualCamera` — материал старше Unity 6. Читать как идею, не как инструкцию.

---

## Как читать этот каталог

Пометки версий: **[U6]** — материал явно заявлен под Unity 6 / 6.1 / 6.2 / 6.3; **[2022]** — эпоха Unity 2021–2022 LTS, API частично устарел; **[архив]** — 2018–2020, брать только концепции.

Дата обращения ко всем ссылкам — **2026-08-19**. Названия каналов и даты публикации видео проверены запросом к страницам YouTube, не по памяти. URL, которые не удалось подтвердить, в файл не попали.

---

## Официальные бесплатные e-book Unity

Скачиваются бесплатно с `unity.com/resources` после короткой формы. Все ссылки проверены (HTTP 200), названия взяты со страниц.

| E-book | Ссылка | Зачем нам |
|---|---|---|
| C# Code Style Guide (Unity 6 edition) | https://unity.com/resources/c-sharp-style-guide-unity-6 | Единый стиль кода; основа правил именования в проекте |
| Level up your code with design patterns and SOLID | https://unity.com/resources/design-patterns-solid-ebook | 11 паттернов с примерами; State и Observer нужны для машины и трафика |
| Create modular game architecture with ScriptableObjects | https://unity.com/resources/create-modular-game-architecture-scriptableobjects-unity-6 | Ровно наш подход к конфигам характеристик машины |
| Introduction to URP for advanced creators (Unity 6 edition) | https://unity.com/resources/introduction-to-urp-advanced-creators-unity-6 | Самое полное описание URP: настройки, свет, тени, Render Graph |
| Create shaders and visual effects with URP (Unity 6) | https://unity.com/resources/create-shaders-visual-effects-urp-unity-6 | «Кулинарная книга» шейдеров и эффектов под URP |
| Unity 6: The Definitive Guide to Advanced Visual Effects | https://unity.com/resources/creating-advanced-vfx-unity6 | VFX Graph (GPU-система частиц): дым, искры, пыль из-под колёс |
| Ultimate Guide to Profiling Unity 6 Games | https://unity.com/resources/ultimate-guide-to-profiling-unity-games-unity-6 | Методика профилирования, а не набор советов |
| Optimize your game performance for consoles and PCs (Unity 6) | https://unity.com/resources/console-pc-game-performance-optimization-unity-6 | Наша платформа — десктоп; в этой редакции разделы по HDRP |
| Optimize performance for mobile, XR & web games (Unity 6) | https://unity.com/resources/mobile-xr-web-game-performance-optimization-unity-6 | Здесь разделы про URP — читать вместе с предыдущей |
| Create Scalable & Performant UI with UI Toolkit in Unity 6 | https://unity.com/resources/scalable-performant-ui-uitoolkit-unity-6 | Старт по UI Toolkit (UI на разметке UXML/USS вместо uGUI) |
| Tips to Increase Productivity in Unity 6 | https://unity.com/resources/tips-improve-productivity-workflow-unity-6 | Мелочи редактора, экономящие часы |
| The Ultimate Unity Gamedev Field Guide | https://unity.com/resources/unity-game-dev-field-guide | Обзорная карта возможностей Unity — для владельца проекта |
| Best practices for project organization and version control | https://unity.com/resources/best-practices-version-control-unity-6 | Структура папок, `.meta`, что коммитить |
| Intro to the DOTS Features & Samples in Unity 6 | https://unity.com/resources/dots-concepts-features-samples-resources-unity-6 | Пригодится, если трафик вырастет до тысяч машин |
| High-End Lighting & Environments with HDRP in Unity 6 | https://unity.com/resources/lighting-environments-hdrp-unity-6 | Нам не нужен (мы на URP), но полезен как справочник по свету |

Сопровождающие проекты с кодом [U6]:

- Демо всех 11 паттернов — https://github.com/Unity-Technologies/game-programming-patterns-demo
- ScriptableObjects Paddle Ball (обновлён под 6.1) — https://assetstore.unity.com/packages/templates/tutorials/scriptableobjects-paddle-ball-project-325743
- Bagel Game — World Space UI, кастомные шейдеры и SVG в UI Toolkit — https://github.com/Unity-Technologies/BagelGame
- QuizU (UI Toolkit + паттерны) — https://assetstore.unity.com/packages/essentials/tutorial-projects/quizu-a-ui-toolkit-sample-268492
- Dragon Crashers — UI Toolkit — https://assetstore.unity.com/packages/essentials/tutorial-projects/dragon-crashers-ui-toolkit-sample-project-231178
- ECS Samples — https://github.com/Unity-Technologies/EntityComponentSystemSamples ; Megacity Metro (город на DOTS) — https://github.com/Unity-Technologies/Megacity-Metro

---

## Официальные видеосерии Unity

Все плейлисты — с канала Unity (https://www.youtube.com/@unity). Совпадение с пакетами Unity 6.3 проверено по мануалу 6000.3.

- **Input System Tutorials** [U6] — https://www.youtube.com/playlist?list=PLX2vGYjWbI0RpLvO3B7aH-ObfcOifMD20 — 7 серий: редактор Input Actions, скриптинг, мобильное управление, связка с UI Toolkit, ребайндинг, `PlayerInput`, `PlayerInputManager`. Пакет `com.unity.inputsystem` 1.20.0.
- **Cinemachine 3.1 Tutorials** [U6] — https://www.youtube.com/playlist?list=PLX2vGYjWbI0QiMBrmyzbxZeHepAbhVOJa — 5 серий; в 6.3 стоит Cinemachine 3.1.7. Cinemachine 3 — не апдейт фич, а смена формата: классы переименованы (`CinemachineVirtualCamera` → `CinemachineCamera`), апгрейд с 2.x требует ручной работы (руководство: https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineUpgradeFrom2.html).
- **Unity AI Navigation 2.0 Tutorials** [U6] — https://www.youtube.com/playlist?list=PLX2vGYjWbI0SsXFD1Gjo-8kFEpzk5k4Kh — 4 серии: основы NavMesh, линки и препятствия, рантайм-поверхности, летающие NPC. Пакет `com.unity.ai.navigation` 2.0.14.
- **UI Toolkit Tutorials** [U6] — https://www.youtube.com/playlist?list=PLX2vGYjWbI0S3Zx5Dv7htBcWLBfKWf2qI — 14 роликов, включая фичи 6.3: World Space Render Mode, кастомные шейдеры для UI, SVG, рантайм-биндинг данных, ListView/ScrollView, темы и USS-переменные, drag-and-drop, тултипы.
- **Render Graph** [U6] — https://www.youtube.com/playlist?list=PLX2vGYjWbI0RwCoL6a96ltbG9JCSE6j1m — Render Graph, новая система описания проходов рендера в URP Unity 6; отсюда начинать, если будем писать свой Renderer Feature.
- **Unity Profiler Walkthrough & Tutorials** [U6] — https://www.youtube.com/playlist?list=PLX2vGYjWbI0QNVu6deCYvWGX7Gj0GmdmE — Profiler, Profile Analyzer, Memory Profiler: три инструмента, три ролика.
- **Unity Tip Tuesdays** [U6] — https://www.youtube.com/playlist?list=PLX2vGYjWbI0SUyNX-WuICwLqNQX2Fb4o8 — минутные советы, выходят регулярно.
- **Unite 2025** — https://www.youtube.com/playlist?list=PLX2vGYjWbI0TBAgte6nJfL9expGYuPqUO ; **Unite 2024** — https://www.youtube.com/playlist?list=PLX2vGYjWbI0TV4Di9Uhg22OkOH41VcVez

---

## Unity Learn и документация

- Unity Learn — https://learn.unity.com/ (бесплатно, нужен аккаунт); все pathway — https://learn.unity.com/pathways
- **Unity Essentials Pathway** [U6] — https://learn.unity.com/pathway/unity-essentials — стартовая точка для владельца проекта: навигация по редактору, GameObject, префабы, базовый C#, физика, сборка билда.
- **Junior Programmer Pathway** [U6] — https://learn.unity.com/pathway/junior-programmer — продолжение: C# и типовые игровые механики. Ещё курс с нуля: https://learn.unity.com/course/create-with-code
- Best practice guides в мануале 6.3 — https://docs.unity3d.com/6000.3/Documentation/Manual/best-practice-guides.html ; в 6.3 туда входит «UI Toolkit for advanced Unity developers» — https://docs.unity3d.com/6000.3/Documentation/Manual/best-practice-guides/ui-toolkit-for-advanced-unity-developers/bpg-uiad-index.html
- Раздел Optimization — https://docs.unity3d.com/6000.3/Documentation/Manual/analysis.html ; введение в URP — https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-introduction.html
- Исходники C#-части движка (когда документация недоговаривает) — https://github.com/Unity-Technologies/UnityCsReference

Важное замечание: в итогах 2025 года Unity прямо признаёт, что продвинутого контента на Learn мало и это зона роста на 2026. Поэтому по сложным темам идти в e-book и на YouTube, а Learn использовать только для старта.

---

## YouTube-каналы

### Живые и актуальные под Unity 6

Даты — последнее видео на момент проверки 2026-08-19.

| Канал | Ссылка | Последнее видео | Чем полезен | Риск устаревших туториалов |
|---|---|---|---|---|
| Unity (официальный) | https://www.youtube.com/@unity | 08.2026 | Серии по пакетам, Unite-доклады, Tip Tuesday | Низкий; старые плейлисты помечены годами |
| Code Monkey | https://www.youtube.com/@CodeMonkeyUnity | 08.2026 | Полные бесплатные курсы, разбор новостей Unity | Средний: много видео 2020–2022 со старым Input и API |
| git-amend | https://www.youtube.com/@git-amend | 08.2026 | Архитектура: Event Bus, `.asmdef`, ScriptableObject, паттерны, Cinemachine 3.1 | Низкий: свежее снято на Unity 6 |
| Tarodev | https://www.youtube.com/@Tarodev | активен | Сжатые разборы «как правильно»: `Awaitable`, UI Toolkit, рендер тысяч объектов | Средний: сильные видео 2022–2023 |
| Game Dev Guide | https://www.youtube.com/c/GameDevGuide | 08.2026 | Инструменты редактора, кастомные окна, UI, производительность физики | Низкий-средний |
| Sasquatch B Studios | https://www.youtube.com/@sasquatchbgames | 08.2026 | Практика: пулы объектов, атрибуты, порядок инициализации сцены | Низкий |
| Sunny Valley Studio | https://www.youtube.com/@SunnyValleyStudio | 06.2026 | Партнёр Unity по серии статей о чистом коде; разборы «что нового и что сломается» | Низкий |
| LlamAcademy | https://www.youtube.com/@LlamAcademy | 08.2026 | Точечные разборы: Raycast, Event Channel на ScriptableObject, UI Toolkit | Низкий |
| Sebastian Lague | https://www.youtube.com/@SebastianLague | 07.2026 | Не туториалы, а «как устроен алгоритм»: рендер, симуляции, физика | Неприменимо |
| Freya Holmér | https://www.youtube.com/@acegikmo | 01.2025 | Математика для геймдева: сплайны, векторы, интерполяция | Неприменимо |
| Jason Weimann | https://www.youtube.com/@Unity3dCollege | 12.2025 | 27-часовой курс с нуля (12.2025), архитектурные разборы | Средний: много старого архива |
| Christina Creates Games | https://www.youtube.com/@ChristinaCreatesGames | активен | Отмечена Unity как community-креатор | Глубоко не проверял |
| SpeedTutor | https://www.youtube.com/channel/UCwYuQIa9lgjvDiZryUVtFGw | 2026 | Есть свежий курс по контроллеру машины под Unity 6 | Средний |
| Sakura Rabbit | https://www.youtube.com/@sakurarabbit6708 | активен | Не туториалы — реалистичная 3D-графика на Unity, референс визуала | Неприменимо |

### Архив: смотреть можно, копировать код нельзя

| Канал | Ссылка | Последнее видео | Что с ним |
|---|---|---|---|
| iHeartGameDev | https://www.youtube.com/@iHeartGameDev | 12.2024 | Отличный бесплатный курс по процедурной анимации и разборы state machine; новых видео нет |
| Infallible Code | https://www.youtube.com/@InfallibleCode | 07.2022 | Паттерны, юнит-тесты, `.asmdef`, Git для Unity. Концептуально живо, API эпохи 2021 |
| Dave / GameDevelopment | https://www.youtube.com/@davegamedevelopment | 03.2023 | Быстрые механики движения (грэпплинг, вол-ран, дэш). Идеи хорошие, Input старый |
| Brackeys | https://www.youtube.com/channel/UCYbK_tjZ2OrIZFBvU6CCMiA | вернулся в 2024, но с Godot | Все Unity-видео — 2019–2020. Не использовать как инструкцию под Unity 6 |
| Toyful Games | https://www.youtube.com/watch?v=CdPYlj5uZeI | 2021–2022 | Всего пара видео, но по физике машины — лучшее, что есть |

---

## Подборка по темам проекта

Конкретные ссылки на видео — в разделе «Видео и доклады» ниже; здесь только порядок изучения и смысл.

**Физика машины.** Наш выбор по CLAUDE.md — аркадная физика на `Rigidbody` с raycast-подвеской. Порядок: Toyful Games «Making Custom Car Physics» (три силы на колесо: подвеска, боковое сцепление, тяга) → текстовый компаньон https://www.toyfulgames.com/blog/deep-dive-physics → Ash Dev «Arcade Car Controller Part 1» и LUIGI GAME DEV «Raycast Car Physics» как пошаговая реализация → SpeedTutor (вариант на `WheelCollider`) для сравнения подходов. Рабочие код-референсы: https://github.com/SergeyMakeev/ArcadeCarPhysics и https://github.com/hayden-donnelly/vehicle-physics

**URP и свет в Unity 6.** E-book «Introduction to URP for advanced creators» → видео «Understanding URP settings and essentials» → «New lighting features and workflows in Unity 6» (APV, Adaptive Probe Volumes — сетка зондов освещения вместо ручной расстановки; борьба с протечками света) → «How to: Excellently Light a Scene with URP». Доклад «Transitioning from the Built-in Render Pipeline to URP and HDRP» объясняет, почему Built-in больше не выбор. Для собственного прохода рендера — плейлист Render Graph. Единственная живая серия Catlike Coding — https://catlikecoding.com/unity/tutorials/custom-srp/ (обновления под 6.0 / 6.3 / 6.5).

**Cinemachine 3.** Официальная серия из 5 роликов + git-amend «Essential Elements of Cinemachine 3.1» + блог https://unity.com/blog/engine-platform/see-whats-new-with-cinemachine-3

**Input System.** Официальная серия из 7 частей — основной источник. Текстом: https://gamedevbeginner.com/input-in-unity-made-easy-complete-guide-to-the-new-system/ (обновлена в 2025). Тонкости interactions — git-amend «Press vs Hold Interactions».

**ScriptableObject-архитектура.** E-book «Create modular game architecture with ScriptableObjects» → git-amend «Improve your Game Architecture with Scriptable Objects» → git-amend «Advanced Event Bus» и LlamAcademy «Event Bus & SO Event Channels» (связь систем без прямых ссылок) → git-amend «Assembly Definitions Explained» (`.asmdef` как граница зависимостей).

**Оптимизация.** Сначала профилировать: плейлист Profiler → e-book «Ultimate Guide to Profiling Unity 6 Games» → доклады Unite 2024/2025. Прицельно по нашему случаю: Game Dev Guide «Thousands of Objects? Fix Unity Physics Performance», Sasquatch B «Generic Object Pool System».

**VFX Graph и партиклы.** E-book «Definitive Guide to Advanced Visual Effects» (анонс: https://unity.com/blog/unity-6-vfx-graph-ebook) → трёхсерийник Unity «URP Cookbook: Compute shaders» → Cam Ayres «Complete Beginner's Guide to VFX Graph in Unity 6».

**UI Toolkit.** E-book «Create Scalable & Performant UI» → официальная серия из 14 роликов → гайд в мануале 6.3 → Unite 2024 «Getting the best performance with UI Toolkit». Точечные разборы: Tarodev «UI Toolkit Primer», LlamAcademy «Every UI Toolkit Scale Mode Explained».

**Трафик, ИИ-агенты, движущееся окружение.** Официальная серия «AI Navigation 2.0» — базовый инструмент → git-amend «Boids» (стайное поведение без навмеша, дёшево по CPU) → Freya Holmér «The Continuity of Splines» (трафик по дорогам естественно строится на сплайнах, а не на навмеше) → доклады Unite про ECS-трафик как источник архитектурных идей → Unite 2025 «Using DOTS to optimize GameObject gameplay» о том, как внедрять DOTS точечно.

**Математика и «как это устроено».** Freya Holmér «Lerp smoothing is broken» — обязательно перед написанием сглаживания камеры и рулевого ввода: наивный `Lerp(a, b, 0.1f)` в `Update` зависит от FPS. Sebastian Lague — Coding Adventures про алгоритмы и рендер. Tarodev «How To Render 2 Million Objects At 120 FPS» — инстансинг и батчинг наглядно.

---

## Блоги, сообщества, книги

**Блоги.** Unity Blog — https://unity.com/blog ; два поста-навигатора: итоги 2025 https://unity.com/blog/2025-technical-content-round-up и итоги 2024 https://unity.com/blog/2024-technical-content-roundup ; подборки по графике https://unity.com/blog/unity-6-graphics-learning-resources и по оптимизации https://unity.com/blog/unity-6-game-optimization-guides . Game Dev Beginner (John French) — https://gamedevbeginner.com/ , длинные разборы одной темы «до дна», часть статей обновлена в 2025. Catlike Coding — https://catlikecoding.com/unity/tutorials/ , автор прямо предупреждает, что большинство серий сделаны до Unity 2019 LTS.

**Сообщества.** Unity Discussions — https://discussions.unity.com/ ; технические статьи — https://discussions.unity.com/c/technical-articles/23 (там выходит серия Unity + Sunny Valley Studio про чистый код и SOLID-контроллер персонажа). Официальный Discord — https://discord.com/invite/unity . r/Unity3D — https://www.reddit.com/r/Unity3D/ . Стримы Unity — https://www.youtube.com/@unity/streams

**Книги.** «Level up your code with design patterns and SOLID» (Unity, бесплатно, ~150 страниц, 11 паттернов) — единственная «книга», которую стоит прочитать целиком до начала работы над архитектурой. «Game Programming Patterns», Robert Nystrom — бесплатно онлайн https://gameprogrammingpatterns.com/ ; не про Unity, но именно на ней построен официальный e-book. «Hands-On Unity Game Development», Nicolas Borromeo — Unity рекомендует автора в своих материалах (он ведёт доклад «Performance tips & tricks from a Unity consultant» на Unite 2024); стабильную ссылку на магазин подтвердить не удалось — **не подтверждено**, искать по названию. В целом печатные книги по Unity устаревают за 1–2 года, а официальные e-book обновляются под каждую LTS — приоритет у e-book.

---

## Антипаттерны

**Учиться по туториалу, не проверив год выпуска.** Unity 6 переименовал и удалил часть API. Стоп-слова: `rigidbody.velocity` (теперь `Rigidbody.linearVelocity`), `FindObjectOfType<T>()` (теперь `FindFirstObjectByType<T>()` / `FindAnyObjectByType<T>()`), `Input.GetAxis` (legacy Input Manager), `CinemachineVirtualCamera` (в Cinemachine 3 — `CinemachineCamera`), запекание навмеша через окно Navigation вместо компонента `NavMeshSurface` из пакета `com.unity.ai.navigation` 2.x.

**Брать туториалы Brackeys как инструкцию.** Канал не выпускал Unity-контент с 2020 года, а вернувшись в 2024 — переключился на Godot. Педагогика отличная, код мёртвый.

**Копировать `WheelCollider`-туториалы «потому что это реалистично».** `WheelCollider` капризен в настройке и легко даёт нестабильную машину. Для демо-проекта raycast-подвеска предсказуемее — это и есть решение из CLAUDE.md. Смотреть `WheelCollider`-материалы полезно, но как альтернативу, а не как основу.

**Смотреть «10 Unity optimization tips» вместо профилирования.** Советы вида «кэшируйте `GetComponent`» верны, но бесполезны без замера. Правильный порядок: плейлист Profiler → e-book по профилированию → доклады Unite.

**Начинать UI с uGUI, потому что туториалов больше.** В Unity 6.3 UI Toolkit получил World Space UI, кастомные шейдеры и SVG; официальный контент теперь делается под него. uGUI не удалён, но развития не получает.

**Использовать HDRP-материалы для настройки света в URP.** E-book по HDRP отличный, но APV, тени и объёмы там настраиваются иначе. Наш источник — URP-редакция.

**Считать Unity Learn исчерпывающим.** Unity сама пишет в итогах 2025, что продвинутого контента на Learn не хватает. Learn — только для старта.

**Тянуть DOTS/ECS в проект «на будущее».** Доклады про ECS-трафик 2018–2019 годов впечатляют, но это отдельная модель программирования. Доклад Unite 2025 про Survival Kids показывает разумный путь: обычные GameObject, DOTS точечно там, где упёрлись в производительность.

**Копировать `.cs` из видео, не сверяя с документацией 6000.3.** Даже свежие видео могут быть сняты на 6.0/6.1. Сигнатуры проверять на `https://docs.unity3d.com/6000.3/Documentation/ScriptReference/`.

---

## Чек-лист

Перед использованием найденного материала:

- [ ] Посмотрел дату публикации. Старше 2024 — только как источник идей.
- [ ] Проверил, под какую версию Unity сделан материал; в идеале 6.x.
- [ ] Проверил версию пакета: Cinemachine 3.1.x, Input System 1.20.x, AI Navigation 2.0.x — это то, что стоит в нашем 6.3.
- [ ] Пробежал код глазами на стоп-слова (`velocity`, `FindObjectOfType`, `Input.GetAxis`, `CinemachineVirtualCamera`).
- [ ] Если тема архитектурная — сверил с официальным e-book, а не только с видео.
- [ ] Если тема про производительность — сначала профилировал, потом читал.
- [ ] Каждую сигнатуру, которую собираюсь писать в проект, проверил в `docs.unity3d.com/6000.3/`.

Порядок изучения для нашего проекта:

- [ ] 1. Unity Essentials Pathway — базовая навигация в редакторе (владелец проекта).
- [ ] 2. E-book C# Code Style Guide — договориться о стиле до первого коммита.
- [ ] 3. E-book Design patterns and SOLID + демо-репозиторий с 11 паттернами.
- [ ] 4. Официальная серия Input System (7 роликов).
- [ ] 5. Toyful Games про физику машины + e-book ScriptableObjects.
- [ ] 6. Официальная серия Cinemachine 3.1.
- [ ] 7. E-book URP for advanced creators + «New lighting features in Unity 6».
- [ ] 8. Серия AI Navigation 2.0 + git-amend «Boids» — для трафика и окружения.
- [ ] 9. Плейлист Profiler + e-book по профилированию — когда появятся тормоза.
- [ ] 10. UI Toolkit — когда дойдём до интерфейса.

---

## Видео и доклады

Формат: «Название — канал/автор — URL — чем полезно (дата публикации)».

**Физика машины**

- Making Custom Car Physics in Unity (for Very Very Valet) — Toyful Games — https://www.youtube.com/watch?v=CdPYlj5uZeI — эталонный разбор raycast-подвески и сил на колесо (07.2022)
- Making A Physics Based Character Controller In Unity — Toyful Games — https://www.youtube.com/watch?v=qdskE8PJy6Q — тот же подход через силы, для персонажа (05.2021)
- Raycast Car Physics in Unity (Tutorial) — LUIGI GAME DEV — https://www.youtube.com/watch?v=zcKdQ_UUyGE — самый свежий пошаговый raycast-контроллер (01.2026)
- Arcade Car Controller Part 1 | Suspension tutorial — Ash Dev — https://www.youtube.com/watch?v=sWshRRDxdSU — подвеска на четырёх лучах с кодом (03.2024)
- Create a Realistic Car Controller in Unity (Unity 6 Tutorial 2026) — SpeedTutor — https://www.youtube.com/watch?v=DDVxEJ3D8kk — альтернатива на WheelCollider для сравнения (04.2026)
- Unity Car Physics — Lesson 1 — Suspension Physics — BlinkAChu — https://www.youtube.com/watch?v=x0LUiE0dxP0 — старая, но ясная математика подвески (04.2019)

**Ввод и камера**

- Unity Input System in Unity 6 (1/7): Input Action Editor — Unity — https://www.youtube.com/watch?v=TiTKAseu17A — начало официальной серии
- Unity Input System in Unity 6 (2/7): Input System Scripting — Unity — https://www.youtube.com/watch?v=Cd2Erk_bsRY — чтение ввода из кода
- Unity Input System in Unity 6 (6/7): Player Input Component — Unity — https://www.youtube.com/watch?v=beDfIBLfx4c — связка ввода с игроком без ручной подписки
- Press vs Hold Interactions in Unity (With Conditions) — git-amend — https://www.youtube.com/watch?v=x8hI2GjmIAE — тонкости interactions в Input System (08.2026)
- Types of Cinemachine Cameras — Unity — https://www.youtube.com/watch?v=XTVzs4B1d7I — типы камер в Cinemachine 3.1
- Cinemachine Player Controller Cameras — Unity — https://www.youtube.com/watch?v=u0a1F6BlczE — камера, следующая за управляемым объектом (10.2025)
- Essential Elements of Cinemachine 3.1 — git-amend — https://www.youtube.com/watch?v=4xd37R1spKw — сжатый обзор новой архитектуры Cinemachine (09.2024)
- Lerp smoothing is broken — Freya Holmér — https://www.youtube.com/watch?v=LSNQuFEDOyQ — почему наивное сглаживание зависит от FPS (05.2024)

**URP, свет, VFX**

- New lighting features and workflows in Unity 6 — Unity — https://www.youtube.com/watch?v=IpVuIZYFRg4 — APV, протечки света, Scenario Blending (10.2024)
- Understanding URP settings and essentials — Unity — https://www.youtube.com/watch?v=HCXCmHgV7Sk — что делает каждая галочка в URP-ассете (02.2025)
- How to: Excellently Light a Scene with Unity's URP — Unity — https://www.youtube.com/watch?v=mjputyz5Mok — практическая постановка света (07.2025)
- Introduction to the Render Graph in Unity 6 — Unity — https://www.youtube.com/watch?v=U8PygjYAF7A — свой проход рендера в URP Unity 6 (11.2024)
- Transitioning from the Built-in Render Pipeline to URP and HDRP — Unity, Unite 2024 — https://www.youtube.com/watch?v=S1xG9Byf0Go — почему Built-in больше не выбор
- How to write Lit URP shaders in Unity 6 — Light and Shadows — Digvijaysinh Gohil — https://www.youtube.com/watch?v=B9fNaAAADnE — освещённые шейдеры кодом, не через Shader Graph (01.2025)
- URP Cookbook: Compute shaders — Part 1: Particle fun — Unity — https://www.youtube.com/watch?v=omZap7XHxKc — вычислительные шейдеры для частиц (04.2025)
- The Complete Beginner's Guide to VFX Graph in Unity 6 — Cam Ayres — https://www.youtube.com/watch?v=TJGYXUoTmSU — старт по VFX Graph (01.2025)
- Making it shine: Advanced visual techniques in Unity — Unity, Unite 2025 — https://www.youtube.com/watch?v=-M7owmmA4iI — продвинутый визуал (12.2025)

**Производительность**

- Graphics rendering: Getting the best performance with Unity 6 — Unity, Unite 2024 — https://www.youtube.com/watch?v=Oc6T4hh5gaI — снижение CPU/GPU-нагрузки рендера (10.2024)
- Advanced performance tips and tricks from a Unity consultant — Unity, Unite 2025 — https://www.youtube.com/watch?v=wCPDgyR0nSc — типовые ошибки и как их чинить (12.2025)
- Optimizing smarter, not harder with Unity's performance tools — Unity, Unite 2025 — https://www.youtube.com/watch?v=S4xF-eE1pBg — методика, а не список советов (12.2025)
- Understanding Unity memory — Unity, Unite 2025 — https://www.youtube.com/watch?v=0y3erF2tzbI — откуда берутся мусор и фризы GC (12.2025)
- How the heck do you profile a live game? — Unity, Unite 2025 — https://www.youtube.com/watch?v=cMUGMxWZm6U — профилирование в реальных условиях (12.2025)
- Boosting your game performance with Unity 6 Profiling tools — Unity, Unite 2024 — https://www.youtube.com/watch?v=_cV1B2hqXGI — обзор профайлеров Unity 6
- Glow up your graphics with Unity 6.3LTS and beyond — Unity, Unite 2025 — https://www.youtube.com/watch?v=K3-wPnhmDi4 — что нового в графике именно нашей версии (12.2025)
- Thousands of Objects? Do THIS to Fix Unity Physics Performance — Game Dev Guide — https://www.youtube.com/watch?v=2wl2t-p35kA — физика при массе движущихся объектов (06.2026)
- How To Render 2 Million Objects At 120 FPS — Tarodev — https://www.youtube.com/watch?v=6mNj3M1il_c — инстансинг и батчинг наглядно (03.2023)
- The Ultimate Clean, GENERIC Object Pool System — Sasquatch B Studios — https://www.youtube.com/watch?v=Ah3epb2HGCw — обобщённый пул под спавн трафика (05.2025)
- Level up your code with game programming design patterns: Object pool — Unity — https://www.youtube.com/watch?v=U08ScgT3RVM — пул объектов от Unity (01.2024)

**Архитектура кода**

- Improve your Game Architecture with Scriptable Objects — git-amend — https://www.youtube.com/watch?v=bO8WOHCxPq8 — SO как конфиги и как каналы событий (02.2025)
- Unity Assembly Definitions Explained (Architecture, Not Just Compile Times) — git-amend — https://www.youtube.com/watch?v=OqKEaiQrDHY — `.asmdef` как граница зависимостей модулей (02.2026)
- Learn to Build an Advanced Event Bus | Unity Architecture — git-amend — https://www.youtube.com/watch?v=4_DTAnigmaQ — связь систем без прямых ссылок (10.2023)
- Event Bus & Scriptable Object Event Channels — LlamAcademy — https://www.youtube.com/watch?v=95eFgUENnTc — SO-каналы событий на практике
- Level up your code with game programming patterns: Command pattern — Unity — https://www.youtube.com/watch?v=attURV3JWKQ — Command для ввода и отката действий
- Level up your code with game programming patterns: Factory pattern — Unity — https://www.youtube.com/watch?v=lJMY0YdaY9c — фабрика для спавна трафика

**Трафик, ИИ, окружение**

- Boids in Unity: Flocking, Swarming, and Crowds — git-amend — https://www.youtube.com/watch?v=JfBZ7qUc954 — дешёвое групповое поведение (08.2026)
- The Continuity of Splines — Freya Holmér — https://www.youtube.com/watch?v=jvPPXbo87ds — сплайны для дорог и маршрутов трафика (12.2022)
- ECS Track: Spline Based AI Agents — Unite LA — Unity — https://www.youtube.com/watch?v=uK87jZmeT7Y — устройство трафика в Megacity: слияния, съезды, спавн (11.2018)
- Unity ECS for mobile: Metropolis Traffic Simulation — Unity, Unite Copenhagen — https://www.youtube.com/watch?v=iCnYm7kRC1g — масштабная симуляция трафика (10.2019)
- Using DOTS to optimize GameObject gameplay: Survival Kids — Unity, Unite 2025 — https://www.youtube.com/watch?v=ZkvK0mX-id4 — как внедрять DOTS точечно (12.2025)

**UI и общий старт**

- Getting the best performance with UI Toolkit — Unity, Unite 2024 — https://www.youtube.com/watch?v=bECmaYIvZJg — производительность нового UI
- Every UI Toolkit Scale Mode Explained — LlamAcademy — https://www.youtube.com/watch?v=BBORpmOtT8g — масштабирование UI под разрешения (08.2026)
- UI Toolkit Primer — Build UIs like a Programmer — Tarodev — https://www.youtube.com/watch?v=I1JcytXwXM4 — UI Toolkit глазами программиста (08.2023)
- Learn Unity Beginner/Intermediate (FREE COMPLETE Course) — Code Monkey — https://www.youtube.com/watch?v=AmGSEH7QcDg — «Kitchen Chaos», 10+ часов с нуля (01.2023)
- Make a Complete Unity Game – 27 Hour Beginner Course — Jason Weimann — https://www.youtube.com/watch?v=mvdhOEsCkAg — самый свежий длинный курс с нуля (12.2025)
- Learn Procedural Animation in Unity (Free complete course) — iHeartGameDev — https://www.youtube.com/watch?v=cg_vXWPaG8A — процедурная анимация без клипов (12.2024)
- Unity 6.5: What's New (and What Might Break Your Project) — Sunny Valley Studio — https://www.youtube.com/watch?v=xMvGlac2t78 — что ломается при апгрейде с 6.3 (06.2026)
- The Unity Engine roadmap | Unite 2025 — Unity — https://www.youtube.com/watch?v=rEKmARCIkSI — куда движется движок после 6.3 (11.2025)

---

## Источники

Все ссылки открывались 2026-08-19; HTTP-статус и заголовки страниц проверены.

- Unity Blog: https://unity.com/blog/2025-technical-content-round-up , https://unity.com/blog/2024-technical-content-roundup , https://unity.com/blog/unity-6-graphics-learning-resources , https://unity.com/blog/unity-6-game-optimization-guides , https://unity.com/blog/biggest-edition-urp-ebook-unity-6 , https://unity.com/blog/unity-6-vfx-graph-ebook , https://unity.com/blog/engine-platform/see-whats-new-with-cinemachine-3
- E-book Unity: https://unity.com/resources/c-sharp-style-guide-unity-6 , https://unity.com/resources/design-patterns-solid-ebook , https://unity.com/resources/create-modular-game-architecture-scriptableobjects-unity-6 , https://unity.com/resources/introduction-to-urp-advanced-creators-unity-6 , https://unity.com/resources/create-shaders-visual-effects-urp-unity-6 , https://unity.com/resources/creating-advanced-vfx-unity6 , https://unity.com/resources/ultimate-guide-to-profiling-unity-games-unity-6 , https://unity.com/resources/console-pc-game-performance-optimization-unity-6 , https://unity.com/resources/mobile-xr-web-game-performance-optimization-unity-6 , https://unity.com/resources/scalable-performant-ui-uitoolkit-unity-6 , https://unity.com/resources/tips-improve-productivity-workflow-unity-6 , https://unity.com/resources/unity-game-dev-field-guide , https://unity.com/resources/best-practices-version-control-unity-6 , https://unity.com/resources/dots-concepts-features-samples-resources-unity-6 , https://unity.com/resources/lighting-environments-hdrp-unity-6 , https://unity.com/resources/input-system-video-tutorial-series
- Документация 6000.3: https://docs.unity3d.com/6000.3/Documentation/Manual/best-practice-guides.html , https://docs.unity3d.com/6000.3/Documentation/Manual/best-practice-guides/ui-toolkit-for-advanced-unity-developers/bpg-uiad-index.html , https://docs.unity3d.com/6000.3/Documentation/Manual/analysis.html , https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-introduction.html , https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.cinemachine.html , https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.inputsystem.html , https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.ai.navigation.html , https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rigidbody-linearVelocity.html , https://docs.unity3d.com/6000.3/Documentation/Manual/WhatsNewUnity6.html , https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineUpgradeFrom2.html
- Unity Learn: https://learn.unity.com/ , https://learn.unity.com/pathways , https://learn.unity.com/pathway/unity-essentials , https://learn.unity.com/pathway/junior-programmer , https://learn.unity.com/course/create-with-code
- GitHub и Asset Store: https://github.com/Unity-Technologies/game-programming-patterns-demo , https://github.com/Unity-Technologies/BagelGame , https://github.com/Unity-Technologies/EntityComponentSystemSamples , https://github.com/Unity-Technologies/Megacity-Metro , https://github.com/Unity-Technologies/UnityCsReference , https://github.com/SergeyMakeev/ArcadeCarPhysics , https://github.com/hayden-donnelly/vehicle-physics , https://assetstore.unity.com/packages/essentials/tutorial-projects/quizu-a-ui-toolkit-sample-268492 , https://assetstore.unity.com/packages/essentials/tutorial-projects/dragon-crashers-ui-toolkit-sample-project-231178 , https://assetstore.unity.com/packages/templates/tutorials/scriptableobjects-paddle-ball-project-325743
- Сторонние блоги: https://catlikecoding.com/unity/tutorials/ , https://catlikecoding.com/unity/tutorials/custom-srp/ , https://gamedevbeginner.com/input-in-unity-made-easy-complete-guide-to-the-new-system/ , https://www.toyfulgames.com/blog/deep-dive-physics , https://gameprogrammingpatterns.com/
- Сообщества: https://discussions.unity.com/ , https://discussions.unity.com/c/technical-articles/23 , https://discussions.unity.com/t/new-5-part-cinemachine-3-1-youtube-tutorial-series-available/1685256 , https://discussions.unity.com/t/new-ai-navigation-2-0-video-tutorials-series/1565073 , https://discussions.unity.com/t/new-video-tutorial-series-on-ui-toolkit-in-unity-6-4/1708498 , https://discord.com/invite/unity , https://www.reddit.com/r/Unity3D/
- Про уход Brackeys в Godot: https://80.lv/articles/unity-creator-brackeys-is-back-with-godot-tutorials , https://gamefromscratch.com/brackeys-returns-making-godot-tutorials/
- Каналы YouTube: https://www.youtube.com/@unity , https://www.youtube.com/@CodeMonkeyUnity , https://www.youtube.com/c/GameDevGuide , https://www.youtube.com/@Tarodev , https://www.youtube.com/@git-amend , https://www.youtube.com/@Unity3dCollege , https://www.youtube.com/@iHeartGameDev , https://www.youtube.com/@sasquatchbgames , https://www.youtube.com/@acegikmo , https://www.youtube.com/@SebastianLague , https://www.youtube.com/@InfallibleCode , https://www.youtube.com/@davegamedevelopment , https://www.youtube.com/@LlamAcademy , https://www.youtube.com/@SunnyValleyStudio , https://www.youtube.com/@ChristinaCreatesGames , https://www.youtube.com/@sakurarabbit6708 , https://www.youtube.com/channel/UCwYuQIa9lgjvDiZryUVtFGw , https://www.youtube.com/channel/UCYbK_tjZ2OrIZFBvU6CCMiA
- Плейлисты Unity: Input System https://www.youtube.com/playlist?list=PLX2vGYjWbI0RpLvO3B7aH-ObfcOifMD20 , Cinemachine 3.1 https://www.youtube.com/playlist?list=PLX2vGYjWbI0QiMBrmyzbxZeHepAbhVOJa , AI Navigation 2.0 https://www.youtube.com/playlist?list=PLX2vGYjWbI0SsXFD1Gjo-8kFEpzk5k4Kh , UI Toolkit https://www.youtube.com/playlist?list=PLX2vGYjWbI0S3Zx5Dv7htBcWLBfKWf2qI , Render Graph https://www.youtube.com/playlist?list=PLX2vGYjWbI0RwCoL6a96ltbG9JCSE6j1m , Profiler https://www.youtube.com/playlist?list=PLX2vGYjWbI0QNVu6deCYvWGX7Gj0GmdmE , Tip Tuesdays https://www.youtube.com/playlist?list=PLX2vGYjWbI0SUyNX-WuICwLqNQX2Fb4o8 , Unite 2025 https://www.youtube.com/playlist?list=PLX2vGYjWbI0TBAgte6nJfL9expGYuPqUO , Unite 2024 https://www.youtube.com/playlist?list=PLX2vGYjWbI0TV4Di9Uhg22OkOH41VcVez

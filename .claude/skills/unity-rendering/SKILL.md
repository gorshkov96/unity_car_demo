---
name: unity-rendering
description: Картинка — настройки URP и Render Graph, свет и тени, Adaptive Probe Volumes, партиклы и VFX Graph, Shader Graph, постобработка через Volume, UI Toolkit и HUD. Использовать при настройке рендера, освещения, запекания, материалов, эффектов, следов шин или интерфейса.
---

# Рендер, свет, эффекты, UI

Детали — [URP](../../../docs/unity/01-render-urp.md),
[свет](../../../docs/unity/02-lighting-shadows.md),
[эффекты](../../../docs/unity/11-vfx.md),
[UI](../../../docs/unity/12-ui.md).

## URP

- **Render Graph обязателен.** Compatibility Mode удалён в Unity 6.3: любой туториал,
  где `ScriptableRenderPass` переопределяет `Execute()`, для нас мусор.
- **Rendering Path = Forward+.** Условие для GPU Resident Drawer, снимает лимит на
  количество источников света на объект. Deferred не берём — ломает MSAA.
- **GPU Resident Drawer + GPU Occlusion Culling включены.** Цена: `MaterialPropertyBlock`
  под запретом, Static Batching выключаем.
- Три URP-ассета (Low/Medium/High) с **одинаковым** Rendering Path — разные пути в одном
  билде удваивают шейдерные варианты.
- Постобработка — только через **Volume**. Post Processing Stack v2 — legacy, не ставим.
- Depth Texture — вкл (нужен SSAO, декалям, софт-партиклам). Opaque Texture — выкл,
  включать точечно под стекло и воду.
- Camera Stacking не используем: несовместим с TAA.

## Свет

- GI — на **Adaptive Probe Volumes**, не Light Probe Groups (legacy). Едущая машина
  корректно освещается запечённой картой без ручной расстановки пробников.
- Карта статична → лайтмапы для карты + APV для всего движущегося, `Lighting Mode = Shadowmask`.
- Лайтмаппер — **Progressive GPU**: мы на Apple Silicon.
- Солнце — `Mode = Mixed`, иначе его непрямой свет не попадёт в APV и лайтмапы.

## Эффекты

- База — встроенный `ParticleSystem`. VFX Graph подключаем точечно и позже, где нужны
  десятки тысяч частиц.
- Следы шин — **процедурный меш-стрип**, а не `TrailRenderer` и не декали: один меш, один draw call.
- Кузов — URP Lit Shader Graph с включённым **Clear Coat** (лак поверх краски).

## UI

- Весь UI — на **UI Toolkit** (UXML и USS текстовые, значит нормально живут в git).
  Знать контраргумент: документация Unity 6.3 для рантайма всё ещё рекомендует uGUI.
- Спидометр и тахометр — кастомный `VisualElement` с **Painter2D** (рисование дуг кодом).
- Стрелка — `style.rotate` + `transform-origin`, элементу выставить
  `usageHints = UsageHints.DynamicTransform`. Никогда не анимировать `width`/`height`.

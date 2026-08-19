---
name: unity-architecture
description: Как раскладывать код Unity-проекта — композиция компонентов, ScriptableObject-конфиги и event channels, Assembly Definition, границы модулей, стиль C#. Использовать перед созданием любого нового скрипта, MonoBehaviour, ScriptableObject или .asmdef, а также при рефакторинге связей между системами.
---

# Архитектура и стиль кода

Детали — [docs/unity/05-architecture.md](../../../docs/unity/05-architecture.md)
и [docs/unity/15-code-style.md](../../../docs/unity/15-code-style.md).

## Правила, от которых не отступаем без причины

1. **Композиция, не наследование.** Машина — префаб с независимыми компонентами
   (`CarInput`, `CarMotor`, `CarAudio`, `CarVfx`), а не `Car : Vehicle : Entity`.
2. **Три слоя: ввод → логика → визуал.** Компонент, читающий геймпад, не двигает колёса.
   Компонент, считающий физику, не крутит меши. Между слоями — данные и события.
3. **Данные — в ScriptableObject** (`CarSpecSO`, `TrafficSettingsSO`), не числа в коде
   и не ручная расстановка в инспекторе.
4. **Внутри модуля — прямые ссылки через `[SerializeField]`. Между модулями — события.**
   Связь `Vehicle → UI` запрещена физически через `.asmdef`, а не по договорённости.
5. **`.asmdef` с первого коммита кода.** `Game.Core` без зависимостей → `Game.Vehicle`,
   `Game.World`, `Game.Gameplay` → `Game.UI`. Стрелки только вниз.
6. **Чистая логика — в обычных C#-классах** без `MonoBehaviour`. Их покрывают
   EditMode-тестами за секунды, `MonoBehaviour` остаётся тонкой оболочкой.
7. **Физика — только в `FixedUpdate`.** Ввод читается в `Update`, буферизуется, применяется
   в `FixedUpdate`. Визуальное сглаживание — интерполяцией Rigidbody, а не движением физики в `Update`.
8. **Никаких поисков в горячем пути.** `GetComponent`, `FindFirstObjectByType`, `Camera.main`,
   LINQ, `new`, конкатенация строк — не в `Update` и не в `FixedUpdate`. Кешировать в `Awake`.
9. **Синглтон допустим один** — бутстрап сцены. Не `AudioManager.Instance` по всему коду.
10. **DI-контейнер (VContainer, Zenject) на старте не подключаем.** Вернуться к вопросу,
    когда появятся 3+ сцены и нужда подменять реализации в тестах.

## Стиль

- Скобки Allman, отступ 4 пробела, `.editorconfig` в корне — единственный источник правды.
- Приватные поля `_camelCase`, публичные члены и типы `PascalCase`.
- **Никаких публичных полей.** В инспектор — `[SerializeField] private float _maxSpeed;`.
  Наружу — свойство.
- Имя файла совпадает с именем класса — Unity этого требует для `MonoBehaviour`.
- Комментарии и документация — по-русски в `docs/`, XML-doc к публичному API — по-английски.

## Unity 6 переименовал API

`Rigidbody.velocity` → **`linearVelocity`**. `FindObjectOfType<T>()` →
**`FindFirstObjectByType<T>()`**. Cinemachine `CinemachineVirtualCamera` →
**`CinemachineCamera`**. Сомневаешься в сигнатуре — сверься с документацией, не пиши по памяти.

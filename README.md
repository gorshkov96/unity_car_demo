# Unity Car Demo

3D-игра на Unity: управляемый автомобиль, статичная карта, движущиеся объекты
вокруг. Проект — витрина возможностей Unity: физика, рендеринг, анимация,
звук, UI, эффекты.

## Статус

Репозиторий инициализирован. Unity-проект ещё не создан.

## Требования

- Unity **6000.3.21f1** (Unity 6.3), устанавливается через Unity Hub
- macOS / Windows

## Разработка

Правила работы над проектом, архитектурные принципы и соглашения —
в [CLAUDE.md](CLAUDE.md).

### Настройка слияния Unity-сцен (один раз после клона)

```bash
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver '"/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/Tools/UnityYAMLMerge" merge -p "$BASE" "$REMOTE" "$LOCAL" "$MERGED"'
git config merge.unityyamlmerge.recursive binary
```

Без этого конфликты в `.unity` и `.prefab` придётся разрешать вручную в YAML —
что практически невозможно.

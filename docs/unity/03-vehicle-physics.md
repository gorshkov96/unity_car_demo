# Физика автомобиля в Unity 6

Как выбрать модель управления машиной и как её настроить, чтобы она не переворачивалась, не дрожала и красиво выглядела. Читать перед тем, как писать первую строчку контроллера машины, и возвращаться при каждой правке настроек физики.

---

## TL;DR — решения для нашего проекта

1. **Берём аркадную raycast-подвеску на одном `Rigidbody`, а не `WheelCollider`.** Причина не в «реалистичности», а в том, что вся конфигурация живёт в коде и `ScriptableObject`, а не в инспекторе четырёх невидимых компонентов, которые нельзя надёжно править из скрипта. Для витрины технологий это ещё и нагляднее: можно рисовать гизмо силы на каждом колесе.
2. **`WheelCollider` не выбрасываем совсем** — делаем его вторым, альтернативным «драйвером» за общим интерфейсом. Демонстрация «вот аркадная модель, вот симуляторная, переключаем на лету» — сама по себе хороший экспонат.
3. **Ground-геометрия карты должна быть гладкой.** Это жёсткое требование `WheelCollider` (он не катится, а лучом «перепрыгивает» ступеньки), и просто хорошая идея для raycast-подвески. Для процедурной карты — никаких стыков плоскостей с зазорами и вертикальных бортиков высотой в пару сантиметров.
4. **`Fixed Timestep` оставляем 0.02 (50 Гц).** Стабильность колёс добираем не частотой всей физики, а `WheelCollider.ConfigureVehicleSubsteps` (для WC) или собственным подшагом в своём коде (для raycast). Это дешевле: пересчитываются только силы колёс, а не вся сцена.
5. **`Rigidbody` машины: `interpolation = Interpolate`, `collisionDetectionMode = ContinuousDynamic`, масса 1200–1500 кг.** Интерполяция обязательна, иначе при 50 Гц физики и 120 Гц рендера машина будет дрожать.
6. **Центр масс задаём осознанно, а не «опускаем побольше, чтоб не кувыркалось».** Правильный порядок: реальный центр масс → стабилизаторы (anti-roll) → и только потом, если надо, лёгкое смещение вниз. Искусственно заниженный центр масс ломает прыжки и столкновения.
7. **Anti-roll bar пишем сразу, в обоих подходах.** Это ~20 строк, и именно они, а не «магические» настройки трения, убирают перевороты в поворотах.
8. **Никаких `Rigidbody.velocity` и `.drag`** — в Unity 6 это `linearVelocity`, `linearDamping`, `angularDamping`. Большинство готовых репозиториев по теме написаны до Unity 6 и не скомпилируются.
9. **Пакет `com.unity.vehicles` (Unity Vehicles) нам не подходит** — он только для ECS/DOTS и в экспериментальном статусе. Знать о нём стоит, тащить в проект — нет.
10. **Визуал (наклон кузова, вращение колёс, следы шин) делаем отдельными компонентами**, читающими состояние физики, и никогда — наоборот. Это позволит потом подменить водителя на ИИ, ничего не переписывая.

---

## 1. Главный выбор: `WheelCollider` против raycast-подвески

### Что такое `WheelCollider` на самом деле

Это **не круглый коллайдер**. Вопреки гизмо в виде окружности, `WheelCollider` — это один-единственный луч (raycast), пущенный вниз по локальной оси Y через центр колеса. Внутри — модель пружины с демпфером и **slip-based модель трения шины** (сила зависит от проскальзывания резины относительно дороги).

Три следствия, которые определяют всё остальное:

- **Колесо не катится по поверхности.** В Play Mode вращение самого `WheelCollider` не меняется. Крутить нужно отдельный меш колеса, из скрипта. Отсюда обязательное правило: коллайдер и визуальная модель колеса — **разные GameObject**, коллайдер жёстко закреплён относительно кузова.
- **`WheelCollider` игнорирует `PhysicsMaterial`.** Трение считается своей моделью, отдельно от остального движка. Разные покрытия (асфальт/трава/лёд) делаются подменой `forwardFriction`/`sidewaysFriction` из скрипта на основании того, во что попал луч.
- **Луч не умеет заезжать на ступеньку.** Пока центр колеса не пересёк край бордюра, колесо визуально проваливается в него, а потом резко «выпрыгивает» наверх. Unity прямо пишет в мануале: геометрия земли должна быть максимально гладкой.

### Честное сравнение

| Критерий | `WheelCollider` (PhysX Vehicle) | Raycast-подвеска на `Rigidbody` |
|---|---|---|
| Сложность старта | Низкая: накинул 4 компонента, поехало | Средняя: надо написать 150–250 строк |
| Сложность доводки | **Высокая.** Кривые трения, substeps, `forceAppPointDistance`, `sprungMass` взаимодействуют неочевидно | **Низкая.** Каждый параметр — своя строчка кода, видно, что на что влияет |
| Предсказуемость | Средняя: часть модели — чёрный ящик внутри PhysX | **Высокая:** нет чёрного ящика |
| Стабильность на малой / большой скорости | Проблемные зоны: дрожание и «уползание» стоя, подскоки и перевороты на скорости | Хорошая: сам решаешь, что делать при |v| ≈ 0; на скорости лечится downforce |
| Езда по неровностям и бордюрам | Плохо (см. выше про луч) | Так же плохо, если один луч; лечится spherecast |
| Реализм «из коробки» | Выше | Ниже — но для демо это не то же, что «хуже выглядит» |
| Конфигурация как код | Плохо: значения живут в инспекторе, `.prefab` — это YAML | **Отлично:** `ScriptableObject` + код |
| Данные для эффектов | `WheelHit`: `forwardSlip`, `sidewaysSlip`, `force`, `normal`, `collider` — всё готово | Считаешь сам (это же и плюс: считаешь ровно то, что нужно) |

**Рекомендация для нашего проекта — raycast.** Три причины, по убыванию веса:

1. **Наглядность.** Аркадная модель позволяет нарисовать в Scene View каждую силу (пружина, тяга, боковое сцепление) отдельным `Debug.DrawRay`. С `WheelCollider` показывать нечего — силы считаются внутри PhysX.
2. **Сцена как код.** У нас прямое требование: настройка сцены из скриптов, потому что `.prefab`/`.unity` — это YAML с GUID. `WheelCollider` — это ровно тот компонент, который надо расставлять руками в инспекторе и потом руками же подгонять по гизмо.
3. **Предсказуемость.** Демо, которое иногда неожиданно переворачивает машину — плохое демо.

Контраргумент, который надо честно проговорить: если когда-нибудь появится требование «машина должна вести себя как настоящая» (реакция на распределение веса, торможение двигателем, недостаточная/избыточная поворачиваемость из-за геометрии), `WheelCollider` даст это дешевле, чем самописная модель. Поэтому — интерфейс, а не жёсткая привязка.

---

## 2. `WheelCollider`: параметры и болячки

Всё ниже — по документации Unity 6.3.

### Подвеска

| Параметр | Значение по умолчанию | Смысл |
|---|---|---|
| `suspensionSpring.spring` | 35000 Н/м | Жёсткость пружины |
| `suspensionSpring.damper` | 4500 Н·с/м | Демпфер (гаситель колебаний) |
| `suspensionSpring.targetPosition` | 0.5 | Точка покоя на отрезке хода подвески: 0 — полностью разжата, 1 — полностью сжата. Типично 0.3–0.7 |
| `suspensionDistance` | 0.3 м | Полный ход подвески |
| `forceAppPointDistance` | 0 | Точка приложения сил, в метрах вверх от основания колеса. Ноль — у земли; для машины идеал — чуть **ниже** центра масс |
| `mass` / `radius` / `wheelDampingRate` | 20 кг / 0.5 м / 0.25 | Масса колеса (типично 20–80), радиус (обязан совпадать с мешем), замедление вращения без сил |

**Ключевое правило масштаба:** дефолты рассчитаны на машину массой **1500 кг**. PhysX считает силы пропорционально, поэтому если ставишь `Rigidbody.mass = 15`, надо и `spring`/`damper` уменьшить во столько же раз (350 и 45). Не сделаешь — машина будет либо лежать на брюхе, либо стоять на «бетонной» подвеске.

Проверочная формула жёсткости (из Vehicle Physics Pro, работает и для raycast):

```
spring ≈ (mass / wheelCount) * 2 * 9.81 / suspensionDistance
```

Для 1500 кг, 4 колёс и хода 0.3 м даёт ≈ 24 500 Н/м — тот же порядок, что дефолтные 35 000. Логика формулы: каждая стойка должна выдержать двойной вес приходящейся на неё части машины, не упершись в ограничитель хода.

### Трение

Кривая трения (`WheelFrictionCurve`) — это сплайн из двух кусков: (0,0) → (`extremumSlip`, `extremumValue`) → (`asymptoteSlip`, `asymptoteValue`), дальше горизонталь. По оси X — проскальзывание, по оси Y — сила. Физический смысл: резина при малом проскальзывании тянется и держит сильно; когда проскальзывание растёт, шина срывается в скольжение и держит хуже.

Есть две группы: `forwardFriction` (разгон/торможение) и `sidewaysFriction` (удержание машины в повороте). `stiffness` — общий множитель обоих значений силы; `stiffness = 0` полностью отключает трение колеса.

> **Осторожно с числами.** Референс компонента в мануале 6.3 даёт дефолты `extremumSlip = 0.4`, `asymptoteSlip = 0.8`, `asymptoteValue = 0.5`, `extremumValue = 1`, `stiffness = 1`. Scripting API той же версии для `WheelFrictionCurve` пишет пары «0.2f/0.4f» и «0.5f/0.8f» — это, судя по всему, «forward/sideways», и документация тут сама себе не вполне соответствует. Реальные значения смотреть в инспекторе только что созданного компонента, а не по памяти и не по статьям 2015 года.

### Подшаги (substeps)

```csharp
// Вызывать один раз на машину (не на каждое колесо): параметры ставятся на весь vehicle.
// speedThreshold — м/с; ниже порога считаем точнее, потому что именно на малой
// скорости WheelCollider дрожит и «уползает».
_wheels[0].ConfigureVehicleSubsteps(speedThreshold: 5f,
                                    stepsBelowThreshold: 12,
                                    stepsAboveThreshold: 6);
```

Внутри одного `FixedUpdate` симуляция делит шаг на подшаги, считает силы подвески и шин на каждом, суммирует и применяет к телу один раз. Это дешевле, чем глобально уменьшать `Time.fixedDeltaTime`, потому что пересчитывается только машина. Документация значений по умолчанию не называет — считать их надо параметром, который обязательно нужно выставить самому.

### Типичные болячки и что с ними делать

- **Машина переворачивается в повороте на скромной скорости.** Это не баг: реальная машина с такими пропорциями и без стабилизаторов тоже перевернулась бы. Лечение — anti-roll bars (раздел 4), а не «опустить центр масс в пол».
- **Дрожание и вибрация на месте / на малой скорости.** Больше подшагов ниже порога; проверить, что `Rigidbody.mass` соответствует `spring`/`damper`; не ставить `suspensionDistance` слишком маленьким.
- **Кузов «взлетает» над колёсами при входе в Play Mode.** Заведено в issue tracker Unity. Как правило — следствие того, что GameObject колёсных коллайдеров стоят не на уровне центра колеса: по мануалу их надо поднять на половину `suspensionDistance` относительно меша.
- **Подскоки на скорости.** Проверить гладкость коллизионной геометрии дороги, добавить downforce, увеличить `damper`.
- **Колесо «проваливается» в бордюр.** Архитектурное ограничение raycast-модели, не настраивается: либо сглаживать геометрию, либо не использовать `WheelCollider`.
- **Нельзя двигать GameObject колеса в рантайме.** `WheelCollider` кеширует на старте данные, зависящие от расстояния до центра масс. Менять `center` — безопасно, `transform.position` — нет.

### Управление и визуал

```csharp
// FixedUpdate: управление
wheel.motorTorque = throttle * _config.MotorTorque;
wheel.brakeTorque = brake * _config.BrakeTorque;
wheel.steerAngle  = steer * _config.SteeringRange;

// Update (визуал): единственный правильный способ получить позу колеса
wheel.GetWorldPose(out Vector3 pos, out Quaternion rot);
_wheelMesh.SetPositionAndRotation(pos, rot);

// Данные для эффектов. forwardSlip: разгонное проскальзывание отрицательное, тормозное положительное
if (wheel.GetGroundHit(out WheelHit hit))
{
    bool burnout  = hit.forwardSlip < -_config.SlipThreshold;
    bool drifting = Mathf.Abs(hit.sidewaysSlip) > _config.DriftThreshold;
    var  surface  = hit.collider.sharedMaterial; // подмена кривых трения по покрытию
}
```

---

## 3. Аркадная raycast-подвеска: как её писать

Модель: **один `Rigidbody` + один `BoxCollider` (или convex mesh) на кузов + четыре точки-«колеса»**, из каждой вниз пускается луч. Никаких `WheelCollider`, никаких дочерних `Rigidbody`.

### Пружина с демпфером

На каждое колесо в `FixedUpdate`:

```csharp
Transform anchor = _wheelAnchors[i];
float rayLength = _config.SuspensionRestDistance + _config.WheelRadius;

if (!Physics.Raycast(anchor.position, -anchor.up, out RaycastHit hit,
                     rayLength, _config.GroundMask, QueryTriggerInteraction.Ignore))
{
    _grounded[i] = false;
    continue;
}
_grounded[i] = true;

float compression = 1f - (hit.distance / rayLength);   // 1 — сжата полностью, 0 — только коснулась

// Скорость ИМЕННО В ТОЧКЕ КОНТАКТА, а не скорость кузова: машина кренится
// и клюёт носом, у каждого колеса своя вертикальная скорость.
float verticalVel = Vector3.Dot(anchor.up, _body.GetPointVelocity(hit.point));

float force = compression * _config.SpringStrength - verticalVel * _config.SpringDamper;
_body.AddForceAtPosition(anchor.up * force, hit.point);
```

Использование `Rigidbody.GetPointVelocity` вместо `linearVelocity` — самая частая ошибка в самописных подвесках и самый заметный источник «вечно прыгающей» машины.

Демпфер подбирается не наугад, а от критического (при нём подвеска возвращается в покой без колебаний): `damper_critical = 2 * sqrt(springStrength * sprungMass)`, где `sprungMass = Rigidbody.mass / wheelCount`. Для 1500 кг и жёсткости 35 000 это ≈ 7250. Дефолт `WheelCollider` (4500) даёт коэффициент затухания ≈ 0.62 — слегка «пружинящая» подвеска. Для аркады 0.5–0.8 — рабочий диапазон; ближе к 1.0 машина «деревянная», выше 1.0 — вязкая.

### Тяга и торможение

```csharp
float speed     = Vector3.Dot(_body.linearVelocity, transform.forward);
float normSpeed = Mathf.Clamp01(Mathf.Abs(speed) / _config.MaxSpeed);

// Кривая мощности вместо мотора/КПП/сцепления: одна AnimationCurve вместо трёх систем
float available = _config.PowerCurve.Evaluate(normSpeed) * _config.MaxAccelForce;
_body.AddForceAtPosition(anchor.forward * (throttle * available), hit.point);
```

Прикладывать силу **в точке контакта**, а не в центре масс — тогда при резком газе машина сама приседает назад, а при торможении клюёт носом. Бесплатный и очень заметный визуальный эффект.

### Боковое сцепление через изменение скорости

Ключевая идея аркадной модели: не считать физически корректную силу трения, а **напрямую погасить нужную долю боковой скорости колеса за этот шаг**. Так `grip` становится понятной величиной от 0 (лёд) до 1 (рельсы):

```csharp
Vector3 steerDir     = anchor.right;
float   lateralSpeed = Vector3.Dot(steerDir, _body.GetPointVelocity(hit.point));

// grip — доля боковой скорости, которую колесо гасит за шаг. Кривая от нормализованной
// скорости бесплатно даёт «срыв в занос» на высокой скорости.
float grip         = _config.GripCurve.Evaluate(normSpeed) * _gripMultiplier[i];
float desiredAccel = -lateralSpeed * grip / Time.fixedDeltaTime;

// TireMass — «эффективная масса шины», параметр настройки, а не физическая величина
_body.AddForceAtPosition(steerDir * (_config.TireMass * desiredAccel), hit.point);
```

Дрифт в такой модели — это просто временное снижение `_gripMultiplier` на задних колёсах по кнопке ручника. Ручник, «дрифт-режим» и лёд под колёсами становятся одним и тем же параметром.

### Стабилизация и мелочи, без которых не поедет

- **Anti-roll** — то же, что для `WheelCollider` (раздел 4), но проще: сжатия колёс уже посчитаны. **Downforce** — см. раздел 4.
- **В воздухе.** Когда ни одно колесо не заземлено, физика перестаёт что-либо ограничивать, и машина приземляется на крышу. Стандартное аркадное решение: гасить угловую скорость и подруливать тангажом/креном к горизонту через `AddTorque`. Откровенный чит, но он есть во всех аркадных гонках.
- **Сопротивление качению.** Кузов «висит» над землёй и не касается её коллайдером, естественного торможения нет. Добавляем силу, пропорциональную скорости, спроецированной на плоскость дороги, — либо небольшой `linearDamping`.
- **`SphereCast` вместо `Raycast`.** Один луч по центру колеса воспроизводит главный недостаток `WheelCollider` — «проваливание» в стыки. `Physics.SphereCast` радиусом колеса убирает это почти полностью ценой замены одного вызова. Для процедурной карты, где стыки неизбежны, очень выгодный размен.

---

## 4. Общее для обоих подходов

### Центр масс, масса, инерция

- `Rigidbody.automaticCenterOfMass` / `automaticInertiaTensor` — Unity считает и то, и другое по коллайдерам. Как только пишешь в `centerOfMass` или `inertiaTensor`, автоматика для этого свойства отключается; вернуть — `ResetCenterOfMass()` / `ResetInertiaTensor()`.
- Официальный пример Unity опускает центр масс на 1 метр (`centreOfGravityOffset = -1f`) «для стабильности». **Это workaround, а не решение.** Аргумент Эди Мартинеса (автор Vehicle Physics Pro): искусственно заниженный центр масс ломает прыжки, столкновения и любые трюки — машина будет вести себя как гиря. Правильный порядок: реальный центр масс (чуть вниз и в сторону двигателя) → anti-roll bars → и только если всё ещё плохо, лёгкое смещение вниз.
- Масса: 1200–1500 кг для легковой машины. Меньше — только если синхронно масштабируешь жёсткости.
- `inertiaTensor` — насколько тело сопротивляется вращению вокруг каждой оси. Если машина крутится «как шайба», а хочется весомости — увеличить компоненту Y. Трогать осознанно и редко.

### Anti-roll bar (стабилизатор поперечной устойчивости)

Идея: связать колёса одной оси. Когда одно сжимается сильнее другого, часть силы передаётся на противоположное — кузов кренится меньше. Классический скрипт Эди, переписанный под Unity 6 и C#:

```csharp
public sealed class AntiRollBar : MonoBehaviour
{
    [SerializeField] private Rigidbody _body;
    [SerializeField] private WheelCollider _wheelL;
    [SerializeField] private WheelCollider _wheelR;
    // Разумная стартовая величина — примерно равная suspensionSpring.spring, в ньютонах
    [SerializeField] private float _antiRoll = 30000f;

    private void FixedUpdate()
    {
        float travelL = 1f, travelR = 1f;

        bool groundedL = _wheelL.GetGroundHit(out WheelHit hitL);
        if (groundedL)
            travelL = (-_wheelL.transform.InverseTransformPoint(hitL.point).y
                       - _wheelL.radius) / _wheelL.suspensionDistance;

        bool groundedR = _wheelR.GetGroundHit(out WheelHit hitR);
        if (groundedR)
            travelR = (-_wheelR.transform.InverseTransformPoint(hitR.point).y
                       - _wheelR.radius) / _wheelR.suspensionDistance;

        float force = (travelL - travelR) * _antiRoll;

        if (groundedL) _body.AddForceAtPosition(-_wheelL.transform.up * force, _wheelL.transform.position);
        if (groundedR) _body.AddForceAtPosition( _wheelR.transform.up * force, _wheelR.transform.position);
    }
}
```

Один экземпляр на ось. Разница жёсткости переднего и заднего стабилизаторов — самый сильный рычаг влияния на характер поворачиваемости, сильнее, чем правки кривых трения.

Известный побочный эффект: сила прикладывается снаружи колеса, поэтому у нагруженного колеса уменьшается вертикальная нагрузка и, значит, сцепление. Если машина зависла одним колесом на препятствии, стабилизатор будет с полной силой отжимать вверх единственное касающееся земли колесо. Штатный обходной путь — снижать `_antiRoll` на малых скоростях.

### Downforce

```csharp
// В FixedUpdate. Растёт с квадратом скорости — как настоящая аэродинамическая прижимная сила.
float speed = _body.linearVelocity.magnitude;
_body.AddForce(-transform.up * (_config.DownforceCoefficient * speed * speed));
```

Прижимает машину на скорости, убирает подскоки, повышает предельную скорость в повороте. `-transform.up`, а не `Vector3.down`: на крутых виражах и трамплинах прижимать надо к дороге, а не к центру Земли.

### ABS и TCS в упрощённом виде

С `WheelCollider` они пишутся почти в одну строку каждый, потому что `WheelHit.forwardSlip` уже даёт нужную величину (**разгонное проскальзывание отрицательное, тормозное — положительное**):

```csharp
if (wheel.GetGroundHit(out WheelHit hit))
{
    // ABS: колесо заблокировано при торможении — сбросить тормозной момент
    if (hit.forwardSlip > _config.AbsSlipThreshold)
        wheel.brakeTorque *= _config.AbsRelease;   // например 0.5

    // TCS: колесо буксует при разгоне — срезать тягу
    if (hit.forwardSlip < -_config.TcsSlipThreshold)
        wheel.motorTorque *= _config.TcsCut;       // например 0.7
}
```

В raycast-модели то же самое делается через собственное значение проскальзывания. Для демо это ещё и хороший экспонат: тумблер «ABS вкл/выкл» с визуализацией мигания на колёсах.

### FixedUpdate, timestep, интерполяция

- **Вся физика — только в `FixedUpdate`.** Ввод читается в `Update` и буферизуется; в `FixedUpdate` применяется буфер. Одиночные нажатия (ручник, переключение камеры) нельзя терять между физическими шагами — их надо накапливать флагом до ближайшего `FixedUpdate`.
- **`Fixed Timestep` (Edit → Project Settings → Time)** по умолчанию 0.02 с = 50 Гц. Уменьшение до 0.0166 (60 Гц) или 0.01 (100 Гц) даёт более стабильные колёса, но дороже линейно и для всей сцены. Сначала подшаги колёс, потом уже общая частота.
- **`Maximum Allowed Timestep`** ограничивает, сколько физических шагов Unity готова догонять за один кадр. Если его не ограничить, просадка кадра порождает пачку физических шагов, которые делают следующий кадр ещё длиннее — «спираль смерти». Unity рекомендует пробовать значения около 0.1 с; ценой становится замедление физики при тормозах.
- **`Physics.simulationMode`** (`FixedUpdate` по умолчанию / `Update` / `Script`). Для машины оставляем `FixedUpdate`. Режим `Script` (ручной `Physics.Simulate`) — инструмент для детерминированного реплея и сетевого предсказания, а не для «плавности».
- **`Rigidbody.interpolation = Interpolate`** обязательна для машины и для камеры-преследователя. Механика: поза считается между двумя последними физическими шагами, поэтому визуально машина отстаёт ровно на один шаг физики — это нормально и незаметно. `Extrapolate` предсказывает вперёд и умеет визуально «влетать» в стены, потом откатываясь: для машины не годится.
- Важная деталь: когда интерполяция включена, физика владеет трансформом. Любое прямое изменение `transform` (телепорт, респавн) надо сопровождать `Physics.SyncTransforms()`, иначе Unity его проигнорирует.

### Настройки проекта (Edit → Project Settings → Physics)

- **Default Solver Iterations** — по умолчанию 6. Для машины можно не трогать глобально, а поднять точечно: `carRigidbody.solverIterations = 12`. Это и есть рекомендованный Unity подход — низкий глобальный дефолт плюс повышенные значения на конкретных телах.
- **Solver Type**: `Projected Gauss Seidel` (по умолчанию) и `Temporal Gauss Seidel`. TGS лучше сходится, лучше держит большие отношения масс и меньше «накачивает» энергию при выталкивании из проникновений. Если машина ведёт себя странно при столкновениях с лёгкими объектами — переключить и померить.
- **Broadphase Type**: для большой плоской карты с трафиком имеет смысл `Automatic Box Pruning` вместо дефолтного `Sweep and Prune`. **Auto Sync Transforms** по умолчанию выключен, **Reuse Collision Callbacks** включён — оба дефолта правильные, не трогать.
- **Friction Type**: `Patch` — самый дешёвый и стабильный при малом числе итераций; `Two Directional` точнее, но дороже при большом числе точек контакта. На `WheelCollider` не влияет вообще (у него своя модель трения) — влияет на кувыркание кузова после аварии.

---

## 5. Состояние физики в Unity 6 (6.0 → 6.3), август 2026

- **Встроенная 3D-физика — это по-прежнему NVIDIA PhysX (ветка 4.1).** Мануал 6.3 по `WheelCollider` прямо отсылает к документации PhysX 4.1 Vehicles SDK. Никакого нового vehicle-солвера для GameObject-физики в 6.x не появилось.
- **В Unity 6.3 в разделе «What's New» нет ни одного пункта по 3D-физике.** Всё, что там есть про физику — это новый low-level 2D API на Box2D v3 (`UnityEngine.LowLevelPhysics2D`). Для нашей задачи это не значит ничего.
- **Новое в Project Settings → Physics: выпадающий список `GameObject SDK`** (`PhysX` / `None`). Это заготовка под будущий выбор физического бэкенда — сам по себе он ничего не меняет, но объясняет, почему настройки физики в 6.3 переразложены по вкладкам Shared / GameObject / Cloth.
- **Дорожная карта (официальный пост команды физики, май 2025):** единый воркфлоу выбора бэкенда через Project Settings; **Havok Physics для GameObject-проектов** (сейчас Havok доступен только через ECS); direct solver для ECS Unity Physics. Сроков нет. На наши решения сегодня не влияет — но объясняет, почему `WheelCollider` не развивают.
- **Пакет `com.unity.vehicles` («Unity Vehicles»)**, экспериментальный, анонсирован 30 апреля 2025, вошёл в What's New Unity 6.2. Универсальный контроллер для транспорта: любое число колёс, добавление/удаление в рантайме, совместим с Netcode for Entities и клиентским предсказанием, сделан в соавторстве с NWH Coding (авторы NWH Vehicle Physics 2). **Работает только в ECS/DOTS.** Тащить DOTS в проект ради машины — несоразмерная цена; знать о пакете стоит на случай, если проект когда-нибудь поедет в сторону тысяч ИИ-машин.
- **Переименования API, критичные для этой темы** (Unity 6, есть в API-референсе 6.3):

| Было (≤ 2022 LTS) | Стало (Unity 6) |
|---|---|
| `Rigidbody.velocity` | `Rigidbody.linearVelocity` |
| `Rigidbody.drag` | `Rigidbody.linearDamping` |
| `Rigidbody.angularDrag` | `Rigidbody.angularDamping` |
| `PhysicMaterial` | `PhysicsMaterial` |
| `Object.FindObjectOfType<T>()` | `Object.FindFirstObjectByType<T>()` |

Практическое следствие: **почти любой туториал и репозиторий по автомобильной физике в Unity из интернета не скомпилируется в Unity 6 без правок.** Это не признак того, что подход устарел — это признак того, что переименовали свойства.

---

## 6. Визуальные приёмы

- **Наклон кузова.** Если модель кузова — тот же GameObject, что и `Rigidbody`, крен получается физически и бесплатно. Если хочется усилить эффект «для картинки» — добавляем **отдельный визуальный пивот** внутри кузова и докручиваем его на основе боковой перегрузки, ограничив углом в 3–5°. Никогда не крутить сам `Rigidbody` — это ломает физику.
- **Поворот колеса.** У `WheelCollider` — только `GetWorldPose()`: он уже учитывает контакт с землёй, ограничения хода подвески, угол поворота и угол качения. Для raycast-модели считаем сами: позиция = точка попадания луча плюс радиус вверх, угол качения интегрируем как `angle += (speedAlongForward / radius) * Mathf.Rad2Deg * Time.deltaTime`.
- **Следы шин.** `TrailRenderer` на каждом колесе — дёшево, но трейл по умолчанию разворачивается лицом к камере и выглядит лентой в воздухе; лечится `trail.alignment = LineAlignment.TransformZ` плюс разворот трансформа плоскостью к земле. Минус остаётся: след нельзя «поставить на паузу» без разрыва. Более подходящий для витрины вариант — собственный генератор меша, накапливающий сегменты полоски в общий `Mesh` с пулом. URP Decal Renderer Feature годится для разовых пятен, но не для непрерывного следа (каждый след — отдельный проектор).
- **Условие появления следа:** `WheelHit.sidewaysSlip`/`forwardSlip` за порогом (для `WheelCollider`) либо своя величина проскальзывания (для raycast). Этот же сигнал питает звук визга шин и партиклы дыма — поэтому проскальзывание считаем один раз и кладём в состояние колеса, а не пересчитываем в каждом эффекте.
- **Компонентная граница.** `VehicleVisuals`, `TireMarks`, `EngineAudio` читают состояние из физического компонента и не пишут в него. Тогда замена игрока на ИИ-водителя не трогает ни один из них.

---

## 7. Антипаттерны

- **«Опусти центр масс на метр вниз, и машина перестанет переворачиваться».** Самый частый совет в интернете. Он работает — и одновременно превращает машину в утюг: прыжки, столкновения и трюки выглядят неестественно. Сначала anti-roll bars.
- **`Rigidbody.velocity` / `.drag` / `.angularDrag`.** Не существуют в Unity 6. Если статья или репозиторий их использует — это код до 2024 года, и остальные советы в нём тоже стоит проверять.
- **Гнать `Fixed Timestep` до 0.005 «чтобы колёса не дрожали».** Умножает стоимость всей физики сцены (трафик, разрушаемые объекты, партиклы с коллизиями) в четыре раза ради одной машины. Сначала `ConfigureVehicleSubsteps` или подшаг в своём коде.
- **Трогать трансформ `WheelCollider` в рантайме** — крутить, поворачивать, поднимать «просевшее колесо». Поворот задаётся `steerAngle`, вращение отдаётся визуальному мешу; менять `center` можно, `transform.position` — нет (компонент кеширует на старте данные, зависящие от расстояния до центра масс).
- **`Extrapolate` вместо `Interpolate` на машине.** Даёт визуальные заезды в геометрию с последующим откатом.
- **Ставить `Rigidbody` на каждое колесо и соединять `ConfigurableJoint`.** Формально «физически честнее», на практике — гарантированный источник дрожания, дорогой солвер и настройка, которая не сходится. Ни один из разобранных референсов так не делает.
- **Использовать `PhysicsMaterial` дороги, чтобы «сделать лёд», с `WheelCollider`.** Он его игнорирует. Покрытие меняется подменой кривых трения по `WheelHit.collider`.
- **Читать ввод в `FixedUpdate`.** `FixedUpdate` вызывается 0, 1 или несколько раз за кадр — короткие нажатия будут теряться или дублироваться. Читать в `Update`, буферизовать, применять в `FixedUpdate`.
- **`GetComponent`, `Camera.main`, LINQ, `Debug.Log` внутри цикла по колёсам в `FixedUpdate`.** Четыре колеса × 50 Гц — это уже горячий путь. Всё кешировать в `Awake`.
- **Ставить `WheelCollider` на процедурную карту из отдельных плоскостей со стыками** (луч провалится в каждый стык — нужен непрерывный меш дороги либо raycast-модель со `SphereCast`) или **тянуть `com.unity.vehicles` в GameObject-проект** (это экспериментальный ECS-пакет).

---

## 8. Чек-лист

Перед первым запуском:

- [ ] `Rigidbody` только один — на корне машины. Масса 1200–1500 кг. `interpolation = Interpolate`, `collisionDetectionMode = ContinuousDynamic`.
- [ ] Коллайдер кузова — `BoxCollider` или **convex** `MeshCollider`. Не вогнутый меш.
- [ ] Центр масс задан явно и осознанно; понятно, почему именно так.
- [ ] Ввод читается в `Update`, применяется в `FixedUpdate`.
- [ ] Ни одного `Rigidbody.velocity` / `.drag` / `.angularDrag` в проекте.
- [ ] Все параметры машины — в `ScriptableObject`, а не в инспекторе префаба.
- [ ] Коллизионная геометрия дороги гладкая, без вертикальных ступенек и зазоров на стыках.
- [ ] `Fixed Timestep` = 0.02, `Maximum Allowed Timestep` ограничен.

Если выбран `WheelCollider`:

- [ ] Меши колёс и `WheelCollider` — разные GameObject; коллайдеры не вращаются. Позы берутся через `GetWorldPose`.
- [ ] Радиус коллайдера совпадает с радиусом меша; коллайдеры подняты на `suspensionDistance / 2`.
- [ ] `spring`/`damper` соответствуют массе (проверить по формуле из раздела 2).
- [ ] `ConfigureVehicleSubsteps` вызван один раз на машину.
- [ ] `forceAppPointDistance` выставлен так, чтобы силы прикладывались чуть ниже центра масс.
- [ ] Два `AntiRollBar` — на переднюю и заднюю ось.

Если выбрана raycast-подвеска:

- [ ] Демпфер пружины считался от критического (`2 * sqrt(k * m)`), а не подбирался вслепую.
- [ ] Вертикальная скорость берётся через `GetPointVelocity(hit.point)`, а не из `linearVelocity`.
- [ ] Силы прикладываются через `AddForceAtPosition` в точку контакта, а не в центр масс.
- [ ] Есть сопротивление качению и поведение в воздухе (гашение вращения / выравнивание).
- [ ] Луч не попадает в коллайдеры самой машины (`GroundMask` + `QueryTriggerInteraction.Ignore`); рассмотрен `SphereCast`.

Проверка в Play Mode (для ручного прогона):

- [ ] Стоящая машина не дрожит, не «уползает» и не проседает за минуту.
- [ ] Резкий поворот на максимальной скорости не переворачивает машину.
- [ ] Прыжок с трамплина: приземление на колёса, без «прилипания» и без бесконечного отскока.
- [ ] Наезд на бордюр 10 см: колесо заезжает, а не проваливается и не подбрасывает машину.
- [ ] На 120+ FPS при 50 Гц физики машина и камера не дрожат; конус под колёсами не подбрасывает машину.

---

## 9. Видео и доклады

Все ссылки проверены 2026-08-19 (название и канал получены через YouTube oEmbed).

- **Making Custom Car Physics in Unity (for Very Very Valet)** — Toyful Games — https://www.youtube.com/watch?v=CdPYlj5uZeI — главный референс по raycast-подвеске: разбор всех четырёх сил на колесе (подвеска, руление, тяга, торможение) на примере вышедшей игры. Смотреть первым.
- **Arcade Car Physics** — Sergey Makeev — https://www.youtube.com/watch?v=HCuJi2pntOw — демо-видео к репозиторию `SergeyMakeev/ArcadeCarPhysics`: speed curve, стабилизаторы, рулевое Аккермана, downforce, ручник.
- **Space Dust Racing UE4 Arcade Vehicle Physics Tour** — SpaceDustStudios — https://www.youtube.com/watch?v=LG1CtlFRmpU — движок другой, но это самый цитируемый разбор устройства аркадной автомобильной физики вообще; на него ссылаются и Sergey Makeev, и Seena Burns.
- **Supercharged! Vehicle Physics in Skylanders** — GDC Festival of Gaming — https://www.youtube.com/watch?v=Db1AgGavL8E — доклад GDC о том, как делают «весёлую, но управляемую» физику машин в большой аркадной игре.
- **Arcade Car Controller Part 1 | Suspension tutorial Unity** — Ash Dev — https://www.youtube.com/watch?v=sWshRRDxdSU — пошаговая реализация raycast-подвески (2024).
- **Raycast Car Physics in Unity (Tutorial)** — LUIGI GAME DEV — https://www.youtube.com/watch?v=zcKdQ_UUyGE — вторая независимая реализация того же подхода; полезно сравнить с предыдущей.
- **Unity Car Physics - Lesson 1 - Suspension Physics** — BlinkAChu — https://www.youtube.com/watch?v=x0LUiE0dxP0 — старее (2019), но подробно про математику пружины и демпфера.
- **Unity Car Controller With Wheel Collider – Easy Tutorial (2025)** — Solo Game Dev — https://www.youtube.com/watch?v=9Cdky9Wmus8 — свежий проход по `WheelCollider`, если решим делать альтернативный драйвер. Более старые варианты того же: SpeedTutor https://www.youtube.com/watch?v=cS4YQp9Oez0, RoBust Games https://www.youtube.com/watch?v=tQmxW277dXk, GameDevChef https://www.youtube.com/watch?v=Z4HA8zJhGEk.
- **Unity 6 Experimental Vehicle Package – Create Realistic Vehicle Physics with ECS/DOTS!** — Cam Ayres — https://www.youtube.com/watch?v=-x85_Z41OGY — обзор пакета `com.unity.vehicles`; смотреть, только если зайдёт разговор про DOTS.
- **Testing "ArcadeCarPhysics" & "Randomation-Vehicle-Physics" from github** — UnityCoder — https://www.youtube.com/watch?v=yb4yPMB43Cc — быстрое сравнение двух главных открытых референсов в деле.
- **Unity Tutorial - Skid Marks And Skid Sound (Trail Renderer)** — coding edge (pablos lab) — https://www.youtube.com/watch?v=0LOcxZhkVwc — следы шин и звук визга на `TrailRenderer`.

## Открытые референсы (код)

Метаданные GitHub на 2026-08-19.

- `SergeyMakeev/ArcadeCarPhysics` — 454 звезды, последний коммит 2020-03. Самый близкий к тому, что нам нужно: raycast, speed curve, стабилизаторы, Аккерман, downforce, ручник, стабилизация в полёте. Код под Unity 2019 — переименования Unity 6 придётся править.
- `JustInvoke/Randomation-Vehicle-Physics` — 932 звезды, MIT, последний коммит 2022-11, тестировался на 2019.2.9. Полуреалистичная система общего назначения с PDF-мануалом. Хороший источник идей, но объёмный.
- `benmcinnes/ArcadeVehiclePhysics` — 202 звезды, последний коммит 2024-11 — самый свежий из открытых аркадных фреймворков.
- `Unity-Technologies/VehicleTools` (156 звёзд, 2021-05) — официальный сэмпл Unity по риггингу колёсной техники. `unity-car-tutorials/SimpleRaycastVehicle-Unity` (87 звёзд, архивирован, 2016) — минимальная raycast-модель, на которую ссылаются остальные.

---

## 10. Источники

Дата обращения — 2026-08-19.

Официальная документация Unity 6.3 (6000.3):

- https://docs.unity3d.com/6000.3/Documentation/Manual/wheel-colliders-introduction.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/wheel-colliders-friction.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/wheel-colliders-suspension.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/class-WheelCollider.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/WheelColliderTutorial.html
- Scripting API (префикс `https://docs.unity3d.com/6000.3/Documentation/ScriptReference/`): `WheelCollider.html`, `WheelCollider.ConfigureVehicleSubsteps.html`, `WheelFrictionCurve.html`, `WheelHit.html`, `Rigidbody.html`, `Rigidbody-linearVelocity.html`, `Rigidbody-linearDamping.html`, `Physics-defaultSolverIterations.html`, `Physics-simulationMode.html`, `LineAlignment.html`
- Manual (префикс `https://docs.unity3d.com/6000.3/Documentation/Manual/`): `class-PhysicsManager.html`, `class-TimeManager.html`, `rigidbody-interpolation.html`, `com.unity.modules.vehicles.html`, `WhatsNewUnity63.html`, `WhatsNewUnity62.html`
- Пакеты: https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/manual/renderer-feature-decal.html и https://docs.unity3d.com/Packages/com.unity.vehicles@latest/

Официальные материалы и анонсы Unity:

- https://unity.com/how-to/enhanced-physics-performance-smooth-gameplay
- https://discussions.unity.com/t/physics-development-status-and-next-milestones-may-2025/1637988
- https://discussions.unity.com/t/unity-vehicles-experimental-package-now-available/1636923
- https://issuetracker.unity3d.com/issues/vehicle-body-is-lifted-way-above-wheels-containing-a-wheelcollider-component-when-entering-play-mode

Экспертные и сообщественные источники:

- https://www.edy.es/dev/2011/10/the-stabilizer-bars-creating-physically-realistic-stable-vehicles/
- https://vehiclephysics.com/components/wheel-collider/
- https://seenaburns.com/2018/03/02/car-controller/
- https://github.com/SergeyMakeev/ArcadeCarPhysics
- https://github.com/JustInvoke/Randomation-Vehicle-Physics
- https://github.com/benmcinnes/ArcadeVehiclePhysics
- https://github.com/Unity-Technologies/VehicleTools
- https://github.com/unity-car-tutorials/SimpleRaycastVehicle-Unity
- https://www.toyfulgames.com/blog/deep-dive-physics

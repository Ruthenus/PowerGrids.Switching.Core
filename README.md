---

# PowerGrids.Switching.Core

**PowerGrids.Switching.Core** — це високорівневе .NET 10 ядро для розробки систем оперативного керування SCADA (Supervisory Control And Data Acquisition) в електроенергетиці. Побудоване на базі патернів **Command** та **Composite**, воно забезпечує безпечне виконання послідовностей оперативних перемикань з автоматичним скасуванням (Undo) у разі помилок.

## Особливості

* **Safety First**: Вбудована підтримка оперативних блокувань (`Interlock`).
* **Transactional Operations**: Автоматичне скасування всієї послідовності перемикань в разі виникнення технологічного порушення.
* **Structured Logging**: Повна інтеграція з Serilog (з використанням контекстів для диспетчерських назв).

---

## Швидкий старт

### 1. Встановлення пакета

Через NuGet Package Manager або CLI:

```powershell
dotnet add package PowerGrids.Switching.Core

```

### 2. Створення пристрою та логіки блокування

Бібліотека використовує **Primary Constructors**, тому ініціалізація максимально лаконічна:

```csharp
var breaker = new SwitchingDevice(
    type: DeviceType.CircuitBreaker, 
    name: "В-110 кВ Л-10"
);

// Гнучке налаштування блокувань через делегати
breaker.Interlock = (target) => {
    return (true, "Дія дозволена"); 
};

```

### 3. Формування та виконання бланку перемикань

Використовуйте `SwitchingOrderExecutor` для керування кроками:

```csharp
var executor = new SwitchingOrderExecutor();

// Додавання команд (через конструктор або Extension Methods)
executor.Add(new VerifyDeviceStateCommand(breaker, "Перевірка стану", SwitchPosition.Off));
breaker.TurnOn("Ввімкнення за розпорядженням"); // Додає команду в чергу

// Виконання всіх кроків
executor.ExecuteAll();

```

---

## Архітектура

### Командна модель

* `ISwitchOperation` — основний інтерфейс.
* `SwitchDeviceCommand` — одиночна операція зі станом.
* `MacroCommand` — групування команд (Composite).
* `ParallelCommand` — одночасне виконання (наприклад, для складних схем).

### Стани пристроїв

Підтримуються оперативні стани згідно з галузевими стандартами:

* `IntermediateState` (Проміжний)
* `Off` (Вимкнено)
* `On` (Увімкнено)
* `BadState` (Несправність)

---

## Специфікації пакета

* **Target Framework:** `.NET 10.0`
* **Dependencies:** `Serilog (>= 4.3.1)`
* **Author:** Ruslan Kachurovskyi
* **URL:** https://www.nuget.org/packages/PowerGrids.Switching.Core/

---

## Ліцензія

Розповсюджується за ліцензією **MIT**.

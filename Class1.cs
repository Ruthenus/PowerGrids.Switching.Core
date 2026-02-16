using Serilog;
using Serilog.Core;
using System.Xml.Linq;

namespace PowerGrids.Switching.Core
{
    public enum DeviceType { CircuitBreaker, Disconnector, EarthingSwitch }
    public enum SwitchPosition { IntermediateState, Off, On, BadState }


    public class SwitchingDevice(
        DeviceType type,
        string name,
        SwitchPosition initial = SwitchPosition.Off,
        Action<string>? onPositionChanged = null,
        ILogger? logger = null)
    {
        public DeviceType Type { get; } = type;
        public string Name { get; } = name;
        public SwitchPosition Current { get; private set; } = initial;

        private readonly ILogger? _logger =
            logger?.ForContext("ДиспетчерськаНазва", name);
        private readonly Action<string>? _recalculationRequest =
            onPositionChanged;

        public Func<SwitchPosition, (bool IsAllowed, string Message)>
            Interlock
        { get; set; } = (target) =>
                (true, "Оперативне блокування дозволяє дію");

        public void SetPosition(string action, SwitchPosition target)
        {
            var (IsAllowed, Message) = Interlock(target);

            if (!IsAllowed)
            {
                _logger?.Error("БЛОКУВАННЯ (ВІДМОВА) {ДиспетчерськаНазва}: " +
                    "{СтанБлокування}", Name, Message);
                return;
            }

            SwitchPosition previous = Current;
            Current = target;

            _recalculationRequest?.Invoke(action);

            _logger?.Information("{ОперативнаДія}: [{ВихіднеПоложення} -> " +
                "{ЦільовеПоложення}] {ДиспетчерськаНазва}", action, previous,
                target, Name);
        }

        public override string ToString()
        {
            return $"СТАН: {Current,-15} {Name,-45} [{Type}]";
        }
    }


    public interface ISwitchOperation
    {
        void Execute();
        void Undo();
    }


    public class SwitchDeviceCommand(
        SwitchingDevice device,
        string order,
        SwitchPosition target) : ISwitchOperation
    {
        private readonly SwitchingDevice _device = device;
        private readonly string _order = order;
        private readonly SwitchPosition _target = target;

        private SwitchPosition _prev;

        public void Execute()
        {
            _prev = _device.Current;
            _device.SetPosition(_order, _target);
        }

        public void Undo()
        {
            _device.SetPosition($"Повернення до початкового стану: " +
                $"{_order}", _prev);
        }
    }


    public static class SwitchingDeviceExtensions
    {
        public static void TurnOn(this SwitchingDevice device, string reason)
        {
            new SwitchDeviceCommand(device, reason, SwitchPosition.On)
                .Execute();
        }

        public static void TurnOff(this SwitchingDevice device, string reason)
        {
            new SwitchDeviceCommand(device, reason, SwitchPosition.Off)
                .Execute();
        }
    }


    public class VerifyDeviceStateCommand(
        SwitchingDevice device,
        string checkOrder,
        SwitchPosition required,
        ILogger? logger = null) : ISwitchOperation
    {
        private readonly SwitchingDevice _device = device;
        private readonly string _checkOrder = checkOrder;
        private readonly SwitchPosition _required = required;
        private readonly ILogger? _logger =
            logger?.ForContext("ДиспетчерськаНазва", device.Name);

        public event Action<string>? StateVerified;

        public void Execute()
        {
            if (_device.Current != _required)
            {
                var errorMsg = $"КРИТИЧНА ПОМИЛКА! Очікується [{_required}]," +
                    $" а фактично є [{_device.Current}].";
                _logger?.Fatal("{Помилка}. Не виконано: {Розпорядження}",
                    errorMsg, _checkOrder);
                throw new InvalidOperationException($"{errorMsg}. " +
                    $"Не виконано: {_checkOrder}");
            }

            _logger?.Information("ПІДТВЕРДЖЕНО: стан {ДиспетчерськаНазва} " +
                "[{ПотрібнеПоложення}]", _device.Name, _required);
            StateVerified?.Invoke($"ПІДТВЕРДЖЕНО: стан {_device.Name} " +
                $"[{_required}]");
        }

        public void Undo() { }
    }


    public class MacroCommand : ISwitchOperation
    {
        private readonly List<ISwitchOperation> _commands = [];

        public MacroCommand(IEnumerable<ISwitchOperation> commands)
        {
            _commands.AddRange(commands);
        }

        public void Execute()
        {
            _commands.ForEach((cmd) => cmd.Execute());
        }

        public void Undo()
        {
            for (int i = _commands.Count - 1; i >= 0; i--)
            {
                _commands[i].Undo();
            }
        }
    }


    public class ParallelCommand : ISwitchOperation
    {
        private readonly List<ISwitchOperation> _commands = [];

        public ParallelCommand(IEnumerable<ISwitchOperation> commands)
        {
            _commands.AddRange(commands);
        }

        public void Execute()
        {
            var tasks = _commands
                .Select(cmd => Task.Run(() => cmd.Execute()))
                .ToArray();
            Task.WaitAll(tasks);
        }

        public void Undo()
        {
            for (int i = _commands.Count - 1; i >= 0; i--)
            {
                _commands[i].Undo();
            }
        }
    }


    public class SwitchingOrderExecutor(ILogger? logger = null)
    {
        private readonly List<ISwitchOperation> _steps = [];
        private readonly Stack<ISwitchOperation> _undoStack = [];

        private readonly ILogger _logger = (logger ?? Log.Logger)
            .ForContext<SwitchingOrderExecutor>();

        public void Add(ISwitchOperation cmd) => _steps.Add(cmd);

        public void ExecuteAll()
        {
            int step = 1;

            try
            {
                foreach (var cmd in _steps)
                {
                    _logger.Information("Крок {НомерКроку}: ", step);
                    cmd.Execute();
                    _undoStack.Push(cmd);
                    step++;
                }
                _logger.Information("ОПЕРАТИВНІ ПЕРЕМИКАННЯ УСПІШНО ВИКОНАНО!");
            }

            catch (Exception ex)
            {
                _logger.Fatal(ex, "ТЕХНОЛОГІЧНЕ ПОРУШЕННЯ на кроці " +
                    "{НомерКроку}: {ПовідомленняПомилки}", step, ex.Message);
                UndoAll();
            }
        }

        private void UndoAll()
        {
            _logger.Warning("СКАСУВАННЯ ВСІХ ЗМІН");
            while (_undoStack.Count > 0)
            {
                _undoStack.Pop().Undo();
            }
            _logger.Information("Повернуто до безпечного стану.");
        }

        public void Clear()
        {
            _steps.Clear();
            _undoStack.Clear();
            _logger.Debug("Список команд та стек скасування очищено");
        }
    }
}
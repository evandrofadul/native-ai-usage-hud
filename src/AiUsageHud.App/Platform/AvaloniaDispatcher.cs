using AiUsageHud.Presentation.Platform;
using Avalonia.Threading;

namespace AiUsageHud.App.Platform;

/// <summary>Avalonia implementation of the shared <see cref="IUiDispatcher"/> (a <see cref="DispatcherTimer"/>).</summary>
public sealed class AvaloniaDispatcher : IUiDispatcher
{
    public IUiTimer CreateTimer(TimeSpan interval) => new AvaloniaTimer(interval);

    private sealed class AvaloniaTimer : IUiTimer
    {
        private readonly DispatcherTimer _timer;

        public AvaloniaTimer(TimeSpan interval)
        {
            _timer = new DispatcherTimer { Interval = interval };
            _timer.Tick += (_, _) => Tick?.Invoke();
        }

        public event Action? Tick;
        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();
    }
}

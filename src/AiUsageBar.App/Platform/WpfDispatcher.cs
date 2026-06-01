using System.Windows.Threading;
using AiUsageBar.Presentation.Platform;

namespace AiUsageBar.App.Platform;

/// <summary>WPF implementation of the shared <see cref="IUiDispatcher"/> (a <see cref="DispatcherTimer"/>).</summary>
public sealed class WpfDispatcher : IUiDispatcher
{
    public IUiTimer CreateTimer(TimeSpan interval) => new WpfTimer(interval);

    private sealed class WpfTimer : IUiTimer
    {
        private readonly DispatcherTimer _timer;

        public WpfTimer(TimeSpan interval)
        {
            _timer = new DispatcherTimer { Interval = interval };
            _timer.Tick += (_, _) => Tick?.Invoke();
        }

        public event Action? Tick;
        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();
    }
}

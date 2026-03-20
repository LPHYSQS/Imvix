using System.Threading;

namespace ImvixPro.Services
{
    public sealed class ConversionPauseController
    {
        private readonly ManualResetEventSlim _resumeEvent = new(initialState: true);

        public bool IsPaused => !_resumeEvent.IsSet;

        public void Pause()
        {
            _resumeEvent.Reset();
        }

        public void Resume()
        {
            _resumeEvent.Set();
        }

        public void WaitIfPaused(CancellationToken cancellationToken)
        {
            _resumeEvent.Wait(cancellationToken);
        }
    }
}

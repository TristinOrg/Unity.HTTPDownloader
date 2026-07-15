using System;
using System.Threading;
using System.Threading.Tasks;

namespace UnityHTTPDownloader
{
    /// <summary>
    /// Exposes control, events, progress, and completion for one download.
    /// </summary>
    public sealed class HTTPDownloadHandle : IDisposable
    {
        private readonly object                  mLock = new();
        private readonly HTTPDownloader          mOwner;
        private readonly CancellationTokenSource mLifetimeCancellation = new();
        private CancellationTokenSource          mAttemptCancellation;
        private TaskCompletionSource<bool>       mResumeSignal;
        private int                              mState;
        private bool                             mDisposed;

        public Guid                Id      { get; } = Guid.NewGuid();
        public HTTPDownloadRequest Request { get; }
        public Task<HTTPDownloadResult> Completion { get; internal set; }
        public HTTPDownloadProgress Progress { get; internal set; } = new();

        public HTTPDownloadState State => (HTTPDownloadState)Volatile.Read(ref mState);

        public event Action<HTTPDownloadHandle,HTTPDownloadProgress> ProgressChanged;
        public event Action<HTTPDownloadHandle,HTTPDownloadState>    StateChanged;

        internal CancellationToken LifetimeToken => mLifetimeCancellation.Token;

        /// <summary>
        /// Creates a handle owned by the supplied downloader.
        /// </summary>
        /// <param name="owner">The service that runs the transfer.</param>
        /// <param name="request">The immutable request identity.</param>
        internal HTTPDownloadHandle(HTTPDownloader owner,HTTPDownloadRequest request)
        {
            mOwner  = owner;
            Request = request;
            SetState(HTTPDownloadState.Queued);
        }

        /// <summary>
        /// Pauses the active transfer while preserving its partial data.
        /// </summary>
        public void Pause()
        {
            lock (mLock)
            {
                if (mDisposed || State != HTTPDownloadState.Downloading && State != HTTPDownloadState.Connecting)
                    return;
                mResumeSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
                SetState(HTTPDownloadState.Paused);
                mAttemptCancellation?.Cancel();
            }
        }

        /// <summary>
        /// Resumes a paused transfer from its persisted partial data.
        /// </summary>
        public void Resume()
        {
            lock (mLock)
            {
                if (mDisposed || State != HTTPDownloadState.Paused)
                    return;
                mResumeSignal?.TrySetResult(true);
            }
        }

        /// <summary>
        /// Permanently cancels the queued, active, or paused transfer.
        /// </summary>
        public void Cancel()
        {
            if (mDisposed)
                return;
            mLifetimeCancellation.Cancel();
            lock (mLock)
                mResumeSignal?.TrySetCanceled();
        }

        /// <summary>
        /// Cancels the transfer and releases resources owned by this handle.
        /// </summary>
        public void Dispose()
        {
            if (mDisposed)
                return;
            Cancel();
            mDisposed = true;
            mAttemptCancellation?.Dispose();
            mLifetimeCancellation.Dispose();
        }

        /// <summary>
        /// Creates the linked cancellation token for a single network attempt.
        /// </summary>
        /// <param name="token">The parent operation token.</param>
        /// <returns>The attempt token.</returns>
        internal CancellationToken BeginAttempt(CancellationToken token)
        {
            lock (mLock)
            {
                mAttemptCancellation?.Dispose();
                mAttemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(token,mLifetimeCancellation.Token);
                return mAttemptCancellation.Token;
            }
        }

        /// <summary>
        /// Waits asynchronously until a paused transfer is resumed.
        /// </summary>
        internal async Task WaitWhilePausedAsync()
        {
            Task signal;
            lock (mLock)
                signal = mResumeSignal?.Task;
            if (signal != null)
                await signal.ConfigureAwait(false);
        }

        /// <summary>
        /// Stores and dispatches a new progress snapshot.
        /// </summary>
        /// <param name="progress">The current immutable progress snapshot.</param>
        internal void SetProgress(HTTPDownloadProgress progress)
        {
            Progress = progress;
            mOwner.Post(() => ProgressChanged?.Invoke(this,progress));
        }

        /// <summary>
        /// Changes and dispatches the current lifecycle state.
        /// </summary>
        /// <param name="state">The new lifecycle state.</param>
        internal void SetState(HTTPDownloadState state)
        {
            Interlocked.Exchange(ref mState,(int)state);
            if (Progress != null)
                Progress.State = state;
            mOwner?.Post(() => StateChanged?.Invoke(this,state));
        }
    }
}

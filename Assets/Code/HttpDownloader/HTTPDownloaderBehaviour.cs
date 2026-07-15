using System;
using UnityEngine;

namespace UnityHTTPDownloader
{
    /// <summary>
    /// Owns a downloader in a Unity scene and releases it automatically.
    /// </summary>
    public sealed class HTTPDownloaderBehaviour : MonoBehaviour
    {
        [SerializeField] private int  mMaxConcurrentDownloads = 4;
        [SerializeField] private int  mMaxRetries             = 3;
        [SerializeField] private int  mTimeoutSeconds         = 30;
        [SerializeField] private int  mBufferSizeKilobytes    = 128;
        [SerializeField] private long mBytesPerSecondLimit;

        public HTTPDownloader Downloader { get; private set; }

        /// <summary>
        /// Creates the owned downloader from the serialized component settings.
        /// </summary>
        private void Awake()
        {
            var options = new HTTPDownloaderOptions
            {
                MaxConcurrentDownloads = Math.Max(1,mMaxConcurrentDownloads),
                MaxRetries             = Math.Max(0,mMaxRetries),
                RequestTimeout         = TimeSpan.FromSeconds(Math.Max(1,mTimeoutSeconds)),
                BufferSize             = Math.Max(4,mBufferSizeKilobytes) * 1024,
                BytesPerSecondLimit    = mBytesPerSecondLimit > 0 ? mBytesPerSecondLimit : (long?)null
            };
            Downloader = new HTTPDownloader(options);
        }

        /// <summary>
        /// Cancels active work and releases the owned downloader.
        /// </summary>
        private void OnDestroy()
        {
            Downloader?.Dispose();
            Downloader = null;
        }
    }
}

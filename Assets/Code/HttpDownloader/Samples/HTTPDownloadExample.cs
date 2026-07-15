using System;
using UnityEngine;

namespace UnityHTTPDownloader.Samples
{
    /// <summary>
    /// Demonstrates a resumable and checksum-verified download from a MonoBehaviour.
    /// </summary>
    public sealed class HTTPDownloadExample : MonoBehaviour
    {
        [SerializeField] private string mUrl;
        [SerializeField] private string mFileName = "download.bin";
        [SerializeField] private string mExpectedSHA256;

        private HTTPDownloader     mDownloader;
        private HTTPDownloadHandle mHandle;

        /// <summary>
        /// Creates the sample downloader on Unity's main thread.
        /// </summary>
        private void Awake()
        {
            mDownloader = new HTTPDownloader(new HTTPDownloaderOptions
            {
                MaxConcurrentDownloads = 3,
                MaxRetries             = 4
            });
        }

        /// <summary>
        /// Starts or resumes the configured example download.
        /// </summary>
        public async void StartDownload()
        {
            try
            {
                var destination = System.IO.Path.Combine(Application.persistentDataPath,mFileName);
                var request     = new HTTPDownloadRequest(mUrl,destination)
                {
                    ExistingFilePolicy = HTTPExistingFilePolicy.Resume,
                    ChecksumAlgorithm  = string.IsNullOrWhiteSpace(mExpectedSHA256)
                                       ? HTTPChecksumAlgorithm.None
                                       : HTTPChecksumAlgorithm.SHA256,
                    ExpectedChecksum   = mExpectedSHA256
                };
                mHandle = mDownloader.Download(request);
                mHandle.ProgressChanged += OnProgressChanged;
                var result = await mHandle.Completion;
                if (result.IsSuccess)
                    Debug.Log($"Downloaded to {result.DestinationPath} in {result.Duration.TotalSeconds:F1}s.");
                else
                    Debug.LogError($"Download ended as {result.State}: {result.Error}");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }

        /// <summary>
        /// Pauses the example download.
        /// </summary>
        public void PauseDownload()
        {
            mHandle?.Pause();
        }

        /// <summary>
        /// Resumes the example download.
        /// </summary>
        public void ResumeDownload()
        {
            mHandle?.Resume();
        }

        /// <summary>
        /// Cancels the example download.
        /// </summary>
        public void CancelDownload()
        {
            mHandle?.Cancel();
        }

        /// <summary>
        /// Writes the latest transfer metrics to the Unity console.
        /// </summary>
        /// <param name="handle">The reporting handle.</param>
        /// <param name="progress">The progress snapshot.</param>
        private static void OnProgressChanged(HTTPDownloadHandle handle,HTTPDownloadProgress progress)
        {
            Debug.Log($"{progress.Progress:P1} | {progress.BytesPerSecond / 1024d:F1} KiB/s | " +
                      $"ETA {progress.EstimatedRemaining}");
        }

        /// <summary>
        /// Releases the downloader when the sample component is destroyed.
        /// </summary>
        private void OnDestroy()
        {
            mDownloader?.Dispose();
        }
    }
}

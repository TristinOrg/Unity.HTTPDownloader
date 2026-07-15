using System;
using System.Collections.Generic;

namespace UnityHTTPDownloader
{
    /// <summary>
    /// Defines every lifecycle state a download can enter.
    /// </summary>
    public enum HTTPDownloadState
    {
        Queued,
        Connecting,
        Downloading,
        Paused,
        Verifying,
        Completed,
        Failed,
        Canceled
    }

    /// <summary>
    /// Defines the supported file checksum algorithms.
    /// </summary>
    public enum HTTPChecksumAlgorithm
    {
        None,
        MD5,
        SHA256
    }

    /// <summary>
    /// Defines how an existing destination file is handled.
    /// </summary>
    public enum HTTPExistingFilePolicy
    {
        Resume,
        Overwrite,
        SkipIfValid,
        Fail
    }

    /// <summary>
    /// Provides an immutable snapshot of current transfer progress and performance.
    /// </summary>
    public sealed class HTTPDownloadProgress
    {
        public HTTPDownloadState State              { get; internal set; }
        public long              BytesDownloaded    { get; internal set; }
        public long?             TotalBytes         { get; internal set; }
        public double            BytesPerSecond     { get; internal set; }
        public TimeSpan?         EstimatedRemaining { get; internal set; }
        public int               Attempt            { get; internal set; }

        public double Progress
        {
            get
            {
                if (!TotalBytes.HasValue || TotalBytes.Value <= 0)
                    return 0d;
                return Math.Min(1d, (double)BytesDownloaded / TotalBytes.Value);
            }
        }
    }

    /// <summary>
    /// Provides the terminal outcome of one download operation.
    /// </summary>
    public sealed class HTTPDownloadResult
    {
        public bool              IsSuccess       { get; internal set; }
        public bool              WasResumed      { get; internal set; }
        public bool              WasSkipped      { get; internal set; }
        public string            DestinationPath { get; internal set; }
        public long              FileSize        { get; internal set; }
        public int               Attempts        { get; internal set; }
        public TimeSpan          Duration        { get; internal set; }
        public Exception         Error           { get; internal set; }
        public HTTPDownloadState State           { get; internal set; }
    }

    /// <summary>
    /// Provides the aggregate outcome of a batch download.
    /// </summary>
    public sealed class HTTPBatchDownloadResult
    {
        public IReadOnlyList<HTTPDownloadResult> Results { get; internal set; }

        public bool IsSuccess
        {
            get
            {
                if (Results == null)
                    return false;
                for (int i = 0, cnt = Results.Count; i < cnt; i++)
                    if (!Results[i].IsSuccess)
                        return false;
                return true;
            }
        }
    }
}

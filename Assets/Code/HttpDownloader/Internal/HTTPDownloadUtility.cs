using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UnityHTTPDownloader.Internal
{
    /// <summary>
    /// Provides checksum and retry-delay operations used by the transfer pipeline.
    /// </summary>
    internal static class HTTPDownloadUtility
    {
        /// <summary>
        /// Computes the configured checksum without blocking the calling thread.
        /// </summary>
        /// <param name="path">The file to hash.</param>
        /// <param name="algorithm">The checksum algorithm.</param>
        /// <param name="bufferSize">The read buffer size.</param>
        /// <param name="token">The cancellation token.</param>
        /// <returns>The lowercase hexadecimal checksum.</returns>
        public static async Task<string> ComputeChecksumAsync(string path,HTTPChecksumAlgorithm algorithm,
                                                               int bufferSize,CancellationToken token)
        {
            var hash = algorithm == HTTPChecksumAlgorithm.MD5 ? (HashAlgorithm)MD5.Create() : SHA256.Create();
            using (hash)
            using (var stream = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,
                                               bufferSize,FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[bufferSize];
                int read;
                while ((read = await stream.ReadAsync(buffer,0,buffer.Length,token).ConfigureAwait(false)) > 0)
                    hash.TransformBlock(buffer,0,read,null,0);
                hash.TransformFinalBlock(Array.Empty<byte>(),0,0);
                var value = new StringBuilder(hash.Hash.Length * 2);
                for (int i = 0, cnt = hash.Hash.Length; i < cnt; i++)
                    value.Append(hash.Hash[i].ToString("x2"));
                return value.ToString();
            }
        }

        /// <summary>
        /// Calculates an exponential retry delay with bounded jitter.
        /// </summary>
        /// <param name="attempt">The one-based failed attempt number.</param>
        /// <param name="baseDelay">The initial delay.</param>
        /// <param name="maxDelay">The maximum delay.</param>
        /// <returns>The delay before the next attempt.</returns>
        public static TimeSpan GetRetryDelay(int attempt,TimeSpan baseDelay,TimeSpan maxDelay)
        {
            var exponential = baseDelay.TotalMilliseconds * Math.Pow(2d,Math.Max(0,attempt - 1));
            var jitter      = new Random(unchecked(Environment.TickCount * 31 + attempt)).NextDouble() * 250d;
            return TimeSpan.FromMilliseconds(Math.Min(maxDelay.TotalMilliseconds,exponential + jitter));
        }
    }

    /// <summary>
    /// Limits the average transfer throughput of one download attempt.
    /// </summary>
    internal sealed class HTTPRateLimiter
    {
        private readonly long      mBytesPerSecond;
        private readonly Stopwatch mStopwatch = Stopwatch.StartNew();
        private long               mBytes;

        /// <summary>
        /// Creates a limiter for the supplied byte rate.
        /// </summary>
        /// <param name="bytesPerSecond">The maximum average transfer rate.</param>
        public HTTPRateLimiter(long bytesPerSecond)
        {
            mBytesPerSecond = bytesPerSecond;
        }

        /// <summary>
        /// Delays when required to keep the average transfer rate within the limit.
        /// </summary>
        /// <param name="bytes">The number of newly transferred bytes.</param>
        /// <param name="token">The cancellation token.</param>
        public async Task ThrottleAsync(int bytes,CancellationToken token)
        {
            if (mBytesPerSecond <= 0)
                return;
            mBytes += bytes;
            var expectedSeconds = (double)mBytes / mBytesPerSecond;
            var delaySeconds    = expectedSeconds - mStopwatch.Elapsed.TotalSeconds;
            if (delaySeconds > 0d)
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds),token).ConfigureAwait(false);
            if (mStopwatch.Elapsed.TotalSeconds >= 10d)
            {
                mBytes = 0;
                mStopwatch.Restart();
            }
        }
    }
}

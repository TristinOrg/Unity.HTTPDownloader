using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using UnityHTTPDownloader.Internal;

namespace UnityHTTPDownloader
{
    /// <summary>
    /// Downloads files with safe resume, bounded concurrency, retry, and validation support.
    /// </summary>
    public sealed class HTTPDownloader : IDisposable
    {
        private readonly HTTPDownloaderOptions                mOptions;
        private readonly HttpClient                            mClient;
        private readonly SemaphoreSlim                         mConcurrency;
        private readonly SynchronizationContext                mCallbackContext;
        private readonly object                                mHandleLock       = new();
        private readonly List<HTTPDownloadHandle>               mHandles          = new();
        private readonly ConcurrentDictionary<string,SemaphoreSlim> mDestinationLocks = new(StringComparer.OrdinalIgnoreCase);
        private bool                                           mDisposed;

        public IReadOnlyList<HTTPDownloadHandle> ActiveDownloads
        {
            get
            {
                lock (mHandleLock)
                    return mHandles.ToArray();
            }
        }

        /// <summary>
        /// Creates a downloader and captures the current synchronization context when configured.
        /// </summary>
        /// <param name="options">Global downloader settings.</param>
        /// <param name="handler">An optional custom HTTP handler for testing or platform integration.</param>
        public HTTPDownloader(HTTPDownloaderOptions options = null,HttpMessageHandler handler = null)
        {
            mOptions = options ?? new HTTPDownloaderOptions();
            ValidateOptions(mOptions);
            mCallbackContext = mOptions.CaptureCurrentContext ? SynchronizationContext.Current : null;
            mConcurrency     = new SemaphoreSlim(mOptions.MaxConcurrentDownloads,mOptions.MaxConcurrentDownloads);
            mClient          = handler == null ? new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.None,
                AllowAutoRedirect      = true
            }) : new HttpClient(handler);
            mClient.Timeout = Timeout.InfiniteTimeSpan;
            mClient.DefaultRequestHeaders.UserAgent.ParseAdd(mOptions.UserAgent);
        }

        /// <summary>
        /// Queues one request and returns its controllable handle immediately.
        /// </summary>
        /// <param name="request">The file download request.</param>
        /// <param name="token">An optional external cancellation token.</param>
        /// <returns>The handle that exposes state, progress, and completion.</returns>
        public HTTPDownloadHandle Download(HTTPDownloadRequest request,CancellationToken token = default)
        {
            ThrowIfDisposed();
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            ValidateRequest(request);

            var handle = new HTTPDownloadHandle(this,request);
            lock (mHandleLock)
                mHandles.Add(handle);
            handle.Completion = RunAsync(handle,token);
            return handle;
        }

        /// <summary>
        /// Downloads a collection using the configured concurrency limit.
        /// </summary>
        /// <param name="requests">The requests to queue.</param>
        /// <param name="token">An optional external cancellation token.</param>
        /// <returns>The aggregate result containing every individual result.</returns>
        public async Task<HTTPBatchDownloadResult> DownloadManyAsync(IEnumerable<HTTPDownloadRequest> requests,
                                                                     CancellationToken token = default)
        {
            if (requests == null)
                throw new ArgumentNullException(nameof(requests));
            var handles = requests.Select(item => Download(item,token)).ToArray();
            var results = await Task.WhenAll(handles.Select(item => item.Completion)).ConfigureAwait(false);
            return new HTTPBatchDownloadResult { Results = results };
        }

        /// <summary>
        /// Permanently cancels every operation owned by this downloader.
        /// </summary>
        public void CancelAll()
        {
            HTTPDownloadHandle[] handles;
            lock (mHandleLock)
                handles = mHandles.ToArray();
            for (int i = 0, cnt = handles.Length; i < cnt; i++)
                handles[i].Cancel();
        }

        /// <summary>
        /// Cancels active operations and releases HTTP and synchronization resources.
        /// </summary>
        public void Dispose()
        {
            if (mDisposed)
                return;
            mDisposed = true;
            CancelAll();
            mClient.Dispose();
        }

        /// <summary>
        /// Invokes an event directly or posts it to the captured synchronization context.
        /// </summary>
        /// <param name="action">The event invocation to dispatch.</param>
        internal void Post(Action action)
        {
            if (action == null)
                return;
            if (mCallbackContext == null || SynchronizationContext.Current == mCallbackContext)
                action();
            else
                mCallbackContext.Post(_ => action(),null);
        }

        /// <summary>
        /// Runs the full retry, pause, cancellation, and result lifecycle for one handle.
        /// </summary>
        /// <param name="handle">The operation handle.</param>
        /// <param name="externalToken">The caller cancellation token.</param>
        /// <returns>The final operation result.</returns>
        private async Task<HTTPDownloadResult> RunAsync(HTTPDownloadHandle handle,CancellationToken externalToken)
        {
            var stopwatch = Stopwatch.StartNew();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(handle.LifetimeToken,externalToken);
            var token     = linked.Token;
            var attempts  = 0;
            var resumed   = false;
            var lockTaken = false;
            var destinationLockTaken = false;
            var destinationLock = mDestinationLocks.GetOrAdd(handle.Request.DestinationPath,_ => new SemaphoreSlim(1,1));

            try
            {
                await destinationLock.WaitAsync(token).ConfigureAwait(false);
                destinationLockTaken = true;
                await mConcurrency.WaitAsync(token).ConfigureAwait(false);
                lockTaken = true;
                var maxRetries = handle.Request.MaxRetries ?? mOptions.MaxRetries;

                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (handle.State == HTTPDownloadState.Paused)
                        await handle.WaitWhilePausedAsync().ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    attempts++;
                    var attemptToken = handle.BeginAttempt(token);
                    try
                    {
                        var result = await DownloadAttemptAsync(handle,attempts,attemptToken).ConfigureAwait(false);
                        resumed |= result.WasResumed;
                        handle.SetState(HTTPDownloadState.Completed);
                        return CreateResult(handle,true,resumed,result.WasSkipped,attempts,stopwatch.Elapsed,null);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested && handle.State == HTTPDownloadState.Paused)
                    {
                        await handle.WaitWhilePausedAsync().ConfigureAwait(false);
                    }
                    catch (Exception error) when (IsTransient(error) && attempts <= maxRetries)
                    {
                        var delay = HTTPDownloadUtility.GetRetryDelay(attempts,mOptions.RetryBaseDelay,mOptions.RetryMaxDelay);
                        await Task.Delay(delay,token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException error)
            {
                handle.SetState(HTTPDownloadState.Canceled);
                CleanupPartialIfRequired(handle.Request);
                return CreateResult(handle,false,resumed,false,attempts,stopwatch.Elapsed,error);
            }
            catch (Exception error)
            {
                handle.SetState(HTTPDownloadState.Failed);
                CleanupPartialIfRequired(handle.Request);
                return CreateResult(handle,false,resumed,false,attempts,stopwatch.Elapsed,error);
            }
            finally
            {
                if (lockTaken)
                    mConcurrency.Release();
                if (destinationLockTaken)
                    destinationLock.Release();
                lock (mHandleLock)
                    mHandles.Remove(handle);
            }
        }

        /// <summary>
        /// Performs one validated HTTP transfer attempt.
        /// </summary>
        /// <param name="handle">The operation handle.</param>
        /// <param name="attempt">The one-based attempt number.</param>
        /// <param name="token">The attempt cancellation token.</param>
        /// <returns>The attempt completion details.</returns>
        private async Task<DownloadAttemptResult> DownloadAttemptAsync(HTTPDownloadHandle handle,int attempt,
                                                                        CancellationToken token)
        {
            var request      = handle.Request;
            var finalPath    = request.DestinationPath;
            var partialPath  = mOptions.UseTemporaryFile ? finalPath + ".part" : finalPath;
            var metadataPath = partialPath + ".httpmeta";
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath));

            var existing = await ProcessExistingFileAsync(handle,request,finalPath,token).ConfigureAwait(false);
            if (existing != null)
                return existing;

            if (request.ExistingFilePolicy == HTTPExistingFilePolicy.Overwrite)
                DeletePartial(partialPath,metadataPath);

            var localLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0L;
            var oldMetadata = HTTPDownloadMetadata.Load(metadataPath);
            if (oldMetadata != null && !string.Equals(oldMetadata.Url,request.Url.AbsoluteUri,StringComparison.Ordinal))
            {
                DeletePartial(partialPath,metadataPath);
                localLength = 0L;
                oldMetadata = null;
            }
            if (localLength > 0 && oldMetadata == null)
            {
                DeletePartial(partialPath,metadataPath);
                localLength = 0L;
            }

            handle.SetState(HTTPDownloadState.Connecting);
            using var message = CreateMessage(request,localLength,oldMetadata);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(request.RequestTimeout ?? mOptions.RequestTimeout);
            using var response = await mClient.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,
                                                         timeout.Token).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && localLength > 0)
            {
                var serverLength = response.Content.Headers.ContentRange?.Length;
                if (serverLength.HasValue && serverLength.Value == localLength)
                    return await FinalizeAsync(handle,request,partialPath,metadataPath,true,attempt,token).ConfigureAwait(false);
                DeletePartial(partialPath,metadataPath);
                return await DownloadAttemptAsync(handle,attempt,token).ConfigureAwait(false);
            }

            if (!response.IsSuccessStatusCode)
            {
                if (IsRetryableStatus(response.StatusCode))
                    throw new HttpRequestException($"The server returned {(int)response.StatusCode} {response.ReasonPhrase}.");
                throw new InvalidOperationException($"The server returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }
            var isPartial = response.StatusCode == HttpStatusCode.PartialContent && localLength > 0;
            if (localLength > 0 && !isPartial)
            {
                DeletePartial(partialPath,metadataPath);
                localLength = 0;
            }

            var responseLength = response.Content.Headers.ContentLength;
            var totalLength    = response.Content.Headers.ContentRange?.Length ??
                                 (responseLength.HasValue ? responseLength.Value + localLength : (long?)null);
            ValidateExpectedSize(request,totalLength);
            EnsureDiskSpace(finalPath,totalLength,localLength);

            var metadata = new HTTPDownloadMetadata
            {
                Url          = request.Url.AbsoluteUri,
                ETag         = response.Headers.ETag?.ToString(),
                LastModified = response.Content.Headers.LastModified?.ToString("R"),
                TotalLength  = totalLength
            };
            metadata.Save(metadataPath);

            var fileMode = isPartial ? FileMode.Append : FileMode.Create;
            using (var network = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var file = new FileStream(partialPath,fileMode,FileAccess.Write,FileShare.Read,
                                             mOptions.BufferSize,FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await CopyAsync(handle,network,file,localLength,totalLength,attempt,request.BytesPerSecondLimit ??
                                mOptions.BytesPerSecondLimit,token).ConfigureAwait(false);
                await file.FlushAsync(token).ConfigureAwait(false);
            }

            return await FinalizeAsync(handle,request,partialPath,metadataPath,isPartial,attempt,token).ConfigureAwait(false);
        }

        /// <summary>
        /// Copies the response stream to disk while reporting metrics and enforcing a rate limit.
        /// </summary>
        /// <param name="handle">The operation handle.</param>
        /// <param name="source">The HTTP response stream.</param>
        /// <param name="destination">The partial file stream.</param>
        /// <param name="initialBytes">The number of bytes already stored.</param>
        /// <param name="totalBytes">The known total size, when available.</param>
        /// <param name="attempt">The one-based attempt number.</param>
        /// <param name="speedLimit">The optional byte-per-second limit.</param>
        /// <param name="token">The attempt cancellation token.</param>
        private async Task CopyAsync(HTTPDownloadHandle handle,Stream source,Stream destination,long initialBytes,
                                     long? totalBytes,int attempt,long? speedLimit,CancellationToken token)
        {
            var buffer        = new byte[mOptions.BufferSize];
            var downloaded    = initialBytes;
            var sampleBytes   = initialBytes;
            var speedWatch    = Stopwatch.StartNew();
            var progressWatch = Stopwatch.StartNew();
            var rateLimiter   = speedLimit.HasValue ? new HTTPRateLimiter(speedLimit.Value) : null;
            handle.SetState(HTTPDownloadState.Downloading);

            while (true)
            {
                var read = await source.ReadAsync(buffer,0,buffer.Length,token).ConfigureAwait(false);
                if (read <= 0)
                    break;
                await destination.WriteAsync(buffer,0,read,token).ConfigureAwait(false);
                downloaded += read;
                if (rateLimiter != null)
                    await rateLimiter.ThrottleAsync(read,token).ConfigureAwait(false);

                if (progressWatch.Elapsed < mOptions.ProgressInterval)
                    continue;
                var elapsed = Math.Max(0.001d,speedWatch.Elapsed.TotalSeconds);
                var speed   = (downloaded - sampleBytes) / elapsed;
                ReportProgress(handle,downloaded,totalBytes,speed,attempt);
                sampleBytes = downloaded;
                speedWatch.Restart();
                progressWatch.Restart();
            }
            ReportProgress(handle,downloaded,totalBytes,0d,attempt);
        }

        /// <summary>
        /// Validates the completed partial file and atomically promotes it to the destination.
        /// </summary>
        /// <param name="handle">The operation handle.</param>
        /// <param name="request">The download request.</param>
        /// <param name="partialPath">The partial file path.</param>
        /// <param name="metadataPath">The resume metadata path.</param>
        /// <param name="wasResumed">Whether this attempt appended remote bytes.</param>
        /// <param name="attempt">The one-based attempt number.</param>
        /// <param name="token">The cancellation token.</param>
        /// <returns>The completion details.</returns>
        private async Task<DownloadAttemptResult> FinalizeAsync(HTTPDownloadHandle handle,HTTPDownloadRequest request,
                                                                 string partialPath,string metadataPath,bool wasResumed,
                                                                 int attempt,CancellationToken token)
        {
            if (!File.Exists(partialPath))
                throw new FileNotFoundException("The partial download file is missing.",partialPath);
            var size = new FileInfo(partialPath).Length;
            if (request.ExpectedSize.HasValue && size != request.ExpectedSize.Value)
                throw new InvalidDataException($"File size mismatch. Expected {request.ExpectedSize.Value}, received {size}.");
            if (request.ChecksumAlgorithm != HTTPChecksumAlgorithm.None)
            {
                handle.SetState(HTTPDownloadState.Verifying);
                var actual = await HTTPDownloadUtility.ComputeChecksumAsync(partialPath,request.ChecksumAlgorithm,
                                                                             mOptions.BufferSize,token).ConfigureAwait(false);
                if (!string.Equals(actual,NormalizeChecksum(request.ExpectedChecksum),StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{request.ChecksumAlgorithm} checksum mismatch. Expected " +
                                                   $"{request.ExpectedChecksum}, received {actual}.");
            }
            if (!string.Equals(partialPath,request.DestinationPath,StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(request.DestinationPath))
                    File.Delete(request.DestinationPath);
                File.Move(partialPath,request.DestinationPath);
            }
            if (File.Exists(metadataPath))
                File.Delete(metadataPath);
            ReportProgress(handle,size,size,0d,attempt);
            return new DownloadAttemptResult { WasResumed = wasResumed };
        }

        /// <summary>
        /// Applies the selected policy to an existing final destination file.
        /// </summary>
        /// <param name="handle">The operation handle.</param>
        /// <param name="request">The download request.</param>
        /// <param name="path">The final destination path.</param>
        /// <param name="token">The cancellation token.</param>
        /// <returns>A completed result when the network can be skipped, otherwise null.</returns>
        private async Task<DownloadAttemptResult> ProcessExistingFileAsync(HTTPDownloadHandle handle,
                                                                            HTTPDownloadRequest request,string path,
                                                                            CancellationToken token)
        {
            if (!File.Exists(path))
                return null;
            if (request.ExistingFilePolicy == HTTPExistingFilePolicy.Fail)
                throw new InvalidOperationException($"Destination file already exists: {path}");
            if (request.ExistingFilePolicy == HTTPExistingFilePolicy.Overwrite)
                return null;

            var valid = !request.ExpectedSize.HasValue || new FileInfo(path).Length == request.ExpectedSize.Value;
            if (valid && request.ChecksumAlgorithm != HTTPChecksumAlgorithm.None)
            {
                handle.SetState(HTTPDownloadState.Verifying);
                var actual = await HTTPDownloadUtility.ComputeChecksumAsync(path,request.ChecksumAlgorithm,
                                                                             mOptions.BufferSize,token).ConfigureAwait(false);
                valid = string.Equals(actual,NormalizeChecksum(request.ExpectedChecksum),StringComparison.OrdinalIgnoreCase);
            }
            if (valid && request.ExistingFilePolicy == HTTPExistingFilePolicy.SkipIfValid)
            {
                var size = new FileInfo(path).Length;
                ReportProgress(handle,size,size,0d,0);
                return new DownloadAttemptResult { WasSkipped = true };
            }
            if (request.ExistingFilePolicy == HTTPExistingFilePolicy.Resume)
            {
                var partialPath = mOptions.UseTemporaryFile ? path + ".part" : path;
                if (!string.Equals(partialPath,path,StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(partialPath))
                        File.Delete(partialPath);
                    File.Move(path,partialPath);
                }
                return null;
            }
            return null;
        }

        /// <summary>
        /// Creates a GET request with custom headers and conditional range headers when possible.
        /// </summary>
        /// <param name="request">The download request.</param>
        /// <param name="offset">The local byte offset.</param>
        /// <param name="metadata">Persisted remote validators.</param>
        /// <returns>The configured HTTP request message.</returns>
        private static HttpRequestMessage CreateMessage(HTTPDownloadRequest request,long offset,
                                                         HTTPDownloadMetadata metadata)
        {
            var message = new HttpRequestMessage(HttpMethod.Get,request.Url);
            foreach (var header in request.Headers)
                if (!message.Headers.TryAddWithoutValidation(header.Key,header.Value))
                    message.Content?.Headers.TryAddWithoutValidation(header.Key,header.Value);
            if (offset > 0)
            {
                message.Headers.Range = new RangeHeaderValue(offset,null);
                if (!string.IsNullOrEmpty(metadata?.ETag) && EntityTagHeaderValue.TryParse(metadata.ETag,out var tag))
                    message.Headers.IfRange = new RangeConditionHeaderValue(tag);
                else if (DateTimeOffset.TryParse(metadata?.LastModified,out var modified))
                    message.Headers.IfRange = new RangeConditionHeaderValue(modified);
            }
            return message;
        }

        /// <summary>
        /// Publishes a new immutable progress snapshot.
        /// </summary>
        /// <param name="handle">The reporting handle.</param>
        /// <param name="bytes">The downloaded byte count.</param>
        /// <param name="total">The optional total byte count.</param>
        /// <param name="speed">The recent transfer rate.</param>
        /// <param name="attempt">The one-based attempt number.</param>
        private static void ReportProgress(HTTPDownloadHandle handle,long bytes,long? total,double speed,int attempt)
        {
            handle.SetProgress(new HTTPDownloadProgress
            {
                State              = handle.State,
                BytesDownloaded    = bytes,
                TotalBytes         = total,
                BytesPerSecond     = speed,
                EstimatedRemaining = total.HasValue && speed > 0d
                                   ? TimeSpan.FromSeconds(Math.Max(0d,(total.Value - bytes) / speed))
                                   : (TimeSpan?)null,
                Attempt            = attempt
            });
        }

        /// <summary>
        /// Creates the public terminal result for one operation.
        /// </summary>
        /// <param name="handle">The completed handle.</param>
        /// <param name="success">Whether the operation succeeded.</param>
        /// <param name="resumed">Whether any attempt resumed partial data.</param>
        /// <param name="skipped">Whether an existing valid file was reused.</param>
        /// <param name="attempts">The number of attempts.</param>
        /// <param name="duration">The total operation duration.</param>
        /// <param name="error">The terminal error, when present.</param>
        /// <returns>The public result.</returns>
        private static HTTPDownloadResult CreateResult(HTTPDownloadHandle handle,bool success,bool resumed,bool skipped,
                                                        int attempts,TimeSpan duration,Exception error)
        {
            var path = handle.Request.DestinationPath;
            return new HTTPDownloadResult
            {
                IsSuccess       = success,
                WasResumed      = resumed,
                WasSkipped      = skipped,
                DestinationPath = path,
                FileSize        = File.Exists(path) ? new FileInfo(path).Length : 0L,
                Attempts        = attempts,
                Duration        = duration,
                Error           = error,
                State           = handle.State
            };
        }

        /// <summary>
        /// Determines whether an exception is eligible for automatic retry.
        /// </summary>
        /// <param name="error">The attempt error.</param>
        /// <returns>True for transient network and I/O failures.</returns>
        private static bool IsTransient(Exception error)
        {
            if (error is InvalidDataException)
                return false;
            if (error is HttpRequestException || error is IOException || error is TaskCanceledException)
                return true;
            return false;
        }

        /// <summary>
        /// Determines whether an HTTP response status is eligible for automatic retry.
        /// </summary>
        /// <param name="status">The HTTP response status.</param>
        /// <returns>True for timeout, throttling, and server-error responses.</returns>
        private static bool IsRetryableStatus(HttpStatusCode status)
        {
            var code = (int)status;
            return status == HttpStatusCode.RequestTimeout || code == 429 || code >= 500 && code <= 599;
        }

        /// <summary>
        /// Removes partial state when the request opts out of retaining failed data.
        /// </summary>
        /// <param name="request">The failed or canceled request.</param>
        private void CleanupPartialIfRequired(HTTPDownloadRequest request)
        {
            if (request.KeepPartialFileOnError)
                return;
            var partialPath = mOptions.UseTemporaryFile ? request.DestinationPath + ".part" : request.DestinationPath;
            DeletePartial(partialPath,partialPath + ".httpmeta");
        }

        /// <summary>
        /// Deletes a partial file and its metadata when they exist.
        /// </summary>
        /// <param name="partialPath">The partial file path.</param>
        /// <param name="metadataPath">The metadata file path.</param>
        private static void DeletePartial(string partialPath,string metadataPath)
        {
            if (File.Exists(partialPath))
                File.Delete(partialPath);
            if (File.Exists(metadataPath))
                File.Delete(metadataPath);
        }

        /// <summary>
        /// Rejects a server length that conflicts with an explicitly expected size.
        /// </summary>
        /// <param name="request">The download request.</param>
        /// <param name="serverSize">The server-reported total size.</param>
        private static void ValidateExpectedSize(HTTPDownloadRequest request,long? serverSize)
        {
            if (request.ExpectedSize.HasValue && serverSize.HasValue && request.ExpectedSize.Value != serverSize.Value)
                throw new InvalidDataException($"Server file size mismatch. Expected {request.ExpectedSize.Value}, " +
                                               $"server reported {serverSize.Value}.");
        }

        /// <summary>
        /// Ensures the destination volume has enough available storage for remaining bytes.
        /// </summary>
        /// <param name="destination">The final destination path.</param>
        /// <param name="total">The known total byte count.</param>
        /// <param name="existing">The byte count already stored.</param>
        private static void EnsureDiskSpace(string destination,long? total,long existing)
        {
            if (!total.HasValue)
                return;
            var root = Path.GetPathRoot(destination);
            if (string.IsNullOrEmpty(root))
                return;
            var drive    = new DriveInfo(root);
            var required = Math.Max(0L,total.Value - existing);
            if (drive.AvailableFreeSpace < required)
                throw new InvalidOperationException($"Not enough disk space. Required {required} bytes, available " +
                                                    $"{drive.AvailableFreeSpace} bytes.");
        }

        /// <summary>
        /// Normalizes user-provided checksum text for ordinal comparison.
        /// </summary>
        /// <param name="checksum">The source checksum text.</param>
        /// <returns>The normalized checksum.</returns>
        private static string NormalizeChecksum(string checksum)
        {
            return checksum?.Replace("-",string.Empty).Trim() ?? string.Empty;
        }

        /// <summary>
        /// Validates global downloader settings before resources are allocated.
        /// </summary>
        /// <param name="options">The options to validate.</param>
        private static void ValidateOptions(HTTPDownloaderOptions options)
        {
            if (options.MaxConcurrentDownloads <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.MaxConcurrentDownloads));
            if (options.MaxRetries < 0)
                throw new ArgumentOutOfRangeException(nameof(options.MaxRetries));
            if (options.BufferSize < 4096)
                throw new ArgumentOutOfRangeException(nameof(options.BufferSize));
            if (options.RequestTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options.RequestTimeout));
        }

        /// <summary>
        /// Validates request-level overrides and checksum requirements.
        /// </summary>
        /// <param name="request">The request to validate.</param>
        private static void ValidateRequest(HTTPDownloadRequest request)
        {
            if (request.MaxRetries < 0)
                throw new ArgumentOutOfRangeException(nameof(request.MaxRetries));
            if (request.RequestTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(request.RequestTimeout));
            if (request.BytesPerSecondLimit <= 0)
                throw new ArgumentOutOfRangeException(nameof(request.BytesPerSecondLimit));
            if (request.ChecksumAlgorithm != HTTPChecksumAlgorithm.None && string.IsNullOrWhiteSpace(request.ExpectedChecksum))
                throw new ArgumentException("ExpectedChecksum is required when checksum validation is enabled.");
        }

        /// <summary>
        /// Throws when a public operation is attempted after disposal.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (mDisposed)
                throw new ObjectDisposedException(nameof(HTTPDownloader));
        }

        /// <summary>
        /// Carries internal completion details from one successful attempt.
        /// </summary>
        private sealed class DownloadAttemptResult
        {
            public bool WasResumed { get; set; }
            public bool WasSkipped { get; set; }
        }
    }
}

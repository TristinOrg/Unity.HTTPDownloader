using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace UnityHTTPDownloader.Tests
{
    /// <summary>
    /// Verifies download completion, resume safety, validation, retry, and existing-file behavior.
    /// </summary>
    public sealed class HTTPDownloaderTests
    {
        private string mDirectory;

        /// <summary>
        /// Creates an isolated temporary directory for each test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            mDirectory = Path.Combine(Path.GetTempPath(),"UnityHTTPDownloaderTests",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(mDirectory);
        }

        /// <summary>
        /// Removes all files created by the completed test.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(mDirectory))
                Directory.Delete(mDirectory,true);
        }

        /// <summary>
        /// Verifies that a complete response is atomically written to the destination.
        /// </summary>
        [Test]
        public async Task Download_CompletesFileAtomically()
        {
            var data    = Encoding.UTF8.GetBytes("complete download payload");
            var handler = new DelegateHandler((_,__) => CreateResponse(data,HttpStatusCode.OK));
            using var downloader = CreateDownloader(handler);
            var path    = Path.Combine(mDirectory,"complete.bin");
            var handle  = downloader.Download(new HTTPDownloadRequest("https://example.test/complete.bin",path));
            var result  = await handle.Completion;

            Assert.That(result.IsSuccess,Is.True,result.Error?.ToString());
            Assert.That(File.ReadAllBytes(path),Is.EqualTo(data));
            Assert.That(File.Exists(path + ".part"),Is.False);
            Assert.That(result.State,Is.EqualTo(HTTPDownloadState.Completed));
        }

        /// <summary>
        /// Verifies that matching metadata produces a conditional byte-range request.
        /// </summary>
        [Test]
        public async Task Download_ResumesWithRangeAndValidator()
        {
            var data       = Encoding.UTF8.GetBytes("0123456789abcdefghijklmnopqrstuvwxyz");
            var initial    = 11;
            var path       = Path.Combine(mDirectory,"resume.bin");
            var partial    = path + ".part";
            var metadata   = partial + ".httpmeta";
            var url        = "https://example.test/resume.bin";
            var etag       = "\"version-1\"";
            var rangeStart = -1L;
            File.WriteAllBytes(partial,data.Take(initial).ToArray());
            WriteMetadata(metadata,url,etag,data.Length);

            var handler = new DelegateHandler((request,__) =>
            {
                rangeStart = request.Headers.Range?.Ranges.First().From ?? -1L;
                Assert.That(request.Headers.IfRange?.EntityTag?.ToString(),Is.EqualTo(etag));
                var response = CreateResponse(data.Skip((int)rangeStart).ToArray(),HttpStatusCode.PartialContent);
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(rangeStart,data.Length - 1,data.Length);
                return response;
            });
            using var downloader = CreateDownloader(handler);
            var result = await downloader.Download(new HTTPDownloadRequest(url,path)).Completion;

            Assert.That(result.IsSuccess,Is.True,result.Error?.ToString());
            Assert.That(result.WasResumed,Is.True);
            Assert.That(rangeStart,Is.EqualTo(initial));
            Assert.That(File.ReadAllBytes(path),Is.EqualTo(data));
        }

        /// <summary>
        /// Verifies that a transient server response is retried and later succeeds.
        /// </summary>
        [Test]
        public async Task Download_RetriesTransientServerFailure()
        {
            var attempts = 0;
            var data     = Encoding.UTF8.GetBytes("retry payload");
            var handler  = new DelegateHandler((_,__) =>
            {
                attempts++;
                return attempts == 1 ? CreateResponse(Array.Empty<byte>(),HttpStatusCode.ServiceUnavailable)
                                     : CreateResponse(data,HttpStatusCode.OK);
            });
            using var downloader = CreateDownloader(handler,TimeSpan.Zero);
            var request = new HTTPDownloadRequest("https://example.test/retry.bin",Path.Combine(mDirectory,"retry.bin"))
            {
                MaxRetries = 1
            };
            var result = await downloader.Download(request).Completion;

            Assert.That(result.IsSuccess,Is.True,result.Error?.ToString());
            Assert.That(result.Attempts,Is.EqualTo(2));
            Assert.That(attempts,Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies that checksum mismatch fails without promoting the partial file.
        /// </summary>
        [Test]
        public async Task Download_FailsInvalidChecksum()
        {
            var data = Encoding.UTF8.GetBytes("invalid checksum payload");
            using var downloader = CreateDownloader(new DelegateHandler((_,__) => CreateResponse(data,HttpStatusCode.OK)));
            var path    = Path.Combine(mDirectory,"checksum.bin");
            var request = new HTTPDownloadRequest("https://example.test/checksum.bin",path)
            {
                ChecksumAlgorithm = HTTPChecksumAlgorithm.SHA256,
                ExpectedChecksum  = new string('0',64),
                MaxRetries        = 3
            };
            var result = await downloader.Download(request).Completion;

            Assert.That(result.IsSuccess,Is.False);
            Assert.That(result.Attempts,Is.EqualTo(1));
            Assert.That(result.Error,Is.TypeOf<InvalidDataException>());
            Assert.That(File.Exists(path),Is.False);
            Assert.That(File.Exists(path + ".part"),Is.True);
        }

        /// <summary>
        /// Verifies that a valid existing file is reused without sending a request.
        /// </summary>
        [Test]
        public async Task Download_SkipsValidExistingFile()
        {
            var data     = Encoding.UTF8.GetBytes("already downloaded");
            var path     = Path.Combine(mDirectory,"existing.bin");
            var requests = 0;
            File.WriteAllBytes(path,data);
            var handler = new DelegateHandler((_,__) =>
            {
                requests++;
                return CreateResponse(data,HttpStatusCode.OK);
            });
            using var downloader = CreateDownloader(handler);
            var request = new HTTPDownloadRequest("https://example.test/existing.bin",path)
            {
                ExistingFilePolicy = HTTPExistingFilePolicy.SkipIfValid,
                ExpectedSize       = data.Length
            };
            var result = await downloader.Download(request).Completion;

            Assert.That(result.IsSuccess,Is.True,result.Error?.ToString());
            Assert.That(result.WasSkipped,Is.True);
            Assert.That(requests,Is.Zero);
        }

        /// <summary>
        /// Creates a downloader configured for deterministic and fast tests.
        /// </summary>
        /// <param name="handler">The fake HTTP handler.</param>
        /// <param name="retryDelay">An optional retry delay override.</param>
        /// <returns>The configured downloader.</returns>
        private static HTTPDownloader CreateDownloader(HttpMessageHandler handler,TimeSpan? retryDelay = null)
        {
            return new HTTPDownloader(new HTTPDownloaderOptions
            {
                CaptureCurrentContext = false,
                MaxRetries            = 2,
                RetryBaseDelay        = retryDelay ?? TimeSpan.FromMilliseconds(1),
                RetryMaxDelay         = retryDelay ?? TimeSpan.FromMilliseconds(1)
            },handler);
        }

        /// <summary>
        /// Creates a response containing the supplied bytes and status.
        /// </summary>
        /// <param name="data">The response body.</param>
        /// <param name="status">The response status.</param>
        /// <returns>The HTTP response.</returns>
        private static HttpResponseMessage CreateResponse(byte[] data,HttpStatusCode status)
        {
            return new HttpResponseMessage(status) { Content = new ByteArrayContent(data) };
        }

        /// <summary>
        /// Writes resume metadata in the same portable representation as the runtime.
        /// </summary>
        /// <param name="path">The metadata path.</param>
        /// <param name="url">The remote URL.</param>
        /// <param name="etag">The remote ETag.</param>
        /// <param name="length">The total remote length.</param>
        private static void WriteMetadata(string path,string url,string etag,long length)
        {
            var values = new[] { url,etag,string.Empty };
            var lines  = values.Select(item => Convert.ToBase64String(Encoding.UTF8.GetBytes(item)))
                               .Concat(new[] { length.ToString() });
            File.WriteAllLines(path,lines,Encoding.UTF8);
        }

        /// <summary>
        /// Routes HTTP requests to a test-provided synchronous response factory.
        /// </summary>
        private sealed class DelegateHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage,CancellationToken,HttpResponseMessage> mHandler;

            /// <summary>
            /// Creates a handler backed by the supplied response factory.
            /// </summary>
            /// <param name="handler">The response factory.</param>
            public DelegateHandler(Func<HttpRequestMessage,CancellationToken,HttpResponseMessage> handler)
            {
                mHandler = handler;
            }

            /// <summary>
            /// Produces the configured fake response for one HTTP request.
            /// </summary>
            /// <param name="request">The HTTP request.</param>
            /// <param name="cancellationToken">The cancellation token.</param>
            /// <returns>The fake response task.</returns>
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                                    CancellationToken cancellationToken)
            {
                return Task.FromResult(mHandler(request,cancellationToken));
            }
        }
    }
}

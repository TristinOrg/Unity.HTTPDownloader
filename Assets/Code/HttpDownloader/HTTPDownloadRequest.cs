using System;
using System.Collections.Generic;
using System.IO;

namespace UnityHTTPDownloader
{
    /// <summary>
    /// Configures one remote file, local destination, and validation policy.
    /// </summary>
    public sealed class HTTPDownloadRequest
    {
        public Uri                       Url                    { get; }
        public string                    DestinationPath        { get; }
        public Dictionary<string,string> Headers                { get; } = new();
        public HTTPExistingFilePolicy    ExistingFilePolicy     { get; set; } = HTTPExistingFilePolicy.Resume;
        public HTTPChecksumAlgorithm     ChecksumAlgorithm      { get; set; }
        public string                    ExpectedChecksum       { get; set; }
        public long?                     ExpectedSize           { get; set; }
        public int?                      MaxRetries              { get; set; }
        public TimeSpan?                 RequestTimeout         { get; set; }
        public long?                     BytesPerSecondLimit    { get; set; }
        public bool                      KeepPartialFileOnError { get; set; } = true;
        public object                    UserData               { get; set; }

        /// <summary>
        /// Creates and validates a request for one remote file and local destination.
        /// </summary>
        /// <param name="url">The absolute HTTP or HTTPS URL.</param>
        /// <param name="destinationPath">The final local file path.</param>
        public HTTPDownloadRequest(string url,string destinationPath)
        {
            if (!Uri.TryCreate(url,UriKind.Absolute,out Uri uri))
                throw new ArgumentException("A valid absolute HTTP or HTTPS URL is required.",nameof(url));
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("Only HTTP and HTTPS URLs are supported.",nameof(url));
            if (string.IsNullOrWhiteSpace(destinationPath))
                throw new ArgumentException("A destination path is required.",nameof(destinationPath));

            Url             = uri;
            DestinationPath = Path.GetFullPath(destinationPath);
        }

        /// <summary>
        /// Adds or replaces an HTTP request header and returns this request.
        /// </summary>
        /// <param name="name">The header name.</param>
        /// <param name="value">The header value.</param>
        /// <returns>This request for fluent configuration.</returns>
        public HTTPDownloadRequest WithHeader(string name,string value)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A header name is required.",nameof(name));
            Headers[name] = value ?? string.Empty;
            return this;
        }
    }

    /// <summary>
    /// Configures shared defaults and resource limits for one downloader instance.
    /// </summary>
    public sealed class HTTPDownloaderOptions
    {
        public int       MaxConcurrentDownloads { get; set; } = 4;
        public int       MaxRetries             { get; set; } = 3;
        public int       BufferSize             { get; set; } = 128 * 1024;
        public TimeSpan  RequestTimeout         { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan  ProgressInterval       { get; set; } = TimeSpan.FromMilliseconds(200);
        public TimeSpan  RetryBaseDelay         { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan  RetryMaxDelay          { get; set; } = TimeSpan.FromSeconds(30);
        public long?     BytesPerSecondLimit    { get; set; }
        public bool      UseTemporaryFile       { get; set; } = true;
        public bool      CaptureCurrentContext  { get; set; } = true;
        public string    UserAgent              { get; set; } = "Unity-HTTPDownloader/2.0";
    }
}

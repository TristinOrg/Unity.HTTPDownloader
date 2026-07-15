# Unity HTTP Downloader

A production-oriented HTTP/HTTPS file downloader for Unity with safe resume support, bounded concurrency, retries, validation, pause/resume controls, bandwidth limiting, and main-thread event dispatch.

## Requirements

- Unity 2021.3 LTS or newer
- API Compatibility Level: `.NET Standard 2.1` or `.NET Framework`
- HTTP/HTTPS endpoints that support standard byte-range requests for resume support

The repository originally targeted Unity 2019.3. The rewritten source intentionally uses target-typed `new()` expressions and therefore requires a newer Unity C# compiler.

## Features

- Resumable downloads using `Range` and `If-Range`
- Persistent `.httpmeta` validators using ETag or Last-Modified
- Safe fallback when a server ignores byte ranges
- Atomic `.part` file completion
- Pause, resume, and cancellation
- Configurable concurrent download limit
- Exponential retry delay with jitter
- Per-request timeout and retry overrides
- MD5 and SHA-256 validation
- Expected file-size validation
- Existing-file policies: resume, overwrite, validate-and-skip, or fail
- Per-request or downloader-wide bandwidth limits
- Progress, transfer speed, attempt count, and ETA snapshots
- Batch downloads with individual results
- Custom request headers for authorization, cookies, or CDN parameters
- Free disk-space check before transfer
- Redirect support
- Unity main-thread event dispatch when created on the main thread
- Automatic lifecycle management through `HTTPDownloaderBehaviour`
- No `Thread.Abort`, polling timer, coroutine, or mandatory `MonoBehaviour`

## Quick start

```csharp
using System.IO;
using UnityEngine;
using UnityHTTPDownloader;

public sealed class PatchDownloader : MonoBehaviour
{
    private HTTPDownloader mDownloader;

    private void Awake()
    {
        mDownloader = new HTTPDownloader(new HTTPDownloaderOptions
        {
            MaxConcurrentDownloads = 4,
            MaxRetries             = 3
        });
    }

    public async void DownloadPatch()
    {
        string path = Path.Combine(Application.persistentDataPath,"patch.bundle");
        HTTPDownloadRequest request = new("https://cdn.example.com/patch.bundle",path)
        {
            ExistingFilePolicy = HTTPExistingFilePolicy.Resume,
            ChecksumAlgorithm  = HTTPChecksumAlgorithm.SHA256,
            ExpectedChecksum   = "YOUR_LOWER_OR_UPPER_CASE_SHA256"
        };

        HTTPDownloadHandle handle = mDownloader.Download(request);
        handle.ProgressChanged += (_,progress) =>
        {
            Debug.Log($"{progress.Progress:P1} - {progress.BytesPerSecond / 1024d:F1} KiB/s");
        };

        HTTPDownloadResult result = await handle.Completion;
        if (!result.IsSuccess)
            Debug.LogError(result.Error);
    }

    private void OnDestroy()
    {
        mDownloader.Dispose();
    }
}
```

Create the downloader on Unity's main thread if event handlers access Unity APIs. Progress and state events are then posted back to that captured synchronization context.

## Pause, resume, and cancel

```csharp
HTTPDownloadHandle handle = downloader.Download(request);

handle.Pause();
handle.Resume();
handle.Cancel();

HTTPDownloadResult result = await handle.Completion;
```

Pausing cancels the active network read but keeps the `.part` file and `.httpmeta` file. Resuming sends a new validated byte-range request. Canceling is permanent. Partial files are kept by default so a new request can resume later.

## Batch downloads

```csharp
HTTPDownloadRequest[] requests =
{
    new("https://cdn.example.com/a.bundle",Path.Combine(root,"a.bundle")),
    new("https://cdn.example.com/b.bundle",Path.Combine(root,"b.bundle")),
    new("https://cdn.example.com/c.bundle",Path.Combine(root,"c.bundle"))
};

HTTPBatchDownloadResult batch = await downloader.DownloadManyAsync(requests);
Debug.Log($"Batch successful: {batch.IsSuccess}");
```

`MaxConcurrentDownloads` bounds the number of active transfers. Remaining items wait asynchronously without creating dedicated threads.

## Authorization and custom headers

```csharp
HTTPDownloadRequest request = new(url,path)
    .WithHeader("Authorization","Bearer " + accessToken)
    .WithHeader("X-Client-Version",Application.version);
```

Never log requests containing secrets. Header values remain in memory for the lifetime of the request.

## Existing-file policies

| Policy | Behavior |
| --- | --- |
| `Resume` | Continues a `.part` file when matching persisted validators are available. |
| `Overwrite` | Deletes partial state and starts from byte zero. |
| `SkipIfValid` | Skips an existing final file only when configured size/checksum rules pass. |
| `Fail` | Returns a failed result if the final destination exists. |

For `SkipIfValid`, provide `ExpectedSize`, a checksum, or both. Without validation data, any existing file is considered valid.

## Temporary files and resume metadata

With `UseTemporaryFile = true` (the default), a destination such as `patch.bundle` uses:

- `patch.bundle.part` for incomplete bytes
- `patch.bundle.part.httpmeta` for URL, ETag, Last-Modified, and known total size
- `patch.bundle` only after validation succeeds

The metadata file prevents unsafe concatenation when the remote resource changes. A partial file without matching metadata is restarted from byte zero. If the server rejects the range or returns a complete response, the downloader also restarts safely.

## Configuration

```csharp
HTTPDownloaderOptions options = new()
{
    MaxConcurrentDownloads = 4,
    MaxRetries             = 3,
    BufferSize             = 128 * 1024,
    RequestTimeout         = TimeSpan.FromSeconds(30),
    ProgressInterval       = TimeSpan.FromMilliseconds(200),
    RetryBaseDelay         = TimeSpan.FromSeconds(1),
    RetryMaxDelay          = TimeSpan.FromSeconds(30),
    BytesPerSecondLimit    = null,
    UseTemporaryFile       = true,
    CaptureCurrentContext  = true
};
```

Each `HTTPDownloadRequest` can override retry count, timeout, and bandwidth limit.

## Error handling

Normal transfer failures are returned in `HTTPDownloadResult`; invalid API arguments throw immediately. Always inspect both `IsSuccess` and `Error`.

Retries are intended for network and I/O failures. Validation failures, bad arguments, insufficient storage, and cancellation are not silently converted into successful downloads.

## Platform notes

- Desktop, Android, and iOS use `HttpClient` and the platform networking stack.
- WebGL does not expose normal filesystem and streaming HTTP behavior, so this downloader is not intended for WebGL builds.
- A server must return `206 Partial Content` for a range request. If it returns `200 OK`, the downloader safely truncates and restarts.
- Mobile operating systems may suspend the app. The persisted partial file allows a later application session to continue.

## Migration from 1.x

The original global classes (`HTTPTask`, `HTTPRequest`, `HTTPDownloadIndie`, and `HTTPDownloadBatch`) relied on raw threads and `Thread.Abort` and have been removed. Replace them with one long-lived `HTTPDownloader`, one `HTTPDownloadRequest` per file, and the returned `HTTPDownloadHandle`.

Old:

```csharp
HTTPParamIndie parameter = new HTTPParamIndie(url,path,md5);
HTTPDownloadIndie download = new HTTPDownloadIndie(parameter);
download.DownLoad();
```

New:

```csharp
HTTPDownloadRequest request = new(url,path)
{
    ChecksumAlgorithm = HTTPChecksumAlgorithm.MD5,
    ExpectedChecksum  = md5
};
HTTPDownloadHandle handle = downloader.Download(request);
```

## License

MIT. See [LICENSE](LICENSE).

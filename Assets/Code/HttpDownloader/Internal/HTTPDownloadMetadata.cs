using System;
using System.IO;
using System.Text;

namespace UnityHTTPDownloader.Internal
{
    /// <summary>
    /// Persists remote validators required for safe cross-session range requests.
    /// </summary>
    internal sealed class HTTPDownloadMetadata
    {
        public string Url          { get; set; }
        public string ETag         { get; set; }
        public string LastModified { get; set; }
        public long?  TotalLength  { get; set; }

        /// <summary>
        /// Loads persisted resume metadata or returns null when it is unavailable or invalid.
        /// </summary>
        /// <param name="path">The metadata file path.</param>
        /// <returns>The parsed metadata, or null.</returns>
        public static HTTPDownloadMetadata Load(string path)
        {
            if (!File.Exists(path))
                return null;
            try
            {
                var lines = File.ReadAllLines(path,Encoding.UTF8);
                if (lines.Length < 4)
                    return null;
                return new HTTPDownloadMetadata
                {
                    Url          = Decode(lines[0]),
                    ETag         = Decode(lines[1]),
                    LastModified = Decode(lines[2]),
                    TotalLength  = long.TryParse(lines[3],out var size) ? size : (long?)null
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Persists this metadata using an encoding safe for arbitrary header values.
        /// </summary>
        /// <param name="path">The metadata file path.</param>
        public void Save(string path)
        {
            var content = string.Join(Environment.NewLine,
                                         Encode(Url),
                                         Encode(ETag),
                                         Encode(LastModified),
                                         TotalLength?.ToString() ?? string.Empty);
            File.WriteAllText(path,content,Encoding.UTF8);
        }

        /// <summary>
        /// Converts a metadata value to Base64.
        /// </summary>
        /// <param name="value">The source value.</param>
        /// <returns>The encoded value.</returns>
        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        /// <summary>
        /// Converts a Base64 metadata value to text.
        /// </summary>
        /// <param name="value">The encoded value.</param>
        /// <returns>The decoded value.</returns>
        private static string Decode(string value)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
    }
}

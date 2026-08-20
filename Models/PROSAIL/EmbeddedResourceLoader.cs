using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Models.PROSAIL
{
    /// <summary>Helper for reading bundled PROSAIL data files (spectral constants, sensor SRFs) that are compiled into Models.dll as EmbeddedResource.</summary>
    internal static class EmbeddedResourceLoader
    {
        /// <summary>Reads the full text of an embedded resource.</summary>
        /// <param name="resourceName">Fully-qualified resource name (e.g. "Models.PROSAIL.InputProperties.SpectralData.SpecSOIL.json").</param>
        public static string ReadText(string resourceName)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException(
                        $"Embedded resource '{resourceName}' not found. Ensure the file is included as an EmbeddedResource in Models.csproj.");
                using (StreamReader reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }

        /// <summary>Per-T cache, so each distinct result type gets its own key space.</summary>
        private static class Cache<T>
        {
            public static readonly ConcurrentDictionary<string, Lazy<T>> Items = new ConcurrentDictionary<string, Lazy<T>>();
        }

        /// <summary>
        /// Returns a process-wide-shared result of <paramref name="factory"/> for a given cache key (typically
        /// the resource name), so many model instances in one process (e.g. an HPC run using --cpu-count) parse
        /// the same embedded resource only once instead of once per instance.
        /// </summary>
        /// <param name="cacheKey">Key identifying the cached value, typically the resource name.</param>
        /// <param name="factory">Loads/parses the value on first request for this key.</param>
        public static T GetOrLoad<T>(string cacheKey, Func<T> factory)
        {
            return Cache<T>.Items.GetOrAdd(cacheKey, _ => new Lazy<T>(factory, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }
    }
}

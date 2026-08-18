using System;
using System.IO;
using System.Reflection;

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
    }
}

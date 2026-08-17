using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace UnitTests.PROSAIL
{
    /// <summary>Resolves the Rscript executable path across platforms, for tests only.</summary>
    internal static class RScriptLocator
    {
        public static string FindRscriptPath()
        {
            try
            {
                return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? FindOnWindows()
                    : FindOnUnix();
            }
            catch
            {
                return null;
            }
        }

        private static string FindOnWindows()
        {
            string programFiles = Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
            string rRoot = Path.Combine(programFiles, "R");
            if (!Directory.Exists(rRoot))
                return null;

            // R installs as e.g. "R-4.4.1"; pick the highest version present.
            string versionDir = Directory.GetDirectories(rRoot)
                                          .OrderByDescending(d => d)
                                          .FirstOrDefault();
            if (versionDir == null)
                return null;

            string rScript64 = Path.Combine(versionDir, "bin", "x64", "Rscript.exe");
            string rScript32 = Path.Combine(versionDir, "bin", "Rscript.exe");
            if (File.Exists(rScript64))
                return rScript64;
            if (File.Exists(rScript32))
                return rScript32;
            return null;
        }

        private static string FindOnUnix()
        {
            var psi = new ProcessStartInfo("which", "Rscript")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var process = Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(5000);
                return string.IsNullOrEmpty(output) ? null : output;
            }
        }
    }
}

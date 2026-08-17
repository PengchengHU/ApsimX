using APSIM.Shared.Utilities;
using Models.PROSAIL.BSM;
using Newtonsoft.Json;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace UnitTests.PROSAIL
{
    [TestFixture]
    public class BsmCoreTests
    {
        private static readonly string RelativeBsmTestInputPath = "..\\..\\..\\Tests\\UnitTests\\PROSAIL\\bsmTestInputs.json";
        private static string BsmTestInputPath => PathUtilities.GetAbsolutePath(RelativeBsmTestInputPath, AppDomain.CurrentDomain.BaseDirectory);

        private static readonly string RelativeBsmDataPath = "..\\..\\..\\Models\\PROSAIL\\InputProperties\\SpectralData\\BSM_GSV.json";
        private static string DefaultBsmDataPath => PathUtilities.GetAbsolutePath(RelativeBsmDataPath, AppDomain.CurrentDomain.BaseDirectory);

        private readonly string RScriptPath = RScriptLocator.FindRscriptPath();

        private static readonly string RelativeRBsmWrapperPath = "..\\..\\..\\Tests\\UnitTests\\PROSAIL\\BSMWrapper.R";
        private static string RBsmWrapperPath => PathUtilities.GetAbsolutePath(RelativeRBsmWrapperPath, AppDomain.CurrentDomain.BaseDirectory);

        private readonly double Tolerance = 1e-3;

        [OneTimeSetUp]
        public void CheckRSetup()
        {
            Assert.That(File.Exists(RScriptPath), $"Rscript executable not found. Install R to run these tests.");
        }

        public class BsmTestInput
        {
            public double B { get; set; }
            public double lat { get; set; }
            public double lon { get; set; }
            public double SMp { get; set; }
        }

        private static List<BsmTestInput> LoadBsmTestInputs()
        {
            string json = File.ReadAllText(BsmTestInputPath);
            return JsonConvert.DeserializeObject<List<BsmTestInput>>(json);
        }

        private double CompareArrays(double[] expected, double[] actual, string context = "")
        {
            Assert.That(actual, Is.Not.Null, $"Actual array is null. {context}");
            Assert.That(actual.Length, Is.EqualTo(expected.Length), $"Array lengths differ. Expected {expected.Length}, Actual {actual.Length}. {context}");
            double maxDiff = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                double diff = Math.Abs(expected[i] - actual[i]);
                if (diff > maxDiff) maxDiff = diff;
            }
            Assert.That(maxDiff <= Tolerance, $"Array max difference {maxDiff} exceeds tolerance {Tolerance}. {context}");
            return maxDiff;
        }

        private static double[] ExtractDoubleArray(object rResultValue)
        {
            if (rResultValue == null) return null;
            if (rResultValue is double[] dArray) return dArray;
            if (rResultValue is Newtonsoft.Json.Linq.JArray jArray) return jArray.ToObject<double[]>();
            if (rResultValue is List<object> objList) return objList.Select(Convert.ToDouble).ToArray();
            throw new InvalidCastException($"Cannot convert R result value of type {rResultValue.GetType()} to double[]. Value: {rResultValue}");
        }

        /// <summary>
        /// Runs BSMWrapper.R with the given BSM parameters and returns the result dictionary.
        /// BSMWrapper.R takes only InputJsonPath and OutputJsonPath (no function-name argument).
        /// </summary>
        private Dictionary<string, object> RunRBsm(Dictionary<string, object> parameters)
        {
            string tempInputFile = Path.GetTempFileName();
            string tempOutputFile = Path.GetTempFileName();
            Dictionary<string, object> results = null;

            try
            {
                string inputJson = JsonConvert.SerializeObject(parameters, Formatting.Indented);
                File.WriteAllText(tempInputFile, inputJson);

                string arguments = $"\"{RBsmWrapperPath}\" \"{tempInputFile}\" \"{tempOutputFile}\"";

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = RScriptPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                TestContext.Progress.WriteLine($"Running R: {psi.FileName} {psi.Arguments}");

                using (var process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();

                    bool exited = process.WaitForExit(60000);

                    if (!exited)
                    {
                        try { process.Kill(); } catch { }
                        Assert.Fail($"R BSM process timed out. Output: {output} Error: {error}");
                    }

                    TestContext.Progress.WriteLine($"R Output stream:\n{output}");

                    if (process.ExitCode != 0)
                        Assert.Fail($"BSMWrapper.R execution failed. Exit Code: {process.ExitCode}\nR Error:\n{error}\nR Output:\n{output}");

                    if (!string.IsNullOrWhiteSpace(error))
                        TestContext.Progress.WriteLine($"R Error Stream (might contain warnings):\n{error}");

                    if (!File.Exists(tempOutputFile) || new FileInfo(tempOutputFile).Length == 0)
                        Assert.Fail($"BSMWrapper.R did not produce output: {tempOutputFile}. Output: {output} Error: {error}");

                    string outputJson = File.ReadAllText(tempOutputFile);
                    results = JsonConvert.DeserializeObject<Dictionary<string, object>>(outputJson);
                    if (results == null)
                        Assert.Fail($"Failed to deserialize BSMWrapper.R output JSON: {tempOutputFile}.\n{outputJson}");
                }
            }
            catch (Exception ex)
            {
                Assert.Fail($"Exception during BSMWrapper.R execution: {ex}");
            }
            finally
            {
                if (File.Exists(tempInputFile)) File.Delete(tempInputFile);
                if (File.Exists(tempOutputFile)) File.Delete(tempOutputFile);
            }

            return results ?? new Dictionary<string, object>();
        }

        [Test]
        public void BsmValidationTest()
        {
            var testInputs = LoadBsmTestInputs();
            BsmSpectralData bsmData = BsmCore.LoadBsmData(DefaultBsmDataPath);

            foreach (var input in testInputs)
            {
                Console.WriteLine($"Testing BSM: B={input.B}, lat={input.lat}, lon={input.lon}, SMp={input.SMp}");

                // C# implementation
                SoilOptics csResult = BsmCore.BSM(input.B, input.lat, input.lon, input.SMp, bsmData);

                // R implementation via BSMWrapper.R
                var rParams = new Dictionary<string, object>
                {
                    { "B",   input.B },
                    { "lat", input.lat },
                    { "lon", input.lon },
                    { "SMp", input.SMp }
                };
                var rResult = RunRBsm(rParams);

                double[] rReflectance = ExtractDoubleArray(rResult["reflectance"]);

                CompareArrays(rReflectance, csResult.Reflectance.ToArray(),
                    $"BSM B={input.B} lat={input.lat} lon={input.lon} SMp={input.SMp}");
            }
        }
    }
}

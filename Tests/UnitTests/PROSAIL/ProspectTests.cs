using NUnit.Framework;
using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using Newtonsoft.Json;
using APSIM.Shared.Utilities;
using static Models.PROSAIL.PROSPECT.ProspectCore;

namespace UnitTests.PROSAIL
{
    [TestFixture]
    public class ProspecCoreTests
    {
        private readonly string RScriptPath = RScriptLocator.FindRscriptPath();

        private static readonly string RelativeProspectScriptPath = "..\\..\\..\\Tests\\UnitTests\\PROSAIL\\ProspectImplementation.R";
        private static string RProspectScript => PathUtilities.GetAbsolutePath(RelativeProspectScriptPath, AppDomain.CurrentDomain.BaseDirectory);

        private static readonly string RelativeTestCasesFilePath = "..\\..\\..\\Tests\\UnitTests\\PROSAIL\\ProspectTestCases.json";
        private static string TestCasesFile => PathUtilities.GetAbsolutePath(RelativeTestCasesFilePath, AppDomain.CurrentDomain.BaseDirectory);

        private readonly double Tolerance = 1e-2;

        [OneTimeSetUp]
        public void CheckRSetup()
        {
            Assert.That(File.Exists(RScriptPath), $"Rscript executable not found. Install R to run these tests.");
        }

        public class ProspectTestCase
        {
            public string Name { get; set; }
            public double N { get; set; }
            public double CAB { get; set; }
            public double CAR { get; set; }
            public double EWT { get; set; }
            public double LMA { get; set; }
            public double Alpha { get; set; }
        }

        [Test]
        public void ValidateProspectImplementation()
        {
            var testCases = LoadTestCases();
            foreach (var testCase in testCases)
            {
                var rResults = RunRImplementation(testCase);
                var csharpResults = RunCSharpImplementation(testCase);

                double maxReflDiff = CompareArrays(csharpResults.Reflectance, rResults["Reflectance"]);
                double maxTranDiff = CompareArrays(csharpResults.Transmittance, rResults["Transmittance"]);

                Assert.That(maxReflDiff <= Tolerance, $"{testCase.Name} Reflectance diff {maxReflDiff} exceeds tolerance {Tolerance}");
                Assert.That(maxTranDiff <= Tolerance, $"{testCase.Name} Transmittance diff {maxTranDiff} exceeds tolerance {Tolerance}");
            }
        }

        private List<ProspectTestCase> LoadTestCases()
        {
            if (!File.Exists(TestCasesFile)) throw new FileNotFoundException(TestCasesFile);
            return JsonConvert.DeserializeObject<List<ProspectTestCase>>(File.ReadAllText(TestCasesFile));
        }

        private static LeafOptics RunCSharpImplementation(ProspectTestCase testCase)
        {
            try
            {
                return Prospect(N: testCase.N, CAB: testCase.CAB, CAR: testCase.CAR, EWT: testCase.EWT, LMA: testCase.LMA, Alpha: testCase.Alpha);
            }
            catch (Exception ex)
            {
                Assert.Fail($"PROSPECT C# implementation failed for {testCase.Name}: {ex.Message}");
                LeafOptics optics = default;
                return optics; // Unreachable due to Assert.Fail, but required for return type
            }
        }

        private Dictionary<string, double[]> RunRImplementation(ProspectTestCase testCase)
        {
            string inputFile = Path.GetTempFileName();
            string outputFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(inputFile, JsonConvert.SerializeObject(testCase));
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = RScriptPath,
                    Arguments = $"\"{RProspectScript}\" \"{inputFile}\" \"{outputFile}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit(30000);
                    if (process.ExitCode != 0) throw new Exception($"R error: {error}");
                }

                return JsonConvert.DeserializeObject<Dictionary<string, double[]>>(File.ReadAllText(outputFile));
            }
            finally
            {
                File.Delete(inputFile);
                File.Delete(outputFile);
            }
        }

        private static double CompareArrays(double[] a, double[] b)
        {
            if (a.Length != b.Length) throw new ArgumentException("Array length mismatch");
            return a.Zip(b, (x, y) => Math.Abs(x - y)).Max();
        }
    }
}
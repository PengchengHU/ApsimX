using NUnit.Framework; // Using NUnit framework
using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Models.Sail;
using APSIM.Shared.Utilities;
using static Models.Prospect.ProspectCore;
using static Models.Sail.SailUtilities;

namespace UnitTests
{
    [TestFixture] // NUnit attribute for a test class
    public class SailUtilitiesTests
    {
        private readonly string RScriptPath = @"C:\Program Files\R\R-4.4.1\bin\Rscript.exe";
        //private readonly string RSailScriptWrapper = Path.Combine(TestContext.CurrentContext.TestDirectory, "SailUtilitiesWrapper.R");
        private readonly string RSailScriptWrapper = @"D:\ApsimX\Tests\UnitTests\PROSPECT\SailUtilitiesWrapper.R";
        
        // Leaf optical constants data
        private static readonly string RelativeOpticalDataPath = "..\\..\\..\\Models\\PROSAIL\\PROSPECT\\SpecPROSPECT_FullRange.json";
        private static string DefaultOpticalDataPath => PathUtilities.GetAbsolutePath(RelativeOpticalDataPath, AppDomain.CurrentDomain.BaseDirectory);

        // Atmospheric data (direct and diffuse radiation for clear conditions)
        private static readonly string RelativeSpecATMDataPath = "..\\..\\..\\Models\\PROSAIL\\PROSPECT\\SpecATM.json";
        private static string DefaultSpecATMDataPath => PathUtilities.GetAbsolutePath(RelativeSpecATMDataPath, AppDomain.CurrentDomain.BaseDirectory);

        // Soil reflectance data
        private static readonly string RelativeSpecSoilDataPath = "..\\..\\..\\Models\\PROSAIL\\PROSPECT\\SpecSoil.json";
        private static string DefaultSpecSoilDataPath => PathUtilities.GetAbsolutePath(RelativeSpecSoilDataPath, AppDomain.CurrentDomain.BaseDirectory);

        // Example Prospect simualted data
        private static readonly string RelativeProspectOutDataPath = "..\\..\\..\\Models\\PROSAIL\\PROSPECT\\SpecSoil.json";
        private static string DefaultProspectOutDataPath => PathUtilities.GetAbsolutePath(RelativeProspectOutDataPath, AppDomain.CurrentDomain.BaseDirectory);

        private readonly double Tolerance = 1e-3;

        // Helper class for JSON deserialization
        private class SpecATMDataJason
        {
            public double[] Wavelength { get; set; }
            public double[] DirectLight { get; set; }
            public double[] DiffuseLight { get; set; }
        }

        // Helper class for JSON deserialization
        private class SpecSoilDataJson
        {
            /// <summary>
            /// Wavelengths (nm). Should match simulation wavelengths.
            /// </summary>
            public double[] Wavelength { get; set; }

            /// <summary>
            /// Soil reflectance spectrum (unitless fraction).
            /// </summary>
            public double[] Dry_Soil { get; set; }

            /// <summary>
            /// Soil reflectance spectrum (unitless fraction).
            /// </summary>
            public double[] Wet_Soil { get; set; }
        }

        private class LeafOpticsDataJson
        {
            /// <summary>Wavelengths (nm). Should match simulation wavelengths.</summary>
            public double[] Wavelength { get; set; }

            /// <summary>Leaf reflectance spectrum (unitless fraction) (unitless fraction).</summary>
            public double[] Reflectance { get; set; }

            /// <summary> Leaf transmittance spectrum (unitless fraction).</summary>
            public double[] Transmittance { get; set; }
        }

        [OneTimeSetUp]
        public void CheckRSetup()
        {
            Assert.That(File.Exists(RScriptPath), $"Rscript.exe not found at: {RScriptPath}.");
            Assert.That(File.Exists(RSailScriptWrapper), $"R wrapper script not found at: {RSailScriptWrapper}.");
            Assert.That(File.Exists(DefaultOpticalDataPath), $"R wrapper script not found at: {DefaultOpticalDataPath}.");
            Assert.That(File.Exists(DefaultSpecATMDataPath), $"R wrapper script not found at: {DefaultSpecATMDataPath}.");
            Assert.That(File.Exists(DefaultSpecSoilDataPath), $"R wrapper script not found at: {DefaultSpecSoilDataPath}.");
            Assert.That(File.Exists(DefaultProspectOutDataPath), $"R wrapper script not found at: {DefaultProspectOutDataPath}.");
        }

        #region Read inputs
        // Helper to load atmospheric data
        private static SpecAtmSensor CreateSampleAtm(string DefaultSpecATMDataPath)
        {
            string json = File.ReadAllText(DefaultSpecATMDataPath);
            var ATMData = JsonConvert.DeserializeObject<SpecATMDataJason>(json);

            return new SpecAtmSensor
            {
                Wavelength = ATMData.Wavelength,
                DirectLight = ATMData.DirectLight,
                DiffuseLight = ATMData.DiffuseLight
            };
        }

        // Helper to create simple soil properties
        private static SoilProperties CreateSampleSoil(string DefaultSpecSoilDataPath)
        {
            string json = File.ReadAllText(DefaultSpecSoilDataPath);
            var soilData = JsonConvert.DeserializeObject<SpecSoilDataJson>(json);

            return new SoilProperties
            {
                Wavelength = soilData.Wavelength,
                Reflectance = soilData.Wet_Soil
            };
        }

        // Helper to create sample PROSPECT leaf optical constants
        private static LeafOpticalConsts CreateSampleLeafOpticalConstants()
        {
            var constants = LoadLocalOpticalData();
            return constants;
        }

        // Helper to create a sample LeafOptics object (e.g., for BrownLOP input)
        private static LeafOptics CreateSampleLeafOptics(string DefaultProspectOutDataPath)
        {
            string json = File.ReadAllText(DefaultProspectOutDataPath);
            var leafOpticsData = JsonConvert.DeserializeObject<LeafOpticsDataJson>(json);
            return new LeafOptics
            {
                Wavelength = leafOpticsData.Wavelength,
                Reflectance = leafOpticsData.Reflectance,
                Transmittance = leafOpticsData.Transmittance
            };
        }
        #endregion

        #region Helpers

        // Helpers for comparisons
        private double CompareScalars(double expected, double actual, string context = "")
        {
            double diff = Math.Abs(expected - actual);
            Assert.That(diff <= Tolerance, $"Scalar mismatch {context}. Expected: {expected}, Actual: {actual}, Diff: {diff}");
            return diff; // Return difference for potential logging or max calc
        }

        private double CompareArrays(double[] expected, double[] actual, string context = "")
        {
            Assert.That(actual, Is.Not.Null, $"Actual array is null. {context}");
            Assert.That(actual.Length, Is.EqualTo(expected.Length), $"Array lengths differ. Expected {expected.Length}, Actual {actual.Length}. {context}");

            if (expected.Length == 0) return 0.0; // Empty arrays are equal

            double maxDiff = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                double diff = Math.Abs(expected[i] - actual[i]);
                if (diff > maxDiff) maxDiff = diff;
            }
            Assert.That(maxDiff <= Tolerance, $"Array max difference {maxDiff} exceeds tolerance {Tolerance}. {context}");
            return maxDiff;
        }

        // Helper to extract double array from R result dictionary value (might be JArray)
        private static double[] ExtractDoubleArray(object rResultValue)
        {
            if (rResultValue == null) return null;
            if (rResultValue is double[] dArray) return dArray;
            if (rResultValue is JArray jArray) return jArray.ToObject<double[]>();
            if (rResultValue is List<object> objList) return objList.Select(Convert.ToDouble).ToArray(); // Handle list of numbers
                                                                                                         // Add other potential conversions if needed
            throw new InvalidCastException($"Cannot convert R result value of type {rResultValue.GetType()} to double[]. Value: {rResultValue}");
        }

        // Helpers for comparing methods
        private double CompareFoliarDistribution(FoliarDistributionResult expected, FoliarDistributionResult actual, string context = "")
        {
            double diffLidf = CompareArrays(expected.Lidf, actual.Lidf, $"Lidf {context}");
            double diffLitab = CompareArrays(expected.Litab, actual.Litab, $"Litab {context}"); // Litab should be exact
            return Math.Max(diffLidf, diffLitab); // Return max difference found
        }

        private double CompareVolscattResult(VolscattResult expected, VolscattResult actual, string context = "")
        {
            double diffChiS = CompareScalars(expected.Chi_s, actual.Chi_s, $"Chi_s {context}");
            double diffChiO = CompareScalars(expected.Chi_o, actual.Chi_o, $"Chi_o {context}");
            double diffFrho = CompareScalars(expected.Frho, actual.Frho, $"Frho {context}");
            double diffFtau = CompareScalars(expected.Ftau, actual.Ftau, $"Ftau {context}");
            return new[] { diffChiS, diffChiO, diffFrho, diffFtau }.Max();
        }

        private double CompareScatteringResult(ScatteringResult expected, ScatteringResult actual, string context = "")
        {
            double diffTdd = CompareArrays(expected.Tdd, actual.Tdd, $"Tdd {context}");
            double diffRdd = CompareArrays(expected.Rdd, actual.Rdd, $"Rdd {context}");
            double diffTsd = CompareArrays(expected.Tsd, actual.Tsd, $"Tsd {context}");
            double diffRsd = CompareArrays(expected.Rsd, actual.Rsd, $"Rsd {context}");
            double diffTdo = CompareArrays(expected.Tdo, actual.Tdo, $"Tdo {context}");
            double diffRdo = CompareArrays(expected.Rdo, actual.Rdo, $"Rdo {context}");
            double diffRsod = CompareArrays(expected.Rsod, actual.Rsod, $"Rsod {context}");
            return new[] { diffTdd, diffRdd, diffTsd, diffRsd, diffTdo, diffRdo, diffRsod }.Max();
        }

        private double CompareAdjustedProspectResult(AdjustedProspectResult expected, AdjustedProspectResult actual, string context = "")
        {
            Assert.That(actual.GreenLOP, Is.Not.Null, $"Actual GreenLOP is null. {context}");
            Assert.That(expected.GreenLOP, Is.Not.Null, $"Expected GreenLOP is null. {context}");

            double diffGreenR = CompareArrays(expected.GreenLOP.Reflectance, actual.GreenLOP.Reflectance, $"GreenLOP Refl {context}");
            double diffGreenT = CompareArrays(expected.GreenLOP.Transmittance, actual.GreenLOP.Transmittance, $"GreenLOP Trans {context}");
            double diffBrownR = 0, diffBrownT = 0;

            bool expectBrown = expected.BrownLOP != null && expected.BrownLOP.Value.Reflectance != null;
            bool actualBrown = actual.BrownLOP != null && actual.BrownLOP.Value.Reflectance != null;

            Assert.That(expectBrown, Is.EqualTo(actualBrown), $"Array lengths differ.  Expected: {expectBrown}, Actual: {actualBrown}. {context}");

            if (expectBrown && actualBrown)
            {
                diffBrownR = CompareArrays(expected.BrownLOP.Value.Reflectance, actual.BrownLOP.Value.Reflectance, $"BrownLOP Refl {context}");
                diffBrownT = CompareArrays(expected.BrownLOP.Value.Transmittance, actual.BrownLOP.Value.Transmittance, $"BrownLOP Trans {context}");
            }
            return new[] { diffGreenR, diffGreenT, diffBrownR, diffBrownT }.Max();
        }


        /// <summary>
        /// Executes a specified function from the R wrapper script with given parameters.
        /// Handles JSON serialization/deserialization and process execution.
        /// </summary>
        /// <param name="functionName">The R function name (e.g., "Compute_BRF").</param>
        /// <param name="parameters">A dictionary containing function parameters.</param>
        /// <returns>A dictionary containing the results deserialized from R's JSON output.</returns>
        private Dictionary<string, object> RunRImplementation(string functionName, Dictionary<string, object> parameters)
        {
            string tempInputFile = Path.GetTempFileName();
            string tempOutputFile = Path.GetTempFileName();
            Dictionary<string, object> results = null;

            try
            {
                // 1. Serialize parameters to JSON and write to input file
                string inputJson = JsonConvert.SerializeObject(parameters, Formatting.Indented);
                File.WriteAllText(tempInputFile, inputJson);

                // 2. Construct Rscript arguments
                // Ensure paths with spaces are quoted properly
                string arguments = $"\"{RSailScriptWrapper}\" \"{functionName}\" \"{tempInputFile}\" \"{tempOutputFile}\"";

                // 3. Configure and start the R process
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = RScriptPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true, // Capture R console output
                    RedirectStandardError = true,  // Capture R errors
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                TestContext.Progress.WriteLine($"Running R: {psi.FileName} {psi.Arguments}");

                using (var process = Process.Start(psi))
                {
                    // Read output/error streams asynchronously or synchronously
                    // Synchronous read (simpler for tests, might hang if R produces excessive output)
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();

                    // Wait for the process to exit (with a timeout)
                    bool exited = process.WaitForExit(60000); // 60 second timeout

                    if (!exited)
                    {
                        try { process.Kill(); } catch { } // Try to kill runaway process
                        Assert.Fail($"R process timed out for function {functionName}. Output: {output} Error: {error}");
                    }

                    TestContext.Progress.WriteLine($"R Output stream:\n{output}"); // Log R output

                    // 4. Check for errors
                    if (process.ExitCode != 0)
                    {
                        Assert.Fail($"R script execution failed for function {functionName}. Exit Code: {process.ExitCode}\n R Error Stream:\n{error}\n R Output Stream:\n{output}");
                    }
                    if (!string.IsNullOrWhiteSpace(error)) // Also log non-fatal errors/warnings from R
                    {
                        TestContext.Progress.WriteLine($"R Error Stream (might contain warnings):\n{error}");
                    }


                    // 5. Read and deserialize results JSON from the output file
                    if (!File.Exists(tempOutputFile) || new FileInfo(tempOutputFile).Length == 0)
                    {
                        Assert.Fail($"R script did not produce the output file or it was empty: {tempOutputFile}. Output: {output} Error: {error}");
                    }

                    string outputJson = File.ReadAllText(tempOutputFile);
                    results = JsonConvert.DeserializeObject<Dictionary<string, object>>(outputJson);
                    if (results == null)
                    {
                        Assert.Fail($"Failed to deserialize R output JSON from: {tempOutputFile}. JSON content:\n{outputJson}");
                    }
                }
            }
            catch (Exception ex)
            {
                Assert.Fail($"An exception occurred during R script execution or processing for function {functionName}: {ex}");
            }
            finally
            {
                // 6. Clean up temporary files
                if (File.Exists(tempInputFile)) File.Delete(tempInputFile);
                if (File.Exists(tempOutputFile)) File.Delete(tempOutputFile);
            }

            return results ?? new Dictionary<string, object>(); // Return empty dictionary if something went wrong before deserialization
        }
        #endregion


        // Test methods in SailUtilities

        [Test]
        public void TestComputeBRF()
        {
            // Arrange C# Inputs
            //double[] wavelengths = { 400, 500, 600 };
            double[] rdot_in = { 0.1, 0.2, 0.3 };
            double[] rsot_in = { 0.15, 0.25, 0.35 };
            double tts_in = 30.0;
            var atm_in = CreateSampleAtm(DefaultSpecATMDataPath);

            // Arrange R Inputs Dictionary (Keys match R function parameters)
            var r_params = new Dictionary<string, object> {
                { "rdot", rdot_in },
                { "rsot", rsot_in },
                { "tts", tts_in },
                // Structure matching R's expected list for SpecATM_Sensor
                { "SpecATM_Sensor", new {
                     Wavelength = atm_in.Wavelength, // Pass wavelength needed by wrapper
                     Direct_Light = atm_in.DirectLight,
                     Diffuse_Light = atm_in.DiffuseLight }
                 }
            };

            // Act (C#)
            double[] actual_brf = ComputeBRF(rdot_in, rsot_in, tts_in, atm_in);

            // Act (R)
            var r_results = RunRImplementation("Compute_BRF", r_params);
            double[] expected_brf = ExtractDoubleArray(r_results["BRF"]); // Extract result using helper

            // Assert
            CompareArrays(expected_brf, actual_brf, "Compute_BRF");
        }

        [Test]
        public void TestComputeFAPAR()
        {
            // Arrange C# Inputs
            //double[] wavelengths = { 400, 550, 700, 800 }; // Include points inside and outside PAR range
            double[] abs_dir_in = { 0.8, 0.85, 0.75, 0.1 };
            double[] abs_hem_in = { 0.7, 0.75, 0.65, 0.05 };
            double tts_in = 45.0;
            var atm_in = CreateSampleAtm(DefaultSpecATMDataPath);
            double parMin = 400, parMax = 700;

            // Arrange R Inputs Dictionary
            var r_params = new Dictionary<string, object> {
                { "abs_dir", abs_dir_in },
                { "abs_hem", abs_hem_in },
                { "tts", tts_in },
                { "SpecATM_Sensor", new {
                     Wavelength=atm_in.Wavelength, // Pass Wavelength needed by R wrapper
                     Direct_Light = atm_in.DirectLight,
                     Diffuse_Light = atm_in.DiffuseLight }
                 },
                { "PAR_range", new double[] { parMin, parMax } } // R expects PAR_range
            };

            // Act (C#)
            double actual_fapar = ComputeFAPAR(abs_dir_in, abs_hem_in, tts_in, atm_in, parMin, parMax);

            // Act (R)
            var r_results = RunRImplementation("Compute_fAPAR", r_params);
            // R wrapper names the scalar result after the function name
            double expected_fapar = Convert.ToDouble(r_results["Compute_fAPAR"]);

            // Assert
            CompareScalars(expected_fapar, actual_fapar, "Compute_fAPAR");
        }

        [Test]
        public void TestComputeAlbedo()
        {
            // Arrange C# Inputs
            //double[] wavelengths = { 400, 800, 1200, 1600, 2000, 2400 };
            double[] rsdstar_in = { 0.1, 0.4, 0.5, 0.4, 0.3, 0.2 };
            double[] rddstar_in = { 0.12, 0.42, 0.52, 0.42, 0.32, 0.22 };
            double tts_in = 20.0;
            var atm_in = CreateSampleAtm(DefaultSpecATMDataPath);
            double rangeMin = 400, rangeMax = 2400;

            // Arrange R Inputs Dictionary
            var r_params = new Dictionary<string, object> {
                 { "rsdstar", rsdstar_in },
                 { "rddstar", rddstar_in },
                 { "tts", tts_in },
                 { "SpecATM_Sensor", new {
                      Wavelength=atm_in.Wavelength, // Pass Wavelength needed by R wrapper
                      Direct_Light = atm_in.DirectLight,
                      Diffuse_Light = atm_in.DiffuseLight }
                  },
                 { "PAR_range", new double[] { rangeMin, rangeMax } } // R function uses PAR_range param name
             };

            // Act (C#)
            double actual_albedo = ComputeAlbedo(rsdstar_in, rddstar_in, tts_in, atm_in, rangeMin, rangeMax);

            // Act (R)
            var r_results = RunRImplementation("Compute_albedo", r_params);
            // R wrapper names the scalar result after the function name
            double expected_albedo = Convert.ToDouble(r_results["Compute_albedo"]);

            // Assert
            CompareScalars(expected_albedo, actual_albedo, "Compute_albedo");
        }

        [Test]
        public void TestCampbell()
        {
            // Arrange C# Inputs
            double ala_in = 57.0; // Standard spherical angle approx

            // Arrange R Inputs Dictionary
            var r_params = new Dictionary<string, object> { { "ala", ala_in } };

            // Act (C#)
            var actualResult = Campbell(ala_in);

            // Act (R)
            var r_results = RunRImplementation("campbell", r_params);
            // R wrapper returns list directly, extract components
            var expectedResult = new FoliarDistributionResult
            {
                Lidf = ExtractDoubleArray(r_results["lidf"]),
                Litab = ExtractDoubleArray(r_results["litab"])
            };

            // Assert
            CompareFoliarDistribution(expectedResult, actualResult, "Campbell");
        }

        [Test]
        public void TestDladgen()
        {
            // Arrange C# Inputs
            double a_in = -0.35; // Example: Spherical approx
            double b_in = -0.15;

            // Arrange R Inputs Dictionary
            var r_params = new Dictionary<string, object> { { "a", a_in }, { "b", b_in } };

            // Act (C#)
            var actualResult = Dladgen(a_in, b_in);

            // Act (R)
            var r_results = RunRImplementation("dladgen", r_params);
            // R wrapper returns list directly, extract components
            var expectedResult = new FoliarDistributionResult
            {
                Lidf = ExtractDoubleArray(r_results["lidf"]),
                Litab = ExtractDoubleArray(r_results["litab"])
            };

            // Assert
            CompareFoliarDistribution(expectedResult, actualResult, "Dladgen");
        }

        [Test]
        public void TestDcum()
        {
            // Arrange C# Inputs
            double a_in = 0.0; double b_in = 0.0; double t_in = 45.0; // Uniform case, 45 deg

            // Arrange R Inputs Dictionary
            var r_params = new Dictionary<string, object> { { "a", a_in }, { "b", b_in }, { "t", t_in } };

            // Act (C#)
            double actualResult = Dcum(a_in, b_in, t_in);

            // Act (R)
            var r_results = RunRImplementation("dcum", r_params);
            // R wrapper names the scalar result after the function name
            double expectedResult = Convert.ToDouble(r_results["dcum"]);

            // Assert
            CompareScalars(expectedResult, actualResult, "Dcum");
        }


        // --- Tests for J Functions ---
        [TestCase(1.0, 0.5, 1.0, TestName = "Jfunc1_Normal")]
        [TestCase(0.5, 0.5, 1.0, TestName = "Jfunc1_Singularity")]
        [TestCase(0.001, 0.0005, 1.0, TestName = "Jfunc1_SmallDiff")]
        [TestCase(0.5, 0.5, 0.0, TestName = "Jfunc1_ZeroT")] // Added test case
        public void TestJfunc1(double k, double l, double t)
        {
            var r_params = new Dictionary<string, object> { { "k", k }, { "l", l }, { "t", t } };
            double actual = Jfunc1(k, l, t);
            var r_results = RunRImplementation("Jfunc1", r_params);
            double expected = Convert.ToDouble(r_results["Jfunc1"]); // Wrapper names result after function
            CompareScalars(expected, actual, $"Jfunc1(k={k},l={l},t={t})");
        }

        [TestCase(1.0, 0.5, 1.0, TestName = "Jfunc2_Normal")]
        [TestCase(0.5, -0.5, 1.0, TestName = "Jfunc2_SumZero")]
        [TestCase(0.001, -0.001, 0.001, TestName = "Jfunc2_SumZeroSmallT")] // Added test case
        [TestCase(0.0001, 0.0001, 1.0, TestName = "Jfunc2_SmallSum")]
        [TestCase(0.5, 0.5, 0.0, TestName = "Jfunc2_ZeroT")] // Added test case
        public void TestJfunc2(double k, double l, double t)
        {
            var r_params = new Dictionary<string, object> { { "k", k }, { "l", l }, { "t", t } };
            double actual = Jfunc2(k, l, t);
            var r_results = RunRImplementation("Jfunc2", r_params);
            double expected = Convert.ToDouble(r_results["Jfunc2"]); // Wrapper names result after function
            CompareScalars(expected, actual, $"Jfunc2(k={k},l={l},t={t})");
        }

        // TestJfunc3 omitted as it calls Jfunc2

        [TestCase(0.5, 1.0, TestName = "Jfunc4_Normal")]
        [TestCase(0.0001, 1.0, TestName = "Jfunc4_NearZeroM")]
        [TestCase(0.0, 1.0, TestName = "Jfunc4_ZeroM")]
        [TestCase(0.5, 0.0, TestName = "Jfunc4_ZeroT")] // Added test case
        public void TestJfunc4_R(double m, double t)
        {
            var r_params = new Dictionary<string, object> { { "m", m }, { "t", t } };
            double actual = Jfunc4(m, t);
            var r_results = RunRImplementation("Jfunc4", r_params);
            double expected = Convert.ToDouble(r_results["Jfunc4"]); // Wrapper names result after function
            CompareScalars(expected, actual, $"Jfunc4(m={m},t={t})");
        }

        [Test]
        public void TestVolscatt()
        {
            // Arrange C# Inputs
            double tts = 30, tto = 10, psi = 60, ttl = 45;

            // Arrange R Inputs Dictionary
            var r_params = new Dictionary<string, object> { { "tts", tts }, { "tto", tto }, { "psi", psi }, { "ttl", ttl } };

            // Act (C#)
            var actual = Volscatt(tts, tto, psi, ttl);

            // Act (R)
            var r_results = RunRImplementation("volscatt", r_params);
            // R wrapper returns list directly, extract components
            var expected = new VolscattResult
            {
                Chi_s = Convert.ToDouble(r_results["chi_s"]),
                Chi_o = Convert.ToDouble(r_results["chi_o"]),
                Frho = Convert.ToDouble(r_results["frho"]),
                Ftau = Convert.ToDouble(r_results["ftau"])
            };

            // Assert
            CompareVolscattResult(expected, actual, "Volscatt");
        }

        // --- Tests for Check Functions ---
        [Test]
        public void TestCheckSpectralSamplingValid() // No R comparison needed
        {
            //double[] lambda = { 400, 500, 600 };
            var leafOpticalConstants = CreateSampleLeafOpticalConstants();
            var soil = CreateSampleSoil(DefaultSpecSoilDataPath);
            var atm = CreateSampleAtm(DefaultSpecATMDataPath);
            Assert.DoesNotThrow(() => CheckSpectralSampling(leafOpticalConstants, soil, atm));
        }

        [Test]
        public void TestCheckSpectralSamplingLengthMismatch() // No R comparison needed
        {
            //double[] lambda1 = { 400, 500, 600 }; 
            //double[] lambda2 = { 400, 500 };
            var leafOpticalConstants = CreateSampleLeafOpticalConstants();
            var soil = CreateSampleSoil(DefaultSpecSoilDataPath); 
            var atm = CreateSampleAtm(DefaultSpecATMDataPath);
            Assert.Throws<ArgumentException>(() => CheckSpectralSampling(leafOpticalConstants, soil, atm));
        }

        [Test]
        public void TestCheckSpectralSamplingValueMismatch() // No R comparison needed
        {
            //double[] lambda1 = { 400, 500, 600 }; 
            //double[] lambda2 = { 400, 501, 600 };
            var leafOpticalConstants = CreateSampleLeafOpticalConstants();
            var soil = CreateSampleSoil(DefaultSpecSoilDataPath); 
            var atm = CreateSampleAtm(DefaultSpecATMDataPath);
            Assert.Throws<ArgumentException>(() => CheckSpectralSampling(leafOpticalConstants, soil, atm));
        }

        [Test]
        public void TestCheckBrownLopValid() // No R comparison needed
        {
            //double[] lambda = { 400, 500, 600 };
            var brownLop = CreateSampleLeafOptics(DefaultProspectOutDataPath);
            double[] lambda = brownLop.Wavelength;
            var inputs = new List<ProspectInputs> { new ProspectInputs() };
            Assert.DoesNotThrow(() => CheckBrownLOP(brownLop, lambda, inputs));
        }

        [Test]
        public void TestCheckBrownLopNullData() // No R comparison needed
        {
            double[] lambda = { 400, 500, 600 };
            var brownLop = CreateSampleLeafOptics(DefaultProspectOutDataPath); 
            brownLop.Wavelength = null;
            var inputs = new List<ProspectInputs> { new ProspectInputs() };
            Assert.Throws<ArgumentException>(() => CheckBrownLOP(brownLop, lambda, inputs));
        }

        [Test]
        public void TestCheckBrownLopSpectralMismatch() // No R comparison needed
        {
            double[] lambdaRef = { 400, 500, 600 }; 
            //double[] lambdaBrown = { 400, 501, 600 };
            var brownLop = CreateSampleLeafOptics(DefaultProspectOutDataPath);
            var inputs = new List<ProspectInputs> { new ProspectInputs() };
            Assert.Throws<ArgumentException>(() => CheckBrownLOP(brownLop, lambdaRef, inputs));
        }

        [Test]
        public void TestAdjustProspectTo4SAIL()
        {
            // Arrange C# Inputs
            string sailVersion = "4SAIL";
            double[] lambda = { 400, 500, 600 }; // Simple lambda for test structure
            var prospectConst = CreateSampleLeafOpticalConstants();
            var inputs = new List<ProspectInputs> {
                  new ProspectInputs(cab: 50, car: 10) // Green params
             };
            double fractionBrown = 0.0; // Irrelevant for 4SAIL path

            // Arrange R Inputs Dictionary
            var r_params = new Dictionary<string, object> {
                  {"sailVersion", sailVersion}, // Use the variable name C# uses, wrapper handles mapping
                  {"prospectConstants", prospectConst}, // Pass the C# struct, wrapper handles mapping
                  {"inputProspectList", inputs}, // Pass C# list, wrapper handles mapping
                  {"fractionBrown", fractionBrown}, // Use C# name
                  {"brownLOP", null} // Explicitly null
             };

            // Act (C#)
            var actual = AdjustProspectToSail(sailVersion, prospectConst, inputs, fractionBrown, null);

            // Act (R)
            var r_results = RunRImplementation("adjust_PROSPECT_2_SAIL", r_params);
            // R wrapper formats output nicely
            var expected = new AdjustedProspectResult
            {
                GreenLOP = new LeafOptics
                {
                    Wavelength = lambda, // Assume R returns data matching lambda
                    Reflectance = ExtractDoubleArray(r_results["GreenLOP_Reflectance"]),
                    Transmittance = ExtractDoubleArray(r_results["GreenLOP_Transmittance"])
                },
                BrownLOP = null // 4SAIL should always have null BrownLOP
            };

            // Assert
            CompareAdjustedProspectResult(expected, actual, "adjust_PROSPECT_2_SAIL (4SAIL)");
        }

        [Test]
        public void TestAdjustProspectToSailExternalBrownLOP()
        {
            // Arrange C# Inputs
            string sailVersion = "4SAIL2";
            double[] lambda = { 400, 500, 600 };
            var prospectConst = CreateSampleLeafOpticalConstants();
            var inputs = new List<ProspectInputs> {
                   new ProspectInputs(cab: 50, car: 10) // Green input
              };
            double fractionBrown = 0.3;
            var externalBrownLop = CreateSampleLeafOptics(DefaultProspectOutDataPath); // Provide external BrownLOP

            // Arrange R Inputs Dictionary
            var r_params = new Dictionary<string, object> {
                   {"sailVersion", sailVersion},
                   {"prospectConstants", prospectConst},
                   {"inputProspectList", inputs}, // Only first input used by R when BrownLOP provided
                   {"fractionBrown", fractionBrown},
                   {"brownLOP", externalBrownLop} // Pass the C# object, wrapper handles mapping
              };

            // Act (C#)
            var actual = AdjustProspectToSail(sailVersion, prospectConst, inputs, fractionBrown, externalBrownLop);

            // Act (R)
            var r_results = RunRImplementation("adjust_PROSPECT_2_SAIL", r_params);
            // R wrapper formats output
            var expected = new AdjustedProspectResult
            {
                GreenLOP = new LeafOptics
                {
                    Wavelength = lambda,
                    Reflectance = ExtractDoubleArray(r_results["GreenLOP_Reflectance"]),
                    Transmittance = ExtractDoubleArray(r_results["GreenLOP_Transmittance"])
                },
                BrownLOP = new LeafOptics
                { // Expect R to return the brown LOP info
                    Wavelength = lambda,
                    Reflectance = ExtractDoubleArray(r_results["BrownLOP_Reflectance"]),
                    Transmittance = ExtractDoubleArray(r_results["BrownLOP_Transmittance"])
                }
            };

            // Assert
            CompareAdjustedProspectResult(expected, actual, "adjust_PROSPECT_2_SAIL (ExtBrown)");
            // Also verify C# returned the *exact* external object reference if that's the expected behavior (it should)
            // This check is subtle: Did adjust_PROSPECT_2_SAIL just pass the reference through?
            // Assert.AreSame(externalBrownLop, actual.BrownLOP, "External BrownLOP object reference should be preserved.");
            // However, the R comparison is the primary goal.
        }

        [Test]
        public void TestAdjustProspectToSAIL2FractionZero()
        {
            // Arrange C# Inputs
            string sailVersion = "4SAIL2";
            double[] lambda = { 400, 500, 600 };
            var prospectConst = CreateSampleLeafOpticalConstants();
            var inputs = new List<ProspectInputs> {
                   new ProspectInputs(cab: 50, car: 10) // Green input
              };
            double fractionBrown = 0.0; // Zero brown fraction
            //LeafOptics externalBrownLop;

            // Arrange R Inputs Dictionary
            var r_params = new Dictionary<string, object> {
                   {"sailVersion", sailVersion}, {"prospectConstants", prospectConst},
                   {"inputProspectList", inputs}, {"fractionBrown", fractionBrown},
                   {"brownLOP", null}
               };

            // Act (C#)
            var actual = AdjustProspectToSail(sailVersion, prospectConst, inputs, fractionBrown, null);

            // Act (R)
            var r_results = RunRImplementation("adjust_PROSPECT_2_SAIL", r_params);
            // R code sets BrownLOP <- GreenLOP in this case
            var expected = new AdjustedProspectResult
            {
                GreenLOP = new LeafOptics
                {
                    Wavelength = lambda,
                    Reflectance = ExtractDoubleArray(r_results["GreenLOP_Reflectance"]),
                    Transmittance = ExtractDoubleArray(r_results["GreenLOP_Transmittance"])
                },
                BrownLOP = new LeafOptics
                { // Expect Brown = Green from R
                    Wavelength = lambda,
                    Reflectance = ExtractDoubleArray(r_results["BrownLOP_Reflectance"]), // Should match Green
                    Transmittance = ExtractDoubleArray(r_results["BrownLOP_Transmittance"]) // Should match Green
                }
            };

            // Assert
            CompareAdjustedProspectResult(expected, actual, "adjust_PROSPECT_2_SAIL (FracZero)");
            // Also explicitly check C# Brown == Green
            CompareArrays(actual.GreenLOP.Reflectance, actual.BrownLOP.Value.Reflectance, "C# Brown Refl vs Green Refl (FracZero)");
            CompareArrays(actual.GreenLOP.Transmittance, actual.BrownLOP.Value.Transmittance, "C# Brown Trans vs Green Trans (FracZero)");
        }

        [Test]
        public void TestAdjustProspectToSail2TwoInputs()
        {
            // Arrange C# Inputs
            string sailVersion = "4SAIL2";
            //double[] lambda = { 400, 500, 600 };
            var leafConst = CreateSampleLeafOpticalConstants();
            double[] lambda = leafConst.Wavelength.ToArray();
            var inputs = new List<ProspectInputs> {
                   new ProspectInputs(cab: 50, car: 10), // Green input
                   new ProspectInputs(cab: 5, car: 1, brown: 0.5) // Brown input
              };
            double fractionBrown = 0.3; // Non-zero fraction
            //LeafOptics externalBrownLop = null;

            // Arrange R Inputs Dictionary
            var r_params = new Dictionary<string, object> {
                   {"sailVersion", sailVersion}, {"prospectConstants", leafConst},
                   {"inputProspectList", inputs}, // Pass list of two inputs
                   {"fractionBrown", fractionBrown},
                   {"brownLOP", null}
               };

            // Act (C#)
            var actual = AdjustProspectToSail(sailVersion, leafConst, inputs, fractionBrown, null);

            // Act (R)
            var r_results = RunRImplementation("adjust_PROSPECT_2_SAIL", r_params);
            // R code simulates both Green and Brown
            var expected = new AdjustedProspectResult
            {
                GreenLOP = new LeafOptics
                {
                    Wavelength = lambda,
                    Reflectance = ExtractDoubleArray(r_results["GreenLOP_Reflectance"]),
                    Transmittance = ExtractDoubleArray(r_results["GreenLOP_Transmittance"])
                },
                BrownLOP = new LeafOptics
                { // Expect distinct Brown LOP from R
                    Wavelength = lambda,
                    Reflectance = ExtractDoubleArray(r_results["BrownLOP_Reflectance"]),
                    Transmittance = ExtractDoubleArray(r_results["BrownLOP_Transmittance"])
                }
            };

            // Assert
            CompareAdjustedProspectResult(expected, actual, "adjust_PROSPECT_2_SAIL (TwoInput)");
        }

        [Test]
        public void TestNonConservativeScattering()
        {
            // Arrange C# Inputs
            //double[] wavelengths = { 500, 700 }; 
            //int n = wavelengths.Length;
            // Example plausible inputs (ensure m > 0.01)
            double[] m_in = { 0.1, 0.15 };
            double lai_in = 2.0;
            double[] att_in = { 0.9, 0.85 };
            double[] sigb_in = { 0.08, 0.07 }; // Ensure att^2 - m^2 = sigb^2 * (something > 0)
            double ks_in = 0.5;
            double ko_in = 0.6;
            double[] sf_in = { 0.2, 0.15 };
            double[] sb_in = { 0.3, 0.25 };
            double[] vf_in = { 0.25, 0.2 }; 
            double[] vb_in = { 0.35, 0.3 };
            double tss_in = Math.Exp(-ks_in * lai_in);
            double too_in = Math.Exp(-ko_in * lai_in);

            // Arrange R Inputs Dictionary
            var r_params = new Dictionary<string, object> {
                   {"m",m_in},{"lai",lai_in},{"att",att_in},{"sigb",sigb_in}, {"ks",ks_in},{"ko",ko_in},
                   {"sf",sf_in},{"sb",sb_in},{"vf",vf_in},{"vb",vb_in},{"tss",tss_in},{"too",too_in}
              };

            // Act (C#)
            var actual = NonConservativeScattering(m_in, lai_in, att_in, sigb_in, ks_in, ko_in, sf_in, sb_in, vf_in, vb_in, tss_in, too_in);

            // Act (R)
            var r_results = RunRImplementation("NonConservativeScattering", r_params);
            // R wrapper returns list directly
            var expected = new ScatteringResult
            {
                Tdd = ExtractDoubleArray(r_results["tdd"]),
                Rdd = ExtractDoubleArray(r_results["rdd"]),
                Tsd = ExtractDoubleArray(r_results["tsd"]),
                Rsd = ExtractDoubleArray(r_results["rsd"]),
                Tdo = ExtractDoubleArray(r_results["tdo"]),
                Rdo = ExtractDoubleArray(r_results["rdo"]),
                Rsod = ExtractDoubleArray(r_results["rsod"])
            };

            // Assert
            CompareScatteringResult(expected, actual, "NonConservativeScattering");
        }

        [Test]
        public void TestConservativeScattering()
        {
            // Arrange C# Inputs
            //double[] wavelengths = { 500, 700 }; 
            //int n = wavelengths.Length;
            // Example plausible inputs (ensure m <= 0.01, often near 0)
            double[] m_in = { 0.005, 0.001 };
            double lai_in = 2.0;
            // For near conservative, att ≈ sigb
            double[] att_in = { 0.5, 0.4 };
            double[] sigb_in = { 0.499, 0.399 }; // Close to att
            double ks_in = 0.5; 
            double ko_in = 0.6;
            double[] sf_in = { 0.2, 0.15 }; 
            double[] sb_in = { 0.3, 0.25 };
            double[] vf_in = { 0.25, 0.2 }; 
            double[] vb_in = { 0.35, 0.3 };
            double tss_in = Math.Exp(-ks_in * lai_in); 
            double too_in = Math.Exp(-ko_in * lai_in);

            // Arrange R Inputs Dictionary
            var r_params = new Dictionary<string, object> {
                   {"m",m_in},{"lai",lai_in},{"att",att_in},{"sigb",sigb_in}, {"ks",ks_in},{"ko",ko_in},
                   {"sf",sf_in},{"sb",sb_in},{"vf",vf_in},{"vb",vb_in},{"tss",tss_in},{"too",too_in}
              };

            // Act (C#)
            var actual = ConservativeScattering(m_in, lai_in, att_in, sigb_in, ks_in, ko_in, sf_in, sb_in, vf_in, vb_in, tss_in, too_in);

            // Act (R)
            var r_results = RunRImplementation("ConservativeScattering", r_params);
            // R wrapper returns list directly
            var expected = new ScatteringResult
            {
                Tdd = ExtractDoubleArray(r_results["tdd"]),
                Rdd = ExtractDoubleArray(r_results["rdd"]),
                Tsd = ExtractDoubleArray(r_results["tsd"]),
                Rsd = ExtractDoubleArray(r_results["rsd"]),
                Tdo = ExtractDoubleArray(r_results["tdo"]),
                Rdo = ExtractDoubleArray(r_results["rdo"]),
                Rsod = ExtractDoubleArray(r_results["rsod"])
            };

            // Assert
            CompareScatteringResult(expected, actual, "ConservativeScattering");
        }

    } // End TestFixture class
} // End namespace
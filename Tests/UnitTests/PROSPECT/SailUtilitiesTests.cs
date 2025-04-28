using NUnit.Framework; // Using NUnit framework
using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra; // Required for ProspectCore types if passed through
using Newtonsoft.Json; // For potential JSON input/output with R script
using Models.Sail; // Namespace for SailUtilities and its types
using Models.Prospect; // Namespace for ProspectCore and its types

namespace UnitTests
{
    [TestFixture]
    public class SailUtilitiesTests
    {
        // --- Configuration ---
        // TODO: Update these paths if using R script execution
        private readonly string RScriptPath = @"C:\Program Files\R\R-4.4.1\bin\Rscript.exe"; // Example path
        private readonly string RSailScriptWrapper = @"D:\Path\To\Your\SailUtilitiesWrapper.R"; // Wrapper for Lib_PROSAIL.R
        private readonly double Tolerance = 1e-6; // Tolerance for floating-point comparisons

        // --- Test Input Data Generation Helpers ---

        // Helper to create simple atmospheric data
        private SailUtilities.SpecAtmSensor CreateSampleAtm(double[] wavelengths)
        {
            int n = wavelengths.Length;
            return new SailUtilities.SpecAtmSensor
            {
                Wavelength = wavelengths,
                DirectLight = Enumerable.Repeat(1.0, n).ToArray(), // Example: Constant 1.0 W/m2/nm
                DiffuseLight = Enumerable.Repeat(0.2, n).ToArray() // Example: Constant 0.2 W/m2/nm
            };
        }

        // Helper to create simple soil properties
        private SailUtilities.SoilProperties CreateSampleSoil(double[] wavelengths)
        {
            int n = wavelengths.Length;
            return new SailUtilities.SoilProperties
            {
                Wavelength = wavelengths,
                Reflectance = Enumerable.Repeat(0.15, n).ToArray() // Example: Constant 0.15 reflectance
            };
        }

        // Helper to create sample PROSPECT constants (loads real data if possible)
        private ProspectCore.SpectralConstants CreateSampleProspectConstants(double[] wavelengths)
        {
            try
            {
                var constants = ProspectCore.LoadLocalSpectralData();
                // Optional: Interpolate or select wavelengths if needed to match the test 'wavelengths'
                // This example assumes the loaded constants match the test wavelengths implicitly
                if (!constants.Wavelength.ToArray().SequenceEqual(wavelengths))
                {
                    Console.WriteLine("Warning: Loaded spectral constants wavelengths differ from test wavelengths. Ensure compatibility.");
                    // Add interpolation/selection logic here if required.
                    // For now, return potentially mismatched data or fallback to dummy.
                }
                return constants;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WARNING: Failed to load real spectral data for tests: {ex.Message}. Using dummy data.");
                int n = wavelengths.Length;
                // Create basic dummy data structure matching wavelengths length
                return new ProspectCore.SpectralConstants
                {
                    Wavelength = Vector<double>.Build.DenseOfArray(wavelengths),
                    RefractiveIndex = Vector<double>.Build.Dense(n, 1.4),
                    SAC_CAB = Vector<double>.Build.Dense(n, 0.01),
                    SAC_CAR = Vector<double>.Build.Dense(n, 0.005),
                    SAC_EWT = Vector<double>.Build.Dense(n, 0.0001),
                    SAC_LMA = Vector<double>.Build.Dense(n, 0.0005),
                    Tav40 = Vector<double>.Build.Dense(n, 0.95),
                    Tav90 = Vector<double>.Build.Dense(n, 0.90),
                    SAC_ANT = Vector<double>.Build.Dense(n, 0.001),
                    SAC_BROWN = Vector<double>.Build.Dense(n, 0.002),
                    SAC_PROT = Vector<double>.Build.Dense(n, 0.0003),
                    SAC_CBC = Vector<double>.Build.Dense(n, 0.0004)
                };
            }
        }

        // Helper to create a sample LeafOptics object (e.g., for BrownLOP input)
        private SailUtilities.LeafOptics CreateSampleLeafOptics(double[] wavelengths)
        {
            int n = wavelengths.Length;
            return new SailUtilities.LeafOptics
            {
                Wavelength = wavelengths,
                Reflectance = Enumerable.Repeat(0.1, n).ToArray(), // Example brown leaf refl
                Transmittance = Enumerable.Repeat(0.01, n).ToArray() // Example brown leaf trans
            };
        }


        // --- Comparison Helper Methods ---

        private double CompareScalars(double expected, double actual)
        {
            return Math.Abs(expected - actual);
        }

        private double CompareArrays(double[] expected, double[] actual)
        {
            if (expected == null || actual == null) throw new ArgumentNullException("Input arrays cannot be null for comparison.");
            if (expected.Length != actual.Length) throw new ArgumentException($"Array length mismatch: Expected {expected.Length}, Actual {actual.Length}");
            if (expected.Length == 0) return 0.0; // Empty arrays are equal
            return expected.Zip(actual, (e, a) => Math.Abs(e - a)).Max();
        }

        // Add helpers for comparing custom structs if needed
        private double CompareFoliarDistribution(SailUtilities.FoliarDistributionResult expected, SailUtilities.FoliarDistributionResult actual)
        {
            double diffLidf = CompareArrays(expected.Lidf, actual.Lidf);
            double diffLitab = CompareArrays(expected.Litab, actual.Litab); // Litab should be exact ideally
            return Math.Max(diffLidf, diffLitab); // Return max difference found
        }

        private double CompareVolscattResult(SailUtilities.VolscattResult expected, SailUtilities.VolscattResult actual)
        {
            double diffChiS = CompareScalars(expected.Chi_s, actual.Chi_s);
            double diffChiO = CompareScalars(expected.Chi_o, actual.Chi_o);
            double diffFrho = CompareScalars(expected.Frho, actual.Frho);
            double diffFtau = CompareScalars(expected.Ftau, actual.Ftau);
            return new[] { diffChiS, diffChiO, diffFrho, diffFtau }.Max();
        }

        private double CompareScatteringResult(SailUtilities.ScatteringResult expected, SailUtilities.ScatteringResult actual)
        {
            double diffTdd = CompareArrays(expected.Tdd, actual.Tdd);
            double diffRdd = CompareArrays(expected.Rdd, actual.Rdd);
            double diffTsd = CompareArrays(expected.Tsd, actual.Tsd);
            double diffRsd = CompareArrays(expected.Rsd, actual.Rsd);
            double diffTdo = CompareArrays(expected.Tdo, actual.Tdo);
            double diffRdo = CompareArrays(expected.Rdo, actual.Rdo);
            double diffRsod = CompareArrays(expected.Rsod, actual.Rsod);
            return new[] { diffTdd, diffRdd, diffTsd, diffRsd, diffTdo, diffRdo, diffRsod }.Max();
        }

        private double CompareAdjustedProspectResult(SailUtilities.AdjustedProspectResult expected, SailUtilities.AdjustedProspectResult actual)
        {
            double diffGreenR = CompareArrays(expected.GreenLOP.Reflectance, actual.GreenLOP.Reflectance);
            double diffGreenT = CompareArrays(expected.GreenLOP.Transmittance, actual.GreenLOP.Transmittance);
            double diffBrownR = 0, diffBrownT = 0;
            if (expected.BrownLOP != null && actual.BrownLOP != null)
            {
                diffBrownR = CompareArrays(expected.BrownLOP.Reflectance, actual.BrownLOP.Reflectance);
                diffBrownT = CompareArrays(expected.BrownLOP.Transmittance, actual.BrownLOP.Transmittance);
            }
            else if (expected.BrownLOP != actual.BrownLOP) // One is null, the other isn't
            {
                return double.MaxValue; // Indicate mismatch
            }
            return new[] { diffGreenR, diffGreenT, diffBrownR, diffBrownT }.Max();
        }


        // --- R Script Execution (Placeholder) ---
        /// <summary>
        /// Executes a function from the Lib_PROSAIL.R script via a wrapper.
        /// *** PLACEHOLDER - Requires implementation ***
        /// </summary>
        /// <param name="functionName">The R function name (e.g., "Compute_BRF").</param>
        /// <param name="parameters">A dictionary or object containing function parameters.</param>
        /// <returns>A dictionary or object containing the results from R.</returns>
        private Dictionary<string, object> RunRImplementation(string functionName, Dictionary<string, object> parameters)
        {
            Console.WriteLine($"--- Running R Implementation Placeholder for: {functionName} ---");
            Console.WriteLine($"    Inputs: {JsonConvert.SerializeObject(parameters)}");
            Console.WriteLine($"    *** Requires actual R script execution implementation ***");
            Console.WriteLine($"    *** Returning DUMMY results - Replace with actual R output ***");

            // ** PLACEHOLDER IMPLEMENTATION **
            // 1. Serialize `parameters` to JSON (or another format R script can read).
            // 2. Write JSON to a temporary input file.
            // 3. Construct Rscript command: RScriptPath RSailScriptWrapper functionName inputFile outputFile
            // 4. Execute Rscript process (similar to ProspectTests.cs).
            // 5. Read and deserialize results JSON from the temporary output file.
            // 6. Handle errors from R process.
            // 7. Delete temporary files.

            // Return DUMMY data structure based on function name - MUST BE REPLACED
            var dummyResults = new Dictionary<string, object>();
            switch (functionName)
            {
                case "Compute_BRF":
                    dummyResults["BRF"] = new double[] { 0.1131729, 0.2131729, 0.3131729 }; // Example dummy
                    break;
                case "Compute_fAPAR":
                    dummyResults["fAPAR"] = 0.802454; // Example dummy
                    break;
                case "Compute_albedo":
                    dummyResults["albedo"] = 0.3409389; // Example dummy
                    break;
                case "campbell":
                    dummyResults["lidf"] = new double[] { 0.0019, 0.0073, 0.0168, 0.0316, 0.0526, 0.0801, 0.1127, 0.1448, 0.0666, 0.0708, 0.0754, 0.0798, 0.2596 };
                    dummyResults["litab"] = new double[] { 5.0, 15.0, 25.0, 35.0, 45.0, 55.0, 65.0, 75.0, 81.0, 83.0, 85.0, 87.0, 89.0 };
                    break;
                // Add cases for other functions...
                case "dladgen":
                    dummyResults["lidf"] = new double[] { /* Fill based on R output for specific a, b */ };
                    dummyResults["litab"] = new double[] { 5.0, 15.0, 25.0, 35.0, 45.0, 55.0, 65.0, 75.0, 81.0, 83.0, 85.0, 87.0, 89.0 };
                    break;
                case "dcum":
                    dummyResults["f"] = 0.0; // Scalar result
                    break;
                case "Jfunc1":
                case "Jfunc2":
                case "Jfunc3":
                case "Jfunc4":
                    dummyResults["Jout"] = 0.0; // Scalar result
                    break;
                case "volscatt":
                    dummyResults["chi_s"] = 0.0; dummyResults["chi_o"] = 0.0; dummyResults["frho"] = 0.0; dummyResults["ftau"] = 0.0;
                    break;
                case "adjust_PROSPECT_2_SAIL": // This returns a list containing dataframes in R
                                               // Represent structure - actual data needed from R
                    dummyResults["GreenLOP_Reflectance"] = new double[] { 0.1, 0.1, 0.1 };
                    dummyResults["GreenLOP_Transmittance"] = new double[] { 0.1, 0.1, 0.1 };
                    // BrownLOP might be null or same as Green depending on inputs
                    dummyResults["BrownLOP_Reflectance"] = new double[] { 0.1, 0.1, 0.1 };
                    dummyResults["BrownLOP_Transmittance"] = new double[] { 0.1, 0.1, 0.1 };
                    break;
                // ... other functions like NonConservativeScattering return multiple arrays ...
                case "NonConservativeScattering":
                case "ConservativeScattering":
                    dummyResults["tdd"] = new double[] { 0.1 }; dummyResults["rdd"] = new double[] { 0.1 };
                    dummyResults["tsd"] = new double[] { 0.1 }; dummyResults["rsd"] = new double[] { 0.1 };
                    dummyResults["tdo"] = new double[] { 0.1 }; dummyResults["rdo"] = new double[] { 0.1 };
                    dummyResults["rsod"] = new double[] { 0.1 };
                    break;
                default:
                    throw new NotImplementedException($"Dummy R result structure not defined for function: {functionName}");
            }
            return dummyResults;
        }


        // --- Test Methods for SailUtilities ---

        [Test]
        public void TestCompute_BRF_R_Comparison()
        {
            // Arrange C# Inputs
            double[] wavelengths = { 400, 500, 600 };
            double[] rdot_in = { 0.1, 0.2, 0.3 };
            double[] rsot_in = { 0.15, 0.25, 0.35 };
            double tts_in = 30.0;
            var atm_in = CreateSampleAtm(wavelengths);

            // Arrange R Inputs (match C#)
            var r_params = new Dictionary<string, object> {
                { "rdot", rdot_in },
                { "rsot", rsot_in },
                { "tts", tts_in },
                { "SpecATM_Sensor", new { Direct_Light = atm_in.DirectLight, Diffuse_Light = atm_in.DiffuseLight } } // R expects list/dataframe like structure
            };

            // Act (C#)
            double[] actual_brf = SailUtilities.Compute_BRF(rdot_in, rsot_in, tts_in, atm_in);

            // Act (R - Placeholder Call)
            var r_results = RunRImplementation("Compute_BRF", r_params);
            double[] expected_brf = (double[])r_results["BRF"]; // Extract result (assuming name 'BRF')

            // Assert
            double maxDiff = CompareArrays(expected_brf, actual_brf);
            Assert.That(maxDiff <= Tolerance, $"Compute_BRF Max Diff {maxDiff} exceeds tolerance {Tolerance}");
        }

        [Test]
        public void TestCompute_fAPAR_R_Comparison()
        {
            // Arrange C# Inputs
            double[] wavelengths = { 400, 550, 700, 800 }; // Include points inside and outside PAR range
            double[] abs_dir_in = { 0.8, 0.85, 0.75, 0.1 };
            double[] abs_hem_in = { 0.7, 0.75, 0.65, 0.05 };
            double tts_in = 45.0;
            var atm_in = CreateSampleAtm(wavelengths);

            // Arrange R Inputs
            var r_params = new Dictionary<string, object> {
                { "abs_dir", abs_dir_in },
                { "abs_hem", abs_hem_in },
                { "tts", tts_in },
                { "SpecATM_Sensor", new { Wavelength=atm_in.Wavelength, Direct_Light = atm_in.DirectLight, Diffuse_Light = atm_in.DiffuseLight } },
                { "PAR_range", new double[] {400, 700} } // Explicitly pass PAR range
            };

            // Act (C#)
            double actual_fapar = SailUtilities.Compute_fAPAR(abs_dir_in, abs_hem_in, tts_in, atm_in, 400, 700);

            // Act (R - Placeholder Call)
            var r_results = RunRImplementation("Compute_fAPAR", r_params);
            double expected_fapar = Convert.ToDouble(r_results["fAPAR"]); // Extract scalar result

            // Assert
            double maxDiff = CompareScalars(expected_fapar, actual_fapar);
            Assert.That(maxDiff <= Tolerance, $"Compute_fAPAR Diff {maxDiff} exceeds tolerance {Tolerance}");
        }

        [Test]
        public void TestCompute_albedo_R_Comparison()
        {
            // Arrange C# Inputs
            double[] wavelengths = { 400, 800, 1200, 1600, 2000, 2400 };
            double[] rsdstar_in = { 0.1, 0.4, 0.5, 0.4, 0.3, 0.2 };
            double[] rddstar_in = { 0.12, 0.42, 0.52, 0.42, 0.32, 0.22 };
            double tts_in = 20.0;
            var atm_in = CreateSampleAtm(wavelengths);
            double rangeMin = 400, rangeMax = 2400;

            // Arrange R Inputs
            var r_params = new Dictionary<string, object> {
                 { "rsdstar", rsdstar_in },
                 { "rddstar", rddstar_in },
                 { "tts", tts_in },
                 { "SpecATM_Sensor", new { Wavelength=atm_in.Wavelength, Direct_Light = atm_in.DirectLight, Diffuse_Light = atm_in.DiffuseLight } },
                 { "PAR_range", new double[] { rangeMin, rangeMax } } // R uses PAR_range param name
             };

            // Act (C#)
            double actual_albedo = SailUtilities.Compute_albedo(rsdstar_in, rddstar_in, tts_in, atm_in, rangeMin, rangeMax);

            // Act (R - Placeholder Call)
            var r_results = RunRImplementation("Compute_albedo", r_params);
            double expected_albedo = Convert.ToDouble(r_results["albedo"]);

            // Assert
            double maxDiff = CompareScalars(expected_albedo, actual_albedo);
            Assert.That(maxDiff <= Tolerance, $"Compute_albedo Diff {maxDiff} exceeds tolerance {Tolerance}");
        }

        [Test]
        public void TestCampbell_R_Comparison()
        {
            // Arrange C# Inputs
            double ala_in = 57.0; // Standard spherical angle approx

            // Arrange R Inputs
            var r_params = new Dictionary<string, object> { { "ala", ala_in } };

            // Act (C#)
            var actualResult = SailUtilities.Campbell(ala_in);

            // Act (R - Placeholder Call)
            var r_results = RunRImplementation("campbell", r_params);
            var expectedResult = new SailUtilities.FoliarDistributionResult
            {
                Lidf = (double[])r_results["lidf"],
                Litab = (double[])r_results["litab"]
            };

            // Assert
            double maxDiff = CompareFoliarDistribution(expectedResult, actualResult);
            Assert.That(maxDiff <= Tolerance, $"Campbell Max Diff {maxDiff} exceeds tolerance {Tolerance}");
        }

        [Test]
        public void TestDladgen_R_Comparison()
        {
            // Arrange C# Inputs
            double a_in = -0.35; // Example: Spherical approx
            double b_in = -0.15;

            // Arrange R Inputs
            var r_params = new Dictionary<string, object> { { "a", a_in }, { "b", b_in } };

            // Act (C#)
            var actualResult = SailUtilities.Dladgen(a_in, b_in);

            // Act (R - Placeholder Call)
            var r_results = RunRImplementation("dladgen", r_params);
            var expectedResult = new SailUtilities.FoliarDistributionResult
            {
                Lidf = (double[])r_results["lidf"],
                Litab = (double[])r_results["litab"]
            };

            // Assert
            double maxDiff = CompareFoliarDistribution(expectedResult, actualResult);
            Assert.That(maxDiff <= Tolerance, $"Dladgen Max Diff {maxDiff} exceeds tolerance {Tolerance}");
        }

        [Test]
        public void TestDcum_R_Comparison()
        {
            // Arrange C# Inputs
            double a_in = 0.0; double b_in = 0.0; double t_in = 45.0; // Uniform case, 45 deg

            // Arrange R Inputs
            var r_params = new Dictionary<string, object> { { "a", a_in }, { "b", b_in }, { "t", t_in } };

            // Act (C#)
            double actualResult = SailUtilities.Dcum(a_in, b_in, t_in);

            // Act (R - Placeholder Call)
            var r_results = RunRImplementation("dcum", r_params);
            double expectedResult = Convert.ToDouble(r_results["f"]);

            // Assert
            double maxDiff = CompareScalars(expectedResult, actualResult);
            Assert.That(maxDiff <= Tolerance, $"Dcum Diff {maxDiff} exceeds tolerance {Tolerance}");
        }


        // --- Tests for J Functions ---
        [TestCase(1.0, 0.5, 1.0)] // Example inputs k, l, t
        [TestCase(0.5, 0.5, 1.0)] // Test singularity case k=l
        [TestCase(0.001, 0.0005, 1.0)]
        public void TestJfunc1_R_Comparison(double k, double l, double t)
        {
            var r_params = new Dictionary<string, object> { { "k", k }, { "l", l }, { "t", t } };
            double actual = SailUtilities.Jfunc1(k, l, t);
            var r_results = RunRImplementation("Jfunc1", r_params);
            double expected = Convert.ToDouble(r_results["Jout"]);
            Assert.That(CompareScalars(expected, actual) <= Tolerance, $"Jfunc1(k={k},l={l},t={t}) Diff");
        }

        [TestCase(1.0, 0.5, 1.0)]
        [TestCase(0.5, -0.5, 1.0)] // Test k+l = 0 case
        [TestCase(0.001, 0.0005, 1.0)]
        public void TestJfunc2_R_Comparison(double k, double l, double t)
        {
            var r_params = new Dictionary<string, object> { { "k", k }, { "l", l }, { "t", t } };
            double actual = SailUtilities.Jfunc2(k, l, t);
            var r_results = RunRImplementation("Jfunc2", r_params);
            double expected = Convert.ToDouble(r_results["Jout"]);
            Assert.That(CompareScalars(expected, actual) <= Tolerance, $"Jfunc2(k={k},l={l},t={t}) Diff");
        }

        // TestJfunc3 omitted as it's identical to Jfunc2 in the provided code

        [TestCase(0.5, 1.0)] // Example inputs m, t
        [TestCase(0.0001, 1.0)] // Test near-zero case
        [TestCase(0.0, 1.0)] // Test zero case
        public void TestJfunc4_R_Comparison(double m, double t)
        {
            var r_params = new Dictionary<string, object> { { "m", m }, { "t", t } };
            double actual = SailUtilities.Jfunc4(m, t);
            var r_results = RunRImplementation("Jfunc4", r_params);
            double expected = Convert.ToDouble(r_results["Jout"]);
            Assert.That(CompareScalars(expected, actual) <= Tolerance, $"Jfunc4(m={m},t={t}) Diff");
        }

        // --- Test for Volscatt ---
        [Test]
        public void TestVolscatt_R_Comparison()
        {
            // Arrange C# Inputs
            double tts = 30, tto = 10, psi = 60, ttl = 45;

            // Arrange R Inputs
            var r_params = new Dictionary<string, object> { { "tts", tts }, { "tto", tto }, { "psi", psi }, { "ttl", ttl } };

            // Act (C#)
            var actual = SailUtilities.Volscatt(tts, tto, psi, ttl);

            // Act (R - Placeholder Call)
            var r_results = RunRImplementation("volscatt", r_params);
            var expected = new SailUtilities.VolscattResult
            {
                Chi_s = Convert.ToDouble(r_results["chi_s"]),
                Chi_o = Convert.ToDouble(r_results["chi_o"]),
                Frho = Convert.ToDouble(r_results["frho"]),
                Ftau = Convert.ToDouble(r_results["ftau"])
            };

            // Assert
            double maxDiff = CompareVolscattResult(expected, actual);
            Assert.That(maxDiff <= Tolerance, $"Volscatt Max Diff {maxDiff} exceeds tolerance {Tolerance}");
        }

        // --- Tests for Check Functions ---
        [Test]
        public void TestCheckSpectralSampling_Valid()
        {
            // Arrange
            double[] lambda = { 400, 500, 600 };
            var prospectConst = CreateSampleProspectConstants(lambda); // Uses lambda
            var soil = CreateSampleSoil(lambda); // Uses lambda
            var atm = CreateSampleAtm(lambda); // Uses lambda

            // Act & Assert
            Assert.DoesNotThrow(() => SailUtilities.check_SpectralSampling(prospectConst, soil, atm),
                "check_SpectralSampling should not throw for valid matching inputs.");
        }

        [Test]
        public void TestCheckSpectralSampling_LengthMismatch()
        {
            // Arrange
            double[] lambda1 = { 400, 500, 600 };
            double[] lambda2 = { 400, 500 }; // Different length
            var prospectConst = CreateSampleProspectConstants(lambda1);
            var soil = CreateSampleSoil(lambda2);
            var atm = CreateSampleAtm(lambda1);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => SailUtilities.check_SpectralSampling(prospectConst, soil, atm),
                "check_SpectralSampling should throw ArgumentException for length mismatch.");
        }

        [Test]
        public void TestCheckSpectralSampling_ValueMismatch()
        {
            // Arrange
            double[] lambda1 = { 400, 500, 600 };
            double[] lambda2 = { 400, 501, 600 }; // Different value
            var prospectConst = CreateSampleProspectConstants(lambda1);
            var soil = CreateSampleSoil(lambda1); // Use lambda1 here
            var atm = CreateSampleAtm(lambda2); // Use lambda2 here

            // Act & Assert
            Assert.Throws<ArgumentException>(() => SailUtilities.check_SpectralSampling(prospectConst, soil, atm),
                "check_SpectralSampling should throw ArgumentException for value mismatch.");
        }


        [Test]
        public void TestCheckBrownLOP_Valid()
        {
            // Arrange
            double[] lambda = { 400, 500, 600 };
            var brownLop = CreateSampleLeafOptics(lambda);
            var inputs = new List<SailUtilities.ProspectInput> { new SailUtilities.ProspectInput() }; // Single input

            // Act & Assert
            Assert.DoesNotThrow(() => SailUtilities.check_BrownLOP(brownLop, lambda, inputs),
                "check_BrownLOP should not throw for valid BrownLOP.");
        }

        [Test]
        public void TestCheckBrownLOP_NullLambda()
        {
            // Arrange
            double[] lambda = { 400, 500, 600 };
            var brownLop = CreateSampleLeafOptics(lambda); // Valid LOP structure
            brownLop.Wavelength = null; // Make wavelength null
            var inputs = new List<SailUtilities.ProspectInput> { new SailUtilities.ProspectInput() };

            // Act & Assert
            Assert.Throws<ArgumentException>(() => SailUtilities.check_BrownLOP(brownLop, lambda, inputs),
                "check_BrownLOP should throw ArgumentException for null Wavelength in BrownLOP.");
        }

        [Test]
        public void TestCheckBrownLOP_SpectralMismatch()
        {
            // Arrange
            double[] lambdaRef = { 400, 500, 600 };
            double[] lambdaBrown = { 400, 501, 600 }; // Different wavelength
            var brownLop = CreateSampleLeafOptics(lambdaBrown);
            var inputs = new List<SailUtilities.ProspectInput> { new SailUtilities.ProspectInput() };

            // Act & Assert
            Assert.Throws<ArgumentException>(() => SailUtilities.check_BrownLOP(brownLop, lambdaRef, inputs),
                "check_BrownLOP should throw ArgumentException for spectral mismatch.");
        }

        [Test]
        public void TestCheckBrownLOP_MultipleInputsWarning()
        {
            // Arrange
            double[] lambda = { 400, 500, 600 };
            var brownLop = CreateSampleLeafOptics(lambda);
            var inputs = new List<SailUtilities.ProspectInput> { // Multiple inputs
                 new SailUtilities.ProspectInput(),
                 new SailUtilities.ProspectInput()
             };
            // Redirect Console.Out to capture warning (optional, more complex setup)
            // StringWriter sw = new StringWriter();
            // var originalOut = Console.Out;
            // Console.SetOut(sw);

            // Act & Assert
            Assert.DoesNotThrow(() => SailUtilities.check_BrownLOP(brownLop, lambda, inputs),
                "check_BrownLOP should not throw when multiple inputs are present (only warn).");

            // Assert warning message was printed (optional)
            // Console.SetOut(originalOut); // Restore console output
            // string output = sw.ToString();
            // Assert.That(output.Contains("Warning: External BrownLOP provided along with multiple PROSPECT input"));
        }


        // --- Tests for adjust_PROSPECT_2_SAIL ---
        // These are more like integration tests as they call ProspectCore

        [Test]
        public void TestAdjustProspect_4SAIL()
        {
            // Arrange
            string sailVersion = "4SAIL";
            double[] lambda = { 400, 500, 600 };
            var prospectConst = CreateSampleProspectConstants(lambda);
            var inputs = new List<SailUtilities.ProspectInput> {
                  new SailUtilities.ProspectInput(cab: 50, car: 10) // Single input set for green
             };
            double fractionBrown = 0.0; // Not used by 4SAIL logic path

            // Expected: Should run Prospect for Green, BrownLOP should be null
            // We need the R reference for the *output* of PROSPECT called with greenIn parameters
            // And BrownLOP should be null.

            // Arrange R Inputs (Hypothetical - R test needs PROSPECT call)
            var r_params = new Dictionary<string, object> {
                  {"SAILversion", sailVersion},
                  {"Spec_Sensor", new { lambda=lambda /* ... add other R spectr fields */}}, // R needs full spectral data
                  {"Input_PROSPECT", new List<object> { // List containing one set of params
                         new { N=inputs[0].N, CHL=inputs[0].CAB, CAR=inputs[0].CAR, ANT=inputs[0].ANT, BROWN=inputs[0].BROWN, EWT=inputs[0].EWT, LMA=inputs[0].LMA, PROT=inputs[0].PROT, CBC=inputs[0].CBC, alpha=inputs[0].Alpha }
                  }},
                  {"fraction_brown", fractionBrown},
                  {"BrownLOP", null}
             };

            // Act (C#)
            var actual = SailUtilities.adjust_PROSPECT_2_SAIL(sailVersion, prospectConst, inputs, fractionBrown, null);

            // Act (R - Placeholder Call)
            // NOTE: R implementation needs prospect::PROSPECT call inside.
            // Wrapper needs to return structure similar to AdjustedProspectResult
            var r_results = RunRImplementation("adjust_PROSPECT_2_SAIL", r_params);
            var expected = new SailUtilities.AdjustedProspectResult
            {
                GreenLOP = new SailUtilities.LeafOptics
                {
                    Wavelength = lambda,
                    Reflectance = (double[])r_results["GreenLOP_Reflectance"], // Extract from R result
                    Transmittance = (double[])r_results["GreenLOP_Transmittance"] // Extract from R result
                },
                BrownLOP = null // Expect null for 4SAIL
            };


            // Assert
            Assert.IsNotNull(actual.GreenLOP, "GreenLOP should not be null for 4SAIL.");
            Assert.IsNull(actual.BrownLOP, "BrownLOP should be null for 4SAIL.");
            double maxDiff = CompareAdjustedProspectResult(expected, actual); // Compares GreenLOP only effectively
            Assert.That(maxDiff <= Tolerance, $"adjust_PROSPECT_2_SAIL (4SAIL) Max Diff {maxDiff} exceeds tolerance {Tolerance}");
        }

        [Test]
        public void TestAdjustProspect_4SAIL2_ExternalBrownLOP()
        {
            // Arrange
            string sailVersion = "4SAIL2";
            double[] lambda = { 400, 500, 600 };
            var prospectConst = CreateSampleProspectConstants(lambda);
            var inputs = new List<SailUtilities.ProspectInput> {
                   new SailUtilities.ProspectInput(cab: 50, car: 10) // Green input (used)
                   // ,new SailUtilities.ProspectInput(cab: 5, car: 1) // Brown input (ignored if external BrownLOP given)
              };
            double fractionBrown = 0.3;
            var externalBrownLop = CreateSampleLeafOptics(lambda); // Provide external BrownLOP

            // Expected: Should run Prospect for Green, should use externalBrownLop directly

            // Arrange R Inputs
            var r_params = new Dictionary<string, object> {
                   {"SAILversion", sailVersion},
                   {"Spec_Sensor", new { lambda=lambda /* ... */}},
                   {"Input_PROSPECT", new List<object> { // Only first set matters here
                         new { N=inputs[0].N, CHL=inputs[0].CAB, CAR=inputs[0].CAR, /*...*/ alpha=inputs[0].Alpha }
                   }},
                   {"fraction_brown", fractionBrown},
                   {"BrownLOP", new { // Structure matching R expectations
                          Wavelength = externalBrownLop.Wavelength,
                          Reflectance = externalBrownLop.Reflectance,
                          Transmittance = externalBrownLop.Transmittance
                   }}
              };

            // Act (C#)
            var actual = SailUtilities.adjust_PROSPECT_2_SAIL(sailVersion, prospectConst, inputs, fractionBrown, externalBrownLop);

            // Act (R - Placeholder Call)
            var r_results = RunRImplementation("adjust_PROSPECT_2_SAIL", r_params);
            var expected = new SailUtilities.AdjustedProspectResult
            {
                GreenLOP = new SailUtilities.LeafOptics
                {
                    Wavelength = lambda,
                    Reflectance = (double[])r_results["GreenLOP_Reflectance"],
                    Transmittance = (double[])r_results["GreenLOP_Transmittance"]
                },
                BrownLOP = new SailUtilities.LeafOptics
                { // Expect external LOP data (from R call)
                    Wavelength = lambda,
                    Reflectance = (double[])r_results["BrownLOP_Reflectance"],
                    Transmittance = (double[])r_results["BrownLOP_Transmittance"]
                }
            };


            // Assert
            Assert.IsNotNull(actual.GreenLOP, "GreenLOP should not be null.");
            Assert.IsNotNull(actual.BrownLOP, "BrownLOP should not be null when provided externally.");
            // Compare C# result's BrownLOP against the *provided* external one
            Assert.That(CompareArrays(externalBrownLop.Reflectance, actual.BrownLOP.Reflectance) <= Tolerance, "External BrownLOP Reflectance mismatch.");
            Assert.That(CompareArrays(externalBrownLop.Transmittance, actual.BrownLOP.Transmittance) <= Tolerance, "External BrownLOP Transmittance mismatch.");
            // Compare C# GreenLOP against R GreenLOP
            double maxDiffGreen = CompareArrays(expected.GreenLOP.Reflectance, actual.GreenLOP.Reflectance);
            Assert.That(maxDiffGreen <= Tolerance, $"adjust_PROSPECT_2_SAIL (ExtBrown) Green Refl Diff {maxDiffGreen}");
            // Potentially compare C# BrownLOP against R BrownLOP if R wrapper returns it correctly
            // double maxDiff = CompareAdjustedProspectResult(expected, actual);
            // Assert.That(maxDiff <= Tolerance, $"adjust_PROSPECT_2_SAIL (ExtBrown) Max Diff {maxDiff}");
        }

        [Test]
        public void TestAdjustProspect_4SAIL2_FractionZero()
        {
            // Arrange
            string sailVersion = "4SAIL2";
            double[] lambda = { 400, 500, 600 };
            var prospectConst = CreateSampleProspectConstants(lambda);
            var inputs = new List<SailUtilities.ProspectInput> {
                   new SailUtilities.ProspectInput(cab: 50, car: 10) // Green input
              };
            double fractionBrown = 0.0; // Zero brown fraction
            SailUtilities.LeafOptics externalBrownLop = null; // No external LOP

            // Expected: Should run Prospect for Green, BrownLOP should be identical to GreenLOP

            // Arrange R Inputs
            // ... setup r_params similar to above, fraction_brown = 0, BrownLOP = null ...
            var r_params = new Dictionary<string, object> { /* ... */ }; // Fill based on R test case

            // Act (C#)
            var actual = SailUtilities.adjust_PROSPECT_2_SAIL(sailVersion, prospectConst, inputs, fractionBrown, externalBrownLop);

            // Act (R - Placeholder Call)
            // var r_results = RunRImplementation("adjust_PROSPECT_2_SAIL", r_params);
            // var expected = ... // Setup expected based on R result (Brown=Green)

            // Assert
            Assert.IsNotNull(actual.GreenLOP, "GreenLOP should not be null.");
            Assert.IsNotNull(actual.BrownLOP, "BrownLOP should not be null when fraction is zero.");
            // Check that BrownLOP matches GreenLOP
            Assert.That(CompareArrays(actual.GreenLOP.Reflectance, actual.BrownLOP.Reflectance) <= Tolerance, "BrownLOP Reflectance should match GreenLOP for fraction_brown=0.");
            Assert.That(CompareArrays(actual.GreenLOP.Transmittance, actual.BrownLOP.Transmittance) <= Tolerance, "BrownLOP Transmittance should match GreenLOP for fraction_brown=0.");
            // Compare against R reference if available
            // double maxDiff = CompareAdjustedProspectResult(expected, actual);
            // Assert.That(maxDiff <= Tolerance, $"adjust_PROSPECT_2_SAIL (FracZero) Max Diff {maxDiff}");
        }

        [Test]
        public void TestAdjustProspect_4SAIL2_TwoInputs()
        {
            // Arrange
            string sailVersion = "4SAIL2";
            double[] lambda = { 400, 500, 600 };
            var prospectConst = CreateSampleProspectConstants(lambda);
            var inputs = new List<SailUtilities.ProspectInput> {
                   new SailUtilities.ProspectInput(cab: 50, car: 10), // Green input
                   new SailUtilities.ProspectInput(cab: 5, car: 1, brown: 0.5) // Brown input
              };
            double fractionBrown = 0.3; // Non-zero fraction
            SailUtilities.LeafOptics externalBrownLop = null; // No external LOP

            // Expected: Should run Prospect for Green (input[0]), and Brown (input[1])

            // Arrange R Inputs
            // ... setup r_params with two Input_PROSPECT entries ...
            var r_params = new Dictionary<string, object> { /* ... */ }; // Fill based on R test case

            // Act (C#)
            var actual = SailUtilities.adjust_PROSPECT_2_SAIL(sailVersion, prospectConst, inputs, fractionBrown, externalBrownLop);

            // Act (R - Placeholder Call)
            // var r_results = RunRImplementation("adjust_PROSPECT_2_SAIL", r_params);
            // var expected = ... // Setup expected based on R result (distinct Green/Brown LOPs)

            // Assert
            Assert.IsNotNull(actual.GreenLOP, "GreenLOP should not be null.");
            Assert.IsNotNull(actual.BrownLOP, "BrownLOP should not be null when two inputs are provided.");
            // Check BrownLOP is likely different from GreenLOP (basic check)
            Assert.Greater(CompareArrays(actual.GreenLOP.Reflectance, actual.BrownLOP.Reflectance), Tolerance, "BrownLOP should differ from GreenLOP when simulated from different inputs.");
            // Compare against R reference
            // double maxDiff = CompareAdjustedProspectResult(expected, actual);
            // Assert.That(maxDiff <= Tolerance, $"adjust_PROSPECT_2_SAIL (TwoInput) Max Diff {maxDiff}");
        }

        // TODO: Add tests for Scattering functions (NonConservativeScattering, ConservativeScattering)
        // These will require careful setup of input arrays (m, att, sigb, sf, sb, vf, vb) and scalars (lai, ks, ko, tss, too)
        // Example Structure:
        /*
        [Test]
        public void TestNonConservativeScattering_R_Comparison()
        {
            // Arrange C# Inputs
            double[] wavelengths = { 500, 700 };
            int n = wavelengths.Length;
            double[] m_in = { 0.1, 0.05 };
            double lai_in = 2.0;
            double[] att_in = { 0.9, 0.8 };
            double[] sigb_in = { 0.1, 0.05 };
            double ks_in = 0.5;
            double ko_in = 0.6;
            double[] sf_in = { 0.2, 0.15 };
            double[] sb_in = { 0.3, 0.25 };
            double[] vf_in = { 0.25, 0.2 };
            double[] vb_in = { 0.35, 0.3 };
            double tss_in = Math.Exp(-ks_in * lai_in);
            double too_in = Math.Exp(-ko_in * lai_in);

             // Arrange R Inputs (dictionary)
             // ...

             // Act (C#)
             var actual = SailUtilities.NonConservativeScattering(m_in, lai_in, att_in, sigb_in, ks_in, ko_in, sf_in, sb_in, vf_in, vb_in, tss_in, too_in);

             // Act (R - Placeholder)
             // var r_results = RunRImplementation("NonConservativeScattering", r_params);
             // var expected = ... // Populate expected ScatteringResult from r_results

             // Assert
             // double maxDiff = CompareScatteringResult(expected, actual);
             // Assert.That(maxDiff <= Tolerance, $"NonConservativeScattering Max Diff {maxDiff}");
        }
        */

    }
}
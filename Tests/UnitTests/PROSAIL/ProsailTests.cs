using APSIM.Core;
using APSIM.Shared.Utilities;
using DocumentFormat.OpenXml.Wordprocessing;
using MathNet.Numerics.LinearAlgebra;
using Models;
using Models.PostSimulationTools;
using Models.Prosail;
using Models.PROSAIL;
using Models.PROSAIL.BSM;
using Models.PROSAIL.PROSPECT;
using Models.PROSAIL.Sail;
using Models.PROSAIL.SAIL;
using Newtonsoft.Json;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using static Models.Prosail.ProsailCore;
using static Models.PROSAIL.PROSPECT.ProspectCore;
using static Models.PROSAIL.SAIL.SailUtilities;

namespace UnitTests.PROSAIL
{
    [TestFixture]
    public class ProsailCoreTests
    {
        private static readonly string RelativeTestInputPath = "..\\..\\..\\Tests\\UnitTests\\PROSAIL\\fourSailTestInputs.json";
        private static string TestInputPath => PathUtilities.GetAbsolutePath(RelativeTestInputPath, AppDomain.CurrentDomain.BaseDirectory);

        private static readonly string RelativeSpecSoilDataPath = "..\\..\\..\\Tests\\UnitTests\\PROSAIL\\SpecSOIL.json";
        private static string DefaultSpecSoilDataPath => PathUtilities.GetAbsolutePath(RelativeSpecSoilDataPath, AppDomain.CurrentDomain.BaseDirectory);

        // Path to Rscript executable
        private readonly string RScriptPath = RScriptLocator.FindRscriptPath();

        // SailUtilitiesWrapper.R path
        private static readonly string RelativeRSailScriptWrapperPath = "..\\..\\..\\Tests\\UnitTests\\PROSAIL\\SailUtilitiesWrapper.R";
        private static string RSailScriptWrapperPath => PathUtilities.GetAbsolutePath(RelativeRSailScriptWrapperPath, AppDomain.CurrentDomain.BaseDirectory);


        private readonly double Tolerance = 1e-3;

        [OneTimeSetUp]
        public void CheckRSetup()
        {
            Assert.That(File.Exists(RScriptPath), $"Rscript executable not found. Install R to run these tests.");
        }

        // Helper to load test inputs
        private static List<ProsailTestInput> LoadTestInputs()
        {
            string json = File.ReadAllText(TestInputPath);
            return JsonConvert.DeserializeObject<List<ProsailTestInput>>(json);
        }

        // Helper to compare arrays
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

        // Helper to extract double array from R result
        private static double[] ExtractDoubleArray(object rResultValue)
        {
            if (rResultValue == null) return null;
            if (rResultValue is double[] dArray) return dArray;
            if (rResultValue is Newtonsoft.Json.Linq.JArray jArray) return jArray.ToObject<double[]>();
            if (rResultValue is List<object> objList) return objList.Select(Convert.ToDouble).ToArray();
            throw new InvalidCastException($"Cannot convert R result value of type {rResultValue.GetType()} to double[]. Value: {rResultValue}");
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
                string arguments = $"\"{RSailScriptWrapperPath}\" \"{functionName}\" \"{tempInputFile}\" \"{tempOutputFile}\"";

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
                    // Read output/error streams
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();

                    // Wait for the process to exit (with a timeout)
                    bool exited = process.WaitForExit(60000); // 60 second timeout

                    if (!exited)
                    {
                        try { process.Kill(); } catch { }
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

        public class ProsailTestInput
        {
            public double N { get; set; }
            public double CAB { get; set; }
            public double CAR { get; set; }
            public double EWT { get; set; }
            public double LMA { get; set; }
            public double ANT { get; set; }
            public double BROWN { get; set; }
            public double PROT { get; set; }
            public double CBC { get; set; }
            public double Alpha { get; set; }
            public double LAI { get; set; }
            public double HotSpot { get; set; }
            public int TypeLidf { get; set; }
            public double LIDFa { get; set; }
            public double? LIDFb { get; set; }
            public double FractionBrown { get; set; }
            public double Dissociation { get; set; }
            public double CrownCover { get; set; }
            public double TreeShape { get; set; }
            public double Psoil { get; set; }
            public double SunZenithAngle { get; set; }
            public double ObserverZenithAngle { get; set; }
            public double RelativeAzimuthAngle { get; set; }
            public string SailVersion { get; set; }

            // Brown leaf PROSPECT parameters (optional; when present, triggers 2-input mode for 4SAIL2)
            public double? BrownN     { get; set; }
            public double? BrownCAB   { get; set; }
            public double? BrownCAR   { get; set; }
            public double? BrownEWT   { get; set; }
            public double? BrownLMA   { get; set; }
            public double? BrownANT   { get; set; }
            public double? BrownBROWN { get; set; }
            public double? BrownPROT  { get; set; }
            public double? BrownCBC   { get; set; }
        }

        [Test]
        public void ProsailValidationTest_fourSail()
        {
            var testInputs = LoadTestInputs().Where(t => t.SailVersion == "4SAIL").ToList();
            LeafOpticalConsts leafConst = GetCachedLeafOpticalConstants();
            WetDrySoilReflectance cachedWetDrySoilReflectance = LoadWetDrySoilReflectanData(DefaultSpecSoilDataPath);
            
            foreach (var testInput in testInputs)
            {
                Console.WriteLine($"Testing with input parameters: LAI={testInput.LAI:F2}, " +
                    $"N ={testInput.N:F2}, CAB={testInput.CAB:F2}, CAR={testInput.CAR:F2}, " +
                    $"LMA={testInput.LMA:F2}, " +
                    $"SunZenith={testInput.SunZenithAngle:F2}, ObserverZenith={testInput.ObserverZenithAngle:F2}, " +
                    $"RelativeAzimuth={testInput.RelativeAzimuthAngle:F2}, HotSpot={testInput.HotSpot:F2}, " +
                    $"TypeLidf={testInput.TypeLidf}, LIDFa={testInput.LIDFa:F2}, FractionBrown={testInput.FractionBrown:F2}, " +
                    $"Psoil={testInput.Psoil:F2}");

                SoilOptics soil = CalculateSoilReflectanceFromWetDry(cachedWetDrySoilReflectance,
                    testInput.Psoil);

                // C# implementation
                var resultCS = ProsailCore.PRO4SAIL(
                    leafOpticalConstants: leafConst,
                    N: testInput.N,
                    CAB: testInput.CAB,
                    CAR: testInput.CAR,
                    ANT: testInput.ANT,
                    BROWN: testInput.BROWN, 
                    EWT: testInput.EWT,
                    LMA: testInput.LMA,
                    PROT: testInput.PROT,
                    CBC: testInput.CBC,
                    Alpha: testInput.Alpha,
                    TypeLidf: testInput.TypeLidf,
                    LIDFa: testInput.LIDFa,
                    LIDFb: testInput.LIDFb,
                    LAI: testInput.LAI,
                    HotSpot: testInput.HotSpot,
                    TTS: testInput.SunZenithAngle,
                    TTO: testInput.ObserverZenithAngle,
                    PSI: testInput.RelativeAzimuthAngle,
                    FractionBrown: testInput.FractionBrown,
                    Diss: testInput.Dissociation,
                    Cv: testInput.CrownCover,
                    Zeta: testInput.TreeShape,
                    SoilReflectance: soil,
                    SailVersion: testInput.SailVersion
                );

                // R implementation
                var r_params = new Dictionary<string, object>
                {
                    { "N", testInput.N },
                    { "CAB", testInput.CAB },
                    { "CAR", testInput.CAR },
                    { "ANT", testInput.ANT },
                    { "BROWN", testInput.BROWN },
                    { "EWT", testInput.EWT },
                    { "LMA", testInput.LMA },
                    { "PROT", testInput.PROT },
                    { "CBC", testInput.CBC },
                    { "alpha", testInput.Alpha },
                    { "TypeLidf", testInput.TypeLidf },
                    { "LIDFa", testInput.LIDFa },
                    { "LIDFb", testInput.LIDFb ?? 0.0 }, // Use 0 if null
                    { "lai", testInput.LAI },
                    { "q", testInput.HotSpot },
                    { "tts", testInput.SunZenithAngle },
                    { "tto", testInput.ObserverZenithAngle },
                    { "psi", testInput.RelativeAzimuthAngle },
                    { "fraction_brown", testInput.FractionBrown },
                    { "diss", testInput.Dissociation },
                    { "Cv", testInput.CrownCover },
                    { "Zeta", testInput.TreeShape },
                    { "rsoil", soil.Reflectance },
                    { "SailVersion", testInput.SailVersion }
                    
                };
                var r_results = RunRImplementation("PRO4SAIL", r_params);

                CompareArrays(ExtractDoubleArray(r_results["rdot"]), resultCS.Rdot, "rdot");
                CompareArrays(ExtractDoubleArray(r_results["rsot"]), resultCS.Rsot, "rsot");
                CompareArrays(ExtractDoubleArray(r_results["rddt"]), resultCS.Rddt, "rddt");
                CompareArrays(ExtractDoubleArray(r_results["rsdt"]), resultCS.Rsdt, "rsdt");
                CompareArrays(ExtractDoubleArray(r_results["abs_dir"]), resultCS.Abs_dir, "abs_dir");
                CompareArrays(ExtractDoubleArray(r_results["abs_hem"]), resultCS.Abs_hem, "abs_hem");
                CompareArrays(ExtractDoubleArray(r_results["rsdstar"]), resultCS.Rsdstar, "rsdstar");
                CompareArrays(ExtractDoubleArray(r_results["rddstar"]), resultCS.Rddstar, "rddstar");
                //CompareArrays(ExtractDoubleArray(r_results["fCover"]), resultCS.FCover, "fCover");
            }
        }

        [Test]
        public void ProsailValidationTest_fourSail2()
        {
            var testInputs = LoadTestInputs().Where(t => t.SailVersion == "4SAIL2").ToList();
            LeafOpticalConsts leafConst = GetCachedLeafOpticalConstants();
            WetDrySoilReflectance cachedWetDrySoilReflectance = LoadWetDrySoilReflectanData(DefaultSpecSoilDataPath);

            foreach (var testInput in testInputs)
            {
                Console.WriteLine($"Testing with input parameters: LAI={testInput.LAI:F2}, " +
                    $"N={testInput.N:F2}, CAB={testInput.CAB:F2}, CAR={testInput.CAR:F2}, LMA={testInput.LMA:F2}, " +
                    $"SunZenith={testInput.SunZenithAngle:F2}, ObserverZenith={testInput.ObserverZenithAngle:F2}, " +
                    $"RelativeAzimuth={testInput.RelativeAzimuthAngle:F2}, HotSpot={testInput.HotSpot:F2}, " +
                    $"TypeLidf={testInput.TypeLidf}, LIDFa={testInput.LIDFa:F2}, " +
                    $"FractionBrown={testInput.FractionBrown:F2}, Diss={testInput.Dissociation:F2}, " +
                    $"Cv={testInput.CrownCover:F2}, Zeta={testInput.TreeShape:F2}, " +
                    $"BrownInputs={testInput.BrownN.HasValue}");

                SoilOptics soil = CalculateSoilReflectanceFromWetDry(cachedWetDrySoilReflectance,
                    testInput.Psoil);

                // Build PROSPECT input list; include brown leaf entry when brown parameters are provided
                var inputProspectList = new List<ProspectInputs>
                {
                    new ProspectInputs(n: testInput.N, cab: testInput.CAB, car: testInput.CAR,
                        ant: testInput.ANT, brown: testInput.BROWN, ewt: testInput.EWT,
                        lma: testInput.LMA, prot: testInput.PROT, cbc: testInput.CBC,
                        alpha: testInput.Alpha)
                };
                if (testInput.BrownN.HasValue)
                {
                    inputProspectList.Add(new ProspectInputs(
                        n: testInput.BrownN.Value, cab: testInput.BrownCAB.Value,
                        car: testInput.BrownCAR.Value, ant: testInput.BrownANT.Value,
                        brown: testInput.BrownBROWN.Value, ewt: testInput.BrownEWT.Value,
                        lma: testInput.BrownLMA.Value, prot: testInput.BrownPROT.Value,
                        cbc: testInput.BrownCBC.Value, alpha: testInput.Alpha));
                }

                // C# implementation
                var resultCS = ProsailCore.PRO4SAIL(
                    leafOpticalConstants: leafConst,
                    inputProspectList: inputProspectList,
                    N: testInput.N,
                    CAB: testInput.CAB,
                    CAR: testInput.CAR,
                    ANT: testInput.ANT,
                    BROWN: testInput.BROWN,
                    EWT: testInput.EWT,
                    LMA: testInput.LMA,
                    PROT: testInput.PROT,
                    CBC: testInput.CBC,
                    Alpha: testInput.Alpha,
                    TypeLidf: testInput.TypeLidf,
                    LIDFa: testInput.LIDFa,
                    LIDFb: testInput.LIDFb,
                    LAI: testInput.LAI,
                    HotSpot: testInput.HotSpot,
                    TTS: testInput.SunZenithAngle,
                    TTO: testInput.ObserverZenithAngle,
                    PSI: testInput.RelativeAzimuthAngle,
                    FractionBrown: testInput.FractionBrown,
                    Diss: testInput.Dissociation,
                    Cv: testInput.CrownCover,
                    Zeta: testInput.TreeShape,
                    SoilReflectance: soil,
                    SailVersion: testInput.SailVersion
                );

                // Build R PROSPECT input list (green leaf always first; brown leaf second when provided)
                var rProspectList = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object>
                    {
                        { "N", testInput.N }, { "CAB", testInput.CAB }, { "CAR", testInput.CAR },
                        { "ANT", testInput.ANT }, { "BROWN", testInput.BROWN },
                        { "EWT", testInput.EWT }, { "LMA", testInput.LMA },
                        { "PROT", testInput.PROT }, { "CBC", testInput.CBC }, { "Alpha", testInput.Alpha }
                    }
                };
                if (testInput.BrownN.HasValue)
                {
                    rProspectList.Add(new Dictionary<string, object>
                    {
                        { "N", testInput.BrownN.Value }, { "CAB", testInput.BrownCAB.Value },
                        { "CAR", testInput.BrownCAR.Value }, { "ANT", testInput.BrownANT.Value },
                        { "BROWN", testInput.BrownBROWN.Value }, { "EWT", testInput.BrownEWT.Value },
                        { "LMA", testInput.BrownLMA.Value }, { "PROT", testInput.BrownPROT.Value },
                        { "CBC", testInput.BrownCBC.Value }, { "Alpha", testInput.Alpha }
                    });
                }

                // R implementation
                var r_params = new Dictionary<string, object>
                {
                    { "N", testInput.N },
                    { "CAB", testInput.CAB },
                    { "CAR", testInput.CAR },
                    { "ANT", testInput.ANT },
                    { "BROWN", testInput.BROWN },
                    { "EWT", testInput.EWT },
                    { "LMA", testInput.LMA },
                    { "PROT", testInput.PROT },
                    { "CBC", testInput.CBC },
                    { "alpha", testInput.Alpha },
                    { "TypeLidf", testInput.TypeLidf },
                    { "LIDFa", testInput.LIDFa },
                    { "LIDFb", testInput.LIDFb ?? 0.0 },
                    { "lai", testInput.LAI },
                    { "q", testInput.HotSpot },
                    { "tts", testInput.SunZenithAngle },
                    { "tto", testInput.ObserverZenithAngle },
                    { "psi", testInput.RelativeAzimuthAngle },
                    { "fraction_brown", testInput.FractionBrown },
                    { "diss", testInput.Dissociation },
                    { "Cv", testInput.CrownCover },
                    { "Zeta", testInput.TreeShape },
                    { "rsoil", soil.Reflectance },
                    { "SailVersion", testInput.SailVersion },
                    { "inputProspectList", rProspectList }
                };
                var r_results = RunRImplementation("PRO4SAIL", r_params);

                CompareArrays(ExtractDoubleArray(r_results["rdot"]), resultCS.Rdot, "rdot");
                CompareArrays(ExtractDoubleArray(r_results["rsot"]), resultCS.Rsot, "rsot");
                CompareArrays(ExtractDoubleArray(r_results["rddt"]), resultCS.Rddt, "rddt");
                CompareArrays(ExtractDoubleArray(r_results["rsdt"]), resultCS.Rsdt, "rsdt");
                CompareArrays(ExtractDoubleArray(r_results["abs_dir"]), resultCS.Abs_dir, "abs_dir");
                CompareArrays(ExtractDoubleArray(r_results["abs_hem"]), resultCS.Abs_hem, "abs_hem");
                CompareArrays(ExtractDoubleArray(r_results["rsdstar"]), resultCS.Rsdstar, "rsdstar");
                CompareArrays(ExtractDoubleArray(r_results["rddstar"]), resultCS.Rddstar, "rddstar");
            }
        }

        /// <summary>
        /// Verifies that ProsailModel.OnCreated() loads the "Introduction" memo text from the
        /// Models.Resources.PROSAIL.Introduction.md embedded resource, rather than failing or
        /// returning empty/placeholder content.
        /// </summary>
        [Test]
        public void IntroductionResourceLoads()
        {
            var model = new ProsailModel();
            model.OnCreated();
            var memo = model.Children.OfType<Memo>().First(m => m.Name == "\U0001F4D6 Start Here - Introduction");
            Assert.That(memo.Text, Does.Contain("APSIM-PROSAIL framework"));
        }

        /// <summary>
        /// Minimal IStructure stub for testing expression evaluation without a full APSIM
        /// simulation. Only Get() is meaningful; other members are unused by EvaluateExpression.
        /// </summary>
        private class FakeStructure : IStructure
        {
            public Dictionary<string, object> Values = new();

            public object Get(string namePath, LocatorFlags flags = LocatorFlags.None, INodeModel relativeTo = null)
                => Values.TryGetValue(namePath, out object v) ? v : null;

            public string FileName { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public string Name => throw new NotImplementedException();
            public string FullNameAndPath => throw new NotImplementedException();
            public void AddChild(INodeModel childModel) => throw new NotImplementedException();
            public void ClearEntry(string path) => throw new NotImplementedException();
            public void ClearLocator() => throw new NotImplementedException();
            public T Find<T>(string name = null, INodeModel relativeTo = null) => throw new NotImplementedException();
            public IEnumerable<T> FindAll<T>(string name = null, INodeModel relativeTo = null) => throw new NotImplementedException();
            public T FindChild<T>(string name = null, bool recurse = false, INodeModel relativeTo = null) => throw new NotImplementedException();
            public IEnumerable<T> FindChildren<T>(string name = null, bool recurse = false, INodeModel relativeTo = null) => throw new NotImplementedException();
            public T FindParent<T>(string name = null, bool recurse = false, INodeModel relativeTo = null) => throw new NotImplementedException();
            public IEnumerable<T> FindParents<T>(string name = null, INodeModel relativeTo = null) => throw new NotImplementedException();
            public T FindSibling<T>(string name = null, INodeModel relativeTo = null) => throw new NotImplementedException();
            public IEnumerable<T> FindSiblings<T>(string name = null, INodeModel relativeTo = null) => throw new NotImplementedException();
            public VariableComposite GetObject(string namePath, LocatorFlags flags, INodeModel relativeTo = null) => throw new NotImplementedException();
            public void InsertChild(int index, INodeModel childModel) => throw new NotImplementedException();
            public void RemoveChild(INodeModel childModel) => throw new NotImplementedException();
            public void Rename(string name) => throw new NotImplementedException();
            public void ReplaceChild(INodeModel oldModel, INodeModel newModel) => throw new NotImplementedException();
            public void Set(string namePath, object value, INodeModel relativeTo = null) => throw new NotImplementedException();
        }

        /// <summary>
        /// Verifies that an expression parameter referencing a live model property (e.g.
        /// "[Wheat].Leaf.LAI") is re-resolved to the current value on every call, not just the
        /// first. This is a regression guard for any future caching of parsed expressions: the
        /// parsed expression's syntax tree may safely be cached (the text never changes), but the
        /// resolved *value* must never be - this test would fail if a caching change accidentally
        /// cached the whole result instead of just the parse step.
        /// </summary>
        [Test]
        public void ExpressionParameter_ReEvaluatesLiveValueEveryCall()
        {
            var model = new ProsailModel();
            var structure = new FakeStructure();
            model.Structure = structure;
            MethodInfo evaluateExpression = typeof(ProsailModel).GetMethod("EvaluateExpression", BindingFlags.NonPublic | BindingFlags.Instance);

            structure.Values["[Wheat].Leaf.LAI"] = 2.0;
            Assert.That((double)evaluateExpression.Invoke(model, new object[] { "[Wheat].Leaf.LAI" }), Is.EqualTo(2.0));

            structure.Values["[Wheat].Leaf.LAI"] = 3.5; // simulates the value changing on a later day
            Assert.That((double)evaluateExpression.Invoke(model, new object[] { "[Wheat].Leaf.LAI" }), Is.EqualTo(3.5));

            structure.Values["[Wheat].Leaf.LAI"] = 1.25; // third distinct value, rules out a naive 2-slot cache
            Assert.That((double)evaluateExpression.Invoke(model, new object[] { "[Wheat].Leaf.LAI" }), Is.EqualTo(1.25));
        }

        /// <summary>
        /// Verifies that 4SAIL2 brown-leaf mixing actually happens end-to-end (EvaluateAllParameters
        /// -> ValidateParameterRanges -> CalculateProsail) once distinct green/brown leaf properties
        /// are set, rather than silently falling back to a green-only result the way it used to when
        /// ProsailModel never built a second PROSPECT leaf-parameter set.
        /// </summary>
        [Test]
        public void FourSail2_BrownLeafMixing_ProducesDistinctResultFromGreenOnly()
        {
            LeafOpticalConsts fullConstants = GetCachedLeafOpticalConstants();
            double[] testWavelengths = { 500.0, 650.0, 800.0, 1000.0, 1500.0 };
            LeafOpticalConsts subsetConstants = fullConstants.SubsetByWavelengths(testWavelengths);
            SoilOptics soil = new SoilOptics(
                Vector<double>.Build.DenseOfArray(testWavelengths),
                Vector<double>.Build.Dense(testWavelengths.Length, 0.15));

            CanopyOptics RunModel(string fractionBrown)
            {
                var model = new ProsailModel { LoggingLevel = LogLevel.Error };
                typeof(ProsailModel).GetField("cachedLeafOpticalConstants", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(model, subsetConstants);
                model.inputWavelengths = testWavelengths;
                model.SoilReflectance = soil;

                model.SailVersion = SailVersionTypes.FourSAIL2;
                model.CAB = "40"; model.CABBrown = "10";
                model.CAR = "8"; model.CARBrown = "3";
                model.FractionBrown = fractionBrown;
                model.Dissociation = "1.0";

                MethodInfo evaluateAll = typeof(ProsailModel).GetMethod("EvaluateAllParameters", BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo validate = typeof(ProsailModel).GetMethod("ValidateParameterRanges", BindingFlags.NonPublic | BindingFlags.Instance);
                evaluateAll.Invoke(model, null);

                // Geometry/soil parameters are normally resolved in OnDoEndOfDay (via
                // ProsailInputLoader.ResolveObservationParameter), not EvaluateAllParameters - fill
                // them in directly so ValidateParameterRanges doesn't report them as missing.
                var currentParameterValues = (Dictionary<string, object>)typeof(ProsailModel)
                    .GetProperty("CurrentParameterValues", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(model);
                currentParameterValues["Psoil"] = 0.5;
                currentParameterValues["SunZenithAngle"] = 30.0;
                currentParameterValues["ObserverZenithAngle"] = 0.0;
                currentParameterValues["RelativeAzimuthAngle"] = 0.0;

                validate.Invoke(model, null);
                return model.CalculateProsail();
            }

            CanopyOptics greenOnly = RunModel("0.0");
            CanopyOptics mixed = RunModel("0.3");

            double maxDiff = mixed.Rdot.Zip(greenOnly.Rdot, (a, b) => Math.Abs(a - b)).Max();
            Assert.That(maxDiff, Is.GreaterThan(1e-6),
                "FractionBrown > 0 under 4SAIL2 should produce a different result than FractionBrown = 0 - " +
                "if this fails, brown-leaf mixing has silently fallen back to a green-only result again.");
        }

        /// <summary>
        /// Verifies the normalized PROSAIL database schema: (1) two simulations sharing one database
        /// file both survive - InitializeDatabase no longer does an unconditional DROP TABLE that would
        /// wipe a sibling simulation's rows - and (2) re-initializing the same simulation only clears
        /// that simulation's own previous rows (via the scoped SimulationID DELETE), leaving the other
        /// simulation untouched. Reads back through the compatibility views to also confirm the original
        /// flat SimulationName/Wavelength column shape is preserved.
        /// </summary>
        [Test]
        public void ProsailDatabaseHelper_MultipleSimulationsShareDatabase_DataSurvivesAndRerunOnlyClearsOwnRows()
        {
            string dbPath = Path.Combine(Path.GetTempPath(), $"ProsailDbTest_{Guid.NewGuid():N}.db");
            void NoOpWriteMessage(LogLevel level, string message) { }

            try
            {
                double[] wavelengths = { 500.0, 650.0, 800.0 };

                Dictionary<string, object> MakeParameterValues() => new Dictionary<string, object>
                {
                    ["N"] = 1.5, ["CAB"] = 40.0, ["CAR"] = 8.0, ["EWT"] = 0.01, ["LMA"] = 0.008,
                    ["ANT"] = 0.0, ["BROWN"] = 0.0, ["PROT"] = 0.0, ["CBC"] = 0.0, ["Alpha"] = 40.0,
                    ["LAI"] = 3.0, ["HotSpot"] = 0.1, ["TypeLidf"] = 2.0, ["LIDFa"] = 60.0, ["LIDFb"] = -0.35,
                    ["FractionBrown"] = 0.0, ["Dissociation"] = 0.0, ["CrownCover"] = 1.0, ["TreeShape"] = 1.0,
                    ["Psoil"] = 0.5, ["SunZenithAngle"] = 30.0, ["ObserverZenithAngle"] = 0.0, ["RelativeAzimuthAngle"] = 0.0
                };

                CanopyOptics MakeCanopyOptics() => new CanopyOptics
                {
                    Wavelength = wavelengths,
                    Rdot = wavelengths.Select(w => w / 1000.0).ToArray(),
                    Rsot = wavelengths.Select(w => w / 1000.0).ToArray(),
                    Rddt = wavelengths.Select(w => w / 1000.0).ToArray(),
                    Rsdt = wavelengths.Select(w => w / 1000.0).ToArray(),
                    FCover = wavelengths.Select(w => 0.8).ToArray(),
                    Abs_dir = wavelengths.Select(w => 0.5).ToArray(),
                    Abs_hem = wavelengths.Select(w => 0.5).ToArray(),
                    Rsdstar = wavelengths.Select(w => 0.2).ToArray(),
                    Rddstar = wavelengths.Select(w => 0.2).ToArray()
                };

                void RunOneDay(string simulationName, DateTime date)
                {
                    SQLite db = ProsailDatabaseHelper.InitializeDatabase(dbPath, simulationName.Replace("'", "''"),
                        outputParameters: true, outputCanopyOpticalVariable: true, outputCanopyStateVariable: false,
                        outputCanopyBRF: false, outputReflectanceResampledToSensor: false,
                        out int simulationID, NoOpWriteMessage);
                    try
                    {
                        Dictionary<double, int> wavelengthIdLookup = ProsailDatabaseHelper.RegisterWavelengths(db, wavelengths);
                        ProsailDatabaseHelper.WriteToDatabase(db, simulationID, wavelengthIdLookup, date,
                            MakeParameterValues(), wetDrySoilReflectancePath: null, sailVersionString: "4SAIL", sensorTypeString: "",
                            MakeCanopyOptics(), default, default, spectralResamplingResult: null,
                            outputParameters: true, outputCanopyOpticalVariable: true, outputCanopyStateVariable: false,
                            outputCanopyBRF: false, outputReflectanceResampledToSensor: false, NoOpWriteMessage);
                    }
                    finally
                    {
                        db.CloseDatabase();
                    }
                }

                // Two simulations, each opening its own connection to the same db file - exactly as
                // happens when multiple Simulation nodes share one .apsimx file.
                RunOneDay("SimA", new DateTime(2024, 1, 1));
                RunOneDay("SimB", new DateTime(2024, 1, 1));

                SQLite reader = new SQLite();
                reader.OpenDatabase(dbPath, true);
                DataTable simNames = reader.ExecuteQuery("SELECT DISTINCT SimulationName FROM Parameters ORDER BY SimulationName;");
                reader.CloseDatabase();
                var namesAfterBothRuns = simNames.Rows.Cast<DataRow>().Select(r => (string)r["SimulationName"]).ToList();
                Assert.That(namesAfterBothRuns, Is.EquivalentTo(new[] { "SimA", "SimB" }),
                    "Both simulations sharing one database file should have their Parameters rows survive - " +
                    "if this fails, InitializeDatabase is wiping a sibling simulation's data again.");

                // Re-run SimA on a different date. Only SimA's own rows should be replaced; SimB's row
                // (and the compatibility view's flat SimulationName/Wavelength shape) should be untouched.
                RunOneDay("SimA", new DateTime(2024, 1, 2));

                reader = new SQLite();
                reader.OpenDatabase(dbPath, true);
                DataTable allRows = reader.ExecuteQuery("SELECT SimulationName, Date, Wavelength, Rdot FROM CanopyOpticalVariable ORDER BY SimulationName, Date;");
                reader.CloseDatabase();

                var simADates = allRows.Rows.Cast<DataRow>().Where(r => (string)r["SimulationName"] == "SimA").Select(r => (string)r["Date"]).Distinct().ToList();
                var simBDates = allRows.Rows.Cast<DataRow>().Where(r => (string)r["SimulationName"] == "SimB").Select(r => (string)r["Date"]).Distinct().ToList();

                Assert.That(simADates, Is.EquivalentTo(new[] { "2024-01-02" }),
                    "Re-running SimA should replace its own previous-date rows, not accumulate them.");
                Assert.That(simBDates, Is.EquivalentTo(new[] { "2024-01-01" }),
                    "SimB's rows should be untouched by SimA's re-run - if this fails, the DELETE isn't scoped to SimulationID.");
                Assert.That(allRows.Rows.Count, Is.EqualTo(wavelengths.Length * 2),
                    "Expected exactly one wavelength-row set for SimA's current date plus one for SimB's, via the CanopyOpticalVariable compatibility view.");
            }
            finally
            {
                if (File.Exists(dbPath))
                    File.Delete(dbPath);
            }
        }

        /// <summary>
        /// Verifies RegisterWavelengths only populates _Wavelengths with whatever is actually passed
        /// in - the primitive that ProsailModel.OnCommencing now relies on to skip registering the
        /// full simulation grid when no per-wavelength output (CanopyOpticalVariable/CanopyBRF) is
        /// enabled, and to register only sensor band centers when just ReflectanceResampledToSensor is.
        /// </summary>
        [Test]
        public void ProsailDatabaseHelper_RegisterWavelengths_OnlyPopulatesWhatIsPassedIn()
        {
            string dbPath = Path.Combine(Path.GetTempPath(), $"ProsailWavelengthTest_{Guid.NewGuid():N}.db");
            void NoOpWriteMessage(LogLevel level, string message) { }

            try
            {
                SQLite db = ProsailDatabaseHelper.InitializeDatabase(dbPath, "Simulation",
                    outputParameters: true, outputCanopyOpticalVariable: false, outputCanopyStateVariable: true,
                    outputCanopyBRF: false, outputReflectanceResampledToSensor: false,
                    out int simulationID, NoOpWriteMessage);

                // Mirrors OnCommencing when only Parameters/CanopyStateVariable are enabled: nothing
                // needs a wavelength, so nothing should be registered.
                Dictionary<double, int> emptyLookup = ProsailDatabaseHelper.RegisterWavelengths(db, Enumerable.Empty<double>());
                Assert.That(emptyLookup, Is.Empty,
                    "Registering an empty wavelength set should leave _Wavelengths empty - " +
                    "if this fails, RegisterWavelengths is populating rows it wasn't asked for.");

                // Mirrors OnCommencing when only ReflectanceResampledToSensor is enabled: only the
                // (discontinuous, decimal) sensor band centers should be registered, not the full grid.
                double[] bandCenters = { 490.5, 705.5, 865.3 };
                Dictionary<double, int> bandLookup = ProsailDatabaseHelper.RegisterWavelengths(db, bandCenters);
                db.CloseDatabase();

                Assert.That(bandLookup.Keys, Is.EquivalentTo(bandCenters),
                    "_Wavelengths should contain exactly the sensor band centers passed in, not the full simulation grid.");

                SQLite reader = new SQLite();
                reader.OpenDatabase(dbPath, true);
                int rowCount = reader.ExecuteQueryReturnInt("SELECT COUNT(*) FROM _Wavelengths;", 0);
                reader.CloseDatabase();
                Assert.That(rowCount, Is.EqualTo(bandCenters.Length),
                    "_Wavelengths row count should match only the registered band centers, confirming the full 400-2500 grid was never registered.");
            }
            finally
            {
                if (File.Exists(dbPath))
                    File.Delete(dbPath);
            }
        }

        /// <summary>
        /// Verifies Prospect()'s "already subset to these wavelengths" short-circuit: (1) when the
        /// wavelengths genuinely differ from what LeafOpticalConstants currently holds, the normal
        /// rebuild still runs and correctly narrows the result to just the requested subset (guards
        /// against the short-circuit's match check false-positiving and skipping a rebuild it
        /// shouldn't); and (2) whether or not LeafOpticalConstants was already pre-subset to the exact
        /// same wavelengths, the resulting LeafOptics is identical either way - the short-circuit is a
        /// pure performance change with no effect on output.
        /// </summary>
        [Test]
        public void Prospect_SkipsRedundantWavelengthRebuild_WithoutChangingOutput()
        {
            LeafOpticalConsts fullConstants = GetCachedLeafOpticalConstants();
            double[] narrowWavelengths = { 500.0, 650.0, 800.0 };
            var inputs = new ProspectInputs(n: 1.5, cab: 40.0, car: 8.0, wavelengths: narrowWavelengths);

            // Genuinely differing wavelengths (LeafOpticalConstants still holds the full 400-2500 range):
            // the rebuild path must still run and correctly narrow the result.
            LeafOptics fromRebuild = Prospect(inputs, fullConstants);
            Assert.That(fromRebuild.Wavelength.Length, Is.EqualTo(narrowWavelengths.Length),
                "Requesting a genuine subset of a wider LeafOpticalConstants should still narrow the result - " +
                "if this fails, the short-circuit is skipping a rebuild it shouldn't.");
            Assert.That(fromRebuild.Wavelength, Is.EqualTo(narrowWavelengths));

            // LeafOpticalConstants pre-subset to exactly narrowWavelengths (mirrors ProsailModel's usage:
            // cachedLeafOpticalConstants is subset once in OnCommencing, then the same inputWavelengths
            // array is passed on every daily ProspectInputs) - the short-circuit should take the fast
            // path here, but the output must be identical to the rebuild path above regardless.
            LeafOpticalConsts subsetConstants = fullConstants.SubsetByWavelengths(narrowWavelengths);
            var inputsWithDifferentArrayInstance = new ProspectInputs(n: 1.5, cab: 40.0, car: 8.0,
                wavelengths: narrowWavelengths.ToArray()); // same values, different array instance
            LeafOptics fromShortCircuit = Prospect(inputsWithDifferentArrayInstance, subsetConstants);

            Assert.That(fromShortCircuit.Wavelength, Is.EqualTo(fromRebuild.Wavelength));
            Assert.That(fromShortCircuit.Reflectance, Is.EqualTo(fromRebuild.Reflectance).Within(1e-12),
                "The short-circuit must produce output identical to the full rebuild - it's a pure performance change.");
            Assert.That(fromShortCircuit.Transmittance, Is.EqualTo(fromRebuild.Transmittance).Within(1e-12));
        }

        /// <summary>
        /// Verifies that LoadWetDrySoilReflectanDataFromResource, LoadAtmosphericSpectralDataFromResource,
        /// and BsmCore.LoadBsmDataFromResource each parse a given embedded resource only once per process:
        /// calling them twice with the same resource name must return data backed by the same underlying
        /// array/Vector instances, not two independently-parsed copies. This is what actually reduces memory
        /// when many ProsailModel instances share one process (e.g. an HPC run using --cpu-count).
        /// </summary>
        [Test]
        public void EmbeddedResourceLoaders_ShareOneParsedCopy_AcrossRepeatedCalls()
        {
            WetDrySoilReflectance soil1 = LoadWetDrySoilReflectanDataFromResource("Models.PROSAIL.InputProperties.SpectralData.SpecSOIL.json");
            WetDrySoilReflectance soil2 = LoadWetDrySoilReflectanDataFromResource("Models.PROSAIL.InputProperties.SpectralData.SpecSOIL.json");
            Assert.That(ReferenceEquals(soil1.Wavelength, soil2.Wavelength), Is.True,
                "Repeated loads of the same soil resource should share one parsed copy.");

            AtmosphericSpectralData atm1 = LoadAtmosphericSpectralDataFromResource("Models.PROSAIL.InputProperties.SpectralData.SpecATM.json");
            AtmosphericSpectralData atm2 = LoadAtmosphericSpectralDataFromResource("Models.PROSAIL.InputProperties.SpectralData.SpecATM.json");
            Assert.That(ReferenceEquals(atm1.Wavelength, atm2.Wavelength), Is.True,
                "Repeated loads of the same atmospheric resource should share one parsed copy.");

            BsmSpectralData bsm1 = BsmCore.LoadBsmDataFromResource("Models.PROSAIL.InputProperties.SpectralData.BSM_GSV.json");
            BsmSpectralData bsm2 = BsmCore.LoadBsmDataFromResource("Models.PROSAIL.InputProperties.SpectralData.BSM_GSV.json");
            Assert.That(ReferenceEquals(bsm1.Wavelength, bsm2.Wavelength), Is.True,
                "Repeated loads of the same BSM resource should share one parsed copy.");
        }

        /// <summary>
        /// LoadSpectralResponseFunction shares the immutable raw SRF arrays across repeated loads of the
        /// same resource (avoiding a re-parse per ProsailModel instance), but must still return a distinct
        /// SpectralResponseFunction wrapper each time, because Preprocess() mutates
        /// PrecomputedInputIndices/Weights/TotalWeights in place. Two simulations selecting the same sensor
        /// but different InputWavelengthRange must not corrupt each other's precomputed lookups.
        /// </summary>
        [Test]
        public void LoadSpectralResponseFunction_SharesRawArrays_ButKeepsPreprocessOutputPerInstance()
        {
            const string resourceName = "Models.PROSAIL.InputProperties.SpectralResponseFunctions.Sentinel_2.json";

            SpectralResponseFunction srf1 = LoadSpectralResponseFunction(resourceName);
            SpectralResponseFunction srf2 = LoadSpectralResponseFunction(resourceName);

            Assert.That(ReferenceEquals(srf1, srf2), Is.False,
                "Each caller must get its own SpectralResponseFunction wrapper.");
            Assert.That(ReferenceEquals(srf1.OriginalBandWavelength, srf2.OriginalBandWavelength), Is.True,
                "The immutable raw arrays should be shared, not re-parsed, across wrappers from the same resource.");

            double[] wavelengthsA = srf1.OriginalBandWavelength.Take(5).ToArray();
            double[] wavelengthsB = srf1.OriginalBandWavelength.Skip(5).Take(5).ToArray();

            srf1.Preprocess(wavelengthsA);
            srf2.Preprocess(wavelengthsB);

            Assert.That(srf1.PrecomputedInputIndices, Is.Not.SameAs(srf2.PrecomputedInputIndices),
                "Preprocessing one wrapper with a different wavelength range must not affect the other.");
            Assert.That(srf1.OriginalBandWavelength, Is.SameAs(srf2.OriginalBandWavelength),
                "The raw arrays remain shared even after each wrapper is preprocessed independently.");
        }
    }
}
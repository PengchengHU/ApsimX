using APSIM.Core;
using APSIM.Shared.Utilities;
using DocumentFormat.OpenXml.Wordprocessing;
using MathNet.Numerics.LinearAlgebra;
using Models;
using Models.PostSimulationTools;
using Models.Prosail;
using Models.PROSAIL;
using Models.PROSAIL.PROSPECT;
using Models.PROSAIL.Sail;
using Models.PROSAIL.SAIL;
using Newtonsoft.Json;
using NUnit.Framework;
using System;
using System.Collections.Generic;
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
            var memo = model.Children.OfType<Memo>().First(m => m.Name == "Introduction");
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
    }
}
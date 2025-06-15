using System;
using System.Linq;
using System.IO;
using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.IntegralTransforms;
using Newtonsoft.Json;
using Models.Core;
using APSIM.Shared.Utilities;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;

namespace Models.PROSAIL.PROSPECT
{
    /// <summary>
    /// Implements the PROSPECT radiative transfer model for leaf optical properties
    /// </summary>
    /// <remarks>
    /// Reference: Jacquemoud, S., and Baret, F. (1990). PROSPECT: A model of leaf optical properties spectra.
    /// Acknowledgement: This C# implementation (script) of PROSPECT is implmented based on the 'prospect' R package (https://github.com/jbferet/prospect) 
    /// writen by Dr Jean-Baptiste Feret (jean-baptiste.feret@teledetection.fr). Please properly cite the R package and other papers (as listed in the GitHub page):
    /// Féret, J.-B. and de Boissieu, F. (2024). prospect: an R package to link leaf optical properties with their chemical and
    /// structural properties with the leaf model PROSPECT. Journal of Open Source Software, 9(94), 6027, https://doi.org/10.21105/joss.06027
    /// </remarks>
    public static class ProspectCore
    {
        private static LeafOpticalConsts _cachedOpticalConstants;
        private static readonly object _lock = new object();

        /// <summary>
        /// Contains leaf optical constants required for PROSPECT calculations
        /// </summary>
        public struct ProspectInputs
        {
            /// <summary>Leaf structure parameter N (unitless, avg number of layers).</summary>
            public double N;
            /// <summary>Chlorophyll a + b content (μg/cm²).</summary>
            public double CAB;
            /// <summary>Total carotenoid content (μg/cm²).</summary>
            public double CAR;
            /// <summary>Anthocyanin content (μg/cm²).</summary>
            public double ANT;
            /// <summary>Brown pigment content (arbitrary units).</summary>
            public double BROWN;
            /// <summary>Equivalent Water Thickness (cm or g/cm²).</summary>
            public double EWT;
            /// <summary>Leaf Mass per Area (dry matter content) (g/cm²).</summary>
            public double LMA;
            /// <summary>Protein content (g/cm²).</summary>
            public double PROT;
            /// <summary>Non-protein Carbon-based constituent content (g/cm²).</summary>
            public double CBC;
            /// <summary>Incidence angle for tav calculation (degrees, typically 40 or 59).</summary>
            public double Alpha; // Default often 40 in PROSPECT contexts
            /// <summary>Array of specific wavelengths to simulate (subset of LeafOpticalConstants.Wavelength, optional; defaults to all wavelengths).</summary>
            public double[] Wavelengths;

            /// <summary>
            /// Constructor to initialize PROSPECT input parameters with default values.
            /// </summary>
            public ProspectInputs(double n = 1.5, double cab = 40.0, double car = 8.0, double ant = 0.0,
                double brown = 0.0, double ewt = 0.01, double lma = 0.008,
                double prot = 0.0, double cbc = 0.0, double alpha = 40.0, double[] wavelengths = null)
            {
                N = n; CAB = cab; CAR = car; ANT = ant; BROWN = brown; EWT = ewt; LMA = lma;
                PROT = prot; CBC = cbc; Alpha = alpha; 
                Wavelengths = wavelengths ?? Enumerable.Range(400, 2500 - 400 + 1)
                                               .Select(x => (double)x)
                                               .ToArray();
            }
        }

        /// <summary>
        /// Contains leaf optical constants required for PROSPECT calculations
        /// </summary>
        public struct LeafOpticalConsts
        {
            /// <summary>Wavelength array in nanometers (nm)</summary>
            public Vector<double> Wavelength;      
            /// <summary>Refractive index</summary>
            public Vector<double> RefractiveIndex;   
            /// <summary>Specific absorption coefficient for a + b chlorophyll</summary>
            public Vector<double> SAC_CAB;          
            /// <summary>Specific absorption coefficient for carotenoids</summary>
            public Vector<double> SAC_CAR;          
            /// <summary>Specific absorption coefficient for water</summary>
            public Vector<double> SAC_EWT;          
            /// <summary>Specific absorption coefficient for dry matter</summary>
            public Vector<double> SAC_LMA;         
            /// <summary>Transmissivity at 40° incidence angle</summary>
            public Vector<double> Tav40;            
            /// <summary>Transmissivity at 90° incidence angle</summary>
            public Vector<double> Tav90;            
            /// <summary>Specific absorption coefficient for anthocyanin</summary>
            public Vector<double> SAC_ANT;    
            /// <summary>Specific absorption coefficient for brown pigmentn</summary>
            public Vector<double> SAC_BROWN;  
            /// <summary>Specific absorption coefficient for protein</summary>
            public Vector<double> SAC_PROT;   
            /// <summary>Specific absorption coefficient for non-protein carbon-based constituent</summary>
            public Vector<double> SAC_CBC;
            /// <summary>Dictionary mapping wavelengths to their indices in the Wavelength array (for optimized filtering).</summary>
            public Dictionary<double, int> WavelengthToIndex;
        }

        /// <summary>
        /// Holds leaf optical properties (reflectance and transmittance spectra).
        /// Typically output from the PROSPECT model.
        /// </summary>
        public struct LeafOptics
        {
            /// <summary>Wavelength array in nanometers (nm)</summary>
            public double[] Wavelength;
            /// <summary>Leaf reflectance spectrum (unitless fraction)</summary>
            public double[] Reflectance;
            /// <summary>Leaf transmittance spectrum (unitless fraction)</summary>
            public double[] Transmittance;
            /// <summary>Dictionary mapping wavelengths to their indices</summary>
            public Dictionary<double, int> WavelengthToIndex;

            /// <summary> Indicates whether this LeafOptics object holds data.
            /// Returns false if Wavelength, Reflectance, or Transmittance is null.
            /// </summary>
            public readonly bool HasValue => Wavelength != null && Reflectance != null && Transmittance != null;

            /// <summary>Constructor that automatically creates WavelengthToIndex</summary>
            public LeafOptics(double[] wavelength, double[] reflectance, double[] transmittance)
            {
                Wavelength = wavelength;
                Reflectance = reflectance;
                Transmittance = transmittance;
                WavelengthToIndex = wavelength?.Select((w, i) => new { Wavelength = w, Index = i })
                                              .ToDictionary(x => x.Wavelength, x => x.Index);
            }
        }

        // Relative path from APSIM bin directory to Models\PROSAIL\PROSPECT
        private static readonly string RelativeLeafOpticalDataPath = "..\\..\\..\\Models\\PROSAIL\\PROSPECT\\SpecPROSPECT_FullRange.json";
        private static string DefaultLeafOpticalDataPath => PathUtilities.GetAbsolutePath(RelativeLeafOpticalDataPath, AppDomain.CurrentDomain.BaseDirectory);

        /// <summary>
        /// Gets cached leaf optical constants, loading them if necessary.
        /// </summary>
        /// <returns>The cached leaf optical constants.</returns>
        public static LeafOpticalConsts GetCachedLeafOpticalConstants()
        {
            if (_cachedOpticalConstants.Wavelength == null) // Check if not initialized
            {
                lock (_lock)
                {
                    if (_cachedOpticalConstants.Wavelength == null) // Double-check after acquiring lock
                    {
                        _cachedOpticalConstants = LoadLocalLeafOpticalDataUncached();
                    }
                }
            }
            return _cachedOpticalConstants;
        }

        /// <summary>
        /// Override the method of the PROSPECT model to calculate leaf reflectance and transmittance
        /// </summary>
        /// <param name="ProspectInputs">Prospect inputs</param>
        /// <param name="LeafOpticalConstants">Leaf optical constants (optional)</param>
        /// <returns>An object of LeafOptics containing wavelength, reflectance and transmittance.</returns>
        public static LeafOptics Prospect(ProspectInputs ProspectInputs, LeafOpticalConsts? LeafOpticalConstants = null)
        {
            // Load spectral constants if not provided
            LeafOpticalConsts LeafConstants = LeafOpticalConstants ?? GetCachedLeafOpticalConstants();

            LeafOptics res = Prospect(LeafOpticalConstants: LeafConstants, 
                N: ProspectInputs.N, 
                CAB: ProspectInputs.CAB, 
                CAR: ProspectInputs.CAR, 
                ANT: ProspectInputs.ANT, 
                BROWN: ProspectInputs.BROWN, 
                EWT: ProspectInputs.EWT, 
                LMA: ProspectInputs.LMA, 
                PROT:ProspectInputs.PROT, 
                CBC: ProspectInputs.CBC, 
                Alpha: ProspectInputs.Alpha, 
                Wavelengths: ProspectInputs.Wavelengths);
            return res;
        }

        /// <summary>
        /// Runs the PROSPECT model to calculate leaf reflectance and transmittance
        /// </summary>
        /// <param name="LeafOpticalConstants">Leaf optical constants (optional)</param>
        /// <param name="N">Leaf structure parameter (unitless)</param>
        /// <param name="CAB">Chlorophyll a + b content (μg/cm²)</param>
        /// <param name="CAR">Carotenoid content (μg/cm²)</param>
        /// <param name="EWT">Equivalent Water Thickness (g/cm²)</param>
        /// <param name="LMA">Leaf Mass per Area (g/cm²)</param>
        /// <param name="ANT">Anthocyanin content (μg/cm²)</param>
        /// <param name="BROWN">Brown pigment content (Arbitrary units)</param>
        /// <param name="PROT">Protein content (g/cm²)</param>
        /// <param name="CBC">NonProt Carbon-based constituent content (g/cm²)</param>
        /// <param name="Alpha">Incidence angle in degrees</param>
        /// <param name="Wavelengths">Array of specific wavelengths to simulate (subset of OpticalConstants.Wavelength, optional; defaults to all wavelengths).</param>
        /// <returns>An object of LeafOptics containing wavelength, reflectance and transmittance.</returns>
        /// <exception cref="ArgumentException">Thrown if input parameters are invalid or array lengths mismatch.</exception>
        /// <exception cref="FileNotFoundException">Thrown if the file for leaf optical constants is missing.</exception>
        public static LeafOptics Prospect(
            LeafOpticalConsts? LeafOpticalConstants = null, // Optional parameter with null default
            double N = 1.5,
            double CAB = 40.0,
            double CAR = 8.0,
            double EWT = 0.01,
            double LMA = 0.008,
            double ANT = 0.0,
            double BROWN = 0.0,
            double PROT = 0.0,
            double CBC = 0.0,
            double Alpha = 40.0,
            double[] Wavelengths = null)
        {
            // Load spectral constants if not provided
            LeafOpticalConsts LeafConstants = LeafOpticalConstants ?? GetCachedLeafOpticalConstants();

            // Input validation
            if (N <= 0) throw new ArgumentException("Leaf structure parameter N must be positive.");
            if (CAB < 0 || CAR < 0 || EWT < 0 || LMA < 0 || ANT < 0 || BROWN < 0 || PROT < 0 || CBC < 0)
                throw new ArgumentException("Leaf constituents must be non-negative.");
            if (Alpha < 0 || Alpha > 90)
                throw new ArgumentException("Incidence angle must be between 0 and 90 degrees");

            // Handle custom wavelengths
            if (Wavelengths != null && Wavelengths.Length > 0)
            {
                // Validate that all specified wavelengths are in OpticalConstants.Wavelength
                foreach (double w in Wavelengths)
                {
                    if (!LeafConstants.WavelengthToIndex.ContainsKey(w))
                    {
                        throw new ArgumentException($"Wavelength {w} nm is not in OpticalConstants.Wavelength.");
                    }
                }

                // Map wavelengths to their indices in the original array
                var indices = Wavelengths.Select(w => LeafConstants.WavelengthToIndex[w]).ToArray();

                // Create a new OpticalConstants with only the specified wavelengths
                LeafConstants = new LeafOpticalConsts
                {
                    Wavelength = Vector<double>.Build.DenseOfArray(Wavelengths),
                    RefractiveIndex = Vector<double>.Build.DenseOfEnumerable(indices.Select(i => LeafConstants.RefractiveIndex[i])),
                    SAC_CAB = Vector<double>.Build.DenseOfEnumerable(indices.Select(i => LeafConstants.SAC_CAB[i])),
                    SAC_CAR = Vector<double>.Build.DenseOfEnumerable(indices.Select(i => LeafConstants.SAC_CAR[i])),
                    SAC_EWT = Vector<double>.Build.DenseOfEnumerable(indices.Select(i => LeafConstants.SAC_EWT[i])),
                    SAC_LMA = Vector<double>.Build.DenseOfEnumerable(indices.Select(i => LeafConstants.SAC_LMA[i])),
                    Tav40 = Vector<double>.Build.DenseOfEnumerable(indices.Select(i => LeafConstants.Tav40[i])),
                    Tav90 = Vector<double>.Build.DenseOfEnumerable(indices.Select(i => LeafConstants.Tav90[i])),
                    SAC_ANT = Vector<double>.Build.DenseOfEnumerable(indices.Select(i => LeafConstants.SAC_ANT[i])),
                    SAC_BROWN = Vector<double>.Build.DenseOfEnumerable(indices.Select(i => LeafConstants.SAC_BROWN[i])),
                    SAC_PROT = Vector<double>.Build.DenseOfEnumerable(indices.Select(i => LeafConstants.SAC_PROT[i])),
                    SAC_CBC = Vector<double>.Build.DenseOfEnumerable(indices.Select(i => LeafConstants.SAC_CBC[i])),
                    WavelengthToIndex = Wavelengths.Select((w, i) => new { Wavelength = w, Index = i })
                                                 .ToDictionary(x => x.Wavelength, x => x.Index)
                };
            }

            // Compute total absorption corresponding to each homogeneous layer
            // Kall = (sum of constituent absorptions) / N
            Vector<double> Kall = (CAB * LeafConstants.SAC_CAB +
                                 CAR * LeafConstants.SAC_CAR +
                                 EWT * LeafConstants.SAC_EWT +
                                 LMA * LeafConstants.SAC_LMA +
                                 ANT * LeafConstants.SAC_ANT +
                                 BROWN * LeafConstants.SAC_BROWN +
                                 PROT * LeafConstants.SAC_PROT +
                                 CBC * LeafConstants.SAC_CBC) / N;

            // reflectance and transmittance of one layer (tau)
            Vector<double> tau = ComputeTau(Kall);

            // reflectivity and transmissivity at the interface
            Vector<double> talf = Alpha == 40 ? LeafConstants.Tav40 : ComputeTav(Alpha, LeafConstants.RefractiveIndex);
            Vector<double> ralf = 1.0 - talf;
            Vector<double> t12 = LeafConstants.Tav90;
            Vector<double> r12 = 1.0 - t12;
            Vector<double> t21 = t12.PointwiseDivide(LeafConstants.RefractiveIndex.PointwisePower(2));
            Vector<double> r21 = 1.0 - t21;

            // top surface side
            Vector<double> denom = 1.0 - r21.PointwiseMultiply(r21).PointwiseMultiply(tau.PointwisePower(2));
            Vector<double> Ta = talf.PointwiseMultiply(tau).PointwiseMultiply(t21).PointwiseDivide(denom);
            Vector<double> Ra = ralf + r21.PointwiseMultiply(tau).PointwiseMultiply(Ta);

            // bottom surface side
            Vector<double> T = t12.PointwiseMultiply(tau).PointwiseMultiply(t21).PointwiseDivide(denom);
            Vector<double> R = r12 + r21.PointwiseMultiply(tau).PointwiseMultiply(T);

            // reflectance and transmittance of N layers
            Vector<double> D = (1 + R + T).PointwiseMultiply(1 + R - T)
                              .PointwiseMultiply(1 - R + T).PointwiseMultiply(1 - R - T).PointwiseSqrt();
            Vector<double> Rq = R.PointwisePower(2);
            Vector<double> Tq = T.PointwisePower(2);
            Vector<double> a = (1 + Rq - Tq + D).PointwiseDivide(2 * R);
            Vector<double> b = (1 - Rq + Tq + D).PointwiseDivide(2 * T);

            Vector<double> bNm1 = b.PointwisePower(N - 1);
            Vector<double> bN2 = bNm1.PointwisePower(2);
            Vector<double> a2 = a.PointwisePower(2);
            denom = a2.PointwiseMultiply(bN2) - 1 + 1e-10;

            Vector<double> Rsub = a.PointwiseMultiply(bN2 - 1).PointwiseDivide(denom);
            Vector<double> Tsub = bNm1.PointwiseMultiply(a2 - 1).PointwiseDivide(denom);

            // Case of zero absorption
            for (int i = 0; i < R.Count; i++)
            {
                if (R[i] + T[i] >= 1.0 - 1e-10)
                {
                    Tsub[i] = T[i] / (T[i] + (1 - T[i]) * Math.Max(N - 1, 1e-10));
                    Rsub[i] = 1 - Tsub[i];
                }
            }

            // leaf reflectance and transmittance : combine top layer with next N-1 layers
            denom = 1 - Rsub.PointwiseMultiply(R) + 1e-10;
            Vector<double> transmittance = Ta.PointwiseMultiply(Tsub).PointwiseDivide(denom);
            Vector<double> reflectance = Ra + Ta.PointwiseMultiply(Rsub).PointwiseMultiply(T).PointwiseDivide(denom);

            // Clamp results to physical limits
            reflectance = reflectance.Map(x => Math.Round(Math.Max(0, Math.Min(1, x)), 4)); // 4 digits
            transmittance = transmittance.Map(x => Math.Round(Math.Max(0, Math.Min(1, x)), 4));

            LeafOptics LeafOpticsResult = new LeafOptics
            {
                Wavelength = LeafConstants.Wavelength.ToArray(),
                Reflectance = reflectance.ToArray(),
                Transmittance = transmittance.ToArray()
            };
            return LeafOpticsResult;
        }

        /// <summary>
        /// Computes the reflectance and transmittance of one layer (tau).
        /// </summary>
        /// <param name="k">Absorption coefficient vector (Kall).</param>
        /// <returns>Transmittance vector (tau) for each wavelength.</returns>
        private static Vector<double> ComputeTau(Vector<double> k)
        {
            return k.Map(k_i =>
            {
                // Handle edge cases for the absorption coefficient
                if (k_i <= 0) return 1.0; // No absorption, full transmittance
                if (k_i > 100) return 0.0; // High absorption, no transmittance (prevents overflow)

                // Check if k_i is close to 1, where ExponentialIntegral may fail to converge
                // The error "Continued fraction failed to converge for x=1.0xxx" suggests
                // that SpecialFunctions.ExponentialIntegral(k_i, 1) uses a continued fraction internally,
                // which struggles when k_i ≈ 1 due to slow convergence or oscillation.
                if (Math.Abs(k_i - 1.0) < 0.05) // Threshold for problematic values
                {
                    // Use a series approximation for E_1(k_i) when k_i ≈ 1 to avoid convergence issues
                    // E_1(x) ≈ -γ - ln(x) + x - x^2/4 + x^3/18 (Taylor expansion around x=1)
                    // where γ is the Euler-Mascheroni constant (0.5772156649...)
                    const double gamma = 0.5772156649015329;
                    double delta = k_i - 1.0;
                    double eiApprox = -gamma - Math.Log(k_i) + k_i - k_i * k_i / 4.0 + k_i * k_i * k_i / 18.0;
                    double expTerm = (1 - k_i) * Math.Exp(-k_i);
                    double tauApprox = expTerm + k_i * k_i * eiApprox;

                    // Log the use of the approximation for debugging
                    Console.WriteLine($"Warning: ProspectCore: Using E_1 approximation for k_i={k_i:F6} (close to 1)");

                    // Ensure tau is within physical bounds [0, 1]
                    return Math.Max(0, Math.Min(1, tauApprox));
                }

                try
                {
                    // Standard PROSPECT calculation for tau
                    // tau = (1 - k) * e^(-k) + k^2 * E_1(k)
                    // where E_1(k) is the exponential integral of order 1
                    double expTerm = (1 - k_i) * Math.Exp(-k_i);
                    double eiTerm = k_i * k_i * SpecialFunctions.ExponentialIntegral(k_i, 1);
                    double tau = expTerm + eiTerm;

                    // Ensure tau is within physical bounds [0, 1]
                    return Math.Max(0, Math.Min(1, tau));
                }
                catch (Exception ex)
                {
                    // If ExponentialIntegral fails (e.g., due to continued fraction convergence failure),
                    // fall back to a simple approximation: tau ≈ e^(-k_i)
                    // This is a reasonable approximation for moderate absorption and avoids simulation failure Using approximation tau ≈ e^(-k_i).");
                    Console.WriteLine($"Warning: ProspectCore: Failed to compute ExponentialIntegral for k_i={k_i:F6}: {ex.Message}. Using approximation tau ≈ e^(-k_i).");
                    double tauFallback = Math.Exp(-k_i);

                    // Ensure the fallback value is within physical bounds [0, 1]
                    return Math.Max(0, Math.Min(1, tauFallback));
                }
            });
        }

        /// <summary>
        /// Computes transmissivity of a dielectric plane surface, averaged over all directions
        /// of incidence and over all polarizations.
        /// </summary>
        /// <param name="Alpha">Incidence angle in degrees</param>
        /// <param name="nr">Refractive index vector</param>
        /// <returns>Transmissivity vector</returns>
        private static Vector<double> ComputeTav(double Alpha, Vector<double> nr)
        {
            double rd = Math.PI / 180.0;
            double sa = Math.Sin(Alpha * rd);
            double sa2 = sa * sa;

            return nr.Map(n =>
            {
                double n2 = n * n;
                double np = n2 + 1;
                double nm = n2 - 1;
                double a = (n + 1) * (n + 1) / 2;
                double k = -(n2 - 1) * (n2 - 1) / 4;
                double b2 = sa2 - np / 2;
                double b1 = Alpha == 90 ? 0 : Math.Sqrt(b2 * b2 + k);
                double b = b1 - b2;
                double b3 = b * b * b;
                double a3 = a * a * a;

                double ts = k * k / (6 * b3) + k / b - b / 2 - (k * k / (6 * a3) + k / a - a / 2);
                double tp1 = -2 * n2 * (b - a) / (np * np);
                double tp2 = -2 * n2 * np * Math.Log(b / a) / (nm * nm);
                double tp3 = n2 * (1 / b - 1 / a) / 2;
                double tp4 = 16 * n2 * n2 * (n2 + 1) * Math.Log((2 * np * b - nm * nm) / (2 * np * a - nm * nm)) / (np * np * np * nm * nm);
                double tp5 = 16 * n2 * n2 * n2 * (1 / (2 * np * b - nm * nm) - 1 / (2 * np * a - nm * nm)) / (np * np * np);

                double result = (ts + tp1 + tp2 + tp3 + tp4 + tp5) / (2 * sa2);
                return Math.Max(0, Math.Min(1, result));
            });
        }

        /// <summary>
        /// Load leaf optical data from a local JSON file
        /// </summary>
        public static LeafOpticalConsts LoadLocalLeafOpticalDataUncached()
        {
            string path = DefaultLeafOpticalDataPath;
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Leaf optical data file not found at {path}. Please provide a valid LeafOpticalConstants or ensure the file exists.");
            }

            try
            {
                string json = File.ReadAllText(path);
                var OpticalData = JsonConvert.DeserializeObject<LeafOpticalDataJason>(json);
                if(OpticalData == null || OpticalData.Wavelength == null)
                {
                    throw new InvalidDataException($"Deserialized OpticalData or its Wavelength array is null from file: {path}");
                }

                // Create the wavelength-to-index mapping for speeding up the subset of the specified wavelengths
                var wavelengthToIndex = new Dictionary<double, int>();
                for (int i = 0; i < OpticalData.Wavelength.Length; i++)
                {
                    wavelengthToIndex[OpticalData.Wavelength[i]] = i;
                }

                return new LeafOpticalConsts
                {
                    Wavelength = Vector<double>.Build.DenseOfArray(OpticalData.Wavelength),
                    RefractiveIndex = Vector<double>.Build.DenseOfArray(OpticalData.RefractiveIndex),
                    SAC_CAB = Vector<double>.Build.DenseOfArray(OpticalData.SAC_CAB),
                    SAC_CAR = Vector<double>.Build.DenseOfArray(OpticalData.SAC_CAR),
                    SAC_EWT = Vector<double>.Build.DenseOfArray(OpticalData.SAC_EWT),
                    SAC_LMA = Vector<double>.Build.DenseOfArray(OpticalData.SAC_LMA),
                    Tav40 = Vector<double>.Build.DenseOfArray(OpticalData.Tav40),
                    Tav90 = Vector<double>.Build.DenseOfArray(OpticalData.Tav90),
                    SAC_ANT = Vector<double>.Build.DenseOfArray(OpticalData.SAC_ANT),
                    SAC_BROWN = Vector<double>.Build.DenseOfArray(OpticalData.SAC_BROWN),
                    SAC_PROT = Vector<double>.Build.DenseOfArray(OpticalData.SAC_PROT),
                    SAC_CBC = Vector<double>.Build.DenseOfArray(OpticalData.SAC_CBC),
                    WavelengthToIndex = wavelengthToIndex
                };
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load leaf optical data from {DefaultLeafOpticalDataPath}: {ex.Message}", ex);
            }
        }

        // Helper class for JSON deserialization
        private class LeafOpticalDataJason
        {
            public double[] Wavelength { get; set; }
            public double[] RefractiveIndex { get; set; }
            public double[] SAC_CAB { get; set; }
            public double[] SAC_CAR { get; set; }
            public double[] SAC_EWT { get; set; }
            public double[] SAC_LMA { get; set; }
            public double[] Tav40 { get; set; }
            public double[] Tav90 { get; set; }
            public double[] SAC_ANT { get; set; }    
            public double[] SAC_BROWN { get; set; }  
            public double[] SAC_PROT { get; set; }  
            public double[] SAC_CBC { get; set; }   
        }
    }
}

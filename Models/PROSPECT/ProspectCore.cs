using System;
using System.Linq;
using System.IO;
using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.IntegralTransforms;
using Newtonsoft.Json;
using APSIM.Shared.Utilities; // Add this for PathUtilities

namespace Models.Prospect
{
    /// <summary>
    /// Implements the core PROSPECT radiative transfer model for leaf optical properties
    /// </summary>
    /// <remarks>
    /// Reference: Jacquemoud, S., and Baret, F. (1990). PROSPECT: A model of leaf optical properties spectra.
    /// </remarks>
    public static class ProspectCore
    {
        // 光谱常数结构体
        /// <summary>
        /// Contains spectral constants required for PROSPECT calculations
        /// </summary>
        public struct SpectralConstants
        {
            /// <summary>Wavelength array in nanometers</summary>
            public Vector<double> Wavelengths;      // 波长(nm)
            /// <summary>Refractive index spectrum</summary>
            public Vector<double> RefractiveIndex;   // 折射率
            /// <summary>Specific absorption coefficient for chlorophyll</summary>
            public Vector<double> SAC_CHL;          // 叶绿素比吸收系数
            /// <summary>Specific absorption coefficient for carotenoids</summary>
            public Vector<double> SAC_CAR;          // 类胡萝卜素比吸收系数
            /// <summary>Specific absorption coefficient for water</summary>
            public Vector<double> SAC_EWT;          // 水分比吸收系数
            /// <summary>Specific absorption coefficient for dry matter</summary>
            public Vector<double> SAC_LMA;          // 干物质比吸收系数
            /// <summary>Transmissivity at 40° incidence angle</summary>
            public Vector<double> Tav40;            // 40度入射角透射率
            /// <summary>Transmissivity at 90° incidence angle</summary>
            public Vector<double> Tav90;            // 90度入射角透射率
            /// <summary>Specific absorption coefficient for anthocyanin</summary>
            public Vector<double> SAC_ANT;    // Added
            /// <summary>Specific absorption coefficient for brown pigmentn</summary>
            public Vector<double> SAC_BROWN;  // Added
            /// <summary>Specific absorption coefficient for protein</summary>
            public Vector<double> SAC_PROT;   // Added
            /// <summary>Specific absorption coefficient for non-protein carbon-based constituent</summary>
            public Vector<double> SAC_CBC;    // Added
        }

        /// <summary>
        /// Default path to the local spectral data file in JSON format.
        /// </summary>

        // Relative path from APSIM root
        // private static readonly string RelativeSpectralDataPath = ".\\Models\\PROSPECT\\SpecPROSPECT_FullRange.json";
        
        // Relative path from APSIM bin directory to Models\PROSPECT
        private static readonly string RelativeSpectralDataPath = "..\\..\\..\\Models\\PROSPECT\\SpecPROSPECT_FullRange.json";
        private static string DefaultSpectralDataPath => PathUtilities.GetAbsolutePath(RelativeSpectralDataPath, AppDomain.CurrentDomain.BaseDirectory);

        // 运行PROSPECT模型
        /// <summary>
        /// Runs the PROSPECT model to calculate leaf reflectance and transmittance
        /// </summary>
        /// <param name="spec">Spectral constants container</param>
        /// <param name="N">Leaf structure parameter (unitless)</param>
        /// <param name="CHL">Chlorophyll content (μg/cm²)</param>
        /// <param name="CAR">Carotenoid content (μg/cm²)</param>
        /// <param name="EWT">Equivalent Water Thickness (g/cm²)</param>
        /// <param name="LMA">Leaf Mass per Area (g/cm²)</param>
        /// <param name="ANT">Anthocyanin content (μg/cm²)</param>
        /// <param name="BROWN">Brown pigment content (Arbitrary units)</param>
        /// <param name="PROT">Protein content (g/cm²)</param>
        /// <param name="CBC">NonProt Carbon-based constituent content (g/cm²)</param>
        /// <param name="alpha">Incidence angle in degrees</param>
        /// <returns>Tuple containing reflectance and transmittance spectra</returns>
        public static (Vector<double> Reflectance, Vector<double> Transmittance) Run(
            SpectralConstants? spec = null, // Optional parameter with null default
            double N = 1.5,
            double CHL = 40.0,
            double CAR = 8.0,
            double EWT = 0.01,
            double LMA = 0.008,
            double ANT = 0.0,
            double BROWN = 0.0,
            double PROT = 0.0,
            double CBC = 0.0,
            double alpha = 40.0)
        {
            // Load spectral constants if not provided
            SpectralConstants spectralData = spec ?? LoadLocalSpectralData();

            // Input validation
            if (N <= 0) throw new ArgumentException("Leaf structure parameter N must be positive");
            if (CHL < 0 || CAR < 0 || EWT < 0 || LMA < 0 || ANT < 0 || BROWN < 0 || PROT < 0 || CBC < 0)
                throw new ArgumentException("Leaf constituents must be non-negative");
            if (alpha < 0 || alpha > 90)
                throw new ArgumentException("Incidence angle must be between 0 and 90 degrees");

            // 计算总吸收系数
            Vector<double> Kall = (CHL * spectralData.SAC_CHL +
                                 CAR * spectralData.SAC_CAR +
                                 EWT * spectralData.SAC_EWT +
                                 LMA * spectralData.SAC_LMA +
                                 ANT * spectralData.SAC_ANT +
                                 BROWN * spectralData.SAC_BROWN +
                                 PROT * spectralData.SAC_PROT +
                                 CBC * spectralData.SAC_CBC) / N;

            // 计算单层透射率tau
            Vector<double> tau = ComputeTau(Kall);

            // 计算界面透反射
            Vector<double> talf = alpha == 40 ? spectralData.Tav40 : ComputeTav(alpha, spectralData.RefractiveIndex);
            Vector<double> ralf = 1.0 - talf;
            Vector<double> t12 = spectralData.Tav90;
            Vector<double> r12 = 1.0 - t12;
            Vector<double> t21 = t12.PointwiseDivide(spectralData.RefractiveIndex.PointwisePower(2));
            Vector<double> r21 = 1.0 - t21;

            // 顶层反射透射
            Vector<double> denom = 1.0 - r21.PointwiseMultiply(r21).PointwiseMultiply(tau.PointwisePower(2));
            Vector<double> Ta = talf.PointwiseMultiply(tau).PointwiseMultiply(t21).PointwiseDivide(denom);
            Vector<double> Ra = ralf + r21.PointwiseMultiply(tau).PointwiseMultiply(Ta);

            // 底层反射透射
            Vector<double> T = t12.PointwiseMultiply(tau).PointwiseMultiply(t21).PointwiseDivide(denom);
            Vector<double> R = r12 + r21.PointwiseMultiply(tau).PointwiseMultiply(T);

            // N层叠加计算
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

            // 处理零吸收情况
            for (int i = 0; i < R.Count; i++)
            {
                if (R[i] + T[i] >= 1.0 - 1e-10)
                {
                    Tsub[i] = T[i] / (T[i] + (1 - T[i]) * Math.Max(N - 1, 1e-10));
                    Rsub[i] = 1 - Tsub[i];
                }
            }

            // 最终结果
            denom = 1 - Rsub.PointwiseMultiply(R) + 1e-10;
            Vector<double> transmittance = Ta.PointwiseMultiply(Tsub).PointwiseDivide(denom);
            Vector<double> reflectance = Ra + Ta.PointwiseMultiply(Rsub).PointwiseMultiply(T).PointwiseDivide(denom);

            // Clamp results to physical limits
            reflectance = reflectance.Map(x => Math.Round(Math.Max(0, Math.Min(1, x)), 4)); // 4 digits
            transmittance = transmittance.Map(x => Math.Round(Math.Max(0, Math.Min(1, x)), 4));

            return (reflectance, transmittance);
        }

        // 计算单层透射率tau
        private static Vector<double> ComputeTau(Vector<double> k)
        {
            return k.Map(k_i =>
            {
                if (k_i <= 0) return 1.0;
                if (k_i > 100) return 0.0; // Prevent overflow
                double expTerm = (1 - k_i) * Math.Exp(-k_i);
                double eiTerm = k_i * k_i * SpecialFunctions.ExponentialIntegral(k_i, 1);
                return Math.Max(0, Math.Min(1, expTerm + eiTerm));
            });
        }

        // 计算平均透射率
        private static Vector<double> ComputeTav(double alpha, Vector<double> nr)
        {
            double rd = Math.PI / 180.0;
            double sa = Math.Sin(alpha * rd);
            double sa2 = sa * sa;

            return nr.Map(n =>
            {
                double n2 = n * n;
                double np = n2 + 1;
                double nm = n2 - 1;
                double a = (n + 1) * (n + 1) / 2;
                double k = -(n2 - 1) * (n2 - 1) / 4;
                double b2 = sa2 - np / 2;
                double b1 = alpha == 90 ? 0 : Math.Sqrt(b2 * b2 + k);
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

        // Load spectral data from a local JSON file
        private static SpectralConstants LoadLocalSpectralData()
        {
            string path = DefaultSpectralDataPath;
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Spectral data file not found at {path}. Please provide a valid spec or ensure the file exists.");
            }

            try
            {
                string json = File.ReadAllText(path);
                var data = JsonConvert.DeserializeObject<SpectralDataJson>(json);

                return new SpectralConstants
                {
                    Wavelengths = Vector<double>.Build.DenseOfArray(data.Wavelengths),
                    RefractiveIndex = Vector<double>.Build.DenseOfArray(data.RefractiveIndex),
                    SAC_CHL = Vector<double>.Build.DenseOfArray(data.SAC_CHL),
                    SAC_CAR = Vector<double>.Build.DenseOfArray(data.SAC_CAR),
                    SAC_EWT = Vector<double>.Build.DenseOfArray(data.SAC_EWT),
                    SAC_LMA = Vector<double>.Build.DenseOfArray(data.SAC_LMA),
                    Tav40 = Vector<double>.Build.DenseOfArray(data.Tav40),
                    Tav90 = Vector<double>.Build.DenseOfArray(data.Tav90),
                    SAC_ANT = Vector<double>.Build.DenseOfArray(data.SAC_ANT),
                    SAC_BROWN = Vector<double>.Build.DenseOfArray(data.SAC_BROWN),
                    SAC_PROT = Vector<double>.Build.DenseOfArray(data.SAC_PROT),
                    SAC_CBC = Vector<double>.Build.DenseOfArray(data.SAC_CBC)
                };
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load spectral data from {DefaultSpectralDataPath}: {ex.Message}", ex);
            }
        }

        // Helper class for JSON deserialization
        private class SpectralDataJson
        {
            public double[] Wavelengths { get; set; }
            public double[] RefractiveIndex { get; set; }
            public double[] SAC_CHL { get; set; }
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

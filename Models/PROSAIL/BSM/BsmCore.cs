using MathNet.Numerics.LinearAlgebra;
using Models.PROSAIL;
using Models.PROSAIL.PROSPECT;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Models.PROSAIL.BSM
{
    /// <summary>
    /// JSON deserialization helper for BSM spectral data.
    /// </summary>
    internal class BsmSpectralDataJson
    {
        public double[] wavelength { get; set; }
        public double[] GSV_1 { get; set; }
        public double[] GSV_2 { get; set; }
        public double[] GSV_3 { get; set; }
        public double[] nw { get; set; }
        public double[] kw { get; set; }
    }

    /// <summary>
    /// Implements the BSM (Brightness Soil Model) for soil reflectance simulation.
    /// Based on the BSM model from SCOPE (Christiaan van der Tol),
    /// referenced at http://dx.doi.org/10.1016/j.rse.2020.111870.
    /// </summary>
    public static class BsmCore
    {
        /// <summary>Soil moisture capacity (SMC) — fixed at 25% in the BSM model.</summary>
        private const double SMC = 25.0;

        /// <summary>Effective optical thickness of single water film.</summary>
        private const double DelEff = 0.015;

        /// <summary>Number of water film layers (0 = dry, 1–6 = increasing wetness).</summary>
        private const int NWaterFilms = 7; // k = 0..6

        /// <summary>
        /// Loads BSM spectral data from a JSON file (400–2400 nm).
        /// </summary>
        /// <param name="path">Path to BSM_data.json.</param>
        /// <returns>Populated <see cref="BsmSpectralData"/>.</returns>
        public static BsmSpectralData LoadBsmData(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"BSM data file not found: {path}");

            return ParseBsmData(File.ReadAllText(path), path);
        }

        /// <summary>
        /// Loads BSM spectral data from an embedded resource (400–2400 nm).
        /// </summary>
        /// <param name="resourceName">Fully-qualified embedded resource name.</param>
        /// <returns>Populated <see cref="BsmSpectralData"/>.</returns>
        public static BsmSpectralData LoadBsmDataFromResource(string resourceName)
        {
            return ParseBsmData(EmbeddedResourceLoader.ReadText(resourceName), resourceName);
        }

        private static BsmSpectralData ParseBsmData(string json, string source)
        {
            var raw = JsonConvert.DeserializeObject<BsmSpectralDataJson>(json)
                ?? throw new InvalidOperationException($"Failed to deserialize BSM data from {source}");

            if (raw.wavelength == null || raw.GSV_1 == null || raw.GSV_2 == null ||
                raw.GSV_3 == null || raw.nw == null || raw.kw == null)
                throw new InvalidOperationException("BSM data JSON is missing one or more required arrays.");

            int n = raw.wavelength.Length;
            if (raw.GSV_1.Length != n || raw.GSV_2.Length != n || raw.GSV_3.Length != n ||
                raw.nw.Length != n || raw.kw.Length != n)
                throw new InvalidOperationException("BSM data JSON arrays have inconsistent lengths.");

            var wavelengthToIndex = new Dictionary<double, int>(n);
            for (int i = 0; i < n; i++)
                wavelengthToIndex[raw.wavelength[i]] = i;

            return new BsmSpectralData
            {
                Wavelength = Vector<double>.Build.DenseOfArray(raw.wavelength),
                GSV_1 = Vector<double>.Build.DenseOfArray(raw.GSV_1),
                GSV_2 = Vector<double>.Build.DenseOfArray(raw.GSV_2),
                GSV_3 = Vector<double>.Build.DenseOfArray(raw.GSV_3),
                nw = Vector<double>.Build.DenseOfArray(raw.nw),
                kw = Vector<double>.Build.DenseOfArray(raw.kw),
                WavelengthToIndex = wavelengthToIndex
            };
        }

        /// <summary>
        /// Computes soil reflectance using the BSM model.
        /// The data covers 400–2400 nm; this method extends to 2500 nm by repeating
        /// the last 100 values, matching the R implementation behaviour.
        /// </summary>
        /// <param name="B">Soil brightness (0–1).</param>
        /// <param name="lat">Spectral shape latitude (20–40°).</param>
        /// <param name="lon">Spectral shape longitude (45–65°).</param>
        /// <param name="SMp">Soil moisture volume percentage (5–55%).</param>
        /// <param name="data">Pre-loaded BSM spectral data (400–2400 nm).</param>
        /// <returns><see cref="SoilOptics"/> with wavelengths 400–2500 nm and reflectance.</returns>
        public static SoilOptics BSM(double B, double lat, double lon, double SMp, BsmSpectralData data)
        {
            int n = data.Wavelength.Count;

            // --- Dry soil reflectance ---
            double f1 = B * Math.Sin(lat * Math.PI / 180.0);
            double f2 = B * Math.Cos(lat * Math.PI / 180.0) * Math.Sin(lon * Math.PI / 180.0);
            double f3 = B * Math.Cos(lat * Math.PI / 180.0) * Math.Cos(lon * Math.PI / 180.0);

            Vector<double> rdry = f1 * data.GSV_1 + f2 * data.GSV_2 + f3 * data.GSV_3;

            // --- Wet soil reflectance ---
            double mu = (SMp - 5.0) / SMC;

            Vector<double> rwet;
            if (mu <= 0)
            {
                rwet = rdry;
            }
            else
            {
                Vector<double> nw = data.nw;
                Vector<double> kw = data.kw;

                // TAV(90, 2.0/nw): element-wise division then ComputeTav with scalar result
                Vector<double> nw_inv2 = nw.Map(v => 2.0 / v);
                Vector<double> tav90_2_over_nw = ProspectCore.ComputeTav(90.0, nw_inv2);

                // TAV(90, 2.0): scalar, use a single-element vector
                Vector<double> nr2 = Vector<double>.Build.Dense(1, 2.0);
                double tav90_2 = ProspectCore.ComputeTav(90.0, nr2)[0];

                // TAV(90, nw) and TAV(40, nw)
                Vector<double> tav90_nw = ProspectCore.ComputeTav(90.0, nw);
                Vector<double> tav40_nw = ProspectCore.ComputeTav(40.0, nw);

                // Rbac: background reflectance (Lekner & Dorf 1988)
                // rbac = 1 - (1-rdry) * (rdry * TAV(90, 2/nw) / TAV(90,2) + 1 - rdry)
                Vector<double> rbac = rdry.MapIndexed((i, r) =>
                {
                    double tavRatio = tav90_2_over_nw[i] / tav90_2;
                    return 1.0 - (1.0 - r) * (r * tavRatio + 1.0 - r);
                });

                // p = 1 - TAV(90, nw) / nw^2  (rho21: water to air, diffuse)
                Vector<double> p = nw.MapIndexed((i, nwi) => 1.0 - tav90_nw[i] / (nwi * nwi));

                // Rw = 1 - TAV(40, nw)  (rho12: air to water, direct)
                Vector<double> Rw = tav40_nw.Map(t => 1.0 - t);

                // Poisson weights: fmul[k] = dpois(k, mu) for k = 0..6
                double[] fmul = PoissonProbabilities(mu, NWaterFilms);

                // two-way transmittance tw[k][i] = exp(-2 * kw[i] * deleff * k)
                // Rwet_k[k][i] = Rw[i] + (1-Rw[i])*(1-p[i])*tw[k][i]*rbac[i] / (1 - p[i]*tw[k][i]*rbac[i])
                // rwet[i] = rdry[i]*fmul[0] + sum_{k=1}^{6} Rwet_k[k][i] * fmul[k]
                double[] rwetArr = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double sum = rdry[i] * fmul[0];
                    for (int k = 1; k < NWaterFilms; k++)
                    {
                        double tw = Math.Exp(-2.0 * kw[i] * DelEff * k);
                        double denom = 1.0 - p[i] * tw * rbac[i];
                        double rwetK = Rw[i] + (1.0 - Rw[i]) * (1.0 - p[i]) * tw * rbac[i] / denom;
                        sum += rwetK * fmul[k];
                    }
                    rwetArr[i] = sum;
                }
                rwet = Vector<double>.Build.DenseOfArray(rwetArr);
            }

            // Extend 400–2400 nm → 400–2500 nm by replicating the last 100 values
            // R does: complementary <- tail(res, 100); complementary$wavelength <- seq(2401, 2500)
            int extN = n + 100;
            double[] extWl = new double[extN];
            double[] extRef = new double[extN];

            for (int i = 0; i < n; i++)
            {
                extWl[i] = data.Wavelength[i];
                extRef[i] = rwet[i];
            }
            // Last 100 wavelength values (2301–2400) get remapped to 2401–2500
            for (int i = 0; i < 100; i++)
            {
                extWl[n + i] = 2401.0 + i;
                extRef[n + i] = rwet[n - 100 + i];
            }

            return new SoilOptics(
                Vector<double>.Build.DenseOfArray(extWl),
                Vector<double>.Build.DenseOfArray(extRef)
            );
        }

        /// <summary>
        /// Computes Poisson probability mass function values P(k; mu) for k = 0..count-1.
        /// P(k) = mu^k * exp(-mu) / k!
        /// </summary>
        private static double[] PoissonProbabilities(double mu, int count)
        {
            double[] fmul = new double[count];
            double expNegMu = Math.Exp(-mu);
            double muPowK = 1.0;   // mu^0
            double factK = 1.0;    // 0!

            for (int k = 0; k < count; k++)
            {
                if (k > 0)
                {
                    muPowK *= mu;
                    factK *= k;
                }
                fmul[k] = muPowK * expNegMu / factK;
            }
            return fmul;
        }
    }
}

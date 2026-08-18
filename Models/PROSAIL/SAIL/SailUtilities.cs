using MathNet.Numerics.LinearAlgebra;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static CanopyOptics;
using static Models.Prosail.ProsailCore;
using static Models.PROSAIL.PROSPECT.ProspectCore;
using Models.PROSAIL;

// Define the namespace for SAIL utilities
namespace Models.PROSAIL.SAIL
{
    /// <summary>
    /// Provides static utility functions for SAIL model calculations.
    /// Includes helpers for LIDF, scattering calculations, spectral checks,
    /// fAPAR/Albedo calculations, and PROSPECT integration.
    /// </summary>
    /// <remarks>
    /// Acknowledgement: This C# implementation (script) of PROSAIL is implmented based on the 'prosail' R package (https://github.com/jbferet/prosail) 
    /// writen by Dr Jean-Baptiste Feret (jean-baptiste.feret@teledetection.fr). 
    /// Please appropriately cite the R package and other papers (as listed in the GitHub page).
    /// </remarks>
    public static class SailUtilities
    {
        #region Supporting Data Structures/Classes: define the inputs and outputs for the utility methods
        
        /// <summary>
        /// Represents the result of the Campbell or Dladgen function for Leaf Inclination Distribution (LIDF).
        /// </summary>
        public struct FoliarDistributionResult 
        {
            /// <summary>
            /// Leaf Inclination Distribution Function values (frequencies for each angle bin).
            /// </summary>
            public double[] Lidf { get; set; }

            /// <summary>
            /// Representative Leaf Inclination Angles (degrees) for each frequency bin.
            /// </summary>
            public double[] Litab { get; set; }
        }

        /// <summary>
        /// Represents the result of the Volscatt function (volume scattering components).
        /// These are angle-dependent factors used in SAIL calculations.
        /// </summary>
        public struct VolscattResult
        {
            /// <summary>
            /// Interception function (average projection factor G) for solar direction.
            /// </summary>
            public double Chi_s { get; set; }

            /// <summary>
            /// Interception function (average projection factor G) for viewing direction.
            /// </summary>
            public double Chi_o { get; set; }

            /// <summary>
            /// Scattering phase function component related to leaf reflectance (rho).
            /// </summary>
            public double Frho { get; set; }

            /// <summary>
            /// Scattering phase function component related to leaf transmittance (tau).
            /// </summary>
            public double Ftau { get; set; }
        }

        /// <summary>
        /// Represents the result of NonConservativeScattering or ConservativeScattering functions.
        /// Contains various layer reflectance and transmittance factors used in SAIL layer combination.
        /// </summary>
        public struct ScatteringResult 
        {
            /// <summary>Bi-hemispherical transmittance (Tdd).</summary>
            public double[] Tdd { get; set; }
            /// <summary>Bi-hemispherical reflectance (Rdd).</summary>
            public double[] Rdd { get; set; }
            /// <summary>Directional-hemispherical transmittance (solar incidence, Tsd).</summary>
            public double[] Tsd { get; set; }
            /// <summary>Directional-hemispherical reflectance (solar incidence, Rsd).</summary>
            public double[] Rsd { get; set; }
            /// <summary>Hemispherical-directional transmittance (observer view, Tdo).</summary>
            public double[] Tdo { get; set; }
            /// <summary>Hemispherical-directional reflectance (observer view, Rdo).</summary>
            public double[] Rdo { get; set; }
            /// <summary>Multiple scattering contribution to bi-directional reflectance (Rsod).</summary>
            public double[] Rsod { get; set; }
        }

        /// <summary>
        /// Represents the combined result of PROSPECT runs needed for SAIL,
        /// potentially containing separate optics for green and brown leaves.
        /// Used as input to the SAIL core functions.
        /// </summary>
        public struct AdjustedProspectResult 
        {
            /// <summary>Green leaf optical properties simulated by PROSPECT.</summary>
            public LeafOptics GreenLOP { get; set; }
            /// <summary>Brown leaf optical properties (may be null or same as GreenLOP).</summary>
            public LeafOptics? BrownLOP { get; set; }
        }
        #endregion

        // Constants used within the class
        private const double PI = Math.PI;
        private const double DEGREES_TO_RADIANS = PI / 180.0;

        #region Utility Methods

        /// <summary>
        /// Computes bidirectional reflectance factor (BRF) based on SAIL outputs and solar/diffuse light fractions.
        /// </summary>
        /// <remarks>
        /// The direct and diffuse light components are combined using the approach from:
        /// Francois et al. (2002) Conversion of 400-1100 nm vegetation albedo measurements into total shortwave broadband albedo
        ///     using a canopy radiative transfer model, Agronomie.
        /// Es = direct irradiance, Ed = diffuse irradiance.
        /// </remarks>
        /// <param name="wavelength">wavelength.</param>
        /// <param name="rdot">Hemispherical-directional reflectance factor (R_o) spectrum from SAIL. Array with one value per wavelength.</param>
        /// <param name="rsot">Bi-directional reflectance factor (R_so) spectrum from SAIL. Array with value per wavelength.</param>
        /// <param name="tts">Solar zenith angle (degrees). Single value used for all wavelengths.</param>
        /// <param name="atmosphericSpectralData">Atmospheric spectral data containing DirectLight (Es) and DiffuseLight (Ed) spectra.</param>
        /// <returns>Bidirectional reflectance factor (BRF) spectrum.</returns>
        public static CanopyBRF ComputeBRF(double[] wavelength, double[] rdot, double[] rsot, 
            double tts, AtmosphericSpectralData atmosphericSpectralData)
        {
            // Section: Direct / Diffuse Light Calculation
            double[] Es = atmosphericSpectralData.DirectLight;   // Direct irradiance component
            double[] Ed = atmosphericSpectralData.DiffuseLight;  // Diffuse irradiance component

            // Input validation: Ensure all spectral arrays have the same length
            if (wavelength.Length != rdot.Length || wavelength.Length != rsot.Length || 
                wavelength.Length != Es.Length || wavelength.Length != Ed.Length)
            {
                throw new ArgumentException("ComputeBRF: Input arrays (wavelength, rdot, rsot, Es, Ed) must have the same length.");
            }

            if (!atmosphericSpectralData.HasMatchingWavelengths(wavelength))
            {
                throw new ArgumentException("ComputeBRF: Wavelengths do not match the atmospheric spectral data.");
            }

            // Convert angles to radians
            double rd = DEGREES_TO_RADIANS;
            double solarElevationRad = (90.0 - tts) * rd; // Solar elevation angle in radians
            double sinSolarElevation = Math.Sin(solarElevationRad);

            // Calculate the skyl factor (fraction of diffuse light) based on solar elevation
            // Formula from Francois et al. (2002)
            double skyl = 0.847 - 1.61 * sinSolarElevation + 1.04 * sinSolarElevation * sinSolarElevation;
            // Ensure skyl is within physical bounds [0, 1]
            skyl = Math.Max(0.0, Math.Min(1.0, skyl)); 

            int nLambda = rdot.Length; // Number of spectral points
            double[] BRF = new double[nLambda]; // Initialize the result array

            // Calculate BRF per wavelength
            for (int i = 0; i < nLambda; i++)
            {
                // Calculate effective direct and diffuse irradiance components reaching the canopy
                double effectiveDirectIrradiance = (1.0 - skyl) * Es[i];
                double effectiveDiffuseIrradiance = skyl * Ed[i];
                // Total irradiance for weighting
                double totalIrradiance = effectiveDirectIrradiance + effectiveDiffuseIrradiance;

                if (Math.Abs(totalIrradiance) < 1e-9)
                {
                    // If no light, BRF is undefined or zero. Defaulting to zero.
                    BRF[i] = 0;
                }
                else
                {
                    // Calculate BRF as the weighted average of R_o and R_so based on diffuse/direct fractions
                    // BRF = (R_o * DiffuseIrrad + R_so * DirectIrrad) / TotalIrrad
                    BRF[i] = (rdot[i] * effectiveDiffuseIrradiance + rsot[i] * effectiveDirectIrradiance) / totalIrradiance;
                }
                // Ensure BRF result is within physical bounds [0, 1]
                BRF[i] = Math.Max(0.0, Math.Min(1.0, BRF[i]));
            }

            return new CanopyBRF(wavelength, BRF);
        }

        /// <summary>
        /// Computes the fraction of absorbed photosynthetically active radiation (fAPAR).
        /// </summary>
        /// <remarks>
        /// Uses the direct/diffuse approach from Francois et al. (2002).
        /// Integrates absorbed radiation over the PAR range (typically 400-700 nm).
        /// Requires canopy absorptance values (Abs_dir, Abs_hem) from SAIL output.
        /// </remarks>
        /// <param name="abs_dir">Canopy absorptance spectrum for direct solar flux.</param>
        /// <param name="abs_hem">Canopy absorptance spectrum for hemispherical diffuse flux.</param>
        /// <param name="tts">Solar zenith angle (degrees).</param>
        /// <param name="atmosphericSpectralData">Atmospheric data (DirectLight, DiffuseLight, Wavelength).</param>
        /// <param name="parRangeMin">Minimum wavelength (nm) for PAR integration (default 400).</param>
        /// <param name="parRangeMax">Maximum wavelength (nm) for PAR integration (default 700).</param>
        /// <returns>Fraction of Absorbed Photosynthetically Active Radiation (fAPAR, unitless).</returns>
        public static double ComputeFAPAR(double[] abs_dir, double[] abs_hem, double tts,
            AtmosphericSpectralData atmosphericSpectralData, double parRangeMin = 400, double parRangeMax = 700)
        {
            // Direct / Diffuse Light Calculation
            double[] Es = atmosphericSpectralData.DirectLight;
            double[] Ed = atmosphericSpectralData.DiffuseLight;
            double[] lambda = atmosphericSpectralData.Wavelength;

            // Input validation
            if (abs_dir.Length != abs_hem.Length || abs_dir.Length != Es.Length || abs_dir.Length != Ed.Length || abs_dir.Length != lambda.Length)
            {
                throw new ArgumentException("ComputeFAPAR: Input arrays must have the same length.");
            }

            // Calculate skyl factor (as in Compute_BRF)
            double rd = DEGREES_TO_RADIANS;
            double solarElevationRad = (90.0 - tts) * rd;
            double sinSolarElevation = Math.Sin(solarElevationRad);
            double skyl = 0.847 - 1.61 * sinSolarElevation + 1.04 * sinSolarElevation * sinSolarElevation;
            skyl = Math.Max(0.0, Math.Min(1.0, skyl));

            double totalAbsorbedPAR = 0;    // Accumulator for absorbed PAR energy
            double totalIncidentPAR = 0;    // Accumulator for incident PAR energy

            // Integrate over the PAR range
            for (int i = 0; i < lambda.Length; i++)
            {
                // Check if current wavelength is within the PAR range
                if (lambda[i] >= parRangeMin && lambda[i] <= parRangeMax)
                {
                    // Calculate effective irradiance components
                    double directIrradiance = (1.0 - skyl) * Es[i];
                    double diffuseIrradiance = skyl * Ed[i];
                    double incident = directIrradiance + diffuseIrradiance;

                    // Calculate absorbed energy: AbsDirect * DirectIrrad + AbsHemispheric * DiffuseIrrad
                    double absorbed = abs_dir[i] * directIrradiance + abs_hem[i] * diffuseIrradiance;

                    // Accumulate total incident and absorbed PAR energy
                    // NOTE: This performs simple summation, assuming equal spectral bandwidths.
                    // For higher accuracy with non-uniform sampling, use numerical integration.
                    totalAbsorbedPAR += absorbed;
                    totalIncidentPAR += incident;
                }
            }

            // Calculate fAPAR ratio
            if (Math.Abs(totalIncidentPAR) < 1e-9)
            {
                return 0;
            }

            double fAPAR = totalAbsorbedPAR / totalIncidentPAR;

            // Ensure fAPAR is within physical bounds [0, 1]
            return Math.Max(0.0, Math.Min(1.0, fAPAR));
        }

        /// <summary>
        /// Computes broadband albedo over a specified spectral range.
        /// </summary>
        /// <remarks>
        /// Uses the direct/diffuse approach from Francois et al. (2002).
        /// Requires SAIL outputs Rsdstar (hemispherical reflectance for direct incidence)
        /// and Rddstar (hemispherical reflectance for diffuse incidence).
        /// </remarks>
        /// <param name="rsdstar">Contribution of direct solar flux to albedo.</param>
        /// <param name="rddstar">Contribution of hemispherical diffuse flux to albedo.</param>
        /// <param name="tts">Solar zenith angle (degrees).</param>
        /// <param name="atmosphericSpectralData">Atmospheric spectral data (DirectLight, DiffuseLight, Wavelength).</param>
        /// <param name="albedoRangeMin">Minimum wavelength (nm) for albedo integration (default 400).</param>
        /// <param name="albedoRangeMax">Maximum wavelength (nm) for albedo integration (default 2400).</param>
        /// <returns>Broadband albedo value (unitless fraction) over the specified range.</returns>
        public static double ComputeAlbedo(double[] rsdstar, double[] rddstar, double tts, 
            AtmosphericSpectralData atmosphericSpectralData, double albedoRangeMin = 400, double albedoRangeMax = 2400)
        {
            // Direct / Diffuse Light Calculation
            double[] Es = atmosphericSpectralData.DirectLight;
            double[] Ed = atmosphericSpectralData.DiffuseLight;
            double[] lambda = atmosphericSpectralData.Wavelength;

            // Input validation
            if (rsdstar.Length != rddstar.Length || rsdstar.Length != Es.Length || rsdstar.Length != Ed.Length || rsdstar.Length != lambda.Length)
            {
                throw new ArgumentException("ComputeAlbedo: Input arrays must have the same length.");
            }

            // Calculate skyl factor
            double rd = DEGREES_TO_RADIANS;
            double solarElevationRad = (90.0 - tts) * rd;
            double sinSolarElevation = Math.Sin(solarElevationRad);
            double skyl = 0.847 - 1.61 * sinSolarElevation + 1.04 * sinSolarElevation * sinSolarElevation;
            skyl = Math.Max(0.0, Math.Min(1.0, skyl));

            double totalReflectedEnergy = 0; // Accumulator for reflected energy
            double totalIncidentEnergy = 0;  // Accumulator for incident energy

            // Integrate over the specified albedo range
            for (int i = 0; i < lambda.Length; i++)
            {
                // Check if wavelength is within the range
                if (lambda[i] >= albedoRangeMin && lambda[i] <= albedoRangeMax)
                {
                    // Calculate effective irradiance components
                    double directIrradiance = (1.0 - skyl) * Es[i];
                    double diffuseIrradiance = skyl * Ed[i];
                    double incident = directIrradiance + diffuseIrradiance;

                    // Calculate reflected energy: Rsd* * DirectIrrad + Rdd* * DiffuseIrrad
                    double reflected = rsdstar[i] * directIrradiance + rddstar[i] * diffuseIrradiance;

                    // Accumulate totals
                    // NOTE: Simple summation assumes equal spectral bandwidths. Integration needed for non-uniform sampling.
                    totalReflectedEnergy += reflected;
                    totalIncidentEnergy += incident;
                }
            }

            // Calculate albedo ratio
            if (Math.Abs(totalIncidentEnergy) < 1e-9)
            {
                return 0;
            }

            double albedo = totalReflectedEnergy / totalIncidentEnergy;

            // Ensure albedo is within physical bounds [0, 1]
            return Math.Max(0.0, Math.Min(1.0, albedo));
        }

        /// <summary>
        /// Computes scattering components for non-conservative scattering conditions (m > 0.01).
        /// Internal helper function for SAIL models (specifically 4SAIL2).
        /// Calculates reflectance and transmittance factors for a single layer.
        /// </summary>
        /// <param name="m">SAIL model exponent coefficient array, sqrt((att+sigb)*(att-sigb)).</param>
        /// <param name="lai">Leaf Area Index for the layer.</param>
        /// <param name="att">Attenuation coefficient array (1-sigf).</param>
        /// <param name="sigb">Diffuse backscattering coefficient array.</param>
        /// <param name="ks">Extinction coefficient for solar flux (scalar).</param>
        /// <param name="ko">Extinction coefficient for observed flux (scalar).</param>
        /// <param name="sf">Solar forward scattering coefficient array.</param>
        /// <param name="sb">Solar backscattering coefficient array.</param>
        /// <param name="vf">View forward scattering coefficient array.</param>
        /// <param name="vb">View backscattering coefficient array.</param>
        /// <param name="tss">Directional transmittance (solar) for the layer (scalar).</param>
        /// <param name="too">Directional transmittance (observer) for the layer (scalar).</param>
        /// <returns>A ScatteringResult struct containing Tdd, Rdd, Tsd, Rsd, Tdo, Rdo, Rsod arrays.</returns>
        public static ScatteringResult NonConservativeScattering(
            double[] m, double lai, double[] att, double[] sigb, double ks, double ko,
            double[] sf, double[] sb, double[] vf, double[] vb, double tss, double too)
        {
            int nLambda = m.Length; // Number of spectral bands
            // Input validation
            if (att.Length != nLambda || sigb.Length != nLambda || sf.Length != nLambda || sb.Length != nLambda || vf.Length != nLambda || vb.Length != nLambda)
            {
                throw new ArgumentException("NonConservativeScattering: Input arrays must have the same length as m.");
            }

            // Initialize result arrays
            double[] tdd = new double[nLambda];
            double[] rdd = new double[nLambda];
            double[] tsd = new double[nLambda];
            double[] rsd = new double[nLambda];
            double[] tdo = new double[nLambda];
            double[] rdo = new double[nLambda];
            double[] rsod = new double[nLambda];

            // Perform calculations per wavelength
            for (int i = 0; i < nLambda; i++)
            {
                // Assign local variables for clarity
                double mi = m[i];
                double atti = att[i];
                double sigbi = sigb[i];
                double sfi = sf[i];
                double sbi = sb[i];
                double vfi = vf[i];
                double vbi = vb[i];

                // Calculate rinf (reflectance of infinitely thick layer)
                double rinf = (atti - mi) / sigbi;

                // Intermediate exponential terms related to LAI
                double e1 = Math.Exp(-mi * lai); // exp(-m*LAI)
                double e2 = e1 * e1;           // exp(-2*m*LAI)
                double rinf2 = rinf * rinf;    // rinf^2
                double re = rinf * e1;         // rinf * exp(-m*LAI)

                // Denominator term used in several calculations
                double denom = 1.0 - rinf2 * e2; // 1 - rinf^2 * exp(-2*m*LAI)
                if (Math.Abs(denom) < 1e-12)
                {
                    denom = denom >= 0 ? 1e-12 : -1e-12;
                }

                // Calculate J functions using helper methods
                double J1ks_val = Jfunc1(ks, mi, lai); 
                double J2ks_val = Jfunc2(ks, mi, lai); 
                double J1ko_val = Jfunc1(ko, mi, lai); 
                double J2ko_val = Jfunc2(ko, mi, lai);

                // Calculate intermediate P, Q terms for solar and view directions
                double Ps = (sfi + sbi * rinf) * J1ks_val;
                double Qs = (sfi * rinf + sbi) * J2ks_val;
                double Pv = (vfi + vbi * rinf) * J1ko_val;
                double Qv = (vfi * rinf + vbi) * J2ko_val;

                // Calculate canopy-only (black background) reflectance/transmittance factors
                tdd[i] = (1.0 - rinf2) * e1 / denom;   // Bi-hemispherical Transmittance (Tdd)
                rdd[i] = rinf * (1.0 - e2) / denom;    // Bi-hemispherical Reflectance (Rdd)
                tsd[i] = (Ps - re * Qs) / denom;       // Directional-hemispherical Transmittance (solar, Tsd)
                rsd[i] = (Qs - re * Ps) / denom;       // Directional-hemispherical Reflectance (solar, Rsd)
                tdo[i] = (Pv - re * Qv) / denom;       // Hemispherical-directional Transmittance (view, Tdo)
                rdo[i] = (Qv - re * Pv) / denom;       // Hemispherical-directional Reflectance (view, Rdo)

                // Calculation for multiple scattering contribution (Rsod)
                double z = Jfunc2(ks, ko, lai);
                double g1_denom = ko + mi;
                double g2_denom = ks + mi;
                
                // Avoid division by zero
                if (Math.Abs(g1_denom) < 1e-12)
                {
                    g1_denom = g1_denom >= 0 ? 1e-12 : -1e-12;
                }

                if (Math.Abs(g2_denom) < 1e-12)
                {
                    g2_denom = g2_denom >= 0 ? 1e-12 : -1e-12;
                }

                // Intermediate g1, g2 terms
                double g1 = (z - J1ks_val * too) / g1_denom; // Note: uses layer transmittance 'too'
                double g2 = (z - J1ko_val * tss) / g2_denom; // Note: uses layer transmittance 'tss'

                // Intermediate T terms for Rsod calculation
                double Tv1 = (vfi * rinf + vbi) * g1;
                double Tv2 = (vfi + vbi * rinf) * g2;
                double T1 = Tv1 * (sfi + sbi * rinf);
                double T2 = Tv2 * (sfi * rinf + sbi);
                double T3 = (rdo[i] * Qs + tdo[i] * Ps) * rinf;

                // Denominator for Rsod
                double rsod_denom = 1.0 - rinf2;
                // Avoid division by zero
                if (Math.Abs(rsod_denom) < 1e-12) rsod_denom = rsod_denom >= 0 ? 1e-12 : -1e-12;

                // Multiple scattering contribution to bidirectional canopy reflectance
                rsod[i] = (T1 + T2 - T3) / rsod_denom;
            }

            // Return the struct containing all calculated arrays
            return new ScatteringResult { Tdd = tdd, Rdd = rdd, Tsd = tsd, Rsd = rsd, Tdo = tdo, Rdo = rdo, Rsod = rsod };
        }

        /// <summary>
        /// Computes scattering components for conservative or near-conservative scattering conditions (m no larger than 0.01).
        /// Internal helper function for SAIL models (specifically 4SAIL2). 
        /// Uses different formulae than NonConservativeScattering.
        /// </summary>
        /// <param name="m">SAIL model exponent coefficient array, sqrt((att+sigb)*(att-sigb)).</param>
        /// <param name="lai">Leaf Area Index for the layer.</param>
        /// <param name="att">Attenuation coefficient array (1-sigf).</param>
        /// <param name="sigb">Diffuse backscattering coefficient array.</param>
        /// <param name="ks">Extinction coefficient for solar flux (scalar).</param>
        /// <param name="ko">Extinction coefficient for observed flux (scalar).</param>
        /// <param name="sf">Solar forward scattering coefficient array.</param>
        /// <param name="sb">Solar backscattering coefficient array.</param>
        /// <param name="vf">View forward scattering coefficient array.</param>
        /// <param name="vb">View backscattering coefficient array.</param>
        /// <param name="tss">Directional transmittance (solar) for the layer (scalar).</param>
        /// <param name="too">Directional transmittance (observer) for the layer (scalar).</param>
        /// <returns>A ScatteringResult struct containing Tdd, Rdd, Tsd, Rsd, Tdo, Rdo, Rsod arrays.</returns>
        public static ScatteringResult ConservativeScattering(
           double[] m, double lai, double[] att, double[] sigb, double ks, double ko,
           double[] sf, double[] sb, double[] vf, double[] vb, double tss, double too)
        {
            int nLambda = m.Length; // Number of spectral bands
            // Input validation
            if (att.Length != nLambda || sigb.Length != nLambda || sf.Length != nLambda || sb.Length != nLambda || vf.Length != nLambda || vb.Length != nLambda)
            {
                throw new ArgumentException("ConservativeScattering: Input arrays must have the same length as m.");
            }

            // Initialize result arrays
            double[] tdd = new double[nLambda];
            double[] rdd = new double[nLambda];
            double[] tsd = new double[nLambda];
            double[] rsd = new double[nLambda];
            double[] tdo = new double[nLambda];
            double[] rdo = new double[nLambda];
            double[] rsod = new double[nLambda];

            // Perform calculations per wavelength
            for (int i = 0; i < nLambda; i++)
            {
                // Assign local variables
                double mi = m[i];     // Should be close to 0
                double atti = att[i];
                double sigbi = sigb[i]; // Should be close to atti
                double sfi = sf[i];
                double sbi = sb[i];
                double vfi = vf[i];
                double vbi = vb[i];

                // Near or complete conservative scattering calculations
                double J4_val = Jfunc4(mi, lai); // Use J4 function
                double amsig = atti - sigbi;      // att - sigb (should be close to 0)
                double apsig = atti + sigbi;      // att + sigb (should be close to 2*att or 2*sigb)

                // Denominators for rtp, rtm calculation
                double denom_rtp = 1.0 + amsig * J4_val;
                double denom_rtm = 1.0 + apsig * J4_val;
                // Avoid division by zero
                if (Math.Abs(denom_rtp) < 1e-12) denom_rtp = denom_rtp >= 0 ? 1e-12 : -1e-12;
                if (Math.Abs(denom_rtm) < 1e-12) denom_rtm = denom_rtm >= 0 ? 1e-12 : -1e-12;

                // Intermediate rtp, rtm terms
                double rtp = (1.0 - amsig * J4_val) / denom_rtp;
                double rtm = (-1.0 + apsig * J4_val) / denom_rtm;

                // Calculate Rdd and Tdd for conservative case
                // Note: For perfect conservative scattering (m=0, amsig=0), rtp=1. Rdd+Tdd should equal 1.
                rdd[i] = 0.5 * (rtp + rtm);
                tdd[i] = 0.5 * (rtp - rtm);

                // Denominators involving extinction coefficients and m
                double dns = ks * ks - mi * mi; // k_sun^2 - m^2
                double dno = ko * ko - mi * mi; // k_obs^2 - m^2
                if (Math.Abs(dns) < 1e-12) dns = dns >= 0 ? 1e-12 : -1e-12;
                if (Math.Abs(dno) < 1e-12) dno = dno >= 0 ? 1e-12 : -1e-12;

                // Intermediate coefficients cks, cko, dks, dko
                double cks = (sbi * (ks - atti) - sfi * sigbi) / dns;
                double cko = (vbi * (ko - atti) - vfi * sigbi) / dno;
                double dks = (-sfi * (ks + atti) - sbi * sigbi) / dns;
                double dko = (-vfi * (ko + atti) - vbi * sigbi) / dno;

                // Intermediate bidirectional coefficient ho
                double ko_plus_ks = ko + ks;
                // Avoid division by zero
                if (Math.Abs(ko_plus_ks) < 1e-12) ko_plus_ks = ko_plus_ks >= 0 ? 1e-12 : -1e-12;
                double ho = (sfi * cko + sbi * dko) / ko_plus_ks;

                // Calculate reflectance and transmittance terms using conservative formulae
                rsd[i] = cks * (1.0 - tss * tdd[i]) - dks * rdd[i];
                rdo[i] = cko * (1.0 - too * tdd[i]) - dko * rdd[i];
                tsd[i] = dks * (tss - tdd[i]) - cks * tss * rdd[i];
                tdo[i] = dko * (too - tdd[i]) - cko * too * rdd[i];
                // Multiple scattering contribution to bidirectional reflectance
                rsod[i] = ho * (1.0 - tss * too) - cko * tsd[i] * too - dko * rsd[i];
            }

            // Return the struct containing results
            return new ScatteringResult { Tdd = tdd, Rdd = rdd, Tsd = tsd, Rsd = rsd, Tdo = tdo, Rdo = rdo, Rsod = rsod };
        }


        /// <summary>
        /// Computes the leaf angle distribution function (LIDF) using Campbell's ellipsoidal distribution.
        /// </summary>
        /// <remarks>
        /// Characterised by the average leaf inclination angle (ala) in degrees.
        /// Reference: Campbell 1986.
        /// </remarks>
        /// <param name="ala">Average leaf inclination angle (degrees).</param>
        /// <returns>A FoliarDistributionResult struct containing LIDF values (lidf) and representative angles (litab).</returns>
        public static FoliarDistributionResult Campbell(double ala)
        {
            // Predefined angle bins (midpoints 'litab' and boundaries 'tx1', 'tx2') from R code
            double[] tx1 = { 10.0, 20.0, 30.0, 40.0, 50.0, 60.0, 70.0, 80.0, 82.0, 84.0, 86.0, 88.0, 90.0 }; // Upper bounds
            double[] tx2 = { 0.0, 10.0, 20.0, 30.0, 40.0, 50.0, 60.0, 70.0, 80.0, 82.0, 84.0, 86.0, 88.0 }; // Lower bounds
            int nBins = tx1.Length; // Number of angle bins
            double[] litab = new double[nBins]; // Midpoint angles
            double[] tl1 = new double[nBins];   // Upper bounds in radians
            double[] tl2 = new double[nBins];   // Lower bounds in radians
            double[] freq = new double[nBins];  // Frequency (LIDF value) for each bin

            // Calculate bin midpoints and convert bounds to radians
            for (int i = 0; i < nBins; i++)
            {
                litab[i] = (tx2[i] + tx1[i]) / 2.0; // Midpoint angle
                tl1[i] = tx1[i] * DEGREES_TO_RADIANS; // Upper bound angle (rad)
                tl2[i] = tx2[i] * DEGREES_TO_RADIANS; // Lower bound angle (rad)
            }

            // Calculate eccentricity factor based on average leaf angle (ala)
            double excent = Math.Exp(-1.6184e-5 * Math.Pow(ala, 3) + 2.1145e-3 * Math.Pow(ala, 2) - 1.2390e-1 * ala + 3.2491);
            double totalFreqSum = 0; // Accumulator for normalization

            // Calculate frequency for each angle bin
            for (int i = 0; i < nBins; i++)
            {
                // Handle potential tan(90) issues
                double cos_tl1 = Math.Cos(tl1[i]);
                double cos_tl2 = Math.Cos(tl2[i]);
                double tan_tl1 = Math.Abs(cos_tl1) < 1e-9 ? double.PositiveInfinity : Math.Tan(tl1[i]);
                double tan_tl2 = Math.Abs(cos_tl2) < 1e-9 ? double.PositiveInfinity : Math.Tan(tl2[i]);
                double tan_tl1_sq = tan_tl1 * tan_tl1;
                double tan_tl2_sq = tan_tl2 * tan_tl2;

                // Calculate intermediate x1, x2 based on eccentricity and angles
                double x1, x2;
                // Avoid division by zero or issues with infinite tan
                if (double.IsInfinity(tan_tl1_sq)) x1 = 0;
                else x1 = excent / Math.Sqrt(1.0 + excent * excent * tan_tl1_sq);

                if (double.IsInfinity(tan_tl2_sq)) x2 = 0;
                else x2 = excent / Math.Sqrt(1.0 + excent * excent * tan_tl2_sq);

                // Check for spherical distribution case (excent == 1)
                if (Math.Abs(excent - 1.0) < 1e-9)
                {
                    // For spherical, frequency is proportional to the difference in cosines
                    freq[i] = Math.Abs(cos_tl1 - cos_tl2);
                }
                else // General ellipsoidal case
                {
                    double excent_sq = excent * excent;
                    double one_minus_excent_sq = 1.0 - excent_sq;

                    // If excent is very close to 1, numerical instability might occur. Revert to spherical.
                    if (Math.Abs(one_minus_excent_sq) < 1e-9)
                    {
                        freq[i] = Math.Abs(cos_tl1 - cos_tl2);
                    }
                    else
                    {
                        // Calculate alpha parameter
                        double alpha = excent / Math.Sqrt(Math.Abs(one_minus_excent_sq));
                        double alpha2 = alpha * alpha;
                        double x12 = x1 * x1;
                        double x22 = x2 * x2;
                        double dum1, dum2; // Intermediate integration results

                        if (excent > 1.0) // Prolate spheroid case
                        {
                            // Calculate terms involving sqrt(alpha^2 + x^2)
                            double alpha2_plus_x12 = alpha2 + x12;
                            double alpha2_plus_x22 = alpha2 + x22;
                            // Ensure non-negative sqrt arguments
                            double alpx1 = Math.Sqrt(Math.Max(0, alpha2_plus_x12));
                            double alpx2 = Math.Sqrt(Math.Max(0, alpha2_plus_x22));

                            // Calculate log terms carefully, avoiding log(<=0)
                            double log_arg1 = x1 + alpx1;
                            double log_arg2 = x2 + alpx2;
                            double log_term1 = log_arg1 > 1e-12 ? Math.Log(log_arg1) : Math.Log(1e-12); // Use small positive floor
                            double log_term2 = log_arg2 > 1e-12 ? Math.Log(log_arg2) : Math.Log(1e-12);

                            // Integrate between bounds
                            dum1 = x1 * alpx1 + alpha2 * log_term1;
                            dum2 = x2 * alpx2 + alpha2 * log_term2;
                            freq[i] = Math.Abs(dum1 - dum2); // Difference is the frequency for the bin
                        }
                        else // Oblate spheroid case (excent < 1)
                        {
                            // Calculate terms involving sqrt(alpha^2 - x^2)
                            double alpha2_minus_x12 = alpha2 - x12;
                            double alpha2_minus_x22 = alpha2 - x22;
                            // Ensure non-negative sqrt arguments
                            double almx1 = Math.Sqrt(Math.Max(0, alpha2_minus_x12));
                            double almx2 = Math.Sqrt(Math.Max(0, alpha2_minus_x22));

                            // Calculate asin terms carefully, ensuring argument is in [-1, 1]
                            // Avoid division by zero if alpha is zero (shouldn't happen if excent!=1)
                            double asin_arg1 = Math.Abs(alpha) < 1e-9 ? 0 : x1 / alpha;
                            double asin_arg2 = Math.Abs(alpha) < 1e-9 ? 0 : x2 / alpha;
                            // Clamp argument to valid domain for Asin
                            asin_arg1 = Math.Max(-1.0, Math.Min(1.0, asin_arg1));
                            asin_arg2 = Math.Max(-1.0, Math.Min(1.0, asin_arg2));

                            // Integrate between bounds
                            dum1 = x1 * almx1 + alpha2 * Math.Asin(asin_arg1);
                            dum2 = x2 * almx2 + alpha2 * Math.Asin(asin_arg2);
                            freq[i] = Math.Abs(dum1 - dum2); // Difference is the frequency
                        }
                    }
                }
                totalFreqSum += freq[i]; // Accumulate sum for normalization
            }

            // Normalize the calculated frequencies so they sum to 1
            double[] normalizedFreq = new double[nBins];
            if (Math.Abs(totalFreqSum) > 1e-9) // Avoid division by zero
            {
                for (int i = 0; i < nBins; i++)
                {
                    normalizedFreq[i] = freq[i] / totalFreqSum;
                }
            }

            // Return the LIDF values and corresponding angles
            return new FoliarDistributionResult { Lidf = normalizedFreq, Litab = litab };
        }

        /// <summary>
        /// Computes the leaf angle distribution function (LIDF) using Verhoef's bimodal distribution.
        /// </summary>
        /// <remarks>
        /// Uses the original bimodal LIDF from SAIL.
        /// Parameter 'a' controls average slope, 'b' controls bimodality.
        /// Reference: Verhoef (1998) NLR-TP-98154.
        /// </remarks>
        /// <param name="a">LIDF parameter 'a'.</param>
        /// <param name="b">LIDF parameter 'b'.</param>
        /// <returns>A FoliarDistributionResult struct containing LIDF values and representative angles.</returns>
        public static FoliarDistributionResult Dladgen(double a, double b)
        {
            // Representative angles (degrees) for LIDF bins, from R code
            double[] litab = { 5.0, 15.0, 25.0, 35.0, 45.0, 55.0, 65.0, 75.0, 81.0, 83.0, 85.0, 87.0, 89.0 };
            int nBins = litab.Length; // Number of LIDF bins
            double[] freq_cum = new double[nBins]; // Array to store cumulative frequencies

            // Define the upper bounds (degrees) for each angle bin, corresponding to litab values. From R code.
            double[] angle_bounds = { 10.0, 20.0, 30.0, 40.0, 50.0, 60.0, 70.0, 80.0, 82.0, 84.0, 86.0, 88.0, 90.0 };

            // Calculate the cumulative frequency at the upper bound of each bin using Dcum helper function
            for (int i = 0; i < nBins; i++)
            {
                freq_cum[i] = Dcum(a, b, angle_bounds[i]); // Calculate cumulative frequency up to angle t
            }
            freq_cum[nBins - 1] = 1.0; // match R's hardcode to ensure total cumulative frequency is 1 at 90 degrees

            // Calculate the frequency (LIDF value) for each bin by differencing cumulative frequencies
            double[] freq = new double[nBins]; // Initialize frequency array
            freq[0] = freq_cum[0]; // Frequency of the first bin is its cumulative frequency
            for (int i = 1; i < nBins; i++)
            {
                // Frequency = CumFreq(Upper) - CumFreq(Lower)
                freq[i] = freq_cum[i] - freq_cum[i - 1];
                // Ensure non-negative frequencies due to potential numerical precision issues
                freq[i] = Math.Max(0.0, freq[i]);
            }

            double sumFreq = 0;
            for (int i = 0; i < freq.Length; i++) sumFreq += freq[i];
            if (Math.Abs(sumFreq) > 1e-6 && Math.Abs(sumFreq - 1.0) > 1e-6) // Normalize if sum is not near 0 or 1
            {
                Console.WriteLine($"Warning: Dladgen frequencies sum to {sumFreq:F6}. Normalizing.");
                for (int i = 0; i < nBins; i++) freq[i] /= sumFreq;
            }

            // Return the calculated LIDF frequencies and representative angles
            return new FoliarDistributionResult { Lidf = freq, Litab = litab }; 
        }

        /// <summary>
        /// Computes the cumulative leaf angle distribution function value for Verhoef's LIDF.
        /// Internal helper function used by Dladgen.
        /// </summary>
        /// <param name="a">LIDF parameter 'a'.</param>
        /// <param name="b">LIDF parameter 'b'.</param>
        /// <param name="t">Angle (degrees) up to which cumulative frequency is calculated.</param>
        /// <returns>Cumulative frequency f (fraction of leaves with inclination is no larger than t).</returns>
        public static double Dcum(double a, double b, double t)
        {
            double rd = DEGREES_TO_RADIANS; // Convert degrees to radians
            double f; // Resulting cumulative frequency

            // Handle planophile special case (a >= 1)
            if (a >= 1.0)
            {
                f = 1.0 - Math.Cos(rd * t); //
            }
            else // General case requires iterative solution
            {
                double eps = 1e-8; // Convergence tolerance
                double delx = 1.0; // Initial difference, ensures loop starts
                // Initial guess for transformed angle 'x' based on input angle 't'
                double x = 2.0 * rd * t;
                double p = x; // Target value for iteration function
                int maxIter = 100; // Safety limit for iterations
                int iter = 0;
                double y = 0; // Initialize intermediate variable y

                // Iteratively solve for transformed angle 'x' using Newton-like method
                // Loop until the change 'dx' is smaller than tolerance 'eps'
                while (delx >= eps && iter < maxIter)
                {
                    // Calculate intermediate 'y' based on current 'x'
                    y = a * Math.Sin(x) + 0.5 * b * Math.Sin(2.0 * x);
                    // Calculate the correction step 'dx'
                    double dx = 0.5 * (y - x + p);
                    // Update the transformed angle 'x'
                    x += dx;
                    // Update the change for convergence check
                    delx = Math.Abs(dx);
                    iter++; // Increment iteration counter
                }

                // Check if iteration failed to converge
                if (iter >= maxIter)
                {
                    // Log a warning if convergence wasn't reached
                    Console.WriteLine($"Warning: Dcum iteration did not converge within {maxIter} iterations for t={t}, a={a}, b={b}");
                }

                // Calculate final cumulative frequency 'f' using the last 'y' and target 'p'
                f = (2.0 * y + p) / PI; //
            }
            // Ensure the result 'f' is within physical bounds [0, 1]
            return Math.Max(0.0, Math.Min(1.0, f));
        }


        /// <summary>
        /// J1 function for SAIL calculations, handling singularity near k=l.
        /// Calculates integral: J1(k,l,t) = integral[ exp(-l*x) - exp(-k*x) ] / (k-l) dx from 0 to t
        /// Approximated as: (exp(-l*t) - exp(-k*t)) / (k - l)
        /// </summary>
        /// <param name="k">Parameter k (e.g., extinction coefficient).</param>
        /// <param name="l">Parameter l (e.g., SAIL exponent m).</param>
        /// <param name="t">Parameter t (e.g., LAI).</param>
        /// <returns>Result of the J1 function.</returns>
        public static double Jfunc1(double k, double l, double t) // Method made static
        {
            // Calculate difference scaled by t, used to check for singularity
            double del = (k - l) * t;
            double Jout; // Result

            // Check if k and l are sufficiently different (|del| > tolerance)
            if (Math.Abs(del) > 1e-3)
            {
                // Standard formula
                double exp_lt = Math.Exp(-l * t);
                double exp_kt = Math.Exp(-k * t);
                double k_minus_l = k - l;
                // Handle exact k=l case which wasn't caught by |del| check if t=0
                if (Math.Abs(k_minus_l) < 1e-12)
                {
                    // Use L'Hopital's rule limit: t*exp(-kt)
                    Jout = t * exp_kt;
                }
                else
                {
                    // Standard calculation
                    Jout = (exp_lt - exp_kt) / k_minus_l;
                }
            }
            else // k is close to l: Use Taylor expansion to avoid singularity
            {
                // R code uses this approximation: 0.5 * t * (exp(-k*t) + exp(-l*t)) * (1.0 - del * del / 12.0)
                double exp_lt = Math.Exp(-l * t);
                double exp_kt = Math.Exp(-k * t);
                Jout = 0.5 * t * (exp_kt + exp_lt) * (1.0 - del * del / 12.0);
            }
            return Jout;
        }

        /// <summary>
        /// J2 function for SAIL calculations.
        /// Calculates integral: J2(k,l,t) = integral[ exp(-(k+l)*x) ] dx from 0 to t
        /// Result: (1 - exp(-(k+l)*t)) / (k + l)
        /// </summary>
        /// <param name="k">Parameter k.</param>
        /// <param name="l">Parameter l.</param>
        /// <param name="t">Parameter t.</param>
        /// <returns>Result of the J2 function.</returns>
        public static double Jfunc2(double k, double l, double t)
        {
            // Calculate the sum k+l
            double sum_kl = k + l;
            double Jout; // Result

            // Handle denominator near zero (k+l ≈ 0): indeterminate form 0/0 → NaN (matches R)
            if (Math.Abs(sum_kl) < 1e-9)
            {
                return double.NaN;
            }
            // Handle small exponent case using Taylor expansion exp(-x) ≈ 1 - x
            else if (Math.Abs(sum_kl * t) < 1e-6)
            {
                // (1 - (1 - sum_kl*t)) / sum_kl = sum_kl*t / sum_kl = t
                Jout = t * (1.0 - 0.5 * sum_kl * t);
                // Console.WriteLine($"Warning: sum_kl * tl ≈ 0 ({sum_kl * t:E3}). Return NaN");
                // return double.NaN;
            }
            else // Standard calculation
            {
                Jout = (1.0 - Math.Exp(-sum_kl * t)) / sum_kl;
            }
            return Jout;
        }


        /// <summary>
        /// J3 function for SAIL calculations.
        /// Calculates (1 - exp(-(k+l)*t)) / (k + l).
        /// </summary>
        /// <param name="k">Parameter k.</param>
        /// <param name="l">Parameter l.</param>
        /// <param name="t">Parameter t.</param>
        /// <returns>Result of the J3 function.</returns>
        public static double Jfunc3(double k, double l, double t)
        {
            // Functionally identical to Jfunc2 based on the R code
            return Jfunc2(k, l, t);
        }


        /// <summary>
        /// J4 function for treating (near) conservative scattering in SAIL.
        /// Formula: (1 - exp(-m*t)) / (m * (1 + exp(-m*t)))
        /// Includes approximation for small m*t.
        /// </summary>
        /// <param name="m">SAIL exponent m (should be near 0 for conservative scattering).</param>
        /// <param name="t">Parameter t (e.g., LAI).</param>
        /// <returns>Result of the J4 function.</returns>
        public static double Jfunc4(double m, double t)
        {
            // Calculate the product m*t
            double del = m * t;
            double out_val; // Result

            // Use direct formula if |m*t| is not too small
            if (Math.Abs(del) > 1e-3)
            {
                double exp_del = Math.Exp(-del); // Calculate exponential term
                double denom = m * (1.0 + exp_del); // Calculate denominator
                // Avoid division by zero
                if (Math.Abs(denom) < 1e-12)
                {
                    // Denominator zero likely means m->0 limit applies, or numerical issue. Use limit t/2.
                    out_val = 0.5 * t;
                    Console.WriteLine($"Warning: Jfunc4 denominator near zero for m={m}, t={t}. Using limit t/2.");
                }
                else
                {
                    // Standard calculation
                    out_val = (1.0 - exp_del) / denom;
                }
            }
            // Use Taylor expansion for small |del| (from R code)
            else
            {
                // R formula: 0.5 * t * (1.0 - del * del / 12.0)
                out_val = 0.5 * t * (1.0 - del * del / 12.0);
            }
            return out_val;
        }


        /// <summary>
        /// Compute volume scattering functions (Chi_s, Chi_o) and phase function components (Frho, Ftau).
        /// Internal helper function for SAIL, calculates angle-dependent geometric factors.
        /// Based on Wout Verhoef, april 2001, for CROMA.
        /// </summary>
        /// <param name="tts">Solar zenith angle (degrees).</param>
        /// <param name="tto">Viewing zenith angle (degrees).</param>
        /// <param name="psi">Relative azimuth angle (degrees).</param>
        /// <param name="ttl">Leaf inclination angle (degrees).</param>
        /// <returns>A VolscattResult struct containing chi_s, chi_o, frho, ftau.</returns>
        public static VolscattResult Volscatt(double tts, double tto, double psi, double ttl)
        {
            // Convert input angles from degrees to radians
            double rd = DEGREES_TO_RADIANS;
            double costs = Math.Cos(rd * tts);    // cos(solar zenith)
            double costo = Math.Cos(rd * tto);    // cos(view zenith)
            double sints = Math.Sin(rd * tts);    // sin(solar zenith)
            double sinto = Math.Sin(rd * tto);    // sin(view zenith)
            double cospsi = Math.Cos(rd * psi);   // cos(relative azimuth)
            double psir = rd * psi;             // relative azimuth (rad)
            double costl = Math.Cos(rd * ttl);    // cos(leaf inclination)
            double sintl = Math.Sin(rd * ttl);    // sin(leaf inclination)

            // Intermediate angle products
            double cs = costl * costs; // cos(tl)*cos(ts)
            double co = costl * costo; // cos(tl)*cos(to)
            double ss = sintl * sints; // sin(tl)*sin(ts)
            double so = sintl * sinto; // sin(tl)*sin(to)

            // Calculate transition angles (beta_s, beta_o)
            // These relate to angles where sun/view crosses the leaf normal plane.
            // Calculate cos(beta_s) and cos(beta_o)
            double cosbts = Math.Abs(ss) > 1e-6 ? -cs / ss : 5.0; // Use sentinel > 1 if ss is zero
            double cosbto = Math.Abs(so) > 1e-6 ? -co / so : 5.0; // Use sentinel > 1 if so is zero

            // Determine beta_s and intermediate ds
            double bts, ds;
            if (Math.Abs(cosbts) < 1.0) // Normal case: transition angle exists
            {
                bts = Math.Acos(cosbts); // Angle beta_s (radians)
                ds = ss; // Intermediate factor ds
            }
            else // Horizon case: Sun/leaf geometry means transition doesn't occur conventionally
            {
                // R code sets bts=PI regardless of whether cosbts > 1 or < -1
                bts = PI;
                ds = cs; // Use different intermediate factor ds
            }
            // Calculate Chi_s: Average projection G factor for solar direction
            double chi_s = 2.0 / PI * ((bts - PI * 0.5) * cs + Math.Sin(bts) * ss);

            // Determine beta_o and intermediate doo
            double bto, doo;
            if (Math.Abs(cosbto) < 1.0) // Normal case: transition angle exists
            {
                bto = Math.Acos(cosbto); // Angle beta_o (radians)
                doo = so; // Intermediate factor doo
            }
            // R code has specific logic for tto < 90 horizon case
            else if (tto < 90.0) // Observer above horizon
            {
                bto = PI; // R sets bto = PI
                doo = co; // Use different intermediate factor doo
            }
            else // Observer at or below horizon (tto >= 90)
            {
                bto = 0; // R sets bto = 0
                doo = -co; // R sets doo = -co
            }
            // Calculate Chi_o: Average projection G factor for view direction
            double chi_o = 2.0 / PI * ((bto - PI * 0.5) * co + Math.Sin(bto) * so);

            // Ensure non-negative interception factors (can be slightly negative due to precision)
            chi_s = Math.Max(0.0, chi_s);
            chi_o = Math.Max(0.0, chi_o);

            // Calculate auxiliary azimuth angles for bidirectional scattering phase function
            double btran1 = Math.Abs(bts - bto);
            double btran2 = PI - Math.Abs(bts + bto - PI); 

            // Determine integration limits bt1, bt2, bt3 based on relative azimuth psi
            double bt1, bt2, bt3;
            if (psir <= btran1) { bt1 = psir; bt2 = btran1; bt3 = btran2; }
            else { bt1 = btran1; if (psir <= btran2) { bt2 = psir; bt3 = btran2; } else { bt2 = btran2; bt3 = psir; } }

            // Calculate intermediate terms t1 and t2 for phase function
            double t1 = 2.0 * cs * co + ss * so * cospsi; //
            double t2 = 0;
            if (bt2 > 1e-9) // Avoid calculation if bt2 is zero
            {
                // Formula using intermediate factors ds, doo from R code
                t2 = Math.Sin(bt2) * (2.0 * ds * doo + ss * so * Math.Cos(bt1) * Math.Cos(bt3));
            }

            // Calculate final phase function components Frho and Ftau
            double denom = 2.0 * PI * PI; // Denominator
            double frho = ((PI - bt2) * t1 + t2) / denom; // Component related to reflectance
            double ftau = (-bt2 * t1 + t2) / denom; // Component related to transmittance

            // Ensure non-negativity
            frho = Math.Max(0.0, frho);
            ftau = Math.Max(0.0, ftau);

            // Return results in struct
            return new VolscattResult { Chi_s = chi_s, Chi_o = chi_o, Frho = frho, Ftau = ftau };
        }


        /// <summary>
        /// Checks if spectral sampling (wavelengths) is identical between PROSPECT leaf optical constants, SOIL properties, and ATM data.
        /// Throws an ArgumentException if sampling does not match in length or values.
        /// </summary>
        /// <param name="leafConstants">PROSPECT leaf optical constants object (must contain Wavelength vector).</param>
        /// <param name="soilProperties">Soil spectral properties object (must contain Wavelength array).</param>
        /// <param name="specAtm">Atmosphere spectral properties object (must contain Wavelength array).</param>
        public static void CheckSpectralSampling(LeafOpticalConsts leafConstants, SoilOptics soilProperties, AtmosphericSpectralData specAtm)
        {
            // Extract wavelength arrays
            double[] lambdaLeafConstants = leafConstants.Wavelength?.ToArray();
            double[] lambdaSoil = soilProperties.Wavelength.ToArray();
            double[] lambdaAtm = specAtm.Wavelength;

            // Check if any wavelength array is null
            if (lambdaLeafConstants == null || lambdaSoil == null || lambdaAtm == null)
            {
                throw new ArgumentNullException("CheckSpectralSampling: One or more spectral wavelength arrays are null.");
            }

            // Get lengths
            int lenProspect = lambdaLeafConstants.Length;
            int lenSoil = lambdaSoil.Length;
            int lenAtm = lambdaAtm.Length;

            // Define error message
            string errorMessage = "Please ensure matching spectral sampling (wavelengths and number of bands) between PROSPECT constants, Soil properties, and Atmospheric data.";

            // Check if lengths are consistent
            if (lenProspect != lenSoil || lenProspect != lenAtm)
            {
                Console.WriteLine(errorMessage + $" Lengths: Prospect={lenProspect}, Soil={lenSoil}, Atm={lenAtm}");
                throw new ArgumentException(errorMessage);
            }

            if (lambdaLeafConstants == null || lambdaSoil == null || lambdaAtm == null)
            {
                throw new ArgumentNullException("CheckSpectralSampling: One or more spectral wavelength arrays are null.");
            }
                
            if (lambdaLeafConstants.Length != lambdaSoil.Length || lambdaLeafConstants.Length != lambdaAtm.Length)
            {
                throw new ArgumentException("Please ensure matching spectral sampling (lengths) between ProspectConstants, Soil, and Atm data.");
            }

            // Check if wavelength values match exactly using SequenceEqual
            // Assumes wavelengths are in the same order.
            // May need to sort before checking, fix latter
            if (!lambdaLeafConstants.SequenceEqual(lambdaSoil) || !lambdaLeafConstants.SequenceEqual(lambdaAtm)) 
            {
                // Log the first mismatch point for debugging
                for (int i = 0; i < lenProspect; i++)
                {
                    if (Math.Abs(lambdaLeafConstants[i] - lambdaSoil[i]) > 1e-6 || Math.Abs(lambdaLeafConstants[i] - lambdaAtm[i]) > 1e-6)
                    {
                        Console.WriteLine($"Spectral mismatch at index {i}: Prospect={lambdaLeafConstants[i]}, Soil={lambdaSoil[i]}, Atm={lambdaAtm[i]}");
                        break;
                    }
                }
                throw new ArgumentException("Please ensure matching spectral sampling (wavelength values) between ProspectConstants, Soil, and Atm data.");
            }
        }


        /// <summary>
        /// Checks if provided brown leaf optical properties (BrownLOP) are correctly defined and spectrally compatible.
        /// Throws exceptions for critical errors (missing data, spectral mismatch).
        /// Writes console warnings for non-critical issues (multiple PROSPECT inputs provided).
        /// </summary>
        /// <param name="brownLOP">LeafOptics object for brown leaf properties. Can be null.</param>
        /// <param name="referenceLambda">The reference wavelength array (e.g., from ProspectConstants.Wavelength) that BrownLOP's wavelengths should match.</param>
        /// <param name="inputProspectList">A list of ProspectInput parameters (used only to check count for warning message).</param>
        public static void CheckBrownLOP(LeafOptics? brownLOP, double[] referenceLambda, List<ProspectInputs> inputProspectList)
        {
            // Check wavelength immediately if brownLOP has value
            if (brownLOP.HasValue && brownLOP.Value.Wavelength == null)
            {
                throw new ArgumentException("Wavelength cannot be null", nameof(brownLOP));
            }

            // Only perform checks if a BrownLOP object was actually passed in and has value
            if (brownLOP.HasValue && brownLOP.Value.HasValue)
            {
                // Check spectral domain matching against the reference simulation wavelengths
                if (referenceLambda == null)
                {
                    throw new ArgumentNullException(nameof(referenceLambda), "CheckBrownLOP: Reference lambda array cannot be null.");
                }

                // Compare lengths and values of wavelength arrays
                if (brownLOP.Value.Wavelength.Length != referenceLambda.Length || !brownLOP.Value.Wavelength.SequenceEqual(referenceLambda))
                {
                    string msg = "Spectral domain mismatch: BrownLOP wavelengths do not match the reference simulation wavelengths.";
                    Console.WriteLine(msg);
                    throw new ArgumentException(msg);
                }

                // Issue a warning if BrownLOP is provided AND multiple PROSPECT input sets exist
                if (inputProspectList != null && inputProspectList.Count > 1)
                {
                    Console.WriteLine("Warning: External BrownLOP provided along with multiple PROSPECT input parameter sets.");
                    Console.WriteLine("         Only the first PROSPECT input set will be used for green vegetation simulation.");
                }
            }
        }


        /// <summary>
        /// Prepares leaf optical properties (GreenLOP, BrownLOP) for SAIL by running the PROSPECT model.
        /// Handles the logic for 4SAIL (needs GreenLOP) vs 4SAIL2 (needs GreenLOP and BrownLOP)
        /// based on input parameters and optionally provided external BrownLOP data.
        /// </summary>
        /// <param name="sailVersion">String identifying the SAIL model version: "4SAIL" or "4SAIL2".</param>
        /// <param name="leafOpticalConstants">Leaf optical constants constants required by the PROSPECT model.</param>
        /// <param name="inputProspectList">
        /// A list containing PROSPECT input parameters (SailUtilities.ProspectInput).
        /// Expects 1 set for 4SAIL (or 4SAIL2 with BrownLOP/fractionBrown=0).
        /// Expects 2 sets (first for green, second for brown) for 4SAIL2 if BrownLOP is null and fractionBrown > 0.
        /// </param>
        /// <param name="fractionBrown">Fraction of brown vegetation (0-1). Used only by 4SAIL2 logic when BrownLOP is not provided.</param>
        /// <param name="brownLOP">Optional pre-calculated brown leaf optical properties. If provided, simulation for brown leaves is skipped.</param>
        /// <returns>An AdjustedProspectResult struct containing the calculated GreenLOP and potentially BrownLOP.</returns>
        /// <exception cref="ArgumentException">Thrown if inputs are inconsistent (e.g., null lists, bad SAIL version) or Prospect simulation fails.</exception>
        /// <exception cref="InvalidOperationException">Thrown if ProspectCore.Prospect fails internally.</exception>
        public static AdjustedProspectResult AdjustProspectToSail(
            string sailVersion,
            LeafOpticalConsts leafOpticalConstants,   
            List<ProspectInputs> inputProspectList, 
            double fractionBrown,
            LeafOptics? brownLOP = null)
        {
            // Input Validation
            if (inputProspectList == null || inputProspectList.Count == 0)
            {
                throw new ArgumentException("AdjustProspectToSail: inputProspectList cannot be null or empty.");
            }
            
            if (leafOpticalConstants.Wavelength == null || leafOpticalConstants.Wavelength.Count == 0)
            {
                throw new ArgumentException("AdjustProspectToSail: leafOpticalConstants must contain valid Wavelength data.");
            }
            if (fractionBrown < 0 || fractionBrown > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(fractionBrown), "fractionBrown must be between 0 and 1.");
            }

            // Simulate Green Leaf Optical Properties (greenLOP): always needed
            ProspectInputs greenIn = inputProspectList[0]; // Get parameters for green leaf
            LeafOptics greenLOP;

            try
            {
                // Call the Prospect method from ProspectCore
                greenLOP = Prospect(ProspectInputs: greenIn,
                    LeafOpticalConstants: leafOpticalConstants);
            }
            catch (Exception ex) // Catch potential errors during PROSPECT run
            {
                throw new InvalidOperationException($"PROSPECT simulation failed for Green LOP: {ex.Message}", ex);
            }

            // Determine Brown Leaf Optical Properties based on SAIL version and inputs
            LeafOptics? finalBrownLOP = null; // Initialize Brown LOP result

            if (sailVersion == "4SAIL")
            {
                // 4SAIL only uses GreenLOP. BrownLOP is not needed.
                finalBrownLOP = null;
            }
            else if (sailVersion == "4SAIL2")
            {
                // 4SAIL2 requires both Green and Brown LOP.

                // Case 1: External BrownLOP is provided directly
                if (brownLOP.HasValue && brownLOP.Value.HasValue)
                {
                    // Check if the provided BrownLOP is spectrally valid against prospectConstants wavelengths
                    CheckBrownLOP(brownLOP, leafOpticalConstants.Wavelength.ToArray(), inputProspectList);
                    finalBrownLOP = brownLOP; // Use the externally provided one
                }
                // Case 2: External BrownLOP is NOT provided. Need to simulate or use Green LOP.
                else
                {
                    // Subcase 2a: If fractionBrown is effectively zero, brown leaves don't contribute significantly or are same as green.
                    if (Math.Abs(fractionBrown) < 1e-9)
                    {
                        // Assign Green LOP to Brown LOP (optically identical in this scenario)
                        finalBrownLOP = greenLOP;
                    }
                    // Subcase 2b: fractionBrown > 0, and NO external BrownLOP. Need to simulate using second input set.
                    else
                    {
                        // Check if a second input set for brown leaves exists in the list
                        if (inputProspectList.Count < 2)
                        {
                            // R code prints a warning and implicitly runs as 4SAIL. Mimic warning.
                            Console.WriteLine("Warning: 4SAIL2 needs two sets of PROSPECT inputs (or external BrownLOP/zero fractionBrown).");
                            Console.WriteLine("         Only one input set found. Brown LOP cannot be generated via PROSPECT.");
                            Console.WriteLine("         Proceeding as if 4SAIL was selected (BrownLOP will be effectively null for SAIL core).");
                            finalBrownLOP = null; // No distinct brown LOP simulated
                        }
                        else // Second input set is available
                        {
                            // Get parameters for brown leaf from the second element of the list
                            ProspectInputs brownIn = inputProspectList[1];
                            try
                            {
                                // Call ProspectCore.Prospect for the brown leaf parameters
                                finalBrownLOP = Prospect(ProspectInputs: brownIn,
                                     LeafOpticalConstants: leafOpticalConstants // Use same spectral constants
                                );
                            }
                            catch (Exception ex) // Catch potential errors during PROSPECT run
                            {
                                throw new InvalidOperationException($"PROSPECT simulation failed for Brown LOP: {ex.Message}", ex);
                            }
                        }
                    }
                }
            }
            else // Invalid SAIL version string
            {
                throw new ArgumentException($"Unsupported SAIL version specified: '{sailVersion}'. Use '4SAIL' or '4SAIL2'.");
            }

            // Return the struct containing the final GreenLOP and BrownLOP
            return new AdjustedProspectResult {GreenLOP = greenLOP, BrownLOP = finalBrownLOP};
        }

        /// <summary>
        /// Load spectral data of wet and dry soil from a local JSON file
        /// </summary>
        /// <param> WetDrySoilReflectanceDataPath </param>
        /// <param name="WetDrySoilReflectanceDataPath">Path of Json file containting reflectance of wet and dry soil.</param>
        /// <returns> WetDrySoilReflectance object containing wavelength and reflectance data</returns>
        public static WetDrySoilReflectance LoadWetDrySoilReflectanData(string WetDrySoilReflectanceDataPath)
        {
            if (!File.Exists(WetDrySoilReflectanceDataPath))
            {
                throw new FileNotFoundException($"Soil optical data file not found at {WetDrySoilReflectanceDataPath}. Please provide a valid SoilOptics or ensure the file exists.");
            }

            return ParseWetDrySoilReflectanceJson(File.ReadAllText(WetDrySoilReflectanceDataPath), WetDrySoilReflectanceDataPath);
        }

        /// <summary>
        /// Load spectral data of wet and dry soil from an embedded resource.
        /// </summary>
        /// <param name="resourceName">Fully-qualified embedded resource name.</param>
        /// <returns> WetDrySoilReflectance object containing wavelength and reflectance data</returns>
        public static WetDrySoilReflectance LoadWetDrySoilReflectanDataFromResource(string resourceName)
        {
            return ParseWetDrySoilReflectanceJson(EmbeddedResourceLoader.ReadText(resourceName), resourceName);
        }

        private static WetDrySoilReflectance ParseWetDrySoilReflectanceJson(string json, string source)
        {
            try
            {
                var OpticalData = JsonConvert.DeserializeObject<WetDrySoilReflectanceDataJason>(json);

                // Check if deserialization was successful
                if (OpticalData == null)
                {
                    throw new Exception("Deserialization returned null - invalid JSON format or empty file");
                }

                // Check if required arrays exist and have data
                if (OpticalData.Wavelength == null || OpticalData.Wavelength.Length == 0)
                {
                    throw new Exception("Wavelength data is missing or empty");
                }

                if (OpticalData.Wet_Soil == null || OpticalData.Wet_Soil.Length == 0)
                {
                    throw new Exception("Wet_Soil data is missing or empty");
                }

                if (OpticalData.Dry_Soil == null || OpticalData.Dry_Soil.Length == 0)
                {
                    throw new Exception("Dry_Soil data is missing or empty");
                }

                // Check if all arrays have the same length
                if (OpticalData.Wavelength.Length != OpticalData.Wet_Soil.Length ||
                    OpticalData.Wavelength.Length != OpticalData.Dry_Soil.Length)
                {
                    throw new Exception($"Array length mismatch - Wavelength: {OpticalData.Wavelength.Length}, " +
                                      $"Wet_Soil: {OpticalData.Wet_Soil.Length}, Dry_Soil: {OpticalData.Dry_Soil.Length}");
                }

                var wavelengthToIndex = new Dictionary<double, int>();
                for (int i = 0; i < OpticalData.Wavelength.Length; i++)
                {
                    wavelengthToIndex[OpticalData.Wavelength[i]] = i;
                }

                return new WetDrySoilReflectance
                {
                    Wavelength = Vector<double>.Build.DenseOfArray(OpticalData.Wavelength),
                    DrySoilReflectance = Vector<double>.Build.DenseOfArray(OpticalData.Dry_Soil),
                    WetSoilReflectance = Vector<double>.Build.DenseOfArray(OpticalData.Wet_Soil),
                    WavelengthToIndex = wavelengthToIndex
                };
            }
            catch (JsonException jsonEx)
            {
                throw new Exception($"JSON parsing error in {source}: {jsonEx.Message}", jsonEx);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load soil optical data from {source}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Computes soil reflectance based on the wet and dry soil reflectance data.
        /// </summary>
        /// <param name="wetDrySoilReflectance">Object of WetDrySoilReflectance, containing Wavelenght, Wet_Soil and Dry_Soil</param>
        /// <param name="psoil">Dry to Wet soil factor (unitless; 0 for wet, 1 for dry)</param>
        /// <returns>An object of SoilOptics containing Wavelength and Reflectance.</returns>
        public static SoilOptics CalculateSoilReflectanceFromWetDry(WetDrySoilReflectance wetDrySoilReflectance, double psoil = 0.5)
        {
            // Calculate the weighted reflectance vector
            Vector<double> weightedReflectance = psoil * wetDrySoilReflectance.DrySoilReflectance +
                                                 (1 - psoil) * wetDrySoilReflectance.WetSoilReflectance;

            SoilOptics soilOpticalData = new SoilOptics
            {
                Wavelength = wetDrySoilReflectance.Wavelength,
                Reflectance = weightedReflectance,
                WavelengthToIndex = wetDrySoilReflectance.WavelengthToIndex
            };

            return soilOpticalData;
        }

        /// <summary>
        /// Load atmospheric spectral data from a local JSON file
        /// </summary>
        /// <param name="filePath">Path to the JSON file containing atmospheric spectral data</param>
        /// <returns>AtmosphericSpectralData object containing the loaded data</returns>
        /// <exception cref="FileNotFoundException">Thrown when the specified file is not found</exception>
        /// <exception cref="InvalidDataException">Thrown when the data is missing or invalid</exception>
        public static AtmosphericSpectralData LoadAtmosphericSpectralData(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Atmospheric spectral data file not found at {filePath}");
            }

            return ParseAtmosphericSpectralDataJson(File.ReadAllText(filePath), filePath);
        }

        /// <summary>
        /// Load atmospheric spectral data from an embedded resource.
        /// </summary>
        /// <param name="resourceName">Fully-qualified embedded resource name.</param>
        /// <returns>AtmosphericSpectralData object containing the loaded data</returns>
        public static AtmosphericSpectralData LoadAtmosphericSpectralDataFromResource(string resourceName)
        {
            return ParseAtmosphericSpectralDataJson(EmbeddedResourceLoader.ReadText(resourceName), resourceName);
        }

        private static AtmosphericSpectralData ParseAtmosphericSpectralDataJson(string json, string source)
        {
            try
            {
                var atmData = JsonConvert.DeserializeObject<AtmosphericSpectralDataJason>(json);

                if (atmData == null || atmData.Wavelength == null ||
                    atmData.DirectLight == null || atmData.DiffuseLight == null)
                {
                    throw new InvalidDataException($"Invalid or missing data in: {source}");
                }

                // Validate array lengths match
                if (atmData.Wavelength.Length != atmData.DirectLight.Length ||
                    atmData.Wavelength.Length != atmData.DiffuseLight.Length)
                {
                    throw new InvalidDataException("Wavelength, direct light, and diffuse light arrays must have the same length");
                }

                // Create the wavelength-to-index mapping for optimized filtering
                var wavelengthToIndex = new Dictionary<double, int>();
                for (int i = 0; i < atmData.Wavelength.Length; i++)
                {
                    wavelengthToIndex[atmData.Wavelength[i]] = i;
                }

                return new AtmosphericSpectralData
                {
                    Wavelength = atmData.Wavelength,
                    DirectLight = atmData.DirectLight,
                    DiffuseLight = atmData.DiffuseLight,
                    WavelengthToIndex = wavelengthToIndex
                };
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Failed to parse JSON from {source}: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load atmospheric spectral data from {source}: {ex.Message}", ex);
            }
        }


        /// <summary>
        /// Result of spectral resampling operation containing resampled reflectance data and metadata
        /// </summary>
        public class SpectralResamplingResult
        {
            /// <summary>Wavelength corresponding to the input reflectance </summary>
            public double[] Wavelength { get; set; }

            /// <summary> 
            /// Resampled reflectance data: List where each element is a double[] representing one sensor band.
            /// Each double[] contains reflectance values for all samples in that band.
            /// Structure: ResampledReflectance[bandIndex][sampleIndex] = reflectance value
            /// </summary>
            public List<double[]> Reflectance { get; set; }

            /// <summary> 
            /// Names/identifiers for each sensor band (rows in output).
            /// Length equals number of sensor bands. Derived from SpectralBands or auto-generated.
            /// </summary>
            public string[] BandNames { get; set; }
        }

        /// <summary>
        /// Resamples single reflectance spectrum to sensor spectral bands using spectral response functions
        /// </summary>
        /// <param name="wavelength">Input wavelengths in nanometers</param>
        /// <param name="reflectance">Input reflectance spectrum</param>
        /// <param name="srf">Spectral response function data</param>
        /// <returns>Resampled reflectance data with metadata</returns>
        public static SpectralResamplingResult ResampleReflectanceToSensor(
            double[] wavelength,
            double[] reflectance,
            SpectralResponseFunction srf)
        {
            if (wavelength == null || reflectance == null || srf?.SpectralResponse == null)
                throw new ArgumentException("Input parameters cannot be null");

            if (wavelength.Length != reflectance.Length)
                throw new ArgumentException("Wavelength and reflectance arrays must have the same length");

            // Fast path: use precomputed indices and weights (populated by Preprocess at startup)
            if (srf.PrecomputedInputIndices != null)
            {
                int nbBands = srf.PrecomputedInputIndices.Length;
                var resampledReflectance = new List<double[]>(nbBands);
                for (int i = 0; i < nbBands; i++)
                {
                    int[]    indices = srf.PrecomputedInputIndices[i];
                    double[] weights = srf.PrecomputedWeights[i];
                    double   total   = srf.PrecomputedTotalWeights[i];
                    if (indices.Length > 0 && total > 0)
                    {
                        double weightedSum = 0;
                        for (int j = 0; j < indices.Length; j++)
                            weightedSum += weights[j] * reflectance[indices[j]];
                        resampledReflectance.Add(new double[] { weightedSum / total });
                    }
                    else
                    {
                        resampledReflectance.Add(new double[] { 0.0 });
                    }
                }
                string[] bandNames = new string[nbBands];
                for (int i = 0; i < nbBands; i++)
                    bandNames[i] = srf.SpectralBandName?[i]?.ToString() ?? $"Band_{i + 1}";
                return new SpectralResamplingResult
                {
                    Wavelength = srf.CentralWavelength,
                    Reflectance = resampledReflectance,
                    BandNames = bandNames
                };
            }

            // Fallback path: build lookup on-the-fly (when Preprocess has not been called)
            int nbBandsOrigin = wavelength.Length;
            int nbBandsSensor = srf.SpectralResponse.Count;

            // Handle transposition if needed
            var spectralResponse = srf.SpectralResponse;
            if (spectralResponse.Count == nbBandsOrigin && spectralResponse[0].Length == nbBandsSensor)
            {
                spectralResponse = Enumerable.Range(0, nbBandsSensor)
                    .Select(i => Enumerable.Range(0, nbBandsOrigin)
                        .Select(j => srf.SpectralResponse[j][i]).ToArray())
                    .ToList();
                nbBandsSensor = spectralResponse.Count;
            }

            var wavelengthLookup = new Dictionary<double, int>(wavelength.Length);
            for (int i = 0; i < wavelength.Length; i++)
                wavelengthLookup[wavelength[i]] = i;

            var resampledFallback = new List<double[]>(nbBandsSensor);
            for (int i = 0; i < nbBandsSensor; i++)
            {
                var bandName = srf.SpectralBandName?[i]?.ToString() ?? $"Band_{i + 1}";
                double[] bandSRF = spectralResponse[i];
                int maxPairs = Math.Min(bandSRF.Length, srf.OriginalBandWavelength.Length);
                double totalWeight = 0, weightedSum = 0;
                int matched = 0;
                for (int k = 0; k < maxPairs; k++)
                {
                    if (bandSRF[k] > 0 && wavelengthLookup.TryGetValue(srf.OriginalBandWavelength[k], out int inputIdx))
                    {
                        totalWeight += bandSRF[k];
                        weightedSum += bandSRF[k] * reflectance[inputIdx];
                        matched++;
                    }
                }
                if (matched > 0 && totalWeight > 0)
                    resampledFallback.Add(new double[] { weightedSum / totalWeight });
                else
                {
                    Console.WriteLine($"Warning: No wavelength matches for {bandName} - values set to 0");
                    resampledFallback.Add(new double[] { 0.0 });
                }
            }

            string[] fallbackBandNames = new string[nbBandsSensor];
            for (int i = 0; i < nbBandsSensor; i++)
                fallbackBandNames[i] = srf.SpectralBandName?[i]?.ToString() ?? $"Band_{i + 1}";
            return new SpectralResamplingResult
            {
                Wavelength = srf.CentralWavelength,
                Reflectance = resampledFallback,
                BandNames = fallbackBandNames
            };
        }

        /// <summary>
        /// Represents the sensor spectral response function (SRF) data structure.
        /// </summary>
        public class SpectralResponseFunction
        {
            /// <summary> SRF matrix: [nBands, nWvl] (rows: sensor bands, columns: original bands) </summary>
            public List<double[]> SpectralResponse { get; set; }

            /// <summary> Central wavelength (nm) of each sensor band (length = nBands) </summary>
            public double[] CentralWavelength { get; set; }

            /// <summary> Sensor band names (length = nBands) </summary>
            public object[] SpectralBandName { get; set; }

            /// <summary> Original bands (wavelengths, nm) for which SRF is defined (length = nWvl) </summary>
            public double[] OriginalBandWavelength { get; set; }

            // --- Precomputed data (populated by Preprocess) ---

            /// <summary> For each sensor band: indices into the input reflectance array. </summary>
            public int[][] PrecomputedInputIndices { get; private set; }

            /// <summary> For each sensor band: SRF weights at the valid indices. </summary>
            public double[][] PrecomputedWeights { get; private set; }

            /// <summary> For each sensor band: sum of valid SRF weights. </summary>
            public double[] PrecomputedTotalWeights { get; private set; }

            /// <summary>
            /// Pre-processes this SRF against a given input wavelength grid so that
            /// <see cref="ResampleReflectanceToSensor"/> can avoid rebuilding lookups every call.
            /// Must be called once after loading, with the same wavelength array used for simulation.
            /// </summary>
            public void Preprocess(double[] wavelength)
            {
                if (wavelength == null || SpectralResponse == null)
                    return;

                int nbBandsOrigin = wavelength.Length;

                // Handle transposition: if matrix is [nWvl × nBands] instead of [nBands × nWvl]
                var spectralResponse = SpectralResponse;
                int nbBandsSensor = spectralResponse.Count;
                if (nbBandsSensor == nbBandsOrigin && spectralResponse[0].Length != nbBandsOrigin)
                {
                    int nBands = spectralResponse[0].Length;
                    var transposed = new List<double[]>(nBands);
                    for (int i = 0; i < nBands; i++)
                    {
                        double[] row = new double[nbBandsOrigin];
                        for (int j = 0; j < nbBandsOrigin; j++)
                            row[j] = spectralResponse[j][i];
                        transposed.Add(row);
                    }
                    spectralResponse = transposed;
                    nbBandsSensor = nBands;
                }

                // Build wavelength → index lookup
                var wavelengthLookup = new Dictionary<double, int>(wavelength.Length);
                for (int i = 0; i < wavelength.Length; i++)
                    wavelengthLookup[wavelength[i]] = i;

                PrecomputedInputIndices  = new int[nbBandsSensor][];
                PrecomputedWeights       = new double[nbBandsSensor][];
                PrecomputedTotalWeights  = new double[nbBandsSensor];

                for (int b = 0; b < nbBandsSensor; b++)
                {
                    double[] bandSRF = spectralResponse[b];
                    int maxPairs = Math.Min(bandSRF.Length, OriginalBandWavelength.Length);

                    // First pass: count valid entries
                    int count = 0;
                    for (int k = 0; k < maxPairs; k++)
                        if (bandSRF[k] > 0 && wavelengthLookup.ContainsKey(OriginalBandWavelength[k]))
                            count++;

                    int[]    indices = new int[count];
                    double[] weights = new double[count];
                    double   total   = 0;
                    int      pos     = 0;

                    for (int k = 0; k < maxPairs; k++)
                    {
                        if (bandSRF[k] > 0 && wavelengthLookup.TryGetValue(OriginalBandWavelength[k], out int inputIdx))
                        {
                            indices[pos] = inputIdx;
                            weights[pos] = bandSRF[k];
                            total += bandSRF[k];
                            pos++;
                        }
                    }

                    PrecomputedInputIndices[b]  = indices;
                    PrecomputedWeights[b]       = weights;
                    PrecomputedTotalWeights[b]  = total;
                }
            }
        }

        /// <summary>
        /// Loads a sensor response response function (SRF) from an embedded resource.
        /// </summary>
        /// <param name="resourceName">Fully-qualified embedded resource name</param>
        /// <returns>SpectralResponseFunction object</returns>
        public static SpectralResponseFunction LoadSpectralResponseFunction(string resourceName)
        {
            string json = EmbeddedResourceLoader.ReadText(resourceName);

            // Use an intermediate class for deserialization to handle jagged arrays
            var srfRaw = JsonConvert.DeserializeObject<SpectralResponseFunctionJsonData>(json);

            if (srfRaw == null)
                throw new InvalidDataException("SRF JSON could not be deserialized.");

            if (srfRaw.Spectral_Response == null || srfRaw.Original_Bands == null || srfRaw.Central_WL == null)
                throw new InvalidDataException("SRF JSON missing required fields (Spectral_Response, Original_Bands, Central_WL).");

            int nBands = srfRaw.Spectral_Response.Count;
            if (nBands == 0)
                throw new InvalidDataException("SRF JSON: Spectral_Response array is empty.");

            int nWvl = srfRaw.Spectral_Response[0].Length;
            if (nWvl == 0)
                throw new InvalidDataException("SRF JSON: Spectral_Response inner arrays are empty.");

            // Validate all rows have the same length
            for (int i = 0; i < nBands; i++)
                if (srfRaw.Spectral_Response[i] == null || srfRaw.Spectral_Response[i].Length != nWvl)
                    throw new InvalidDataException($"SRF JSON: Spectral_Response row {i} has inconsistent length.");

            if (srfRaw.Original_Bands.Length != nWvl)
                throw new InvalidDataException("SRF JSON: Original_Bands length does not match Spectral_Response column count.");

            if (srfRaw.Central_WL.Length != nBands)
                throw new InvalidDataException("SRF JSON: Central_WL length does not match number of bands.");

            if (srfRaw.Spectral_Bands != null && srfRaw.Spectral_Bands.Length != nBands)
                throw new InvalidDataException("SRF JSON: Spectral_Bands length does not match number of bands.");

            // Convert jagged array to 2D array
            var srfList = new List<double[]>(nBands);
            for (int i = 0; i < nBands; i++)
                srfList.Add((double[])srfRaw.Spectral_Response[i].Clone());

            return new SpectralResponseFunction
            {
                SpectralResponse = srfList,
                CentralWavelength = srfRaw.Central_WL,
                SpectralBandName = srfRaw.Spectral_Bands,
                OriginalBandWavelength = srfRaw.Original_Bands
            };
        }

        // Helper class for JSON deserialization (handles jagged arrays)
        private class SpectralResponseFunctionJsonData
        {
            public List<double[]> Spectral_Response { get; set; }
            public double[] Central_WL { get; set; } // Central wavelengths of sensor bands
            public object[] Spectral_Bands { get; set; } // Names or identifiers for sensor bands
            public double[] Original_Bands { get; set; } // Original wavelengths for SRF
        }

        /// <summary>Hold the canopy state variables</summary>
        public struct CanopyStateVariables
        {
            /// <summary>Fraction of absorbed photosyntehtically active radiation (fAPAR)</summary>
            public double fAPAR;
            /// <summary>Fraction of Vegetation Cover (fCover = 1 - gap fraction in view direction).</summary>
            public double fcover;
            /// <summary>Bbroadband albedo</summary>
            public double albedo;
        }

        #endregion
    }
}
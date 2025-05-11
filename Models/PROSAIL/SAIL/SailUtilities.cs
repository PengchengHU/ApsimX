using System;
using System.Collections.Generic;
using System.Linq;
//using System.Numerics; // If needed later
using MathNet.Numerics; // Required for SpecialFunctions (ExponentialIntegral)
using MathNet.Numerics.LinearAlgebra; // Required for Vector<double>
using Models.Prospect; // Use namespace from ProspectCore.cs

// Define the namespace for SAIL utilities
namespace Models.Sail
{
    /// <summary>
    /// Provides static utility functions for SAIL model calculations,
    /// translated from the R script Lib_PROSAIL.R.
    /// Includes helpers for LIDF, scattering calculations, spectral checks,
    /// fAPAR/Albedo calculations, and PROSPECT integration.
    /// </summary>
    public static class SailUtilities
    {
        // --- Supporting Data Structures ---
        // These classes/structs define the inputs and outputs for the utility methods.

        /// <summary>
        /// Holds atmospheric sensor spectral data (wavelengths, direct/diffuse irradiance).
        /// Corresponds to the 'SpecATM_Sensor' list in R.
        /// </summary>
        public class SpecAtmSensor
        {
            /// <summary>
            /// Wavelengths (nm).
            /// </summary>
            public double[] Wavelength { get; set; }

            /// <summary>
            /// Direct solar radiation spectrum (e.g., W/m²/nm).
            /// </summary>
            public double[] DirectLight { get; set; }

            /// <summary>
            /// Diffuse sky radiation spectrum (e.g., W/m²/nm).
            /// </summary>
            public double[] DiffuseLight { get; set; }
        }

        /// <summary>
        /// Holds leaf optical properties (reflectance and transmittance spectra).
        /// Corresponds to the 'LeafOptics', 'GreenLOP', 'BrownLOP' lists/dataframes in R.
        /// Typically output from the PROSPECT model.
        /// </summary>
        public class LeafOptics 
        {
            /// <summary>
            /// Wavelengths (nm). Should match the simulation wavelengths.
            /// </summary>
            public double[] Wavelength { get; set; } 

            /// <summary>
            /// Leaf reflectance spectrum (unitless fraction).
            /// </summary>
            public double[] Reflectance { get; set; }

            /// <summary>
            /// Leaf transmittance spectrum (unitless fraction).
            /// </summary>
            public double[] Transmittance { get; set; }
        }

        /// <summary>
        /// Holds PROSPECT model input parameters.
        /// Corresponds to the 'Input_PROSPECT' list/dataframe in R.
        /// </summary>
        public struct ProspectInput 
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

            /// <summary>
            /// Constructor to initialize PROSPECT input parameters with default values.
            /// Defaults match common PROSPECT usage.
            /// </summary>
            public ProspectInput(
                double n = 1.5, double cab = 40.0, double car = 8.0, double ant = 0.0,
                double brown = 0.0, double ewt = 0.01, double lma = 0.008, // Using ProspectCore default for LMA
                double prot = 0.0, double cbc = 0.0, double alpha = 40.0)
            {
                N = n;
                CAB = cab; // Use CAB
                CAR = car;
                ANT = ant;
                BROWN = brown;
                EWT = ewt;
                LMA = lma;
                PROT = prot;
                CBC = cbc;
                Alpha = alpha;
            }
        }

        /// <summary>
        /// Holds soil spectral data (wavelengths and reflectance).
        /// Corresponds to 'SpecSOIL' in R.
        /// </summary>
        public class SoilProperties 
        {
            /// <summary>
            /// Wavelengths (nm). Should match simulation wavelengths.
            /// </summary>
            public double[] Wavelength { get; set; } 

            /// <summary>
            /// Soil reflectance spectrum (unitless fraction).
            /// </summary>
            public double[] Reflectance { get; set; }
        }

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
            /// <summary>Green leaf optical properties.</summary>
            public LeafOptics GreenLOP { get; set; }
            /// <summary>Brown leaf optical properties (may be null or same as GreenLOP).</summary>
            public LeafOptics BrownLOP { get; set; }
        }

        /// <summary>
        /// Represents the output of the SAIL models (FourSAIL, FourSAIL2).
        /// Contains various reflectance factors and derived quantities like fCover and absorptance.
        /// </summary>
        public class SailResult 
        {
            /// <summary>Hemispherical-directional reflectance factor in viewing direction (R_o).</summary>
            public double[] Rdot { get; set; }
            /// <summary>Bi-directional reflectance factor (R_so).</summary>
            public double[] Rsot { get; set; }
            /// <summary>Bi-hemispherical reflectance factor (R_dd).</summary>
            public double[] Rddt { get; set; }
            /// <summary>Directional-hemispherical reflectance factor for solar incident flux (R_sd).</summary>
            public double[] Rsdt { get; set; }
            /// <summary>Fraction of Vegetation Cover (fCover = 1 - gap fraction in view direction).</summary>
            public double[] FCover { get; set; }
            /// <summary>Canopy absorptance for direct solar incident flux (fraction absorbed by canopy+soil system).</summary>
            public double[] Abs_dir { get; set; }
            /// <summary>Canopy absorptance for hemispherical diffuse incident flux (fraction absorbed by canopy+soil system).</summary>
            public double[] Abs_hem { get; set; }
            /// <summary>Contribution of direct solar incident flux to albedo (Hemispherical reflectance for direct incidence, Rsd*).</summary>
            public double[] Rsdstar { get; set; }
            /// <summary>Contribution of hemispherical diffuse incident flux to albedo (Hemispherical reflectance for diffuse incidence, Rdd*).</summary>
            public double[] Rddstar { get; set; }
        }

        // --- End of Supporting Data Structures ---

        // Constants used within the class
        private const double PI = Math.PI;
        private const double DEGREES_TO_RADIANS = PI / 180.0;

        // --- Utility Methods ---

        /// <summary>
        /// Computes bidirectional reflectance factor (BRF) based on SAIL outputs and solar/diffuse light fractions.
        /// </summary>
        /// <remarks>
        /// The direct and diffuse light components are combined using the approach from:
        /// Francois et al. (2002) Conversion of 400-1100 nm vegetation albedo measurements into total shortwave broadband albedo using a canopy radiative transfer model, Agronomie.
        /// Es = direct irradiance, Ed = diffuse irradiance.
        /// </remarks>
        /// <param name="rdot">Hemispherical-directional reflectance factor (R_o) spectrum from SAIL.</param>
        /// <param name="rsot">Bi-directional reflectance factor (R_so) spectrum from SAIL.</param>
        /// <param name="tts">Solar zenith angle (degrees).</param>
        /// <param name="specAtmSensor">Atmospheric data containing DirectLight (Es) and DiffuseLight (Ed) spectra.</param>
        /// <returns>Bidirectional reflectance factor (BRF) spectrum.</returns>
        public static double[] Compute_BRF(double[] rdot, double[] rsot, double tts, SpecAtmSensor specAtmSensor) // Method made static
        {
            // Section: Direct / Diffuse Light Calculation
            double[] Es = specAtmSensor.DirectLight;    // Direct irradiance component
            double[] Ed = specAtmSensor.DiffuseLight;  // Diffuse irradiance component

            // Input validation: Ensure all spectral arrays have the same length
            if (rdot.Length != rsot.Length || rdot.Length != Es.Length || rdot.Length != Ed.Length)
            {
                throw new ArgumentException("Compute_BRF: Input arrays (rdot, rsot, Es, Ed) must have the same length.");
            }

            // Convert angles to radians
            double rd = DEGREES_TO_RADIANS;
            double solarElevationRad = (90.0 - tts) * rd; // Solar elevation angle in radians
            double sinSolarElevation = Math.Sin(solarElevationRad);

            // Calculate the skyl factor (fraction of diffuse light) based on solar elevation
            // Formula from Francois et al. (2002)
            double skyl = 0.847 - 1.61 * sinSolarElevation + 1.04 * sinSolarElevation * sinSolarElevation;
            // Ensure skyl is within physical bounds [0, 1]
            skyl = Math.Max(0.0, Math.Min(1.0, skyl)); // Clamping added for robustness

            int nLambda = rdot.Length; // Number of spectral points
            double[] BRF = new double[nLambda]; // Initialize the result array

            // Calculate BRF per wavelength
            for (int i = 0; i < nLambda; i++)
            {
                // Calculate effective direct and diffuse irradiance components reaching the canopy
                double effectiveDirectIrradiance = (1.0 - skyl) * Es[i];
                // Note: R code uses Ed here. Francois paper might imply skyl relates Es and Ed? Check original paper if critical.
                double effectiveDiffuseIrradiance = skyl * Ed[i];
                // Total irradiance for weighting
                double totalIrradiance = effectiveDirectIrradiance + effectiveDiffuseIrradiance;

                // Avoid division by zero if total irradiance is negligible
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
            return BRF; // Return the calculated BRF spectrum
        }

        /// <summary>
        /// Computes the fraction of absorbed photosynthetically active radiation (fAPAR).
        /// </summary>
        /// <remarks>
        /// Uses the direct/diffuse approach from Francois et al. (2002).
        /// Integrates absorbed radiation over the PAR range (typically 400-700 nm).
        /// Requires canopy absorptance values (Abs_dir, Abs_hem) from SAIL output.
        /// </remarks>
        /// <param name="abs_dir">Canopy absorptance spectrum for direct solar flux (from SailResult).</param>
        /// <param name="abs_hem">Canopy absorptance spectrum for hemispherical diffuse flux (from SailResult).</param>
        /// <param name="tts">Solar zenith angle (degrees).</param>
        /// <param name="specAtmSensor">Atmospheric data (DirectLight, DiffuseLight, Wavelength).</param>
        /// <param name="parRangeMin">Minimum wavelength (nm) for PAR integration (default 400).</param>
        /// <param name="parRangeMax">Maximum wavelength (nm) for PAR integration (default 700).</param>
        /// <returns>Fraction of Absorbed Photosynthetically Active Radiation (fAPAR, unitless).</returns>
        public static double Compute_fAPAR(double[] abs_dir, double[] abs_hem, double tts, SpecAtmSensor specAtmSensor, double parRangeMin = 400, double parRangeMax = 700) // Method made static
        {
            // Section: Direct / Diffuse Light Calculation
            double[] Es = specAtmSensor.DirectLight;
            double[] Ed = specAtmSensor.DiffuseLight;
            double[] lambda = specAtmSensor.Wavelength; // Use Wavelength field

            // Input validation
            if (abs_dir.Length != abs_hem.Length || abs_dir.Length != Es.Length || abs_dir.Length != Ed.Length || abs_dir.Length != lambda.Length)
            {
                throw new ArgumentException("Compute_fAPAR: Input arrays must have the same length.");
            }

            // Calculate skyl factor as in Compute_BRF
            double rd = DEGREES_TO_RADIANS;
            double solarElevationRad = (90.0 - tts) * rd;
            double sinSolarElevation = Math.Sin(solarElevationRad);
            double skyl = 0.847 - 1.61 * sinSolarElevation + 1.04 * sinSolarElevation * sinSolarElevation;
            skyl = Math.Max(0.0, Math.Min(1.0, skyl)); // Clamp

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
                    double diffuseIrradiance = skyl * Ed[i]; // See note in Compute_BRF
                    double incident = directIrradiance + diffuseIrradiance;

                    // Calculate absorbed energy: AbsDirect * DirectIrrad + AbsHemispheric * DiffuseIrrad
                    double absorbed = (abs_dir[i] * directIrradiance + abs_hem[i] * diffuseIrradiance);

                    // Accumulate total incident and absorbed PAR energy
                    // NOTE: This performs simple summation, assuming equal spectral bandwidths.
                    // For higher accuracy with non-uniform sampling, use numerical integration (e.g., trapezoidal rule).
                    totalAbsorbedPAR += absorbed;
                    totalIncidentPAR += incident;
                }
            }

            // Calculate fAPAR ratio
            if (Math.Abs(totalIncidentPAR) < 1e-9)
            {
                return 0; // Avoid division by zero if no incident PAR
            }

            double fAPAR = totalAbsorbedPAR / totalIncidentPAR; //

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
        /// Based on J. Gomez-Dans python implementation approach.
        /// </remarks>
        /// <param name="rsdstar">Contribution of direct solar flux to albedo (from SailResult).</param>
        /// <param name="rddstar">Contribution of hemispherical diffuse flux to albedo (from SailResult).</param>
        /// <param name="tts">Solar zenith angle (degrees).</param>
        /// <param name="specAtmSensor">Atmospheric data (DirectLight, DiffuseLight, Wavelength).</param>
        /// <param name="albedoRangeMin">Minimum wavelength (nm) for albedo integration (default 400).</param>
        /// <param name="albedoRangeMax">Maximum wavelength (nm) for albedo integration (default 2400).</param>
        /// <returns>Broadband albedo value (unitless fraction) over the specified range.</returns>
        public static double Compute_albedo(double[] rsdstar, double[] rddstar, double tts, SpecAtmSensor specAtmSensor, double albedoRangeMin = 400, double albedoRangeMax = 2400) // Method made static
        {
            // Section: Direct / Diffuse Light Calculation
            double[] Es = specAtmSensor.DirectLight;
            double[] Ed = specAtmSensor.DiffuseLight;
            double[] lambda = specAtmSensor.Wavelength; // Use Wavelength field

            // Input validation
            if (rsdstar.Length != rddstar.Length || rsdstar.Length != Es.Length || rsdstar.Length != Ed.Length || rsdstar.Length != lambda.Length)
            {
                throw new ArgumentException("Compute_albedo: Input arrays must have the same length.");
            }

            // Calculate skyl factor
            double rd = DEGREES_TO_RADIANS;
            double solarElevationRad = (90.0 - tts) * rd;
            double sinSolarElevation = Math.Sin(solarElevationRad);
            double skyl = 0.847 - 1.61 * sinSolarElevation + 1.04 * sinSolarElevation * sinSolarElevation;
            skyl = Math.Max(0.0, Math.Min(1.0, skyl)); // Clamp

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
                    double diffuseIrradiance = skyl * Ed[i]; // See note in Compute_BRF
                    double incident = directIrradiance + diffuseIrradiance;

                    // Calculate reflected energy: Rsd* * DirectIrrad + Rdd* * DiffuseIrrad
                    double reflected = (rsdstar[i] * directIrradiance + rddstar[i] * diffuseIrradiance);

                    // Accumulate totals
                    // NOTE: Simple summation assumes equal spectral bandwidths. Integration needed for non-uniform sampling.
                    totalReflectedEnergy += reflected;
                    totalIncidentEnergy += incident;
                }
            }

            // Calculate albedo ratio
            if (Math.Abs(totalIncidentEnergy) < 1e-9)
            {
                return 0; // Avoid division by zero if no incident energy
            }

            double albedo = totalReflectedEnergy / totalIncidentEnergy; //

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
        public static ScatteringResult NonConservativeScattering( // Method made static
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
                double rinf;
                // Handle potential division by zero or near-zero if sigbi is small
                if (Math.Abs(sigbi) < 1e-12)
                {
                    // If sigb is zero, implies conservative scattering (att=m or att=-m).
                    // rinf should approach 1 if att=m, or -1 if att=-m?
                    // SAIL theory suggests rinf -> 1 for conservative case.
                    rinf = 1.0;
                }
                else
                {
                    rinf = (atti - mi) / sigbi;
                }

                // Intermediate exponential terms related to LAI
                double e1 = Math.Exp(-mi * lai); // exp(-m*LAI)
                double e2 = e1 * e1;           // exp(-2*m*LAI)
                double rinf2 = rinf * rinf;    // rinf^2
                double re = rinf * e1;         // rinf * exp(-m*LAI)

                // Denominator term used in several calculations
                double denom = 1.0 - rinf2 * e2; // 1 - rinf^2 * exp(-2*m*LAI)
                                                 // Avoid division by zero, maintain sign
                if (Math.Abs(denom) < 1e-12) denom = (denom >= 0 ? 1e-12 : -1e-12);

                // Calculate J functions using helper methods
                double J1ks_val = Jfunc1(ks, mi, lai); // J1(ks, m, lai)
                double J2ks_val = Jfunc2(ks, mi, lai); // J2(ks, m, lai)
                double J1ko_val = Jfunc1(ko, mi, lai); // J1(ko, m, lai)
                double J2ko_val = Jfunc2(ko, mi, lai); // J2(ko, m, lai)

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
                double z = Jfunc2(ks, ko, lai); // R uses Jfunc2, equivalent to Jfunc3
                double g1_denom = ko + mi;
                double g2_denom = ks + mi;
                // Avoid division by zero
                if (Math.Abs(g1_denom) < 1e-12) g1_denom = (g1_denom >= 0 ? 1e-12 : -1e-12);
                if (Math.Abs(g2_denom) < 1e-12) g2_denom = (g2_denom >= 0 ? 1e-12 : -1e-12);

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
                double rsod_denom = (1.0 - rinf2);
                // Avoid division by zero
                if (Math.Abs(rsod_denom) < 1e-12) rsod_denom = (rsod_denom >= 0 ? 1e-12 : -1e-12);

                // Multiple scattering contribution to bidirectional canopy reflectance
                rsod[i] = (T1 + T2 - T3) / rsod_denom;

                // Optional: Clamp intermediate results to physical bounds [0, 1] if needed, though SAIL theory allows intermediate values outside this.
                // rdd[i] = Math.Max(0.0, Math.Min(1.0, rdd[i]));
                // tdd[i] = Math.Max(0.0, Math.Min(1.0, tdd[i])); // etc. for rsd, tsd, rdo, tdo, rsod
            }

            // Return the struct containing all calculated arrays
            return new ScatteringResult { Tdd = tdd, Rdd = rdd, Tsd = tsd, Rsd = rsd, Tdo = tdo, Rdo = rdo, Rsod = rsod };
        }


        /// <summary>
        /// Computes scattering components for conservative or near-conservative scattering conditions (m no larger than 0.01).
        /// Internal helper function for SAIL models (specifically 4SAIL2). Uses different formulae than NonConservativeScattering.
        /// </summary>
        // (Parameters and return description identical to NonConservativeScattering)
        public static ScatteringResult ConservativeScattering( // Method made static
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
                if (Math.Abs(denom_rtp) < 1e-12) denom_rtp = (denom_rtp >= 0 ? 1e-12 : -1e-12);
                if (Math.Abs(denom_rtm) < 1e-12) denom_rtm = (denom_rtm >= 0 ? 1e-12 : -1e-12);

                // Intermediate rtp, rtm terms
                double rtp = (1.0 - amsig * J4_val) / denom_rtp;
                double rtm = (-1.0 + apsig * J4_val) / denom_rtm; // R uses (-1 + ...)

                // Calculate Rdd and Tdd for conservative case
                // Note: For perfect conservative scattering (m=0, amsig=0), rtp=1. Rdd+Tdd should equal 1.
                rdd[i] = 0.5 * (rtp + rtm);
                tdd[i] = 0.5 * (rtp - rtm);

                // Denominators involving extinction coefficients and m
                double dns = ks * ks - mi * mi; // k_sun^2 - m^2
                double dno = ko * ko - mi * mi; // k_obs^2 - m^2
                                                // Avoid division by zero
                if (Math.Abs(dns) < 1e-12) dns = (dns >= 0 ? 1e-12 : -1e-12);
                if (Math.Abs(dno) < 1e-12) dno = (dno >= 0 ? 1e-12 : -1e-12);

                // Intermediate coefficients cks, cko, dks, dko (Verhoef notation?)
                double cks = (sbi * (ks - atti) - sfi * sigbi) / dns;
                double cko = (vbi * (ko - atti) - vfi * sigbi) / dno;
                double dks = (-sfi * (ks + atti) - sbi * sigbi) / dns;
                double dko = (-vfi * (ko + atti) - vbi * sigbi) / dno;

                // Intermediate bidirectional coefficient ho
                double ko_plus_ks = ko + ks;
                // Avoid division by zero
                if (Math.Abs(ko_plus_ks) < 1e-12) ko_plus_ks = (ko_plus_ks >= 0 ? 1e-12 : -1e-12);
                double ho = (sfi * cko + sbi * dko) / ko_plus_ks;

                // Calculate reflectance and transmittance terms using conservative formulae
                rsd[i] = cks * (1.0 - tss * tdd[i]) - dks * rdd[i];
                rdo[i] = cko * (1.0 - too * tdd[i]) - dko * rdd[i];
                tsd[i] = dks * (tss - tdd[i]) - cks * tss * rdd[i];
                tdo[i] = dko * (too - tdd[i]) - cko * too * rdd[i];
                // Multiple scattering contribution to bidirectional reflectance
                rsod[i] = ho * (1.0 - tss * too) - cko * tsd[i] * too - dko * rsd[i]; // R uses rsd[i] here
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
        public static FoliarDistributionResult Campbell(double ala) // Method made static
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
            // Formula from R code, likely derived from Campbell's work.
            double excent = Math.Exp(-1.6184e-5 * Math.Pow(ala, 3) + 2.1145e-3 * Math.Pow(ala, 2) - 1.2390e-1 * ala + 3.2491);
            double totalFreqSum = 0; // Accumulator for normalization

            // Calculate frequency for each angle bin
            for (int i = 0; i < nBins; i++)
            {
                // Handle potential tan(90) issues
                double cos_tl1 = Math.Cos(tl1[i]);
                double cos_tl2 = Math.Cos(tl2[i]);
                double tan_tl1 = (Math.Abs(cos_tl1) < 1e-9) ? double.PositiveInfinity : Math.Tan(tl1[i]);
                double tan_tl2 = (Math.Abs(cos_tl2) < 1e-9) ? double.PositiveInfinity : Math.Tan(tl2[i]);
                double tan_tl1_sq = tan_tl1 * tan_tl1;
                double tan_tl2_sq = tan_tl2 * tan_tl2;

                // Calculate intermediate x1, x2 based on eccentricity and angles
                double x1, x2;
                // Avoid division by zero or issues with infinite tan
                if (double.IsInfinity(tan_tl1_sq)) x1 = 0;
                else x1 = excent / (Math.Sqrt(1.0 + excent * excent * tan_tl1_sq));

                if (double.IsInfinity(tan_tl2_sq)) x2 = 0;
                else x2 = excent / (Math.Sqrt(1.0 + excent * excent * tan_tl2_sq));

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
                            double log_term1 = (log_arg1 > 1e-12) ? Math.Log(log_arg1) : Math.Log(1e-12); // Use small positive floor
                            double log_term2 = (log_arg2 > 1e-12) ? Math.Log(log_arg2) : Math.Log(1e-12);

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
                            double asin_arg1 = (Math.Abs(alpha) < 1e-9) ? 0 : x1 / alpha;
                            double asin_arg2 = (Math.Abs(alpha) < 1e-9) ? 0 : x2 / alpha;
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
                    normalizedFreq[i] = freq[i] / totalFreqSum; //
                }
            }
            // If sum is zero, result remains array of zeros.

            // Return the LIDF values and corresponding angles
            return new FoliarDistributionResult { Lidf = normalizedFreq, Litab = litab }; //
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
        public static FoliarDistributionResult Dladgen(double a, double b) // Method made static
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

            // Optional: Normalize frequencies if they don't sum exactly to 1 due to precision.
            double sumFreq = freq.Sum();
            if (Math.Abs(sumFreq) > 1e-6 && Math.Abs(sumFreq - 1.0) > 1e-6) // Normalize if sum is not near 0 or 1
            {
                Console.WriteLine($"Warning: Dladgen frequencies sum to {sumFreq:F6}. Normalizing.");
                for (int i = 0; i < nBins; i++) freq[i] /= sumFreq;
            }

            // Return the calculated LIDF frequencies and representative angles
            return new FoliarDistributionResult { Lidf = freq, Litab = litab }; //
        }

        /// <summary>
        /// Computes the cumulative leaf angle distribution function value for Verhoef's LIDF.
        /// Internal helper function used by Dladgen.
        /// </summary>
        /// <param name="a">LIDF parameter 'a'.</param>
        /// <param name="b">LIDF parameter 'b'.</param>
        /// <param name="t">Angle (degrees) up to which cumulative frequency is calculated.</param>
        /// <returns>Cumulative frequency f (fraction of leaves with inclination is no larger than t).</returns>
        public static double Dcum(double a, double b, double t) // Method made static
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
                    x = x + dx;
                    // Update the change for convergence check
                    delx = Math.Abs(dx);
                    iter++; // Increment iteration counter
                }

                // Check if iteration failed to converge
                if (iter >= maxIter)
                {
                    // Log a warning if convergence wasn't reached
                    Console.WriteLine($"Warning: Dcum iteration did not converge within {maxIter} iterations for t={t}, a={a}, b={b}");
                    // R code uses the last calculated 'y'. We will do the same.
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
                // This approximation is robust near the singularity.
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
        public static double Jfunc2(double k, double l, double t) // Method made static
        {
            // Calculate the sum k+l
            double sum_kl = k + l;
            double Jout; // Result

            // Handle denominator near zero (k+l ≈ 0)
            if (Math.Abs(sum_kl) < 1e-9)
            {
                // Use L'Hopital's rule limit: t*exp(-(sum_kl)*t) -> t
                Jout = t;
            }
            // Handle small exponent case using Taylor expansion exp(-x) ≈ 1 - x
            else if (Math.Abs(sum_kl * t) < 1e-6)
            {
                // (1 - (1 - sum_kl*t)) / sum_kl = sum_kl*t / sum_kl = t
                Jout = t;
            }
            else // Standard calculation
            {
                Jout = (1.0 - Math.Exp(-sum_kl * t)) / sum_kl;
            }
            return Jout;
        }


        /// <summary>
        /// J3 function for SAIL calculations. Identical to Jfunc2 in the provided R code.
        /// Calculates (1 - exp(-(k+l)*t)) / (k + l).
        /// </summary>
        /// <param name="k">Parameter k.</param>
        /// <param name="l">Parameter l.</param>
        /// <param name="t">Parameter t.</param>
        /// <returns>Result of the J3 function.</returns>
        public static double Jfunc3(double k, double l, double t) // Method made static
        {
            // Functionally identical to Jfunc2 based on the R code provided
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
        public static double Jfunc4(double m, double t) // Method made static
        {
            // Calculate the product m*t
            double del = m * t;
            double out_val; // Result

            // Handle m exactly zero using limit analysis
            if (Math.Abs(m) < 1e-9)
            {
                // Limit of J4 as m->0 is t/2
                out_val = 0.5 * t;
            }
            // Use direct formula if |m*t| is not too small
            else if (Math.Abs(del) > 1e-3)
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
                // This approximation matches Taylor expansion near m=0.
                out_val = 0.5 * t * (1.0 - del * del / 12.0);
            }
            return out_val;
        }


        /// <summary>
        /// Compute volume scattering functions (Chi_s, Chi_o) and phase function components (Frho, Ftau).
        /// Internal helper function for SAIL, calculates angle-dependent geometric factors.
        /// Based on Wout Verhoef, april 2001, for CROMA (as cited in R code).
        /// </summary>
        /// <param name="tts">Solar zenith angle (degrees).</param>
        /// <param name="tto">Viewing zenith angle (degrees).</param>
        /// <param name="psi">Relative azimuth angle (degrees).</param>
        /// <param name="ttl">Leaf inclination angle (degrees).</param>
        /// <returns>A VolscattResult struct containing chi_s, chi_o, frho, ftau.</returns>
        public static VolscattResult Volscatt(double tts, double tto, double psi, double ttl) // Method made static
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
            double cosbts = (Math.Abs(ss) > 1e-6) ? -cs / ss : 5.0; // Use sentinel > 1 if ss is zero
            double cosbto = (Math.Abs(so) > 1e-6) ? -co / so : 5.0; // Use sentinel > 1 if so is zero

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
            double chi_s = (2.0 / PI) * ((bts - PI * 0.5) * cs + Math.Sin(bts) * ss);

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
            double chi_o = (2.0 / PI) * ((bto - PI * 0.5) * co + Math.Sin(bto) * so);

            // Ensure non-negative interception factors (can be slightly negative due to precision)
            chi_s = Math.Max(0.0, chi_s);
            chi_o = Math.Max(0.0, chi_o);

            // Calculate auxiliary azimuth angles for bidirectional scattering phase function
            double btran1 = Math.Abs(bts - bto); //
            double btran2 = PI - Math.Abs(bts + bto - PI); //

            // Determine integration limits bt1, bt2, bt3 based on relative azimuth psi
            double bt1, bt2, bt3;
            if (psir <= btran1) { bt1 = psir; bt2 = btran1; bt3 = btran2; } //
            else { bt1 = btran1; if (psir <= btran2) { bt2 = psir; bt3 = btran2; } else { bt2 = btran2; bt3 = psir; } } //

            // Calculate intermediate terms t1 and t2 for phase function
            double t1 = 2.0 * cs * co + ss * so * cospsi; //
            double t2 = 0;
            if (bt2 > 1e-9) // Avoid calculation if bt2 is zero
            {
                // Formula using intermediate factors ds, doo from R code
                t2 = Math.Sin(bt2) * (2.0 * ds * doo + ss * so * Math.Cos(bt1) * Math.Cos(bt3)); //
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
        /// Checks if spectral sampling (wavelengths) is identical between PROSPECT constants, SOIL properties, and ATM data.
        /// Throws an ArgumentException if sampling does not match in length or values.
        /// </summary>
        /// <param name="prospectConstants">PROSPECT spectral constants object (must contain Wavelength vector).</param>
        /// <param name="soilProperties">Soil spectral properties object (must contain Wavelength array).</param>
        /// <param name="specAtm">Atmosphere spectral properties object (must contain Wavelength array).</param>
        public static void check_SpectralSampling(ProspectCore.OpticalConstants prospectConstants, SoilProperties soilProperties, SpecAtmSensor specAtm) // Method made static
        {
            // Extract wavelength arrays
            // Note: ProspectCore uses Vector<double>, others use double[]
            double[] lambdaProspect = prospectConstants.Wavelength?.ToArray(); // Convert Vector to array for comparison
            double[] lambdaSoil = soilProperties?.Wavelength;
            double[] lambdaAtm = specAtm?.Wavelength;

            // Check if any wavelength array is null
            if (lambdaProspect == null || lambdaSoil == null || lambdaAtm == null)
            {
                throw new ArgumentNullException("check_SpectralSampling: One or more spectral wavelength arrays are null.");
            }

            // Get lengths
            int lenProspect = lambdaProspect.Length;
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

            // Check if wavelength values match exactly using SequenceEqual
            // Assumes wavelengths are in the same order.
            bool soilMatchesProspect = lambdaProspect.SequenceEqual(lambdaSoil);
            bool atmMatchesProspect = lambdaProspect.SequenceEqual(lambdaAtm);

            if (!soilMatchesProspect || !atmMatchesProspect) //
            {
                // Optionally log the first mismatch point for debugging
                /*
                for(int i=0; i<lenProspect; i++) {
                    if (Math.Abs(lambdaProspect[i] - lambdaSoil[i]) > 1e-6 || Math.Abs(lambdaProspect[i] - lambdaAtm[i]) > 1e-6) {
                       Console.WriteLine($"Spectral mismatch at index {i}: Prospect={lambdaProspect[i]}, Soil={lambdaSoil[i]}, Atm={lambdaAtm[i]}");
                       break;
                    }
                }
                */
                Console.WriteLine(errorMessage); // Log error
                throw new ArgumentException(errorMessage); // Throw exception
            }
            // If all checks pass, the method completes successfully.
        }


        /// <summary>
        /// Checks if provided brown leaf optical properties (BrownLOP) are correctly defined and spectrally compatible.
        /// Throws exceptions for critical errors (missing data, spectral mismatch).
        /// Writes console warnings for non-critical issues (multiple PROSPECT inputs provided).
        /// </summary>
        /// <param name="brownLOP">LeafOptics object representing brown leaf properties. Can be null if not provided externally.</param>
        /// <param name="referenceLambda">The reference wavelength array (e.g., from ProspectConstants) that BrownLOP's wavelengths should match.</param>
        /// <param name="inputProspectList">A list of ProspectInput parameters (used only to check count for warning message).</param>
        public static void check_BrownLOP(LeafOptics brownLOP, double[] referenceLambda, List<ProspectInput> inputProspectList) // Method made static
        {
            // Only perform checks if a BrownLOP object was actually passed in
            if (brownLOP != null)
            {
                // Check required fields (Wavelength, Reflectance, Transmittance) are present and not null
                if (brownLOP.Wavelength == null || brownLOP.Reflectance == null || brownLOP.Transmittance == null)
                {
                    string msg = "Provided BrownLOP must include non-null 'Wavelength', 'Reflectance' and 'Transmittance' arrays."; //
                    Console.WriteLine(msg); // Log error
                    throw new ArgumentException(msg); // Throw exception
                }

                // Check spectral domain matching against the reference simulation wavelengths
                if (referenceLambda == null)
                {
                    throw new ArgumentNullException(nameof(referenceLambda), "check_BrownLOP: Reference lambda array cannot be null.");
                }

                // Compare lengths and values of wavelength arrays
                if (brownLOP.Wavelength.Length != referenceLambda.Length || !brownLOP.Wavelength.SequenceEqual(referenceLambda)) //
                {
                    string msg = "Spectral domain mismatch: BrownLOP wavelengths do not match the reference simulation wavelengths."; //
                    Console.WriteLine(msg); // Log error
                    throw new ArgumentException(msg); // Throw exception
                }

                // Issue a warning if BrownLOP is provided AND multiple PROSPECT input sets exist
                // This indicates the second PROSPECT input set might be redundant.
                if (inputProspectList != null && inputProspectList.Count > 1) //
                {
                    // R code just prints messages, so we do the same
                    Console.WriteLine("Warning: External BrownLOP provided along with multiple PROSPECT input parameter sets."); //
                    Console.WriteLine("         Only the first PROSPECT input set will be used for green vegetation simulation."); //
                }
            }
            // If brownLOP is null, no checks are needed here; adjust_PROSPECT_2_SAIL will handle it.
        }


        /// <summary>
        /// Prepares leaf optical properties (GreenLOP, BrownLOP) for SAIL by running the PROSPECT model.
        /// Handles the logic for 4SAIL (needs GreenLOP) vs 4SAIL2 (needs GreenLOP and BrownLOP)
        /// based on input parameters and optionally provided external BrownLOP data.
        /// </summary>
        /// <param name="sailVersion">String identifying the SAIL model version: "4SAIL" or "4SAIL2".</param>
        /// <param name="prospectConstants">Spectral constants required by the PROSPECT model (e.g., refractive index, SACs).</param>
        /// <param name="inputProspectList">
        /// A list containing PROSPECT input parameters (N, CAB, CAR, etc.).
        /// Expects 1 set for 4SAIL (or 4SAIL2 with BrownLOP/fraction_brown=0).
        /// Expects 2 sets (first for green, second for brown) for 4SAIL2 if BrownLOP is null and fraction_brown > 0.
        /// </param>
        /// <param name="fraction_brown">Fraction of brown vegetation (0-1). Used only by 4SAIL2 logic when BrownLOP is not provided.</param>
        /// <param name="brownLOP">Optional pre-calculated brown leaf optical properties. If provided, simulation for brown leaves is skipped.</param>
        /// <returns>An AdjustedProspectResult struct containing the calculated GreenLOP and potentially BrownLOP.</returns>
        /// <exception cref="ArgumentException">Thrown if inputs are inconsistent (e.g., null lists, bad SAIL version) or Prospect simulation fails.</exception>
        /// <exception cref="InvalidOperationException">Thrown if ProspectCore.Run fails internally.</exception>
        public static AdjustedProspectResult adjust_PROSPECT_2_SAIL( // Method made static
            string sailVersion,
            ProspectCore.OpticalConstants prospectConstants, // Use ProspectCore's struct type
            List<ProspectInput> inputProspectList,            // Use SailUtilities' struct type
            double fraction_brown,
            LeafOptics brownLOP = null)                       // Use SailUtilities' class type
        {
            // --- Input Validation ---
            if (inputProspectList == null || inputProspectList.Count == 0)
            {
                throw new ArgumentException("adjust_PROSPECT_2_SAIL: Input_PROSPECT list cannot be null or empty.");
            }
            // prospectConstants is a struct, so it cannot be null itself, but its contents might be invalid
            if (prospectConstants.Wavelength == null || prospectConstants.Wavelength.Count == 0)
            {
                throw new ArgumentException("adjust_PROSPECT_2_SAIL: ProspectConstants must contain valid wavelength data.");
            }
            if (fraction_brown < 0 || fraction_brown > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(fraction_brown), "fraction_brown must be between 0 and 1.");
            }

            // --- Simulate Green Leaf Optical Properties (Always needed) ---
            ProspectInput greenIn = inputProspectList[0]; // Get parameters for green leaf
            LeafOptics greenLOP; // Result will be stored here

            try
            {
                // Call the static Run method from ProspectCore
                // Use named arguments for clarity and robustness against parameter order changes.
                (Vector<double> refl_g, Vector<double> trans_g) = ProspectCore.Prospect(
                    LeafOpticalConstants: prospectConstants, // Pass the full constants struct
                    N: greenIn.N,           // Pass individual parameters from the input struct
                    CAB: greenIn.CAB,       // Use CAB (consistent with ProspectCore)
                    CAR: greenIn.CAR,
                    ANT: greenIn.ANT,
                    BROWN: greenIn.BROWN,
                    EWT: greenIn.EWT,
                    LMA: greenIn.LMA,
                    PROT: greenIn.PROT,
                    CBC: greenIn.CBC,
                    Alpha: greenIn.Alpha);

                // Convert the Vector<double> results from ProspectCore back to double[] for LeafOptics
                greenLOP = new LeafOptics
                {
                    Wavelength = prospectConstants.Wavelength.ToArray(), // Copy wavelengths from input constants
                    Reflectance = refl_g.ToArray(),                      // Convert Vector to array
                    Transmittance = trans_g.ToArray()                      // Convert Vector to array
                };
            }
            catch (Exception ex) // Catch potential errors during PROSPECT run
            {
                // Wrap the original exception for better diagnostics
                throw new InvalidOperationException($"PROSPECT simulation failed for Green LOP: {ex.Message}", ex);
            }


            // --- Determine Brown Leaf Optical Properties (based on SAIL version and inputs) ---
            LeafOptics finalBrownLOP = null; // Initialize Brown LOP result

            // Logic specific to SAIL version
            if (sailVersion == "4SAIL") //
            {
                // 4SAIL only uses GreenLOP. BrownLOP is not needed and remains null.
                finalBrownLOP = null;
            }
            else if (sailVersion == "4SAIL2") //
            {
                // 4SAIL2 requires both Green and Brown LOP. Determine the source for Brown LOP.

                // Case 1: External BrownLOP object is provided directly
                if (brownLOP != null)
                {
                    // Check if the provided BrownLOP is spectrally valid
                    check_BrownLOP(brownLOP, prospectConstants.Wavelength.ToArray(), inputProspectList);
                    // Use the provided external BrownLOP
                    finalBrownLOP = brownLOP;
                }
                // Case 2: External BrownLOP is *not* provided. Need to simulate or use GreenLOP.
                else
                {
                    // Subcase 2a: If fraction_brown is effectively zero, brown leaves don't contribute.
                    if (Math.Abs(fraction_brown) < 1e-9)
                    {
                        // Assign Green LOP to Brown LOP as they are optically identical in this case
                        finalBrownLOP = greenLOP;
                    }
                    // Subcase 2b: fraction_brown > 0, and no external BrownLOP. Need to simulate using second input set.
                    else
                    {
                        // Check if a second input set for brown leaves exists in the list
                        if (inputProspectList.Count < 2)
                        {
                            // R code prints a warning and implicitly runs as 4SAIL. Mimic warning.
                            Console.WriteLine("Warning: 4SAIL2 requires two sets of PROSPECT inputs (or external BrownLOP/zero fraction_brown)."); //
                            Console.WriteLine("         Only one input set found. Brown LOP cannot be generated via PROSPECT."); //
                            Console.WriteLine("         Proceeding as if 4SAIL was selected (BrownLOP will be effectively null)."); //
                            // Set BrownLOP to null, so downstream 4SAIL2 logic might handle it or fail if it strictly requires it.
                            finalBrownLOP = null;
                        }
                        else // Second input set is available
                        {
                            // Get parameters for brown leaf from the second element of the list
                            ProspectInput brownIn = inputProspectList[1];
                            try
                            {
                                // Call ProspectCore.Run for the brown leaf parameters
                                (Vector<double> refl_b, Vector<double> trans_b) = ProspectCore.Prospect(
                                     LeafOpticalConstants: prospectConstants, // Use same spectral constants
                                     N: brownIn.N, CAB: brownIn.CAB, CAR: brownIn.CAR, ANT: brownIn.ANT,
                                     BROWN: brownIn.BROWN, EWT: brownIn.EWT, LMA: brownIn.LMA,
                                     PROT: brownIn.PROT, CBC: brownIn.CBC, Alpha: brownIn.Alpha);

                                // Convert result to LeafOptics structure
                                finalBrownLOP = new LeafOptics
                                {
                                    Wavelength = prospectConstants.Wavelength.ToArray(),
                                    Reflectance = refl_b.ToArray(),
                                    Transmittance = trans_b.ToArray()
                                };
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

            // Return the struct containing the final GreenLOP and BrownLOP (which might be null or same as green)
            return new AdjustedProspectResult { GreenLOP = greenLOP, BrownLOP = finalBrownLOP };
        }

    } // End of static class SailUtilities
} // End of namespace Models.Sail
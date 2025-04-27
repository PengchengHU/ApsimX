using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics; // Required for Complex numbers if needed, though not directly used in this translation.
using Models.Prospect;

namespace Models.Sail
{
    /// <summary>
    /// Provides utility functions for SAIL model calculations,
    /// translated from the R script Lib_PROSAIL.R.
    /// Create an instance of this class to use the methods.
    /// </summary>
    public static class SailUtilities
    {
        // --- Supporting Data Structures (remain separate) ---

        /// <summary>
        /// Placeholder for atmospheric sensor spectral data.
        /// Corresponds to the 'SpecATM_Sensor' list in R.
        /// </summary>
        public class SpecAtmSensor
        {
            /// <summary>
            /// Wavelengths (nm). Corresponds to SpecATM_Sensor$lambda.
            /// </summary>
            public double[] Wavelength { get; set; }

            /// <summary>
            /// Direct solar radiation. Corresponds to SpecATM_Sensor$Direct_Light.
            /// </summary>
            public double[] DirectLight { get; set; }

            /// <summary>
            /// Diffuse sky radiation. Corresponds to SpecATM_Sensor$Diffuse_Light.
            /// </summary>
            public double[] DiffuseLight { get; set; }
        }

        /// <summary>
        /// Placeholder for leaf optical properties.
        /// Corresponds to the 'LeafOptics', 'GreenLOP', 'BrownLOP' lists/dataframes in R.
        /// </summary>
        public class LeafOptics
        {
            /// <summary>
            /// Wavelengths (nm). Corresponds to LeafOptics$WVL or inferred from context.
            /// </summary>
            public double[] Wavelength { get; set; }

            /// <summary>
            /// Leaf reflectance. Corresponds to LeafOptics$Reflectance.
            /// </summary>
            public double[] Reflectance { get; set; }

            /// <summary>
            /// Leaf transmittance. Corresponds to LeafOptics$Transmittance.
            /// </summary>
            public double[] Transmittance { get; set; }
        }

        /// <summary>
        /// Placeholder for PROSPECT model input parameters.
        /// Corresponds to the 'Input_PROSPECT' list/dataframe in R.
        /// Needs specific fields like N, CHL, CAR, etc.
        /// </summary>
        public struct ProspectInput
        {
            /// <summary>Leaf structure parameter (unitless)</summary>
            public double N;
            /// <summary>Chlorophyll a + b content (μg/cm²)</summary>
            public double CHL;
            /// <summary>Carotenoid content (μg/cm²)</summary>
            public double CAR;
            /// <summary>Anthocyanin content (μg/cm²)</summary>
            public double ANT;
            /// <summary>Brown pigment content (Arbitrary units)</summary>
            public double BROWN;
            /// <summary>Equivalent Water Thickness (g/cm²)</summary>
            public double EWT;
            /// <summary>Leaf Mass per Area (g/cm²)</summary>
            public double LMA; // Nullable if not always provided
            /// <summary>Protein content (g/cm²)</summary>
            public double PROT;
            /// <summary>NonProt Carbon-based constituent content (g/cm²)</summary>
            public double CBC;
            /// <summary>Incidence angle in degrees</summary>
            public double Alpha;
        }


        /// <summary>
        /// Placeholder for PROSPECT spectral constants/data.
        /// Corresponds to 'Spec_Sensor' or 'SpecPROSPECT' in R.
        /// </summary>
        public class SpectralProperties
        {
            /// <summary>
            /// Wavelengths (nm). Corresponds to SpecPROSPECT$lambda.
            /// </summary>
            public double[] Lambda { get; set; }
            // Add other fields as needed (e.g., refractive index, absorption coefficients)
        }

        /// <summary>
        /// Placeholder for soil spectral data.
        /// Corresponds to 'SpecSOIL' in R.
        /// </summary>
        public class SoilProperties
        {
            /// <summary>
            /// Wavelengths (nm). Corresponds to SpecSOIL$lambda.
            /// </summary>
            public double[] Wavelength { get; set; }

            /// <summary>
            /// Soil reflectance spectrum. Corresponds to rsoil or SpecSOIL$Dry_Soil etc.
            /// </summary>
            public double[] Reflectance { get; set; }
        }

        /// <summary>
        /// Represents the result of the Campbell or Dladgen function.
        /// </summary>
        public struct FoliarDistributionResult
        {
            /// <summary>
            /// Leaf Inclination Distribution Function values.
            /// </summary>
            public double[] Lidf { get; set; }

            /// <summary>
            /// Representative Leaf Inclination Angles (degrees).
            /// </summary>
            public double[] Litab { get; set; }
        }

        /// <summary>
        /// Represents the result of the Volscatt function.
        /// </summary>
        public struct VolscattResult
        {
            /// <summary>
            /// Interception function for solar direction.
            /// </summary>
            public double Chi_s { get; set; }

            /// <summary>
            /// Interception function for viewing direction.
            /// </summary>
            public double Chi_o { get; set; }

            /// <summary>
            /// Scattering function component for reflectance.
            /// </summary>
            public double Frho { get; set; }

            /// <summary>
            /// Scattering function component for transmittance.
            /// </summary>
            public double Ftau { get; set; }
        }

        /// <summary>
        /// Represents the result of NonConservativeScattering or ConservativeScattering functions.
        /// Contains various reflectance and transmittance factors.
        /// </summary>
        public struct ScatteringResult
        {
            /// <summary>Bi-hemispherical transmittance</summary>
            public double[] Tdd { get; set; } // Bi-hemispherical transmittance
            /// <summary>Bi-hemispherical reflectance</summary>
            public double[] Rdd { get; set; } // Bi-hemispherical reflectance
            /// <summary>Directional-hemispherical transmittance</summary>
            public double[] Tsd { get; set; } // Directional-hemispherical transmittance
            /// <summary>Directional-hemispherical reflectance</summary>
            public double[] Rsd { get; set; } // Directional-hemispherical reflectance
            /// <summary>Hemispherical-directional transmittance</summary>
            public double[] Tdo { get; set; } // Hemispherical-directional transmittance
            /// <summary>Hemispherical-directional reflectance</summary>
            public double[] Rdo { get; set; } // Hemispherical-directional reflectance
            /// <summary>Multiple scattering contribution to bi-directional reflectance</summary>
            public double[] Rsod { get; set; } // Multiple scattering contribution to bi-directional reflectance
        }

        /// <summary>
        /// Represents the combined result of PROSPECT runs needed for SAIL.
        /// </summary>
        public struct AdjustedProspectResult
        {
            /// <summary>Green leaf optical properties</summary>
            public LeafOptics GreenLOP { get; set; }
            /// <summary>Brown leaf optical properties</summary>
            public LeafOptics BrownLOP { get; set; } // Might be null or same as GreenLOP
        }

        /// <summary>
        /// Represents the output of the SAIL models (fourSAIL, fourSAIL2).
        /// </summary>
        public class SailResult
        {
            /// <summary>
            /// Hemispherical-directional reflectance factor in viewing direction.
            /// </summary>
            public double[] Rdot { get; set; }

            /// <summary>
            /// Bi-directional reflectance factor.
            /// </summary>
            public double[] Rsot { get; set; }

            /// <summary>
            /// Bi-hemispherical reflectance factor.
            /// </summary>
            public double[] Rddt { get; set; }

            /// <summary>
            /// Directional-hemispherical reflectance factor for solar incident flux.
            /// </summary>
            public double[] Rsdt { get; set; }

            /// <summary>
            /// Fraction of Vegetation Cover (= 1 - beam transmittance in the target-view path).
            /// </summary>
            public double[] FCover { get; set; }

            /// <summary>
            /// Canopy absorptance for direct solar incident flux.
            /// </summary>
            public double[] Abs_dir { get; set; }

            /// <summary>
            /// Canopy absorptance for hemispherical diffuse incident flux.
            /// </summary>
            public double[] Abs_hem { get; set; }

            /// <summary>
            /// Contribution of direct solar incident flux to albedo.
            /// </summary>
            public double[] Rsdstar { get; set; }

            /// <summary>
            /// Contribution of hemispherical diffuse incident flux to albedo.
            /// </summary>
            public double[] Rddstar { get; set; }
        }

        // --- End of Supporting Data Structures ---
        // Constants used within the class
        private const double PI = Math.PI;
        private const double DEGREES_TO_RADIANS = PI / 180.0;

        /// <summary>
        /// Computes bidirectional reflectance factor based on outputs from PROSAIL and SZA.
        /// </summary>
        /// <remarks>
        /// The direct and diffuse light are taken into account as proposed by:
        /// Francois et al. (2002) Conversion of 400-1100 nm vegetation albedo
        /// measurements into total shortwave broadband albedo using a canopy
        /// radiative transfer model, Agronomie.
        /// Es = direct
        /// Ed = diffuse
        /// </remarks>
        /// <param name="rdot">Numeric array. Hemispherical-directional reflectance factor in viewing direction.</param>
        /// <param name="rsot">Numeric array. Bi-directional reflectance factor.</param>
        /// <param name="tts">Numeric. Solar zenith angle (degrees).</param>
        /// <param name="specAtmSensor">Object containing direct and diffuse radiation for clear conditions.</param>
        /// <returns>BRF numeric array. Bidirectional reflectance factor.</returns>
        public static double[] Compute_BRF(double[] rdot, double[] rsot, double tts, SpecAtmSensor specAtmSensor)
        {
            // ############################## #
            // ##	direct / diffuse light	##
            // ############################## #
            double[] Es = specAtmSensor.DirectLight; //
            double[] Ed = specAtmSensor.DiffuseLight; //

            if (rdot.Length != rsot.Length || rdot.Length != Es.Length || rdot.Length != Ed.Length)
            {
                throw new ArgumentException("Input arrays must have the same length.");
            }

            double rd = DEGREES_TO_RADIANS; //
            double solarAngleRad = (90.0 - tts) * rd; //
            double sinSolarAngle = Math.Sin(solarAngleRad);

            // diffuse radiation factor (Francois et al., 2002)
            double skyl = 0.847 - 1.61 * sinSolarAngle + 1.04 * sinSolarAngle * sinSolarAngle; //

            int n = rdot.Length;
            double[] BRF = new double[n];
            for (int i = 0; i < n; i++)
            {
                double PARdiro = (1.0 - skyl) * Es[i]; //
                double PARdifo = skyl * Ed[i]; // Note: R code uses skyl*Ed, paper might imply skyl*Es + (1-skyl)*Ed? Check Francois et al. Original R code logic is kept here.
                double denominator = PARdiro + PARdifo; //
                if (Math.Abs(denominator) < 1e-9) // Avoid division by zero
                {
                    BRF[i] = 0; // Or handle as appropriate, e.g., NaN or throw exception
                }
                else
                {
                    BRF[i] = (rdot[i] * PARdifo + rsot[i] * PARdiro) / denominator; //
                }
            }
            return BRF; //
        }

        /// <summary>
        /// Computes fraction of absorbed photosyntehtically active radiation (fAPAR).
        /// </summary>
        /// <remarks>
        /// The direct and diffuse light are taken into account as proposed by:
        /// Francois et al. (2002) Conversion of 400-1100 nm vegetation albedo
        /// measurements into total shortwave broadband albedo using a canopy
        /// radiative transfer model, Agronomie.
        /// Es = direct
        /// Ed = diffuse
        /// </remarks>
        /// <param name="abs_dir">Numeric array. Fraction of direct light absorbed.</param>
        /// <param name="abs_hem">Numeric array. Fraction of diffuse light absorbed.</param>
        /// <param name="tts">Numeric. Solar zenith angle (degrees).</param>
        /// <param name="specAtmSensor">Object containing direct and diffuse radiation and wavelengths.</param>
        /// <param name="parRangeMin">Minimum wavelength (nm) for PAR range (default 400).</param>
        /// <param name="parRangeMax">Maximum wavelength (nm) for PAR range (default 700).</param>
        /// <returns>fAPAR numeric. Fraction of Absorbed Photosynthetically Active Radiation.</returns>
        public static double Compute_fAPAR(double[] abs_dir, double[] abs_hem, double tts, SpecAtmSensor specAtmSensor, double parRangeMin = 400, double parRangeMax = 700)
        {
            // ############################## #
            // ##	direct / diffuse light	##
            // ############################## #
            double[] Es = specAtmSensor.DirectLight; //
            double[] Ed = specAtmSensor.DiffuseLight; //
            double[] lambda = specAtmSensor.Wavelength; //

            if (abs_dir.Length != abs_hem.Length || abs_dir.Length != Es.Length || abs_dir.Length != Ed.Length || abs_dir.Length != lambda.Length)
            {
                throw new ArgumentException("Input arrays must have the same length.");
            }

            double rd = DEGREES_TO_RADIANS; //
            double solarAngleRad = (90.0 - tts) * rd; //
            double sinSolarAngle = Math.Sin(solarAngleRad);

            // diffuse radiation factor (Francois et al., 2002)
            double skyl = 0.847 - 1.61 * sinSolarAngle + 1.04 * sinSolarAngle * sinSolarAngle; //

            double totalAbsorbedPAR = 0;
            double totalIncidentPAR = 0;

            for (int i = 0; i < lambda.Length; i++) //
            {
                // Check if current wavelength is within the PAR range
                if (lambda[i] >= parRangeMin && lambda[i] <= parRangeMax) //
                {
                    double PARdiro = (1.0 - skyl) * Es[i]; //
                    double PARdifo = skyl * Ed[i]; // Check Francois et al. See note in Compute_BRF.
                    double absorbed = (abs_dir[i] * PARdiro + abs_hem[i] * PARdifo); //
                    double incident = PARdiro + PARdifo; //

                    // Simple summation assumes equal spectral bandwidths.
                    // For non-uniform sampling, integration (e.g., trapezoidal rule) would be needed.
                    totalAbsorbedPAR += absorbed; //
                    totalIncidentPAR += incident; //
                }
            }

            if (Math.Abs(totalIncidentPAR) < 1e-9)
            {
                return 0; // Or handle as appropriate
            }

            return totalAbsorbedPAR / totalIncidentPAR; //
        }

        /// <summary>
        /// Computes albedo.
        /// </summary>
        /// <remarks>
        /// Borrowed from python package PROSAIL developed by Jose Gomez Dans.
        /// The direct and diffuse light are taken into account as proposed by:
        /// Francois et al. (2002) Conversion of 400-1100 nm vegetation albedo
        /// measurements into total shortwave broadband albedo using a canopy
        /// radiative transfer model, Agronomie.
        /// Es = direct
        /// Ed = diffuse
        /// </remarks>
        /// <param name="rsdstar">Numeric array. Reflectance factor contribution from direct light.</param>
        /// <param name="rddstar">Numeric array. Reflectance factor contribution from diffuse light.</param>
        /// <param name="tts">Numeric. Solar zenith angle (degrees).</param>
        /// <param name="specAtmSensor">Object containing direct and diffuse radiation and wavelengths.</param>
        /// <param name="albedoRangeMin">Minimum wavelength (nm) for albedo integration range (default 400).</param>
        /// <param name="albedoRangeMax">Maximum wavelength (nm) for albedo integration range (default 2400).</param>
        /// <returns>Albedo numeric. Broadband albedo over the specified range.</returns>
        public static double Compute_albedo(double[] rsdstar, double[] rddstar, double tts, SpecAtmSensor specAtmSensor, double albedoRangeMin = 400, double albedoRangeMax = 2400)
        {
            // ############################## #
            // ##	direct / diffuse light	##
            // ############################## #
            double[] Es = specAtmSensor.DirectLight; //
            double[] Ed = specAtmSensor.DiffuseLight; //
            double[] lambda = specAtmSensor.Wavelength; //

            if (rsdstar.Length != rddstar.Length || rsdstar.Length != Es.Length || rsdstar.Length != Ed.Length || rsdstar.Length != lambda.Length)
            {
                throw new ArgumentException("Input arrays must have the same length.");
            }

            double rd = DEGREES_TO_RADIANS; //
            double solarAngleRad = (90.0 - tts) * rd; //
            double sinSolarAngle = Math.Sin(solarAngleRad);

            // diffuse radiation factor (Francois et al., 2002)
            double skyl = 0.847 - 1.61 * sinSolarAngle + 1.04 * sinSolarAngle * sinSolarAngle; //

            double totalReflected = 0;
            double totalIncident = 0;

            for (int i = 0; i < lambda.Length; i++) //
            {
                // Check if current wavelength is within the albedo integration range
                if (lambda[i] >= albedoRangeMin && lambda[i] <= albedoRangeMax) //
                {
                    double PARdiro = (1.0 - skyl) * Es[i]; //
                    double PARdifo = skyl * Ed[i]; // Check Francois et al. See note in Compute_BRF.
                    double reflected = (rsdstar[i] * PARdiro + rddstar[i] * PARdifo); //
                    double incident = PARdiro + PARdifo; //

                    // Simple summation assumes equal spectral bandwidths.
                    // For non-uniform sampling, integration (e.g., trapezoidal rule) would be needed.
                    totalReflected += reflected; //
                    totalIncident += incident; //
                }
            }

            if (Math.Abs(totalIncident) < 1e-9)
            {
                return 0; // Or handle as appropriate
            }

            return totalReflected / totalIncident; //
        }

        /// <summary>
        /// Computes scattering components for non-conservative scattering conditions (m > 0.01).
        /// Internal helper function for SAIL models.
        /// </summary>
        /// <param name="m">Numeric array. sqrt((att+sigb)*(att-sigb)) for relevant wavelengths.</param>
        /// <param name="lai">Numeric. Leaf Area Index for the layer.</param>
        /// <param name="att">Numeric array. Attenuation coefficient (1-sigf).</param>
        /// <param name="sigb">Numeric array. Backscattering coefficient.</param>
        /// <param name="ks">Numeric. Extinction coefficient for solar flux.</param>
        /// <param name="ko">Numeric. Extinction coefficient for observed flux.</param>
        /// <param name="sf">Numeric array. Scattering coefficient (solar flux -> downward diffuse).</param>
        /// <param name="sb">Numeric array. Scattering coefficient (solar flux -> upward diffuse).</param>
        /// <param name="vf">Numeric array. Scattering coefficient (upward diffuse -> observed).</param>
        /// <param name="vb">Numeric array. Scattering coefficient (downward diffuse -> observed).</param>
        /// <param name="tss">Numeric. Directional transmittance (solar).</param>
        /// <param name="too">Numeric. Directional transmittance (observer).</param>
        /// <returns>A ScatteringResult struct containing computed reflectance and transmittance factors.</returns>
        public static ScatteringResult NonConservativeScattering(
            double[] m, double lai, double[] att, double[] sigb, double ks, double ko,
            double[] sf, double[] sb, double[] vf, double[] vb, double tss, double too)
        {
            int n = m.Length;
            if (att.Length != n || sigb.Length != n || sf.Length != n || sb.Length != n || vf.Length != n || vb.Length != n)
            {
                throw new ArgumentException("Input arrays must have the same length as m.");
            }

            double[] tdd = new double[n];
            double[] rdd = new double[n];
            double[] tsd = new double[n];
            double[] rsd = new double[n];
            double[] tdo = new double[n];
            double[] rdo = new double[n];
            double[] rsod = new double[n];

            for (int i = 0; i < n; i++)
            {
                double mi = m[i]; //
                double atti = att[i]; //
                double sigbi = sigb[i]; //
                double sfi = sf[i]; //
                double sbi = sb[i]; //
                double vfi = vf[i]; //
                double vbi = vb[i]; //

                double e1 = Math.Exp(-mi * lai); //
                double e2 = e1 * e1; //
                double rinf = (atti - mi) / sigbi; //
                double rinf2 = rinf * rinf; //
                double re = rinf * e1; //
                double denom = 1.0 - rinf2 * e2; //

                if (Math.Abs(denom) < 1e-12) denom = 1e-12; // Avoid division by zero, adjust as needed

                double J1ks_val = Jfunc1(ks, mi, lai); //
                double J2ks_val = Jfunc2(ks, mi, lai); //
                double J1ko_val = Jfunc1(ko, mi, lai); //
                double J2ko_val = Jfunc2(ko, mi, lai); //

                double Ps = (sfi + sbi * rinf) * J1ks_val; //
                double Qs = (sfi * rinf + sbi) * J2ks_val; //
                double Pv = (vfi + vbi * rinf) * J1ko_val; //
                double Qv = (vfi * rinf + vbi) * J2ko_val; //

                tdd[i] = (1.0 - rinf2) * e1 / denom; //
                rdd[i] = rinf * (1.0 - e2) / denom; //
                tsd[i] = (Ps - re * Qs) / denom; //
                rsd[i] = (Qs - re * Ps) / denom; //
                tdo[i] = (Pv - re * Qv) / denom; //
                rdo[i] = (Qv - re * Pv) / denom; //

                // Note: Original R code calls Jfunc2(ks,ko,lai), but the formula context
                // within 4SAIL/4SAIL2 NonConservativeScattering implies Jfunc2(ks, mi, lai) and Jfunc2(ko, mi, lai)
                // might be intended here, matching J1ks/J2ks pairs. However, the R code
                // specifically calls Jfunc2(ks,ko,lai) just before calculating g1/g2.
                // Let's stick to the R code's direct implementation for Jfunc2(ks,ko,lai) here.
                // If results seem off, revisit this Jfunc2 call.
                // *Update*: R's Jfunc2(k,l,t) calculates (1-exp(-(k+l)t))/(k+l),
                // R's Jfunc3(k,l,t) calculates (1-exp(-(k+l)t))/(k+l). They are identical in R code provided.
                // The variable 'z' in 4SAIL uses Jfunc3(ks,ko,lai). Let's use that.
                // *Further Update*: R's NonConservativeScattering calls Jfunc2(ks,ko,lai) for 'z'. Let's follow R.
                double z = Jfunc2(ks, ko, lai); // Or Jfunc3(ks, ko, lai) - they are the same in R code provided

                double g1_denom = ko + mi; //
                double g2_denom = ks + mi; //
                if (Math.Abs(g1_denom) < 1e-12) g1_denom = 1e-12;
                if (Math.Abs(g2_denom) < 1e-12) g2_denom = 1e-12;

                double g1 = (z - J1ks_val * too) / g1_denom; // J1ks depends on mi
                double g2 = (z - J1ko_val * tss) / g2_denom; // J1ko depends on mi


                double Tv1 = (vfi * rinf + vbi) * g1; //
                double Tv2 = (vfi + vbi * rinf) * g2; //
                double T1 = Tv1 * (sfi + sbi * rinf); //
                double T2 = Tv2 * (sfi * rinf + sbi); //
                double T3 = (rdo[i] * Qs + tdo[i] * Ps) * rinf; //

                // Multiple scattering contribution to bidirectional canopy reflectance
                double rsod_denom = (1.0 - rinf2); //
                if (Math.Abs(rsod_denom) < 1e-12) rsod_denom = 1e-12; // Avoid division by zero
                rsod[i] = (T1 + T2 - T3) / rsod_denom; //

            }

            return new ScatteringResult { Tdd = tdd, Rdd = rdd, Tsd = tsd, Rsd = rsd, Tdo = tdo, Rdo = rdo, Rsod = rsod }; //
        }


        /// <summary>
        /// Computes scattering components for conservative scattering conditions (m is not larger than 0.01).
        /// Internal helper function for SAIL models.
        /// </summary>
        /// <param name="m">Numeric array. sqrt((att+sigb)*(att-sigb)) for relevant wavelengths.</param>
        /// <param name="lai">Numeric. Leaf Area Index for the layer.</param>
        /// <param name="att">Numeric array. Attenuation coefficient (1-sigf).</param>
        /// <param name="sigb">Numeric array. Backscattering coefficient.</param>
        /// <param name="ks">Numeric. Extinction coefficient for solar flux.</param>
        /// <param name="ko">Numeric. Extinction coefficient for observed flux.</param>
        /// <param name="sf">Numeric array. Scattering coefficient (solar flux -> downward diffuse).</param>
        /// <param name="sb">Numeric array. Scattering coefficient (solar flux -> upward diffuse).</param>
        /// <param name="vf">Numeric array. Scattering coefficient (upward diffuse -> observed).</param>
        /// <param name="vb">Numeric array. Scattering coefficient (downward diffuse -> observed).</param>
        /// <param name="tss">Numeric. Directional transmittance (solar).</param>
        /// <param name="too">Numeric. Directional transmittance (observer).</param>
        /// <returns>A ScatteringResult struct containing computed reflectance and transmittance factors.</returns>
        public static ScatteringResult ConservativeScattering(
           double[] m, double lai, double[] att, double[] sigb, double ks, double ko,
           double[] sf, double[] sb, double[] vf, double[] vb, double tss, double too)
        {
            int n = m.Length;
            if (att.Length != n || sigb.Length != n || sf.Length != n || sb.Length != n || vf.Length != n || vb.Length != n)
            {
                throw new ArgumentException("Input arrays must have the same length as m.");
            }

            double[] tdd = new double[n];
            double[] rdd = new double[n];
            double[] tsd = new double[n];
            double[] rsd = new double[n];
            double[] tdo = new double[n];
            double[] rdo = new double[n];
            double[] rsod = new double[n];

            for (int i = 0; i < n; i++)
            {
                double mi = m[i]; //
                double atti = att[i]; //
                double sigbi = sigb[i]; //
                double sfi = sf[i]; //
                double sbi = sb[i]; //
                double vfi = vf[i]; //
                double vbi = vb[i]; //

                // Near or complete conservative scattering
                double J4_val = Jfunc4(mi, lai); //
                double amsig = atti - sigbi; //
                double apsig = atti + sigbi; //

                double denom_rtp = 1.0 + amsig * J4_val; //
                double denom_rtm = 1.0 + apsig * J4_val; //
                if (Math.Abs(denom_rtp) < 1e-12) denom_rtp = 1e-12; // Avoid division by zero
                if (Math.Abs(denom_rtm) < 1e-12) denom_rtm = 1e-12; // Avoid division by zero

                double rtp = (1.0 - amsig * J4_val) / denom_rtp; //
                double rtm = (-1.0 + apsig * J4_val) / denom_rtm; // R code uses (-1+apsig*J4)
                rdd[i] = 0.5 * (rtp + rtm); //
                tdd[i] = 0.5 * (rtp - rtm); //

                double dns = ks * ks - mi * mi; //
                double dno = ko * ko - mi * mi; //
                if (Math.Abs(dns) < 1e-12) dns = 1e-12; // Avoid division by zero
                if (Math.Abs(dno) < 1e-12) dno = 1e-12; // Avoid division by zero

                double cks = (sbi * (ks - atti) - sfi * sigbi) / dns; //
                double cko = (vbi * (ko - atti) - vfi * sigbi) / dno; //
                double dks = (-sfi * (ks + atti) - sbi * sigbi) / dns; //
                double dko = (-vfi * (ko + atti) - vbi * sigbi) / dno; //

                double ko_plus_ks = ko + ks; //
                if (Math.Abs(ko_plus_ks) < 1e-12) ko_plus_ks = 1e-12; // Avoid division by zero
                double ho = (sfi * cko + sbi * dko) / ko_plus_ks; // R code uses (sf*cko+sb*dko) - assume elementwise

                rsd[i] = cks * (1.0 - tss * tdd[i]) - dks * rdd[i]; //
                rdo[i] = cko * (1.0 - too * tdd[i]) - dko * rdd[i]; //
                tsd[i] = dks * (tss - tdd[i]) - cks * tss * rdd[i]; //
                tdo[i] = dko * (too - tdd[i]) - cko * too * rdd[i]; //
                                                                    // Multiple scattering contribution to bidirectional canopy reflectance
                rsod[i] = ho * (1.0 - tss * too) - cko * tsd[i] * too - dko * rsd[i]; // R code uses 'rsd', implying rsd[i]
            }

            return new ScatteringResult { Tdd = tdd, Rdd = rdd, Tsd = tsd, Rsd = rsd, Tdo = tdo, Rdo = rdo, Rsod = rsod }; //
        }


        /// <summary>
        /// Computes the leaf angle distribution function value (freq) using Campbell's ellipsoidal distribution.
        /// </summary>
        /// <remarks>
        /// Ellipsoidal distribution function characterised by the average leaf
        /// inclination angle in degrees (ala). Campbell 1986.
        /// </remarks>
        /// <param name="ala">Average leaf angle (degrees).</param>
        /// <returns>A FoliarDistributionResult struct containing lidf and litab.</returns>
        public static FoliarDistributionResult Campbell(double ala)
        {
            double[] tx1 = { 10.0, 20.0, 30.0, 40.0, 50.0, 60.0, 70.0, 80.0, 82.0, 84.0, 86.0, 88.0, 90.0 }; //
            double[] tx2 = { 0.0, 10.0, 20.0, 30.0, 40.0, 50.0, 60.0, 70.0, 80.0, 82.0, 84.0, 86.0, 88.0 }; //
            int n = tx1.Length;
            double[] litab = new double[n];
            double[] tl1 = new double[n];
            double[] tl2 = new double[n];
            double[] freq = new double[n]; //

            for (int i = 0; i < n; i++)
            {
                litab[i] = (tx2[i] + tx1[i]) / 2.0; //
                tl1[i] = tx1[i] * DEGREES_TO_RADIANS; //
                tl2[i] = tx2[i] * DEGREES_TO_RADIANS; //
            }

            // Calculate eccentricity factor
            double ala_rad = ala * DEGREES_TO_RADIANS; // Convert ala to radians if needed by formula
            double excent = Math.Exp(-1.6184e-5 * Math.Pow(ala, 3) + 2.1145e-3 * Math.Pow(ala, 2) - 1.2390e-1 * ala + 3.2491); //
            double sum0 = 0; //

            for (int i = 0; i < n; i++) //
            {
                double tan_tl1_sq = Math.Pow(Math.Tan(tl1[i]), 2);
                double tan_tl2_sq = Math.Pow(Math.Tan(tl2[i]), 2);

                // Handle potential division by zero or invalid tan values (e.g., at 90 degrees)
                if (Math.Abs(Math.Cos(tl1[i])) < 1e-9) tan_tl1_sq = double.PositiveInfinity;
                if (Math.Abs(Math.Cos(tl2[i])) < 1e-9) tan_tl2_sq = double.PositiveInfinity;

                double x1, x2;
                // Avoid division by zero if tan is infinite
                if (double.IsInfinity(tan_tl1_sq)) x1 = 0;
                else x1 = excent / (Math.Sqrt(1.0 + excent * excent * tan_tl1_sq)); //

                if (double.IsInfinity(tan_tl2_sq)) x2 = 0;
                else x2 = excent / (Math.Sqrt(1.0 + excent * excent * tan_tl2_sq)); //


                if (Math.Abs(excent - 1.0) < 1e-9) // Spherical distribution case (excent == 1)
                {
                    freq[i] = Math.Abs(Math.Cos(tl1[i]) - Math.Cos(tl2[i])); //
                }
                else
                {
                    double excent_sq = excent * excent; //
                    double one_minus_excent_sq = 1.0 - excent_sq;

                    // Avoid division by zero or sqrt of negative for alpha
                    if (Math.Abs(one_minus_excent_sq) < 1e-9)
                    {
                        // Handle case where excent is very close to 1, treat as spherical?
                        // Or use a limit approach. For simplicity, revert to spherical if very close.
                        freq[i] = Math.Abs(Math.Cos(tl1[i]) - Math.Cos(tl2[i]));
                    }
                    else
                    {
                        double alpha = excent / Math.Sqrt(Math.Abs(one_minus_excent_sq)); //
                        double alpha2 = alpha * alpha; //
                        double x12 = x1 * x1; //
                        double x22 = x2 * x2; //
                        double dum1, dum2;

                        if (excent > 1.0) // Prolate spheroid
                        {
                            double alpha2_plus_x12 = alpha2 + x12; //
                            double alpha2_plus_x22 = alpha2 + x22; //
                                                                   // Avoid potential issues if alpha2+x^2 is near zero (shouldn't happen for excent > 1)
                            double alpx1 = Math.Sqrt(alpha2_plus_x12 > 0 ? alpha2_plus_x12 : 0); //
                            double alpx2 = Math.Sqrt(alpha2_plus_x22 > 0 ? alpha2_plus_x22 : 0); //

                            // Calculate log term carefully: log(x + sqrt(alpha^2 + x^2))
                            double log_term1 = (x1 + alpx1) > 1e-12 ? Math.Log(x1 + alpx1) : -27.6; // Approx log(1e-12)
                            double log_term2 = (x2 + alpx2) > 1e-12 ? Math.Log(x2 + alpx2) : -27.6; //

                            dum1 = x1 * alpx1 + alpha2 * log_term1; //
                            dum2 = x2 * alpx2 + alpha2 * log_term2; //
                            freq[i] = Math.Abs(dum1 - dum2); //
                        }
                        else // Oblate spheroid (excent < 1)
                        {
                            double alpha2_minus_x12 = alpha2 - x12; //
                            double alpha2_minus_x22 = alpha2 - x22; //
                                                                    // Ensure argument of sqrt is non-negative
                            double almx1 = Math.Sqrt(alpha2_minus_x12 > 0 ? alpha2_minus_x12 : 0); //
                            double almx2 = Math.Sqrt(alpha2_minus_x22 > 0 ? alpha2_minus_x22 : 0); //

                            // Ensure argument of asin is within [-1, 1]
                            double asin_arg1 = x1 / alpha; //
                            double asin_arg2 = x2 / alpha; //
                            asin_arg1 = Math.Max(-1.0, Math.Min(1.0, asin_arg1));
                            asin_arg2 = Math.Max(-1.0, Math.Min(1.0, asin_arg2));

                            dum1 = x1 * almx1 + alpha2 * Math.Asin(asin_arg1); //
                            dum2 = x2 * almx2 + alpha2 * Math.Asin(asin_arg2); //
                            freq[i] = Math.Abs(dum1 - dum2); //
                        }
                    }
                }
                sum0 += freq[i]; //
            }

            // Normalize frequencies
            double[] freq0 = new double[n];
            if (Math.Abs(sum0) > 1e-9) //
            {
                for (int i = 0; i < n; i++)
                {
                    freq0[i] = freq[i] / sum0; //
                }
            }
            // else: freq0 remains array of zeros if sum is zero

            return new FoliarDistributionResult { Lidf = freq0, Litab = litab }; //
        }

        /// <summary>
        /// Computes the leaf angle distribution function value (freq) using Verhoef's bimodal distribution.
        /// </summary>
        /// <remarks>
        /// Using the original bimodal distribution function initially proposed in SAIL.
        /// References:
        /// (Verhoef1998) Verhoef, Wout. Theory of radiative transfer models applied
        /// in optical remote sensing of vegetation canopies. Nationaal Lucht en Ruimtevaartlaboratorium, 1998.
        /// http://library.wur.nl/WebQuery/clc/945481.
        /// Requirement: |LIDFa| + |LIDFb| &lt; 1 (Typically, although code doesn't enforce it)
        /// LIDF type 		  a 		b
        /// Planophile 	      1		    0
        /// Erectophile       -1	 	    0
        /// Plagiophile 	  0		    -1
        /// Extremophile 	  0		    1
        /// Spherical 	    -0.35     -0.15
        /// Uniform           0         0
        /// </remarks>
        /// <param name="a">Controls the average leaf slope.</param>
        /// <param name="b">Controls the distribution's bimodality.</param>
        /// <returns>A FoliarDistributionResult struct containing lidf and litab.</returns>
        public static FoliarDistributionResult Dladgen(double a, double b)
        {
            // Representative angles (degrees)
            double[] litab = { 5.0, 15.0, 25.0, 35.0, 45.0, 55.0, 65.0, 75.0, 81.0, 83.0, 85.0, 87.0, 89.0 }; //
            int n = litab.Length;
            double[] freq_cum = new double[n]; // Cumulative frequencies

            // Calculate cumulative frequencies at the upper bounds of the angle bins
            double[] angle_bounds = { 10.0, 20.0, 30.0, 40.0, 50.0, 60.0, 70.0, 80.0, 82.0, 84.0, 86.0, 88.0, 90.0 }; //

            for (int i = 0; i < n; i++) //
            {
                freq_cum[i] = Dcum(a, b, angle_bounds[i]); //
            }

            // Calculate frequencies for each bin by differencing cumulative frequencies
            double[] freq = new double[n]; //
            freq[0] = freq_cum[0]; // First bin frequency is just the first cumulative value
            for (int i = 1; i < n; i++) //
            {
                freq[i] = freq_cum[i] - freq_cum[i - 1]; //
            }

            // Normalize (optional, Dcum should already yield values summing to 1 if angles go to 90)
            // double sumFreq = freq.Sum();
            // if (Math.Abs(sumFreq - 1.0) > 1e-6 && sumFreq != 0) // Check if normalization is needed
            // {
            //     for (int i = 0; i < n; i++) freq[i] /= sumFreq;
            // }


            return new FoliarDistributionResult { Lidf = freq, Litab = litab }; //
        }

        /// <summary>
        /// Computes cumulative leaf angle distribution function value. Helper for Dladgen.
        /// </summary>
        /// <param name="a">Controls the average leaf slope.</param>
        /// <param name="b">Controls the distribution's bimodality.</param>
        /// <param name="t">Angle (degrees).</param>
        /// <returns>Cumulative frequency f.</returns>
        public static double Dcum(double a, double b, double t)
        {
            double rd = DEGREES_TO_RADIANS; //
            double f;

            if (a >= 1.0) // Planophile limit case
            {
                f = 1.0 - Math.Cos(rd * t); //
            }
            else
            {
                double eps = 1e-8; //
                double delx = 1.0; //
                double x = 2.0 * rd * t; // Initial guess for transformed angle
                double p = x;          // Target value
                int maxIter = 100;      // Add iteration limit for safety
                int iter = 0;

                // Iterative solver to find the transformed angle x
                while (delx >= eps && iter < maxIter) //
                {
                    double y = a * Math.Sin(x) + 0.5 * b * Math.Sin(2.0 * x); //
                    double dx = 0.5 * (y - x + p); // Correction step
                    x = x + dx; //
                    delx = Math.Abs(dx); //
                    iter++;
                }

                if (iter >= maxIter)
                {
                    // Handle convergence failure - maybe return NaN or throw?
                    Console.WriteLine($"Warning: Dcum iteration did not converge for t={t}, a={a}, b={b}");
                }


                // Final cumulative frequency calculation
                // Need to recalculate y based on the converged x
                double y_final = a * Math.Sin(x) + 0.5 * b * Math.Sin(2.0 * x); //
                f = (2.0 * y_final + p) / PI; // Note: R uses 'y' from the last loop iteration, which might be slightly off. Using y_final based on converged x. Check original SAIL source if critical.
                                              // R code uses 'y' from *before* the last update to x. Let's match R exactly:
                                              // double y_before_last_update = a * Math.Sin(x-dx) + 0.5 * b * Math.Sin(2.0 * (x-dx)); // x-dx was the previous x
                                              // f = (2.0 * y_before_last_update + p) / PI;
                                              // Re-evaluating: The R code uses 'y' calculated *inside* the loop *before* checking delx.
                                              // The 'y' used in the final 'f' calculation is the one from the last iteration where delx was calculated.
                                              // So, the C# code should calculate 'y' one last time after the loop *or* use the 'y' from the final successful iteration.
                                              // Let's recalculate y after loop based on final x, this seems more correct.
                f = (2.0 * y_final + p) / PI; //

            }
            // Ensure f is within [0, 1] bounds
            return Math.Max(0.0, Math.Min(1.0, f)); //
        }


        /// <summary>
        /// J1 function for SAIL calculations, handles singularity.
        /// Calculates (exp(-l*t) - exp(-k*t)) / (k - l).
        /// </summary>
        /// <param name="k">Numeric. Extinction coefficient or related parameter.</param>
        /// <param name="l">Numeric. Another extinction coefficient or related parameter.</param>
        /// <param name="t">Numeric. Leaf Area Index or path length.</param>
        /// <returns>Result of the J1 function.</returns>
        public static double Jfunc1(double k, double l, double t)
        {
            // J1 function with avoidance of singularity problem
            double del = (k - l) * t; //
            double Jout;

            if (Math.Abs(del) > 1e-3) //
            {
                // Avoid potential NaN if k or l are extremely large resulting in exp->0
                double exp_lt = Math.Exp(-l * t); //
                double exp_kt = Math.Exp(-k * t); //
                Jout = (exp_lt - exp_kt) / (k - l); //
            }
            else // Use Taylor expansion for small |del| to avoid k-l -> 0
            {
                double exp_kt = Math.Exp(-k * t); //
                                                  // The R expansion uses exp(-l*t) as well, let's derive it:
                                                  // Let f(x) = exp(-x*t). Taylor around x=k: exp(-l*t) approx exp(-k*t) - t*exp(-k*t)*(l-k) + ...
                                                  // J1 = (exp(-k*t) - t*exp(-k*t)*(l-k) + ... - exp(-k*t)) / (k-l)
                                                  //    = ( - t*exp(-k*t)*(l-k) + O((l-k)^2) ) / (k-l)
                                                  //    = t * exp(-k*t) + O(k-l)
                                                  // Let's check the R expansion: 0.5 * t * (exp(-k*t) + exp(-l*t)) * (1 - del*del/12)
                                                  // If del is small, l is close to k. exp(-l*t) is close to exp(-k*t).
                                                  // 0.5 * t * (exp(-k*t) + exp(-k*t)) * (1 - 0) = t * exp(-k*t). Matches the first term.
                                                  // Let's use the R version for consistency.
                double exp_lt = Math.Exp(-l * t); //
                Jout = 0.5 * t * (exp_kt + exp_lt) * (1.0 - del * del / 12.0); //

                // Alternative Taylor Expansion of J1 directly around l=k:
                // Let g(l) = (exp(-l*t) - exp(-k*t)) / (k - l). Use L'Hopital's rule as l->k.
                // d/dl (exp(-l*t) - exp(-k*t)) = -t * exp(-l*t)
                // d/dl (k - l) = -1
                // Limit as l->k is (-t * exp(-k*t)) / -1 = t * exp(-k*t). Matches first order.
                // Keep R's implementation.
            }
            return Jout; //
        }

        /// <summary>
        /// J2 function for SAIL calculations.
        /// Calculates (1 - exp(-(k+l)*t)) / (k + l).
        /// </summary>
        /// <param name="k">Numeric. Extinction coefficient or related parameter.</param>
        /// <param name="l">Numeric. Another extinction coefficient or related parameter.</param>
        /// <param name="t">Numeric. Leaf Area Index or path length.</param>
        /// <returns>Result of the J2 function.</returns>
        public static double Jfunc2(double k, double l, double t)
        {
            // J2 function
            double sum_kl = k + l; //
            double Jout;
            if (Math.Abs(sum_kl * t) < 1e-6) // Taylor expansion for exp(-x) approx 1 - x for small x=(k+l)t
            {
                // (1 - (1 - (k+l)t)) / (k+l) = (k+l)t / (k+l) = t
                Jout = t;
            }
            else if (Math.Abs(sum_kl) < 1e-9) // Avoid division by zero if k+l is zero
            {
                // If k+l is zero, the numerator is 1 - exp(0) = 0. Result is indeterminate 0/0.
                // Use limit via L'Hopital's rule (derivative w.r.t sum_kl):
                // d/ds (1 - exp(-s*t)) = t*exp(-s*t)
                // d/ds (s) = 1
                // Limit as s->0 is t*exp(0)/1 = t.
                Jout = t;
            }
            else
            {
                Jout = (1.0 - Math.Exp(-(sum_kl) * t)) / sum_kl; //
            }
            return Jout; //
        }


        /// <summary>
        /// J3 function for SAIL calculations. Identical to Jfunc2 in the provided R code.
        /// Calculates (1 - exp(-(k+l)*t)) / (k + l).
        /// </summary>
        /// <param name="k">Numeric. Extinction coefficient or related parameter.</param>
        /// <param name="l">Numeric. Another extinction coefficient or related parameter.</param>
        /// <param name="t">Numeric. Leaf Area Index or path length.</param>
        /// <returns>Result of the J3 function.</returns>
        public static double Jfunc3(double k, double l, double t)
        {
            // Functionally identical to Jfunc2 based on the R code provided
            return Jfunc2(k, l, t); //
        }


        /// <summary>
        /// J4 function for treating (near) conservative scattering in SAIL.
        /// Calculates (1 - exp(-m*t)) / (m * (1 + exp(-m*t))) or approximation for small m*t.
        /// </summary>
        /// <param name="m">Numeric. Parameter related to scattering properties.</param>
        /// <param name="t">Numeric. Leaf Area Index or path length.</param>
        /// <returns>Result of the J4 function.</returns>
        public static double Jfunc4(double m, double t)
        {
            double del = m * t; //
            double out_val;

            if (Math.Abs(del) > 1e-3) // Use direct formula
            {
                double exp_del = Math.Exp(-del); //
                double denom = m * (1.0 + exp_del); //
                if (Math.Abs(denom) < 1e-12)
                {
                    // Handle division by zero. Check limits.
                    // If m->0, del->0. Use Taylor expansion.
                    // If m is non-zero but 1+exp(-del) is zero, means exp(-del) = -1, impossible for real del.
                    // So only need to handle m->0, which is covered by the else block.
                    // If m is very large positive, exp(-del)->0, out -> 1 / (m * 1) -> 0
                    // If m is very large negative, exp(-del)->inf, out -> (-inf) / (m * inf). Indeterminate. Needs limit.
                    // Limit m-> -inf: let m = -x, x->inf. del = -xt.
                    // (1 - exp(xt)) / (-x * (1 + exp(xt)))
                    // As x->inf, exp(xt) dominates. -> -exp(xt) / (-x * exp(xt)) = 1/x = -1/m.
                    // Let's assume m won't be pathologically large negative.
                    // Fallback if denom is near zero unexpectedly:
                    out_val = 0; // Or NaN or throw.
                }
                else
                {
                    out_val = (1.0 - exp_del) / denom; //
                }
            }
            else // Use Taylor expansion for small |del|
            {
                // R formula: 0.5 * t * (1.0 - del * del / 12.0)
                // Let's verify: exp(-del) approx 1 - del + del^2/2 - del^3/6
                // Numerator: 1 - (1 - del + del^2/2) = del - del^2/2
                // Denominator: m * (1 + (1 - del + del^2/2)) = m * (2 - del + del^2/2)
                // Out approx (del - del^2/2) / (m * (2 - del)) = (mt - m^2t^2/2) / (m * (2 - mt))
                //       = (t - mt^2/2) / (2 - mt)
                //       = t/2 * (1 - mt^2/2) / (1 - mt/2)
                //       = t/2 * (1 - mt^2/2) * (1 + mt/2 + (mt/2)^2 + ...)
                //       = t/2 * (1 + mt/2 + m^2t^2/4 - mt^2/2 + ...)
                //       = t/2 * (1 + mt/2 - m^2t^2/4 + ...)
                //       = t/2 + m*t^2/4 - m^2*t^3/8
                // R formula: 0.5 * t * (1 - (mt)^2 / 12) = t/2 - m^2*t^3 / 24
                // The Taylor expansions don't match perfectly. The R formula is likely a known approximation for this specific function.
                // We will stick to the R formula.
                out_val = 0.5 * t * (1.0 - del * del / 12.0); //
            }
            return out_val; //
        }


        /// <summary>
        /// Compute volume scattering functions and interception coefficients
        /// for given solar zenith, viewing zenith, azimuth and leaf inclination angle.
        /// </summary>
        /// <param name="tts">Solar zenith angle (degrees).</param>
        /// <param name="tto">Viewing zenith angle (degrees).</param>
        /// <param name="psi">Relative azimuth angle (degrees).</param>
        /// <param name="ttl">Leaf inclination angle (degrees).</param>
        /// <returns>A VolscattResult struct containing chi_s, chi_o, frho, ftau.</returns>
        public static VolscattResult Volscatt(double tts, double tto, double psi, double ttl)
        {
            // ********************************************************************************
            // *	chi_s	= interception functions
            // *	chi_o	= interception functions
            // *	frho	= function to be multiplied by leaf reflectance rho
            // *	ftau	= functions to be multiplied by leaf transmittance tau
            // ********************************************************************************
            //	Wout Verhoef, april 2001, for CROMA

            double rd = DEGREES_TO_RADIANS; //
            double costs = Math.Cos(rd * tts); //
            double costo = Math.Cos(rd * tto); //
            double sints = Math.Sin(rd * tts); //
            double sinto = Math.Sin(rd * tto); //
            double cospsi = Math.Cos(rd * psi); //
            double psir = rd * psi; //
            double costl = Math.Cos(rd * ttl); //
            double sintl = Math.Sin(rd * ttl); //
            double cs = costl * costs; // cos(tl)*cos(ts)
            double co = costl * costo; // cos(tl)*cos(to)
            double ss = sintl * sints; // sin(tl)*sin(ts)
            double so = sintl * sinto; // sin(tl)*sin(to)

            // c ..............................................................................
            // c     betas -bts- and betao -bto- computation
            // c     Transition angles (beta) for solar (betas) and view (betao) directions
            // c     if thetav+thetal>pi/2, bottom side of the leaves is observed for leaf azimut
            // c     interval betao+phi<leaf azimut<2pi-betao+phi.
            // c     if thetav+thetal<pi/2, top side of the leaves is always observed, betao=pi
            // c     same consideration for solar direction to compute betas
            // c ..............................................................................

            double cosbts = 5.0; // sentinel value > 1
            if (Math.Abs(ss) > 1e-6) //
            {
                cosbts = -cs / ss; // = -cot(tl)*cot(ts)
            }

            double cosbto = 5.0; // sentinel value > 1
            if (Math.Abs(so) > 1e-6) //
            {
                cosbto = -co / so; // = -cot(tl)*cot(to)
            }

            double bts, ds;
            if (Math.Abs(cosbts) < 1.0) //
            {
                bts = Math.Acos(cosbts); // Transition angle beta_s
                ds = ss; //
            }
            else // Horizon case
            {
                // If cosbts >= 1 (i.e., tts + ttl <= 90 deg), sun always sees top face, bts = pi.
                // If cosbts <= -1 (i.e., sun below horizon for that leaf normal azimuth), bts = 0? (R code sets bts=pi, let's follow)
                bts = PI; //
                ds = cs; // cs = cos(tl)cos(ts) is projection factor?
            }
            // chi_s = (1/pi) * integral( G * domega ) projected onto horizontal plane?
            // G = projection of leaf area onto plane normal to sun direction.
            // Verhoef's formula: chi_s = projection factor G averaged over leaf azimuth
            double chi_s = (2.0 / PI) * ((bts - PI * 0.5) * cs + Math.Sin(bts) * ss); //

            double bto, doo;
            if (Math.Abs(cosbto) < 1.0) //
            {
                bto = Math.Acos(cosbto); // Transition angle beta_o
                doo = so; //
            }
            // R code differs slightly here from comments: uses tto < 90 condition
            // else if (tto < 90) // Original R logic line
            // Let's analyze the original FORTRAN/SAIL logic if possible.
            // If |cosbto| >= 1, it means tto + ttl <= 90 (observer sees top) or tto + ttl >= 270 (impossible)
            // or observer is at nadir/zenith or leaf is horizontal/vertical.
            // If tto + ttl <= 90 (cosbto >= 1), observer always sees top face, bto = pi.
            // If tto is near 90 (grazing view), cosbto is near -cot(tl)*cot(to) -> large negative if to->90.
            // Let's follow R code's logic verbatim:
            else if (tto < 90.0) // Observer above horizon
            {
                bto = PI; //
                doo = co; // co = cos(tl)cos(to)
            }
            else // Observer is exactly at horizon (tto = 90), or below (tto > 90 - not typical)
            {
                bto = 0; // R sets bto = 0
                doo = -co; // R sets doo = -co
            }
            // Verhoef's formula: chi_o = projection factor G averaged over leaf azimuth for observer
            double chi_o = (2.0 / PI) * ((bto - PI * 0.5) * co + Math.Sin(bto) * so); //

            // Ensure non-negative projection factors (can happen due to numerical precision)
            if (chi_s < 0) chi_s = 0; //
            if (chi_o < 0) chi_o = 0; //


            // c ...........................................................................
            // c   Computation of auxiliary azimut angles bt1, bt2, bt3 used
            // c   for the computation of the bidirectional scattering coefficient w
            // c ...........................................................................

            double btran1 = Math.Abs(bts - bto); //
            double btran2 = PI - Math.Abs(bts + bto - PI); //

            double bt1, bt2, bt3;

            if (psir <= btran1) //
            {
                bt1 = psir; //
                bt2 = btran1; //
                bt3 = btran2; //
            }
            else
            {
                bt1 = btran1; //
                if (psir <= btran2) //
                {
                    bt2 = psir; //
                    bt3 = btran2; //
                }
                else
                {
                    bt2 = btran2; //
                    bt3 = psir; //
                }
            }

            double t1 = 2.0 * cs * co + ss * so * cospsi; // Related to phase function for specular
            double t2 = 0; //
            if (bt2 > 1e-9) // Avoid multiplying by sin(0)
            {
                // Note: R uses ds*doo. Let's re-evaluate ds, doo based on conditions.
                // ds = (abs(cosbts)<1) ? ss : cs
                // doo = (abs(cosbto)<1) ? so : (tto<90 ? co : -co)
                // This seems complex. Let's trust the R code's ds and doo variables passed down.
                // Check Verhoef's papers for the definitive formula for 't2'.
                // From Verhoef (1984), Eq A14, seems related to integration limits.
                // Let's assume R code 'ds' and 'doo' are correct intermediate variables.
                t2 = Math.Sin(bt2) * (2.0 * ds * doo + ss * so * Math.Cos(bt1) * Math.Cos(bt3)); //
            }


            double denom = 2.0 * PI * PI; //
            double frho = ((PI - bt2) * t1 + t2) / denom; //
            double ftau = (-bt2 * t1 + t2) / denom; // Should be related to frho(pi - psi)?

            // Ensure non-negativity (can occur due to numerical precision near geometry limits)
            if (frho < 0) frho = 0; //
            if (ftau < 0) ftau = 0; //

            return new VolscattResult { Chi_s = chi_s, Chi_o = chi_o, Frho = frho, Ftau = ftau }; //
        }


        /// <summary>
        /// Checks if spectral sampling is identical between PROSPECT, SOIL, and ATM data.
        /// Throws an exception if sampling does not match.
        /// </summary>
        /// <param name="specPROSPECT">PROSPECT spectral properties object.</param>
        /// <param name="specSOIL">Soil spectral properties object.</param>
        /// <param name="specATM">Atmosphere spectral properties object.</param>
        public static void check_SpectralSampling(SpectralProperties specPROSPECT, SoilProperties specSOIL, SpecAtmSensor specATM)
        {
            double[] l1 = specPROSPECT?.Lambda; //
            double[] l2 = specSOIL?.Wavelength; //
                                            // Assuming SpecAtmSensor is the replacement for SpecATM list from R
            double[] l3 = specATM?.Wavelength; //

            // Basic null checks
            if (l1 == null || l2 == null || l3 == null)
            {
                throw new ArgumentNullException("One or more spectral data inputs are null.");
            }

            int len1 = l1.Length;
            int len2 = l2.Length;
            int len3 = l3.Length;

            string errorMessage = "Please ensure matching spectral sampling (wavelengths and number of bands) between SpecPROSPECT, SpecSOIL and SpecATM"; //

            if (len1 != len2 || len1 != len3) //
            {
                Console.WriteLine(errorMessage); //
                throw new ArgumentException(errorMessage); //
            }

            // Check if wavelengths match exactly
            // Use SequenceEqual for efficient comparison
            bool l1_eq_l2 = l1.SequenceEqual(l2); //
            bool l1_eq_l3 = l1.SequenceEqual(l3); //

            if (!l1_eq_l2 || !l1_eq_l3) //
            {
                // More detailed check for debugging (optional)
                /*
                for(int i=0; i<len1; i++) {
                    if (Math.Abs(l1[i] - l2[i]) > 1e-6 || Math.Abs(l1[i] - l3[i]) > 1e-6) {
                       Console.WriteLine($"Mismatch at index {i}: L1={l1[i]}, L2={l2[i]}, L3={l3[i]}");
                       break;
                    }
                }
                */
                Console.WriteLine(errorMessage); //
                throw new ArgumentException(errorMessage); //
            }

            // If we reach here, sampling is consistent. R function returns invisible(), C# returns void.
        }


        /// <summary>
        /// Checks if brown leaf optical properties (BrownLOP) are correctly defined and compatible.
        /// Throws exceptions if issues are found. Writes messages to console.
        /// </summary>
        /// <param name="brownLOP">LeafOptics object representing brown leaf properties. Can be null if not provided.</param>
        /// <param name="lambda">The reference wavelength array (e.g., from Spec_Sensor) that BrownLOP should match.</param>
        /// <param name="inputProspectList">A list of ProspectInput objects. Used to check if multiple inputs are provided when BrownLOP is used.</param>
        public static void check_BrownLOP(LeafOptics brownLOP, double[] lambda, List<ProspectInput> inputProspectList)
        {
            // Case 1: BrownLOP is provided
            if (brownLOP != null) //
            {
                // Check required fields (assuming LeafOptics class has these properties)
                if (brownLOP.Wavelength == null || brownLOP.Reflectance == null || brownLOP.Transmittance == null) //
                {
                    string msg = "BrownLOP must include non-null 'Lambda', 'Reflectance' and 'Transmittance' arrays."; //
                    Console.WriteLine(msg); //
                    throw new ArgumentException(msg); //
                }

                // Check spectral domain matching
                if (lambda == null)
                {
                    throw new ArgumentNullException(nameof(lambda), "Reference lambda array cannot be null when checking BrownLOP.");
                }

                if (brownLOP.Wavelength.Length != lambda.Length || !brownLOP.Wavelength.SequenceEqual(lambda)) //
                {
                    string msg = "Spectral domain mismatch: BrownLOP wavelengths do not match the reference wavelengths (e.g., Spec_Sensor)."; //
                    Console.WriteLine(msg); //
                    throw new ArgumentException(msg); //
                }

                // Check if multiple PROSPECT inputs were given alongside fixed BrownLOP
                if (inputProspectList != null && inputProspectList.Count > 1) //
                {
                    // This is just a message in R, not an error
                    Console.WriteLine("Warning: BrownLOP defined along with multiple PROSPECT input parameter sets."); //
                    Console.WriteLine("Only the first PROSPECT input set will be used to simulate green vegetation when BrownLOP is provided."); //
                }
            }
            // Case 2: BrownLOP is null - No checks needed here, handled in adjust_PROSPECT_2_SAIL.

            // R function returns invisible(), C# returns void.
        }


        /// <summary>
        /// Prepares leaf optical properties (GreenLOP, BrownLOP) for SAIL by running PROSPECT.
        /// Handles logic for 4SAIL vs 4SAIL2 requirements based on inputs.
        /// </summary>
        /// <param name="sailVersion">String, either "4SAIL" or "4SAIL2".</param>
        /// <param name="specSensor">Spectral properties/constants needed for PROSPECT.</param>
        /// <param name="inputProspectList">A list containing one (for 4SAIL, or 4SAIL2 with BrownLOP/fraction_brown=0) or two (for 4SAIL2 green/brown) PROSPECT input parameter sets.</param>
        /// <param name="fraction_brown">Fraction of brown vegetation (0-1), used only for 4SAIL2 if BrownLOP is null and two Prospect inputs aren't given.</param>
        /// <param name="brownLOP">Optional pre-calculated brown leaf optical properties. If provided, the second item in inputProspectList is ignored.</param>
        /// <returns>An AdjustedProspectResult struct containing GreenLOP and potentially BrownLOP.</returns>
        /// <exception cref="ArgumentException">Thrown if inputs are inconsistent for the selected SAIL version.</exception>
        /// <exception cref="NotImplementedException">Thrown if the PROSPECT simulation call is not implemented.</exception>
        public static AdjustedProspectResult adjust_PROSPECT_2_SAIL(
            string sailVersion,
            SpectralProperties specSensor,
            List<ProspectInput> inputProspectList,
            double fraction_brown,
            LeafOptics brownLOP = null) //
        {
            if (inputProspectList == null || inputProspectList.Count == 0) //
            {
                throw new ArgumentException("Input_PROSPECT list cannot be null or empty.");
            }
            if (specSensor == null)
            {
                throw new ArgumentNullException(nameof(specSensor), "Spec_Sensor cannot be null.");
            }

            // --- Simulate Green Leaf Optical Properties (Always needed) ---
            ProspectInput greenProspectInput = inputProspectList[0]; //
                                                                     // Placeholder for the actual PROSPECT call
                                                                     // You need to implement or call your C# version of the PROSPECT model here
            LeafOptics greenLOP = ProspectCore.Run(specSensor, N: greenProspectInput.N, CAB: greenProspectInput.CHL, CAR: greenProspectInput.CAR,
                EWT: greenProspectInput.EWT, LMA: greenProspectInput.LMA, ANT: greenProspectInput.ANT, BROWN:greenProspectInput.BROWN, 
                PROT: greenProspectInput.PROT, Alpha: greenProspectInput.Alpha); 
            if (greenLOP == null)
            {
                throw new InvalidOperationException("PROSPECT simulation failed to return Green LOP.");
            }

            LeafOptics finalBrownLOP = null; // Initialize brown LOP


            // --- Handle SAIL Version Specific Logic ---
            if (sailVersion == "4SAIL") //
            {
                // 4SAIL only uses GreenLOP. BrownLOP remains null.
                finalBrownLOP = null;
            }
            else if (sailVersion == "4SAIL2") //
            {
                // 4SAIL2 requires both Green and Brown LOP.
                // Priority:
                // 1. Use provided brownLOP if not null.
                // 2. If fraction_brown is 0, use GreenLOP for BrownLOP.
                // 3. If fraction_brown > 0 and brownLOP is null, expect a second ProspectInput.
                // 4. If conditions for 2 or 3 aren't met, it's an error (or fallback to 4SAIL behavior).

                if (brownLOP != null) //
                {
                    // Case 1: External BrownLOP provided. Check its validity.
                    check_BrownLOP(brownLOP, specSensor.Lambda, inputProspectList); //
                    finalBrownLOP = brownLOP; //
                }
                else // brownLOP is null
                {
                    if (Math.Abs(fraction_brown) < 1e-9) //
                    {
                        // Case 2: No brown fraction, use green optics for the brown component.
                        finalBrownLOP = greenLOP; //
                    }
                    else //
                    {
                        // Case 3: Need brown optics, expect second PROSPECT input.
                        if (inputProspectList.Count < 2) //
                        {
                            // R code prints a message and switches to 4SAIL.
                            // In C#, throwing an exception might be clearer, or mimic R.
                            Console.WriteLine("Warning: 4SAIL2 needs two sets of optical properties (or BrownLOP, or fraction_brown=0)."); //
                            Console.WriteLine("Only one PROSPECT input set defined. Brown LOP cannot be generated."); //
                                                                                                                      // Option 1: Throw error
                                                                                                                      // throw new ArgumentException("Insufficient inputs for 4SAIL2 brown leaf simulation.");
                                                                                                                      // Option 2: Mimic R behaviour (effectively reverts to 4SAIL logic where brown is unused)
                            Console.WriteLine("Proceeding as if 4SAIL was selected (BrownLOP will be effectively null)."); //
                            finalBrownLOP = null; // Ensure it's null if SAIL model logic relies on it.
                                                  // Note: The calling code would ideally handle the switch to 4SAIL based on this outcome.
                        }
                        else
                        {
                            // Simulate Brown LOP using the second input set
                            ProspectInput brownProspectInput = inputProspectList[1]; //
                                                                                     // Placeholder for the actual PROSPECT call
                            finalBrownLOP = ProspectCore.Run(specSensor, N: brownProspectInput.N, CAB: brownProspectInput.CHL, CAR: brownProspectInput.CAR,
                                EWT: brownProspectInput.EWT, LMA: brownProspectInput.LMA, ANT: brownProspectInput.ANT, BROWN: brownProspectInput.BROWN, 
                                PROT: brownProspectInput.PROT, Alpha: brownProspectInput.Alpha); //
                            if (finalBrownLOP == null)
                            {
                                throw new InvalidOperationException("PROSPECT simulation failed to return Brown LOP.");
                            }
                        }
                    }
                }
            }
            else
            {
                throw new ArgumentException($"Unsupported SAIL version: {sailVersion}. Use '4SAIL' or '4SAIL2'.");
            }

            return new AdjustedProspectResult { GreenLOP = greenLOP, BrownLOP = finalBrownLOP }; //
        }
    }
}

using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Linq;
using static Models.PROSAIL.SAIL.SailUtilities;


namespace Models.PROSAIL.Sail
{
    /// <summary>
    /// Implements the core 4SAIL and 4SAIL2 canopy reflectance models.
    /// Requires an instance of SailUtils for helper functions.
    /// </summary>
    public static class SailCore
    {
        // private readonly SailUtils _sailUtils;
        private const double PI = Math.PI;
        private const double DEGREES_TO_RADIANS = PI / 180.0;

        /// <summary>
        /// Performs 4SAIL simulation based on leaf/canopy properties and geometry.
        /// </summary>
        /// <param name="leafOptics">Leaf optical properties (reflectance, transmittance).</param>
        /// <param name="typeLidf">Type of leaf inclination distribution function (1 for Verhoef, 2 for Campbell).</param>
        /// <param name="lidfA">LIDF parameter a (average leaf slope or angle).</param>
        /// <param name="lidfB">LIDF parameter b (bimodality, nullable, used only if typeLidf=1).</param>
        /// <param name="lai">Leaf Area Index.</param>
        /// <param name="q">Hot Spot parameter.</param>
        /// <param name="tts">Sun zenith angle (degrees).</param>
        /// <param name="tto">Observer zenith angle (degrees).</param>
        /// <param name="psi">Relative azimuth angle between sun and observer (degrees).</param>
        /// <param name="soilOptics">Soil reflectance properties.</param>
        /// <returns>A SailResult object containing calculated reflectance factors, fCover, and absorptance.</returns>
        /// <exception cref="ArgumentNullException">Thrown if leafOptics or soilProperties are null.</exception>
        /// <exception cref="ArgumentException">Thrown if input array lengths mismatch.</exception>
        public static CanopyOptics FourSAIL(LeafOptics leafOptics, int typeLidf, double lidfA, double? lidfB,
                                 double lai, double q, double tts, double tto, double psi,
                                 SoilOptics soilOptics)
        {
            if (!leafOptics.HasValue)
            {
                throw new ArgumentNullException(nameof(leafOptics));
            }
            if (!soilOptics.HasValue)
            {
                throw new ArgumentNullException(nameof(soilOptics));
            }
            if (leafOptics.Reflectance == null || leafOptics.Transmittance == null || soilOptics.Reflectance == null) 
            {
                throw new ArgumentException("Reflectance/Transmittance arrays in leafOptics and soilOptics cannot be null.");
            }

            if (leafOptics.Reflectance.Length != leafOptics.Transmittance.Length || leafOptics.Reflectance.Length != soilOptics.Reflectance.Count) 
            {
                throw new ArgumentException("Input reflectance/transmittance arrays in leafOptics and soilOptics must have the same length.");
            }                

            // ########################################################################### #
            // #	                 LEAF OPTICAL PROPERTIES	                           #
            // ########################################################################### #
            double[] rho = leafOptics.Reflectance; // leaf reflectance
            double[] tau = leafOptics.Transmittance; // leaf transmittance
            double[] rsoil = soilOptics.Reflectance.ToArray(); // soil reflectance
            int nLambda = rho.Length; // Number of spectral bands

            // Pre-allocate result arrays
            double[] rdot = new double[nLambda];
            double[] rsot = new double[nLambda];
            double[] rddt = new double[nLambda];
            double[] rsdt = new double[nLambda];
            double[] fCover = new double[nLambda];
            double[] abs_dir = new double[nLambda];
            double[] abs_hem = new double[nLambda];
            double[] rsdstar = new double[nLambda];
            double[] rddstar = new double[nLambda];

            // Handle LAI = 0 case separately for efficiency
            if (Math.Abs(lai) < 1e-9 || lai < 0) // Check for LAI=0 or negative LAI
            {
                // If LAI is 0 or invalid, reflectance is just soil reflectance
                // Transmittances are 1, absorptances are 0
                var rsoilVector = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.DenseOfArray(rsoil);
                var zeroVector = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(nLambda, 0.0);

                // Vectorized assignments
                rdot = rsoilVector.ToArray();
                rsot = rsoilVector.ToArray();
                rddt = rsoilVector.ToArray();
                rsdt = rsoilVector.ToArray();
                rsdstar = rsoilVector.ToArray();
                rddstar = rsoilVector.ToArray();
                fCover = zeroVector.ToArray();
                abs_dir = zeroVector.ToArray();
                abs_hem = zeroVector.ToArray();
                
                if (lai < 0)
                {
                    Console.WriteLine("Warning: LAI is negative. Results computed assuming LAI = 0.");
                }

                return new CanopyOptics
                {
                    Rdot = rdot,
                    Rsot = rsot,
                    Rddt = rddt,
                    Rsdt = rsdt,
                    FCover = fCover,
                    Abs_dir = abs_dir,
                    Abs_hem = abs_hem,
                    Rsdstar = rsdstar,
                    Rddstar = rddstar,
                    Wavelength = leafOptics.Wavelength // Pass through wavelengths
                };
            }

            //	Geometric quantities
            double rd = DEGREES_TO_RADIANS;
            double ttsRad = tts * rd;
            double ttoRad = tto * rd;
            double psiRad = psi * rd;
            double cts = Math.Cos(ttsRad); // cos(tts)
            double cto = Math.Cos(ttoRad); // cos(tto)
            double ctscto = cts * cto;      // cos(tts)*cos(tto)
            double tants = Math.Tan(ttsRad); // tan(tts)
            double tanto = Math.Tan(ttoRad); // tan(tto)
            double cospsi = Math.Cos(psiRad); // cos(psi)                                            
            double dso = Math.Sqrt(tants * tants + tanto * tanto - 2.0 * tants * tanto * cospsi);  // Geometric distance factor between sun and observer directions

            //	Generate leaf angle distribution
            FoliarDistributionResult foliarDistrib;
            if (typeLidf == 1)
            {
                if (!lidfB.HasValue) throw new ArgumentException("LIDF parameter b (LIDFb) is required when TypeLidf is 1.");
                foliarDistrib = Dladgen(lidfA, lidfB.Value); // Verhoef's LIDF
            }
            else if (typeLidf == 2)
            {
                foliarDistrib = Campbell(lidfA); // Campbell's LIDF
            }
            else
            {
                throw new ArgumentException("Invalid TypeLidf specified. Use 1 or 2.");
            }
            double[] lidf = foliarDistrib.Lidf;   // LIDF values
            double[] litab = foliarDistrib.Litab; // Corresponding leaf angles (degrees)


            //	Calculate geometric factors associated with extinction and scattering
            //	Initialise sums
            double ks = 0; // Extinction coefficient for solar flux (view-angle independent)
            double ko = 0; // Extinction coefficient for observed flux (solar-angle independent)
            double bf = 0; // Angle factor (related to G-function)
            double sob = 0; // Bidirectional scattering factor (backward)
            double sof = 0; // Bidirectional scattering factor (forward)

            //	Weighted sums over LIDF
            int na = litab.Length; // Number of leaf angle classes
            for (int i = 0; i < na; i++)
            {
                double ttl = litab[i];      // leaf inclination discrete values (degrees)
                double ttlRad = ttl * rd;
                double ctl = Math.Cos(ttlRad); // cos(ttl)
                // SAIL volume scattering phase function gives interception and portions to be multiplied by rho and tau
                VolscattResult resVolscatt = Volscatt(tts, tto, psi, ttl); // Uses angles in degrees
                double chi_s = resVolscatt.Chi_s; // Solar interception factor G(tts, ttl)
                double chi_o = resVolscatt.Chi_o; // Observer interception factor G(tto, ttl)
                double frho = resVolscatt.Frho;   // Phase function component (rho)
                double ftau = resVolscatt.Ftau;   // Phase function component (tau)

                // ***********************************************************************************************************************
                //* SUITS SYSTEM COEFFICIENTS (angle dependent part)
                //*
                //* ks : Extinction coefficient for direct solar flux = Sum( G(tts,ttl) * LIDF(ttl) ) / cos(tts)
                //* ko : Extinction coefficient for direct observed flux = Sum( G(tto,ttl) * LIDF(ttl) ) / cos(tto)
                //* bf : Bi-directional angle factor = Sum( cos(ttl)^2 * LIDF(ttl) )
                //* sob: Bi-directional scattering coefficient (rho part) = Sum ( frho(tts,tto,psi,ttl) * pi / (cts*cto) * LIDF(ttl) )
                //* sof: Bi-directional scattering coefficient (tau part) = Sum ( ftau(tts,tto,psi,ttl) * pi / (cts*cto) * LIDF(ttl) )
                // ***********************************************************************************************************************

                //	Extinction coefficients (contributions)
                double ksli = chi_s / cts; // G_sun / cos(tts)
                double koli = chi_o / cto; // G_obs / cos(tto)

                //	Area scattering coefficient fractions (contributions)
                double sobli = frho * PI / ctscto;
                double sofli = ftau * PI / ctscto;
                double bfli = ctl * ctl; // cos(ttl)^2

                // Weighted sum using LIDF frequency for this angle class
                double lidf_i = lidf[i];
                ks += ksli * lidf_i;
                ko += koli * lidf_i;
                bf += bfli * lidf_i;
                sob += sobli * lidf_i;
                sof += sofli * lidf_i;
            }

            //	Geometric factors (combining extinction and angle factor bf)
            // These factors are independent of wavelength as they only depend on geometry and LIDF
            double sdb = 0.5 * (ks + bf);
            double sdf = 0.5 * (ks - bf);
            double dob = 0.5 * (ko + bf);
            double dof = 0.5 * (ko - bf);
            double ddb = 0.5 * (1.0 + bf); // Diffuse geometry factor (backward)
            double ddf = 0.5 * (1.0 - bf); // Diffuse geometry factor (forward)

            // Direct transmittances through the canopy layer
            double tss = Math.Exp(-ks * lai); // exp(-ks*LAI), Directional transmittance solar (k, lai)
            double too = Math.Exp(-ko * lai); // exp(-ko*LAI), Directional transmittance observer (k, lai)

            // Treatment of the hotspot-effect
            double alf = 1e6; // Default large value (no hotspot)
            if (q > 0) // q = hotspot parameter
            {
                double ks_plus_ko = ks + ko;
                if (Math.Abs(ks_plus_ko) < 1e-9) ks_plus_ko = 1e-9; // Avoid division by zero
                alf = (dso / q) * 2.0 / ks_plus_ko; // Hotspot intensity parameter
            }
            // Limit alpha
            if (alf > 200.0) alf = 200.0;

            double tsstoo; // Joint probability gap fraction (solar and view)
            double sumint; // Integral for single scattering hotspot correction

            if (Math.Abs(alf) < 1e-9) // If alpha is zero (or very close), pure hotspot and no shadow
            {
                tsstoo = tss; // Joint = Solar transmittance if paths coincide
                sumint = (Math.Abs(ks * lai) < 1e-9) ? 1.0 : (1.0 - tss) / (ks * lai); // Integral limit
            }
            else // Outside the hotspot, then calculate overlap integral
            {
                double fhot = lai * Math.Sqrt(ko * ks); // Hotspot function amplitude
                //	Integrate by exponential Simpson method in 20 steps, the steps are arranged according to equal partitioning of the slope of the joint probability function
                double x1 = 0, y1 = 0, sumint_acc = 0;
                double f1 = 1.0; // exp(y1) where y1=0
                double fint = (1.0 - Math.Exp(-alf)) * 0.05; // Step size in probability space
                int nsteps = 20;

                for (int j = 1; j <= nsteps; j++)
                {
                    double x2;
                    if (j < nsteps)
                    {
                        // Inverse transformation to find integration point x2 in geometric space
                        double prob_target = j * fint;
                        if (prob_target >= 1.0) x2 = 1.0; // Clip if needed due to precision
                        else x2 = -Math.Log(1.0 - prob_target) / alf;
                    }
                    else
                    {
                        x2 = 1.0; // Final step goes to full path length
                    }

                    // Calculate exponent for joint probability at x2
                    double y2 = -(ko + ks) * lai * x2 + fhot * (1.0 - Math.Exp(-alf * x2)) / alf;
                    double f2 = Math.Exp(y2); // Joint probability P(x2)

                    // Simpson's rule component (trapezoidal approx here, matches R code)
                    // Integral of P(x) dx from x1 to x2
                    if (Math.Abs(y2 - y1) > 1e-9) // Avoid division by zero if y values are the same
                    {
                        sumint_acc += (f2 - f1) * (x2 - x1) / (y2 - y1); // R code's formula
                    }
                    else
                    { // If y values are same, use simple average probability * dx
                        sumint_acc += 0.5 * (f1 + f2) * (x2 - x1);
                    }

                    // Update for next step
                    x1 = x2;
                    y1 = y2;
                    f1 = f2;
                }
                tsstoo = f1; // Final joint probability exp(-(ko+ks)lai + correction)
                sumint = sumint_acc; // The accumulated integral result
            }
            //	End of hotspot calculation

            // --- Wavelength-dependent calculations ---
            // Initialize wavelength-dependent SAIL coefficients
            double[] sigb = new double[nLambda]; // Diffuse backscattering coeff [rho, tau]
            double[] sigf = new double[nLambda]; // Diffuse forward scattering coeff [rho, tau]
            double[] att = new double[nLambda];  // Attenuation coefficient [sigf]
            double[] m = new double[nLambda];    // SAIL model exponent coefficient [att, sigb]
            double[] sb = new double[nLambda];   // Solar backscattering coeff [rho, tau]
            double[] sf = new double[nLambda];   // Solar forward scattering coeff [rho, tau]
            double[] vb = new double[nLambda];   // View backscattering coeff [rho, tau]
            double[] vf = new double[nLambda];   // View forward scattering coeff [rho, tau]
            double[] w = new double[nLambda];    // Bidirectional scattering coeff [rho, tau]

            double[] rdd = new double[nLambda];  // Canopy bi-hemispherical reflectance (no soil)
            double[] tdd = new double[nLambda];  // Canopy bi-hemispherical transmittance (no soil)
            double[] rsd = new double[nLambda];  // Canopy directional-hemispherical reflectance (no soil)
            double[] tsd = new double[nLambda];  // Canopy directional-hemispherical transmittance (no soil)
            double[] rdo = new double[nLambda];  // Canopy hemispherical-directional reflectance (no soil)
            double[] tdo = new double[nLambda];  // Canopy hemispherical-directional transmittance (no soil)
            double[] rso = new double[nLambda];  // Canopy bidirectional reflectance factor (no soil)
            double[] rsos = new double[nLambda]; // Single scattering contribution to rso
            double[] rsod = new double[nLambda]; // Multiple scattering contribution to rso
            double[] rsost = new double[nLambda];// Single scattering + soil interaction
            double[] rsodt = new double[nLambda];// Multiple scattering + soil interaction

            for (int i = 0; i < nLambda; i++)
            {
                //	Here rho and tau come in (element-wise / per wavelength)
                sigb[i] = ddb * rho[i] + ddf * tau[i]; // Diffuse backscattering
                sigf[i] = ddf * rho[i] + ddb * tau[i]; // Diffuse forward scattering
                att[i] = 1.0 - sigf[i];                // Attenuation = 1 - forward scattering
                double m2 = (att[i] + sigb[i]) * (att[i] - sigb[i]); // Intermediate calc for m
                m[i] = (m2 > 0) ? Math.Sqrt(m2) : 0.0; // Ensure non-negative argument for sqrt

                sb[i] = sdb * rho[i] + sdf * tau[i]; // Solar backscattering coeff
                sf[i] = sdf * rho[i] + sdb * tau[i]; // Solar forward scattering coeff
                vb[i] = dob * rho[i] + dof * tau[i]; // View backscattering coeff
                vf[i] = dof * rho[i] + dob * tau[i]; // View forward scattering coeff
                w[i] = sob * rho[i] + sof * tau[i];  // Bidirectional scattering coeff

                //	Here the LAI comes in (via exponentials and J functions)
                double mi = m[i]; // Use wavelength specific m
                double atti = att[i];
                double sigbi = sigb[i];
                double sfi = sf[i];
                double sbi = sb[i];
                double vfi = vf[i];
                double vbi = vb[i];

                double e1 = Math.Exp(-mi * lai); // exp(-m*LAI)
                double e2 = e1 * e1;           // exp(-2*m*LAI)
                double rinf = (Math.Abs(sigbi) > 1e-9) ? (atti - mi) / sigbi : (atti > mi ? double.PositiveInfinity : double.NegativeInfinity); // Reflectance of infinitely thick canopy
                // Handle potential instability if sigb is near zero (conservative scattering)
                if (Math.Abs(sigbi) < 1e-9) rinf = 1.0; // Approximate for conservative scattering

                double rinf2 = rinf * rinf; // rinf^2
                double re = rinf * e1;       // rinf * exp(-m*LAI)
                double denom = 1.0 - rinf2 * e2; // Denominator 1 - rinf^2 * exp(-2*m*LAI)
                if (Math.Abs(denom) < 1e-12) denom = 1e-12; // Avoid division by zero

                // Calculate J functions (using helper methods from SailUtils)
                double J1ks_val = Jfunc1(ks, mi, lai); // J1(ks, m, lai)
                double J2ks_val = Jfunc2(ks, mi, lai); // J2(ks, m, lai)
                double J1ko_val = Jfunc1(ko, mi, lai); // J1(ko, m, lai)
                double J2ko_val = Jfunc2(ko, mi, lai); // J2(ko, m, lai)

                // Calculate intermediate variables P, Q for solar and view directions
                double Ps = (sfi + sbi * rinf) * J1ks_val;
                double Qs = (sfi * rinf + sbi) * J2ks_val;
                double Pv = (vfi + vbi * rinf) * J1ko_val;
                double Qv = (vfi * rinf + vbi) * J2ko_val;

                // Calculate canopy-only reflectance/transmittance factors (no soil)
                rdd[i] = rinf * (1.0 - e2) / denom;   // Bi-hemispherical Reflectance
                tdd[i] = (1.0 - rinf2) * e1 / denom;  // Bi-hemispherical Transmittance
                rsd[i] = (Qs - re * Ps) / denom;      // Directional-hemispherical Reflectance (solar)
                tsd[i] = (Ps - re * Qs) / denom;      // Directional-hemispherical Transmittance (solar)
                rdo[i] = (Qv - re * Pv) / denom;      // Hemispherical-directional Reflectance (view)
                tdo[i] = (Pv - re * Qv) / denom;      // Hemispherical-directional Transmittance (view)

                // Calculate multiple scattering component (rsod)
                double z = Jfunc3(ks, ko, lai); // J3(ks, ko, lai)
                double g1_denom = ko + mi;
                double g2_denom = ks + mi;
                if (Math.Abs(g1_denom) < 1e-12) g1_denom = 1e-12; // Avoid division by zero
                if (Math.Abs(g2_denom) < 1e-12) g2_denom = 1e-12; // Avoid division by zero

                double g1 = (z - J1ks_val * too) / g1_denom;
                double g2 = (z - J1ko_val * tss) / g2_denom;

                double Tv1 = (vfi * rinf + vbi) * g1;
                double Tv2 = (vfi + vbi * rinf) * g2;
                double T1 = Tv1 * (sfi + sbi * rinf);
                double T2 = Tv2 * (sfi * rinf + sbi);
                double T3 = (rdo[i] * Qs + tdo[i] * Ps) * rinf;

                //	Multiple scattering contribution to bidirectional canopy reflectance
                double rsod_denom = (1.0 - rinf2);
                if (Math.Abs(rsod_denom) < 1e-12) rsod_denom = 1e-12; // Avoid division by zero
                rsod[i] = (T1 + T2 - T3) / rsod_denom; //                

                //	Bidirectional reflectance calculations
                //	Single scattering contribution (including hotspot)
                rsos[i] = w[i] * lai * sumint; // w * LAI * Integral(P_gap(x)) dx

                //	Total canopy contribution (no soil)
                rso[i] = rsos[i] + rsod[i]; // Single + Multiple scattering

                //	Interaction with the soil
                double rsoil_i = rsoil[i]; // Soil reflectance for this wavelength
                double dn = 1.0 - rsoil_i * rdd[i]; // Denominator for soil interaction: 1 - Rsoil*Rdd_canopy
                if (Math.Abs(dn) < 1e-12) dn = 1e-12; // Avoid division by zero

                // rddt: bi-hemispherical reflectance factor (Canopy + Soil)
                rddt[i] = rdd[i] + tdd[i] * rsoil_i * tdd[i] / dn;
                // rsdt: directional-hemispherical reflectance factor for solar incident flux (Canopy + Soil)
                rsdt[i] = rsd[i] + (tsd[i] + tss) * rsoil_i * tdd[i] / dn;
                // rdot: hemispherical-directional reflectance factor in viewing direction (Canopy + Soil)
                rdot[i] = rdo[i] + tdd[i] * rsoil_i * (tdo[i] + too) / dn;

                // rsot: bi-directional reflectance factor (Canopy + Soil)
                // Multiple scattering part + soil interaction
                rsodt[i] = rsod[i] + ((tss + tsd[i]) * tdo[i] + (tsd[i] + tss * rsoil_i * rdd[i]) * too) * rsoil_i / dn;
                // Single scattering part + soil interaction (hotspot included in tsstoo)
                rsost[i] = rsos[i] + tsstoo * rsoil_i;
                // Total bi-directional reflectance factor
                rsot[i] = rsost[i] + rsodt[i];

                // compute directional and hemispherical absorbances (Canopy + Soil system)
                // Fraction absorbed by (Canopy+Soil) = 1 - Total Reflected - Total Transmitted from bottom
                // Transmittance from bottom of system = T_direct_through_soil + T_diffuse_through_soil
                // T_direct_through_soil = Tss_canopy * (1 - Rsoil)
                // T_diffuse_through_soil = Tdd_canopy * (1 - Rsoil) * ??? 
                // Let's use the formula from R code based on energy balance: 1 - R - T
                // T_system = T_direct_through_soil + T_diffuse_reflected_by_soil_transmitted_up_then_down...
                // From R code: T_direct = (1-rsoil)*tss + (1-rsoil)*((tss*rsoil*rdd)+tsd)/dn * Tdd_soil_ ...
                abs_dir[i] = 1.0 - rsdt[i] - ((1.0 - rsoil_i) * tss) - (1.0 - rsoil_i) * ((tss * rsoil_i * rdd[i]) + tsd[i]) / dn; // Absorptance for direct solar flux (System)
                abs_hem[i] = 1.0 - rddt[i] - ((1.0 - rsoil_i) * tdd[i]) - (1.0 - rsoil_i) * (tdd[i] * rdd[i] * rsoil_i) / dn; // Absorptance for hemispherical diffuse flux (System)

                // compute Albedo components (from J. Gomez Dans cited in R code)
                // These represent the hemispherical reflectance factors for direct and diffuse incidence separately.
                rsdstar[i] = rsd[i] + (tss + tsd[i]) * rsoil_i * tdd[i] / dn;
                rddstar[i] = rdd[i] + (tdd[i] * tdd[i] * rsoil_i) / dn;

                // fCover: Fraction of green Vegetation Cover (= 1 - beam transmittance in the target-view path)
                fCover[i] = 1.0 - too;
            } // End of wavelength loop

            return new CanopyOptics
            {
                Rdot = rdot,
                Rsot = rsot,
                Rddt = rddt,
                Rsdt = rsdt,
                FCover = fCover,
                Abs_dir = abs_dir,
                Abs_hem = abs_hem,
                Rsdstar = rsdstar,
                Rddstar = rddstar,
                Wavelength = leafOptics.Wavelength // Assuming leafOptics has Wavelengths property
            };
        }

        /// <summary>
        /// Performs 4SAIL2 simulation, incorporating two layers (green/brown) and clumping.
        /// </summary>
        /// <param name="leafGreen">Leaf optical properties for the green component.</param>
        /// <param name="leafBrown">Leaf optical properties for the brown component.</param>
        /// <param name="typeLidf">Type of leaf inclination distribution function (1 for Verhoef, 2 for Campbell).</param>
        /// <param name="lidfA">LIDF parameter a (average leaf slope or angle).</param>
        /// <param name="lidfB">LIDF parameter b (bimodality, nullable, used only if typeLidf=1).</param>
        /// <param name="lai">Total Leaf Area Index.</param>
        /// <param name="q">Hot Spot parameter.</param>
        /// <param name="tts">Sun zenith angle (degrees).</param>
        /// <param name="tto">Observer zenith angle (degrees).</param>
        /// <param name="psi">Relative azimuth angle between sun and observer (degrees).</param>
        /// <param name="soilOptics">Soil reflectance properties (assumed Lambertian).</param>
        /// <param name="fractionBrown">Fraction of brown leaf area (0-1).</param>
        /// <param name="diss">Layer dissociation factor (0-1).</param>
        /// <param name="cv">Vertical crown cover percentage (0-1).</param>
        /// <param name="zeta">Tree shape factor (ratio of crown diameter to height).</param>
        /// <returns>A SailResult object containing calculated reflectance factors, fCover, and absorptance.</returns>
        /// <exception cref="ArgumentNullException">Thrown if leafGreen, leafBrown or soilProperties are null.</exception>
        /// <exception cref="ArgumentException">Thrown if input array lengths mismatch or parameters are invalid.</exception>
        public static CanopyOptics FourSAIL2(LeafOptics leafGreen, LeafOptics leafBrown,
                                  int typeLidf, double lidfA, double? lidfB, double lai,
                                  double q, double tts, double tto, double psi, SoilOptics soilOptics,
                                  double fractionBrown, double diss, double cv, double zeta)
        {
            // Input Validation
            if (!leafGreen.HasValue) 
            {
                throw new ArgumentNullException(nameof(leafGreen));
            }
            if (!leafBrown.HasValue)
            {
                throw new ArgumentNullException(nameof(leafBrown));
            }
            if (!soilOptics.HasValue)
            {
                throw new ArgumentNullException(nameof(soilOptics));
            }
            if (leafGreen.Reflectance == null || leafGreen.Transmittance == null ||
                leafBrown.Reflectance == null || leafBrown.Transmittance == null ||
                soilOptics.Reflectance == null)
            {
                throw new ArgumentException("Reflectance/Transmittance arrays in leafOptics and soilOptics cannot be null.");
            }                

            int nLambda = leafGreen.Reflectance.Length;
            if (leafGreen.Transmittance.Length != nLambda ||
                leafBrown.Reflectance.Length != nLambda || leafBrown.Transmittance.Length != nLambda ||
                soilOptics.Reflectance.Count != nLambda)
            {
                throw new ArgumentException("Input reflectance/transmittance arrays in leafOptics and soilOptics must have the same length.");
            }

            if (fractionBrown < 0 || fractionBrown > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(fractionBrown), "fractionBrown must be between 0 and 1.");
            }
            if (diss < 0 || diss > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(diss), "diss must be between 0 and 1.");
            }
            if (cv < 0 || cv > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(cv), "Cv (vertical cover) must be between 0 and 1.");
            }
            if (zeta < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(zeta), "Zeta (tree shape) cannot be negative.");
            }

            // Initialization
            double[] rsoil = soilOptics.Reflectance.ToArray(); // Soil reflectance (Lambertian assumption)
            /* Non-Lambertian soil (placeholders, same as Lambertian in this R code version) */
            double[] rddsoil = rsoil;
            double[] rdosoil = rsoil;
            double[] rsdsoil = rsoil;
            double[] rsosoil = rsoil;

            // Pre-allocate result arrays
            double[] rdot = new double[nLambda];
            double[] rsot = new double[nLambda];
            double[] rddt = new double[nLambda];
            double[] rsdt = new double[nLambda];
            double[] fCover = new double[nLambda]; // Calculated later based on tooc
            double[] abs_dir = new double[nLambda]; // Renamed alfast in R
            double[] abs_hem = new double[nLambda]; // Renamed alfadt in R
            double[] rsdstar = new double[nLambda];
            double[] rddstar = new double[nLambda];

            // Handle LAI = 0 case
            if (Math.Abs(lai) < 1e-9 || lai < 0)
            {
                if (lai < 0) Console.WriteLine("Warning: LAI is negative. Results computed assuming LAI = 0.");

                var rsoilVector = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.DenseOfArray(rsoil);
                var zeroVector = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(nLambda, 0.0);

                rdot = rsoilVector.ToArray();
                rsot = rsoilVector.ToArray();
                rddt = rsoilVector.ToArray();
                rsdt = rsoilVector.ToArray();
                rsdstar = rsoilVector.ToArray();
                rddstar = rsoilVector.ToArray();
                fCover = zeroVector.ToArray();
                abs_dir = zeroVector.ToArray();
                abs_hem = zeroVector.ToArray();

                return new CanopyOptics 
                { 
                    Rdot = rdot, 
                    Rsot = rsot, 
                    Rddt = rddt, 
                    Rsdt = rsdt, 
                    FCover = fCover, 
                    Abs_dir = abs_dir, 
                    Abs_hem = abs_hem, 
                    Rsdstar = rsdstar, 
                    Rddstar = rddstar,
                    Wavelength = leafGreen.Wavelength // Assuming leafOptics has Wavelengths property
                };
            }

            // Geometric Quantities
            double rd = DEGREES_TO_RADIANS;
            double ttsRad = tts * rd;
            double ttoRad = tto * rd;
            double psiRad = psi * rd;
            double cts = Math.Cos(ttsRad);
            double cto = Math.Cos(ttoRad);
            double ctscto = cts * cto;
            double tants = Math.Tan(ttsRad);
            double tanto = Math.Tan(ttoRad);
            double cospsi = Math.Cos(psiRad);
            double dso = Math.Sqrt(tants * tants + tanto * tanto - 2.0 * tants * tanto * cospsi);


            // Leaf Angle Distribution
            FoliarDistributionResult foliarDistrib;
            if (typeLidf == 1)
            {
                if (!lidfB.HasValue) throw new ArgumentException("LIDF parameter b (LIDFb) is required when TypeLidf is 1.");
                foliarDistrib = Dladgen(lidfA, lidfB.Value);
            }
            else if (typeLidf == 2)
            {
                foliarDistrib = Campbell(lidfA);
            }
            else
            {
                throw new ArgumentException("Invalid TypeLidf specified. Use 1 or 2.");
            }
            double[] lidf = foliarDistrib.Lidf;
            double[] litab = foliarDistrib.Litab;

            // Clumping Effects
            double Cs = 1.0, Co = 1.0; // Clumping factors for sun and observer
            if (cv < 1.0) // If not fully covered vertically
            {
                // Avoid pow(0, 1/very_small_cos) -> NaN/Inf
                double cs_safe = Math.Max(1e-9, cts);
                double co_safe = Math.Max(1e-9, cto);
                Cs = 1.0 - Math.Pow(1.0 - cv, 1.0 / cs_safe);
                Co = 1.0 - Math.Pow(1.0 - cv, 1.0 / co_safe);
            }
            double Overlap = 0.0;
            if (zeta > 0.0) // If tree shape factor is defined
            {
                Overlap = Math.Min(Cs * (1.0 - Co), Co * (1.0 - Cs)) * Math.Exp(-dso / zeta);
            }
            // Four components of cover based on crown overlaps (Verhoef & Bach 2007)
            double Fcd = Cs * Co + Overlap;           // Both sun and view see crown projection
            double Fcs = (1.0 - Cs) * Co - Overlap;   // Sun sees gap, view sees crown
            double Fod = Cs * (1.0 - Co) - Overlap;   // Sun sees crown, view sees gap
            double Fos = (1.0 - Cs) * (1.0 - Co) + Overlap; // Both sun and view see gap
                                                            // Crown clumping factor (for bidirectional scattering)
            double Fcdc = 1.0 - Math.Pow(1.0 - Fcd, 0.5 / cts + 0.5 / cto); // Needs check, might depend on cts/cto safety

            // --- Effective Leaf Optical Properties (due to dissociation) ---
            double fb = fractionBrown; // Actual fraction brown used in calculations
                                       // Make copies to avoid modifying input LeafOptics objects
            LeafOptics effectiveLeafGreen = new LeafOptics { Reflectance = (double[])leafGreen.Reflectance.Clone(), Transmittance = (double[])leafGreen.Transmittance.Clone() };
            LeafOptics effectiveLeafBrown = new LeafOptics { Reflectance = (double[])leafBrown.Reflectance.Clone(), Transmittance = (double[])leafBrown.Transmittance.Clone() };

            // R code artificial adjustment for fraction_brown = 0 or 1, but might be unnecessary.
            // as fraction_brown=0 should just mean lai1=lai, lai2=0. Replicate it for consistency.
            if (Math.Abs(fractionBrown) < 1e-9) // fraction_brown == 0
            {
                fb = 0.5; // Artificial adjustment
                effectiveLeafBrown.Reflectance = (double[])effectiveLeafGreen.Reflectance.Clone();
                effectiveLeafBrown.Transmittance = (double[])effectiveLeafGreen.Transmittance.Clone();
            }
            else if (Math.Abs(fractionBrown - 1.0) < 1e-9) // fraction_brown == 1
            {
                fb = 0.5; 
                effectiveLeafGreen.Reflectance = (double[])effectiveLeafBrown.Reflectance.Clone();
                effectiveLeafGreen.Transmittance = (double[])effectiveLeafBrown.Transmittance.Clone();
            }

            // Calculate dissociation term 's'
            double s = (1.0 - diss) * fb * (1.0 - fb);

            // Calculate effective optical properties for the two potentially mixed layers
            // rho1/tau1 for the top layer (mostly green), rho2/tau2 for bottom layer (mostly brown)
            double[] rho1 = new double[nLambda];
            double[] tau1 = new double[nLambda];
            double[] rho2 = new double[nLambda];
            double[] tau2 = new double[nLambda];
            double denom1 = (1.0 - fb);
            double denom2 = fb;
            // Avoid division by zero if fb is 0 or 1 (after the artificial adjustment above)
            bool denom1_is_zero = Math.Abs(denom1) < 1e-9;
            bool denom2_is_zero = Math.Abs(denom2) < 1e-9;

            for (int i = 0; i < nLambda; i++)
            {
                // Top layer (more green)
                if (denom1_is_zero)
                {
                    // If 1-fb is zero (i.e., fb=1), layer 1 doesn't exist or has zero LAI, so optics don't matter.
                    // However, R calculation proceeds. If diss=1, s=0, rho1=0/0 -> NaN. If diss<1, s>0.
                    // If fb=1 (after adjustment fb=0.5), denom1 = 0.5.
                    // Assume the artificial fb adjustment prevents denom zero.
                    rho1[i] = ((1.0 - fb - s) * effectiveLeafGreen.Reflectance[i] + s * effectiveLeafBrown.Reflectance[i]) / denom1;
                    tau1[i] = ((1.0 - fb - s) * effectiveLeafGreen.Transmittance[i] + s * effectiveLeafBrown.Transmittance[i]) / denom1;
                }
                else
                {
                    rho1[i] = ((1.0 - fb - s) * effectiveLeafGreen.Reflectance[i] + s * effectiveLeafBrown.Reflectance[i]) / denom1;
                    tau1[i] = ((1.0 - fb - s) * effectiveLeafGreen.Transmittance[i] + s * effectiveLeafBrown.Transmittance[i]) / denom1;
                }

                // Bottom layer (more brown)
                if (denom2_is_zero)
                {
                    // If fb=0 (after adjustment fb=0.5), denom2 = 0.5.
                    rho2[i] = (s * effectiveLeafGreen.Reflectance[i] + (fb - s) * effectiveLeafBrown.Reflectance[i]) / denom2;
                    tau2[i] = (s * effectiveLeafGreen.Transmittance[i] + (fb - s) * effectiveLeafBrown.Transmittance[i]) / denom2;
                }
                else
                {
                    rho2[i] = (s * effectiveLeafGreen.Reflectance[i] + (fb - s) * effectiveLeafBrown.Reflectance[i]) / denom2;
                    tau2[i] = (s * effectiveLeafGreen.Transmittance[i] + (fb - s) * effectiveLeafBrown.Transmittance[i]) / denom2;
                }
            }


            // Geometric Factors (same as 4SAIL)
            double ks = 0, ko = 0, bf = 0, sob = 0, sof = 0;
            int na = litab.Length;
            for (int i = 0; i < na; i++)
            {
                double ttl = litab[i];
                double ttlRad = ttl * rd;
                double ctl = Math.Cos(ttlRad);
                VolscattResult resVolscatt = Volscatt(tts, tto, psi, ttl);
                double ksli = resVolscatt.Chi_s / cts;
                double koli = resVolscatt.Chi_o / cto;
                double sobli = resVolscatt.Frho * PI / ctscto;
                double sofli = resVolscatt.Ftau * PI / ctscto;
                double bfli = ctl * ctl;
                double lidf_i = lidf[i];
                ks += ksli * lidf_i;
                ko += koli * lidf_i;
                bf += bfli * lidf_i;
                sob += sobli * lidf_i;
                sof += sofli * lidf_i;
            }
            double sdb = 0.5 * (ks + bf);
            double sdf = 0.5 * (ks - bf);
            double dob = 0.5 * (ko + bf);
            double dof = 0.5 * (ko - bf);
            double ddb = 0.5 * (1.0 + bf);
            double ddf = 0.5 * (1.0 - bf);

            // Layer LAIs
            // Use the ORIGINAL fractionBrown here, not the adjusted 'fb'
            double lai1 = (1.0 - fractionBrown) * lai; // Top layer LAI (Green)
            double lai2 = fractionBrown * lai; // Bottom layer LAI (Brown)


            // Hotspot Calculation (Two Layers)
            double tss_total = Math.Exp(-ks * lai); // Total direct transmittance (solar)
            double ck = Math.Exp(-ks * lai1); // Direct transmittance through top layer (solar)

            double alf = 1e6; // Default large value (no hotspot)
            if (q > 0)
            {
                double ks_plus_ko = ks + ko;
                if (Math.Abs(ks_plus_ko) < 1e-9) ks_plus_ko = 1e-9;
                alf = (dso / q) * 2.0 / ks_plus_ko;
            }
            if (alf > 200.0) alf = 200.0; // Limit alpha (H. Bach)

            double tsstoo; // Joint probability gap fraction through both layers
            double s1; // Integral for single scattering (top layer)
            double s2; // Integral for single scattering (bottom layer contribution)

            if (Math.Abs(alf) < 1e-9) // Pure hotspot
            {
                tsstoo = tss_total; // Joint = Solar transmittance if paths coincide
                                    // Integrals: Int(exp(-k*x))dx / lai = (1-exp(-k*lai))/(k*lai)
                double ks_lai1 = ks * lai1;
                double ks_lai = ks * lai;
                s1 = (Math.Abs(ks_lai1) < 1e-9) ? 1.0 : (1.0 - ck) / ks_lai1;
                s1 /= lai; 
                s2 = (Math.Abs(ks_lai) < 1e-9) ? 0.0 : (ck - tss_total) / ks_lai; // Integral over layer 2 / lai
                if (Math.Abs(lai) > 1e-9)
                { // Avoid division by zero if lai is 0
                    s1 = (Math.Abs(ks * lai1) < 1e-9) ? lai1 / lai : (1.0 - ck) / (ks * lai); // Integral for layer 1 scaled by total lai
                    s2 = (Math.Abs(ks * lai2) < 1e-9) ? lai2 / lai : (ck - tss_total) / (ks * lai); // Integral for layer 2 scaled by total lai
                }
                else
                {
                    s1 = 0;
                    s2 = 0;
                }

            }
            else // Outside hotspot - Integrate using Simpson/Trapezoidal
            {
                double fhot = lai * Math.Sqrt(ko * ks); // Hotspot function amplitude
                int nsteps = 20;

                // Integrate Layer 1 (path fraction 0 to 1-fb_orig)
                double x1_l1 = 0, y1_l1 = 0, sumint1 = 0;
                double f1_l1 = 1.0; // exp(y1) where y1=0
                double layer1_frac_end = 1.0 - fractionBrown;
                double prob_end_l1 = 1.0 - Math.Exp(-alf * layer1_frac_end);
                double fint1 = prob_end_l1 * (1.0 / nsteps); // Step size in probability space for layer 1

                for (int j = 1; j <= nsteps; j++)
                {
                    double x2_l1;
                    if (j < nsteps)
                    {
                        double prob_target = j * fint1;
                        if (prob_target >= 1.0) x2_l1 = 1.0;
                        else x2_l1 = -Math.Log(1.0 - prob_target) / alf;
                        x2_l1 = Math.Min(x2_l1, layer1_frac_end); // Ensure don't exceed layer 1 fraction
                    }
                    else
                    {
                        x2_l1 = layer1_frac_end; // Final step goes to end of layer 1
                    }

                    double y2_l1 = -(ko + ks) * lai * x2_l1 + fhot * (1.0 - Math.Exp(-alf * x2_l1)) / alf;
                    double f2_l1 = Math.Exp(y2_l1);

                    if (Math.Abs(y2_l1 - y1_l1) > 1e-9)
                        sumint1 += (f2_l1 - f1_l1) * (x2_l1 - x1_l1) / (y2_l1 - y1_l1);
                    else
                        sumint1 += 0.5 * (f1_l1 + f2_l1) * (x2_l1 - x1_l1);

                    x1_l1 = x2_l1;
                    y1_l1 = y2_l1;
                    f1_l1 = f2_l1;
                }
                s1 = sumint1; // Integral for layer 1 (relative to total LAI)

                // Integrate Layer 2 (path fraction 1-fb_orig to 1.0)
                double x1_l2 = layer1_frac_end, y1_l2 = y1_l1, sumint2 = 0; // Start where layer 1 ended
                double f1_l2 = f1_l1;
                double prob_start_l2 = prob_end_l1; // 1.0 - Math.Exp(-alf * x1_l2);
                double prob_end_l2 = 1.0 - Math.Exp(-alf * 1.0); // End at total path fraction 1
                double fint2 = (prob_end_l2 - prob_start_l2) * (1.0 / nsteps); // Step size in probability space for layer 2

                for (int j = 1; j <= nsteps; j++)
                {
                    double x2_l2;
                    if (j < nsteps)
                    {
                        double prob_target = prob_start_l2 + j * fint2;
                        if (prob_target >= 1.0) x2_l2 = 1.0; // Clip if needed
                        else x2_l2 = -Math.Log(1.0 - prob_target) / alf;
                        x2_l2 = Math.Max(x1_l2, Math.Min(x2_l2, 1.0)); // Ensure bounds
                    }
                    else
                    {
                        x2_l2 = 1.0; // Final step goes to full path length 1
                    }

                    double y2_l2 = -(ko + ks) * lai * x2_l2 + fhot * (1.0 - Math.Exp(-alf * x2_l2)) / alf;
                    double f2_l2 = Math.Exp(y2_l2);

                    if (Math.Abs(y2_l2 - y1_l2) > 1e-9)
                        sumint2 += (f2_l2 - f1_l2) * (x2_l2 - x1_l2) / (y2_l2 - y1_l2);
                    else
                        sumint2 += 0.5 * (f1_l2 + f2_l2) * (x2_l2 - x1_l2);

                    x1_l2 = x2_l2;
                    y1_l2 = y2_l2;
                    f1_l2 = f2_l2;
                }
                s2 = sumint2; // Integral for layer 2
                tsstoo = f1_l2; // Final joint probability after both layers
            }

            // Calculate Scattering for Bottom Layer (Layer 2 - Brown)
            // Using lai2 and rho2/tau2
            double[] tss_L2 = new double[nLambda]; // Solar transmittance Layer 2
            double[] too_L2 = new double[nLambda]; // Observer transmittance Layer 2
            double[] sigb_L2 = new double[nLambda];
            double[] sigf_L2 = new double[nLambda];
            double[] att_L2 = new double[nLambda];
            double[] m_L2 = new double[nLambda];
            double[] sf_L2 = new double[nLambda];
            double[] sb_L2 = new double[nLambda];
            double[] vf_L2 = new double[nLambda];
            double[] vb_L2 = new double[nLambda];
            double[] w_L2 = new double[nLambda]; // Bidirectional scattering coeff Layer 2

            // Results for Layer 2 on black soil
            double[] tdd_L2 = new double[nLambda];
            double[] rdd_L2 = new double[nLambda];
            double[] tsd_L2 = new double[nLambda];
            double[] rsd_L2 = new double[nLambda];
            double[] tdo_L2 = new double[nLambda];
            double[] rdo_L2 = new double[nLambda];
            double[] rsod_L2 = new double[nLambda];

            List<int> ncsIndices_L2 = new List<int>(); // Indices for Non-Conservative Scattering
            List<int> csIndices_L2 = new List<int>();  // Indices for Conservative Scattering

            for (int i = 0; i < nLambda; i++)
            {
                tss_L2[i] = Math.Exp(-ks * lai2);
                too_L2[i] = Math.Exp(-ko * lai2);
                sigb_L2[i] = ddb * rho2[i] + ddf * tau2[i];
                sigf_L2[i] = ddf * rho2[i] + ddb * tau2[i];
                att_L2[i] = 1.0 - sigf_L2[i];
                double m2_L2 = (att_L2[i] + sigb_L2[i]) * (att_L2[i] - sigb_L2[i]);
                m_L2[i] = (m2_L2 > 0) ? Math.Sqrt(m2_L2) : 0.0;
                sf_L2[i] = sdf * rho2[i] + sdb * tau2[i];
                sb_L2[i] = sdb * rho2[i] + sdf * tau2[i];
                vf_L2[i] = dof * rho2[i] + dob * tau2[i];
                vb_L2[i] = dob * rho2[i] + dof * tau2[i];
                w_L2[i] = sob * rho2[i] + sof * tau2[i];

                // Segregate indices based on m value
                if (m_L2[i] > 0.01) ncsIndices_L2.Add(i);
                else csIndices_L2.Add(i);
            }

            // Apply Non-Conservative Scattering calculations for relevant indices
            if (ncsIndices_L2.Count > 0)
            {
                double[] m_ncs = ncsIndices_L2.Select(idx => m_L2[idx]).ToArray();
                double[] att_ncs = ncsIndices_L2.Select(idx => att_L2[idx]).ToArray();
                double[] sigb_ncs = ncsIndices_L2.Select(idx => sigb_L2[idx]).ToArray();
                double[] sf_ncs = ncsIndices_L2.Select(idx => sf_L2[idx]).ToArray();
                double[] sb_ncs = ncsIndices_L2.Select(idx => sb_L2[idx]).ToArray();
                double[] vf_ncs = ncsIndices_L2.Select(idx => vf_L2[idx]).ToArray();
                double[] vb_ncs = ncsIndices_L2.Select(idx => vb_L2[idx]).ToArray();

                ScatteringResult resNCS = NonConservativeScattering(
                    m_ncs, lai2, att_ncs, sigb_ncs, ks, ko, sf_ncs, sb_ncs, vf_ncs, vb_ncs,
                    Math.Exp(-ks * lai2), Math.Exp(-ko * lai2)); // Use layer-specific lai2

                // Map results back to the full arrays
                for (int j = 0; j < ncsIndices_L2.Count; j++)
                {
                    int idx = ncsIndices_L2[j];
                    tdd_L2[idx] = resNCS.Tdd[j];
                    rdd_L2[idx] = resNCS.Rdd[j];
                    tsd_L2[idx] = resNCS.Tsd[j];
                    rsd_L2[idx] = resNCS.Rsd[j];
                    tdo_L2[idx] = resNCS.Tdo[j];
                    rdo_L2[idx] = resNCS.Rdo[j];
                    rsod_L2[idx] = resNCS.Rsod[j];
                }
            }

            // Apply Conservative Scattering calculations for relevant indices
            if (csIndices_L2.Count > 0)
            {
                double[] m_cs = csIndices_L2.Select(idx => m_L2[idx]).ToArray();
                double[] att_cs = csIndices_L2.Select(idx => att_L2[idx]).ToArray();
                double[] sigb_cs = csIndices_L2.Select(idx => sigb_L2[idx]).ToArray();
                double[] sf_cs = csIndices_L2.Select(idx => sf_L2[idx]).ToArray();
                double[] sb_cs = csIndices_L2.Select(idx => sb_L2[idx]).ToArray();
                double[] vf_cs = csIndices_L2.Select(idx => vf_L2[idx]).ToArray();
                double[] vb_cs = csIndices_L2.Select(idx => vb_L2[idx]).ToArray();

                ScatteringResult resCS = ConservativeScattering(
                     m_cs, lai2, att_cs, sigb_cs, ks, ko, sf_cs, sb_cs, vf_cs, vb_cs,
                     Math.Exp(-ks * lai2), Math.Exp(-ko * lai2));

                for (int j = 0; j < csIndices_L2.Count; j++)
                {
                    int idx = csIndices_L2[j];
                    tdd_L2[idx] = resCS.Tdd[j];
                    rdd_L2[idx] = resCS.Rdd[j];
                    tsd_L2[idx] = resCS.Tsd[j];
                    rsd_L2[idx] = resCS.Rsd[j];
                    tdo_L2[idx] = resCS.Tdo[j];
                    rdo_L2[idx] = resCS.Rdo[j];
                    rsod_L2[idx] = resCS.Rsod[j];
                }
            }
            // Background properties = Layer 2 on black soil
            double[] rddb = rdd_L2; // Bi-hemispherical Refl. of background (Layer 2)
            double[] rsdb = rsd_L2; // Dir-hem Refl. of background
            double[] rdob = rdo_L2; // Hem-dir Refl. of background
            double[] rsodb = rsod_L2;// Bi-dir Refl. (mult scatt) of background
            double[] tddb = tdd_L2; // Bi-hem Trans. of background
            double[] tsdb = tsd_L2; // Dir-hem Trans. of background
            double[] tdob = tdo_L2; // Hem-dir Trans. of background
            double[] toob = too_L2; // Beam Trans. (obs) of background
            double[] tssb = tss_L2; // Beam Trans. (sun) of background

            // Calculate Scattering for Top Layer (Layer 1 - Green)
            // Using lai1 and rho1/tau1
            double[] tss_L1 = new double[nLambda];
            double[] too_L1 = new double[nLambda];
            double[] sigb_L1 = new double[nLambda];
            double[] sigf_L1 = new double[nLambda];
            double[] att_L1 = new double[nLambda];
            double[] m_L1 = new double[nLambda];
            double[] sf_L1 = new double[nLambda];
            double[] sb_L1 = new double[nLambda];
            double[] vf_L1 = new double[nLambda];
            double[] vb_L1 = new double[nLambda];
            double[] w_L1 = new double[nLambda]; // Bidirectional scattering coeff Layer 1

            double[] tdd_L1 = new double[nLambda];
            double[] rdd_L1 = new double[nLambda];
            double[] tsd_L1 = new double[nLambda];
            double[] rsd_L1 = new double[nLambda];
            double[] tdo_L1 = new double[nLambda];
            double[] rdo_L1 = new double[nLambda];
            double[] rsod_L1 = new double[nLambda];

            List<int> ncsIndices_L1 = new List<int>();
            List<int> csIndices_L1 = new List<int>();

            for (int i = 0; i < nLambda; i++)
            {
                tss_L1[i] = Math.Exp(-ks * lai1);
                too_L1[i] = Math.Exp(-ko * lai1);
                sigb_L1[i] = ddb * rho1[i] + ddf * tau1[i];
                sigf_L1[i] = ddf * rho1[i] + ddb * tau1[i];
                att_L1[i] = 1.0 - sigf_L1[i];
                double m2_L1 = (att_L1[i] + sigb_L1[i]) * (att_L1[i] - sigb_L1[i]);
                m_L1[i] = (m2_L1 > 0) ? Math.Sqrt(m2_L1) : 0.0;
                sf_L1[i] = sdf * rho1[i] + sdb * tau1[i];
                sb_L1[i] = sdb * rho1[i] + sdf * tau1[i];
                vf_L1[i] = dof * rho1[i] + dob * tau1[i];
                vb_L1[i] = dob * rho1[i] + dof * tau1[i];
                w_L1[i] = sob * rho1[i] + sof * tau1[i];

                if (m_L1[i] > 0.01) ncsIndices_L1.Add(i);
                else csIndices_L1.Add(i);
            }

            if (ncsIndices_L1.Count > 0)
            {
                double[] m_ncs = ncsIndices_L1.Select(idx => m_L1[idx]).ToArray();
                double[] att_ncs = ncsIndices_L1.Select(idx => att_L1[idx]).ToArray();
                double[] sigb_ncs = ncsIndices_L1.Select(idx => sigb_L1[idx]).ToArray();
                double[] sf_ncs = ncsIndices_L1.Select(idx => sf_L1[idx]).ToArray();
                double[] sb_ncs = ncsIndices_L1.Select(idx => sb_L1[idx]).ToArray();
                double[] vf_ncs = ncsIndices_L1.Select(idx => vf_L1[idx]).ToArray();
                double[] vb_ncs = ncsIndices_L1.Select(idx => vb_L1[idx]).ToArray();
                ScatteringResult resNCS = NonConservativeScattering(
                    m_ncs, lai1, att_ncs, sigb_ncs, ks, ko, sf_ncs, sb_ncs, vf_ncs, vb_ncs,
                     Math.Exp(-ks * lai1), Math.Exp(-ko * lai1)); // Use layer-specific lai1

                for (int j = 0; j < ncsIndices_L1.Count; j++)
                {
                    int idx = ncsIndices_L1[j];
                    tdd_L1[idx] = resNCS.Tdd[j]; rdd_L1[idx] = resNCS.Rdd[j]; tsd_L1[idx] = resNCS.Tsd[j];
                    rsd_L1[idx] = resNCS.Rsd[j]; tdo_L1[idx] = resNCS.Tdo[j]; rdo_L1[idx] = resNCS.Rdo[j];
                    rsod_L1[idx] = resNCS.Rsod[j];
                }
            }
            if (csIndices_L1.Count > 0)
            {
                double[] m_cs = csIndices_L1.Select(idx => m_L1[idx]).ToArray();
                double[] att_cs = csIndices_L1.Select(idx => att_L1[idx]).ToArray();
                double[] sigb_cs = csIndices_L1.Select(idx => sigb_L1[idx]).ToArray();
                double[] sf_cs = csIndices_L1.Select(idx => sf_L1[idx]).ToArray();
                double[] sb_cs = csIndices_L1.Select(idx => sb_L1[idx]).ToArray();
                double[] vf_cs = csIndices_L1.Select(idx => vf_L1[idx]).ToArray();
                double[] vb_cs = csIndices_L1.Select(idx => vb_L1[idx]).ToArray();
                ScatteringResult resCS = ConservativeScattering(
                     m_cs, lai1, att_cs, sigb_cs, ks, ko, sf_cs, sb_cs, vf_cs, vb_cs,
                     Math.Exp(-ks * lai1), Math.Exp(-ko * lai1));

                for (int j = 0; j < csIndices_L1.Count; j++)
                {
                    int idx = csIndices_L1[j];
                    tdd_L1[idx] = resCS.Tdd[j]; rdd_L1[idx] = resCS.Rdd[j]; tsd_L1[idx] = resCS.Tsd[j];
                    rsd_L1[idx] = resCS.Rsd[j]; tdo_L1[idx] = resCS.Tdo[j]; rdo_L1[idx] = resCS.Rdo[j];
                    rsod_L1[idx] = resCS.Rsod[j];
                }
            }

            // Combine Layers (Adding Method)
            // Reflectances/Transmittances of the combined two-layer canopy (no soil yet)
            double[] rsdt_comb = new double[nLambda]; // Combined Dir-Hem Refl
            double[] rdot_comb = new double[nLambda]; // Combined Hem-Dir Refl
            double[] rsodt_comb = new double[nLambda];// Combined Bi-Dir Refl (Multiple Scatt)
            double[] rsost_comb = new double[nLambda];// Combined Bi-Dir Refl (Single Scatt)
            double[] rsot_comb = new double[nLambda]; // Combined Total Bi-Dir Refl
            double[] rddt_t_comb = new double[nLambda];// Combined Bi-Hem Refl (Top view)
            double[] rddt_b_comb = new double[nLambda];// Combined Bi-Hem Refl (Bottom view)
            double[] tsst_comb = new double[nLambda]; // Combined Beam Trans (Sun)
            double[] toot_comb = new double[nLambda]; // Combined Beam Trans (Obs)
            double[] tsdt_comb = new double[nLambda]; // Combined Dir-Hem Trans
            double[] tdot_comb = new double[nLambda]; // Combined Hem-Dir Trans
            double[] tddt_comb = new double[nLambda]; // Combined Bi-Hem Trans

            for (int i = 0; i < nLambda; i++)
            {
                double rn = 1.0 - rdd_L1[i] * rddb[i]; // Interaction term: 1 - Rdd_L1 * Rdd_L2
                if (Math.Abs(rn) < 1e-12) rn = 1e-12;

                // Term for transmission down L1, reflection up from L2, transmission up L1
                double tup = (tss_L1[i] * rsdb[i] + tsd_L1[i] * rddb[i]) / rn;
                // Term for Tdn = transmitted solar that gets through bottom layer L2
                double tdn = (tsd_L1[i] + tss_L1[i] * rsdb[i] * rdd_L1[i]) / rn; 

                rsdt_comb[i] = rsd_L1[i] + tup * tdd_L1[i]; // R_sd = R_sd_L1 + T_ss_L1*R_sd_L2*T_dd_L1/rn + T_sd_L1*R_dd_L2*T_dd_L1/rn
                rdot_comb[i] = rdo_L1[i] + tdd_L1[i] * (rddb[i] * tdo_L1[i] + rdob[i] * too_L1[i]) / rn;
                rsodt_comb[i] = rsod_L1[i] + (tss_L1[i] * rsodb[i] + tdn * rdob[i]) * too_L1[i] + tup * tdo_L1[i]; // Multiple Scattering BiDi

                // Single scattering combined - weighted sum of contributions from each layer's integral
                // Note: R code uses w1, s1, w2, s2 directly. Need w1=w_L1, w2=w_L2.
                rsost_comb[i] = (w_L1[i] * s1 + w_L2[i] * s2) * lai; // Single Scattering BiDi (* LAI cancels the division in s1/s2?)

                rsot_comb[i] = rsost_comb[i] + rsodt_comb[i]; // Total BiDi

                // Diffuse reflectances of combined layer (viewed from top or bottom)
                rddt_t_comb[i] = rdd_L1[i] + tdd_L1[i] * rddb[i] * tdd_L1[i] / rn; // Top view
                rddt_b_comb[i] = rddb[i] + tddb[i] * rdd_L1[i] * tddb[i] / rn; // Bottom view

                // Transmittances of the combined canopy layers
                tsst_comb[i] = tss_L1[i] * tssb[i]; // Beam down Sun
                toot_comb[i] = too_L1[i] * toob[i]; // Beam up Obs
                tsdt_comb[i] = tss_L1[i] * tsdb[i] + tdn * tddb[i]; // Dir-Hem Trans
                tdot_comb[i] = tdob[i] * too_L1[i] + tddb[i] * (tdo_L1[i] + rdd_L1[i] * rdob[i] * too_L1[i]) / rn; // Hem-Dir Trans
                tddt_comb[i] = tdd_L1[i] * tddb[i] / rn; // Bi-Hem Trans
            }

            // Apply Clumping Effects to Combined Vegetation Layer
            double[] rddcb = new double[nLambda]; // Clumped BiHem Refl (Bottom view)
            double[] rddct = new double[nLambda]; // Clumped BiHem Refl (Top view)
            double[] tddc = new double[nLambda];  // Clumped BiHem Trans
            double[] rsdc = new double[nLambda];  // Clumped DirHem Refl
            double[] tsdc = new double[nLambda];  // Clumped DirHem Trans
            double[] rdoc = new double[nLambda];  // Clumped HemDir Refl
            double[] tdoc = new double[nLambda];  // Clumped HemDir Trans
            double[] tssc = new double[nLambda];  // Clumped Beam Trans (Sun)
            double[] tooc = new double[nLambda];  // Clumped Beam Trans (Obs)
            double[] rsoc = new double[nLambda];  // Clumped BiDir Refl (Crown part)
            double[] tssooc = new double[nLambda]; // Clumped Joint Trans (Sun-Obs)

            for (int i = 0; i < nLambda; i++)
            {
                rddcb[i] = cv * rddt_b_comb[i]; // Clumped Rdd (bottom view) = Cv * Rdd_b
                rddct[i] = cv * rddt_t_comb[i]; // Clumped Rdd (top view) = Cv * Rdd_t
                tddc[i] = 1.0 - cv + cv * tddt_comb[i]; // Clumped Tdd = Gap + Cv * Tdd
                rsdc[i] = Cs * rsdt_comb[i]; // Clumped Rsd = Cs * Rsd
                tsdc[i] = Cs * tsdt_comb[i]; // Clumped Tsd = Cs * Tsd
                rdoc[i] = Co * rdot_comb[i]; // Clumped Rdo = Co * Rdo
                tdoc[i] = Co * tdot_comb[i]; // Clumped Tdo = Co * Tdo
                tssc[i] = 1.0 - Cs + Cs * tsst_comb[i]; // Clumped Tss = Gap + Cs * Tss
                tooc[i] = 1.0 - Co + Co * toot_comb[i]; // Clumped Too = Gap + Co * Too

                // New weight function Fcdc for crown contribution (W. Verhoef, 22-05-08)
                rsoc[i] = Fcdc * rsot_comb[i]; // Bidirectional crown contribution
                // Combined joint transmittance including gaps
                tssooc[i] = Fcd * tsstoo + Fcs * toot_comb[i] + Fod * tsst_comb[i] + Fos; // Gap components (Fcs*Too, Fod*Tss, Fos*1) + Crown component (Fcd*Tsstoo)
            }

            // Canopy absorptance for black background (W. Verhoef, 02-03-04) - Before soil interaction
            // These are absorptances of the clumped vegetation layer itself
            double[] alfas_veg = new double[nLambda]; // Direct absorptance (veg only)
            double[] alfad_veg = new double[nLambda]; // Diffuse absorptance (veg only)
            for (int i = 0; i < nLambda; i++)
            {
                alfas_veg[i] = 1.0 - tssc[i] - tsdc[i] - rsdc[i]; // 1 - Tss - Tsd - Rsd (direct)
                alfad_veg[i] = 1.0 - tddc[i] - rddct[i]; // 1 - Tdd - Rdd (diffuse, top view)
            }

            // Add the Soil Background
            for (int i = 0; i < nLambda; i++)
            {
                double rn_soil = 1.0 - rddcb[i] * rddsoil[i]; // Interaction: 1 - Rdd_veg_bottom * Rdd_soil
                if (Math.Abs(rn_soil) < 1e-12) rn_soil = 1e-12;

                // Term for transmission down clumped veg, reflection up soil, transmission up clumped veg
                double tup_soil = (tssc[i] * rsdsoil[i] + tsdc[i] * rddsoil[i]) / rn_soil;
                // Term for Tdn = transmitted solar that gets through veg then soil system
                double tdn_soil = (tsdc[i] + tssc[i] * rsdsoil[i] * rddcb[i]) / rn_soil; // Tsd originating below veg layer

                // Bi-directional reflectance factor Components
                // Final Reflectances including soil
                rddt[i] = rddct[i] + tddc[i] * rddsoil[i] * tddc[i] / rn_soil; // Final Rdd (Top view)
                rsdt[i] = rsdc[i] + tup_soil * tddc[i];                        // Final Rsd
                rdot[i] = rdoc[i] + tddc[i] * (rddsoil[i] * tdoc[i] + rdosoil[i] * tooc[i]) / rn_soil; // Final Rdo
                rsot[i] = rsoc[i] + tssooc[i] * rsosoil[i] + tdn_soil * rdosoil[i] * tooc[i] + tup_soil * tdoc[i]; // Final Rso

                // fAPAR components
                // Effect of soil background on canopy absorptances (W. Verhoef, 02-03-04)
                // These 'abs_dir' / 'abs_hem' correspond to 'alfast' / 'alfadt' in R code
                // They represent the total absorbed flux fraction by the vegetation layer within the canopy-soil system.
                abs_dir[i] = alfas_veg[i] + tup_soil * alfad_veg[i]; // Direct absorption by veg
                abs_hem[i] = alfad_veg[i] * (1.0 + tddc[i] * rddsoil[i] / rn_soil); // Diffuse absorption by veg

                // Final fCover based on full clumped combined-layre transmittance full clumped combined-layer transmittance
                // WARNING: This is not the same as fCover in R code, which uses `too`
                // fCover[i] = 1.0 - tooc[i];

                // Final fCover based on the green layer
                fCover[i] = 1.0 - too_L1[i];

                // Albedo Components (Rsd*, Rdd*)
                /* WARNING: R code calculates these using variables from the layer combination step, i.e., 
                 * BEFORE clumping and final soil interaction for other reflectances applied to these variables. 
                 * This might be not correct???? 
                 * Translating R code directly by using: rsd_L1, tss_L1, tsd_L1, tdd_L1, rdd_L1, rddb (L2 refl), rsoil (input), rn (L1-L2 interaction) */
                double rn_alb = 1.0 - rdd_L1[i] * rddb[i]; // Interaction term from layer combination step
                if (Math.Abs(rn_alb) < 1e-12) rn_alb = 1e-12;

                /* WARNING: These calculations seem inconsistent with the final rsdt/rddt which include clumping (Cv, Cs, Co).
                 * The R code uses (1) the un-clumped layer 1 and layer 2 properties, (2) rsoil (input) directly instead of rsdsoil/rddsoil used above,
                 * and (3) rn_alb (from layer combination) instead of rn_soil (final soil interaction). 
                 * Corrected calculation: Albedo components should be equal to the final 
                 * hemispherical reflectance factors, which already include all clumping and soil effects.
                rsdstar[i] = rsdt[i];
                rddstar[i] = rddt[i]; */
                // Replicating R code's apparent logic:
                rsdstar[i] = rsd_L1[i] + (tss_L1[i] + tsd_L1[i]) * rsoil[i] * tdd_L1[i] / rn_alb;
                rddstar[i] = rdd_L1[i] + (tdd_L1[i] * tdd_L1[i] * rsoil[i]) / rn_alb;
            } // End wavelength loop


            return new CanopyOptics
            {
                Rdot = rdot,
                Rsot = rsot,
                Rddt = rddt,
                Rsdt = rsdt,
                FCover = fCover,
                Abs_dir = abs_dir,
                Abs_hem = abs_hem,
                Rsdstar = rsdstar,
                Rddstar = rddstar,
                Wavelength = leafGreen.Wavelength
            };
        }
    } 
}
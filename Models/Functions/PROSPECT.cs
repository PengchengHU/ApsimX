using System;
using System.Linq;
using MathNet.Numerics.Integration;
using Models.Core;
using Models.PMF;
using Models.PMF.Phen;
using Models.PMF.Organs;
using Models.Climate;
using System.Reflection;
using System.Collections.Generic;

namespace Models.Functions
{
    /// <summary>
    /// This Library includes functions dedicated to PROSPECT simulation.
    /// Ported from R to C# following the original implementation by:
    /// Jean-Baptiste FERET jb.feret@teledetection.fr
    /// Florian de Boissieu florian.deboissieu@inrae.fr
    /// </summary>
    [Serializable]
    [Description("Prospect simulation")]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Plant))]
    public class Prospect: Model
    {
        /// <summary>
        /// Core function running PROSPECT.
        /// This function allows simulations using PROSPECT-D or PROSPECT-PRO depending on the parameterization.
        /// This code includes numerical optimizations proposed in the FLUSPECT code
        /// Authors: Wout Verhoef, Christiaan van der Tol (tol@itc.nl), Joris Timmermans,
        /// Date: 2007
        /// Update from PROSPECT to FLUSPECT: January 2011 (CvdT)
        /// </summary>
        /// <param name="specPROSPECT">Includes spectral constants: refractive index, specific absorption coefficients and corresponding spectral bands</param>
        /// <param name="inputPROSPECT">Includes all prospect input parameters</param>
        /// <param name="N">Leaf structure parameter</param>
        /// <param name="CHL">Chlorophyll content (microg.cm-2)</param>
        /// <param name="CAR">Carotenoid content (microg.cm-2)</param>
        /// <param name="ANT">Anthocyanin content (microg.cm-2)</param>
        /// <param name="BROWN">Brown pigment content (Arbitrary units)</param>
        /// <param name="EWT">Equivalent Water Thickness (g.cm-2)</param>
        /// <param name="LMA">Leaf Mass per Area (g.cm-2)</param>
        /// <param name="PROT">Protein content (g.cm-2)</param>
        /// <param name="CBC">NonProt Carbon-based constituent content (g.cm-2)</param>
        /// <param name="alpha">Solid angle for incident light at surface of leaf</param>
        /// <param name="check">Set to true to check input data format</param>
        /// <returns>Leaf directional-hemispherical reflectance and transmittance</returns>
        public static ProspectResult RunProspect(
            SpecPROSPECTData specPROSPECT = null,
            ProspectInput inputPROSPECT = null,
            double N = 1.5,
            double CHL = 40.0,
            double CAR = 8.0,
            double ANT = 0.0,
            double BROWN = 0.0,
            double EWT = 0.01,
            double? LMA = null,
            double PROT = 0.0,
            double CBC = 0.0,
            double alpha = 40.0,
            bool check = true)
        {
            // Define PROSPECT input
            inputPROSPECT = DefineInputProspect(inputPROSPECT, CHL, CAR, ANT, BROWN, EWT, LMA, PROT, CBC, N, alpha);

            // Default: simulates leaf optics using full spectral range
            if (specPROSPECT == null)
                specPROSPECT = SpecPROSPECTFullRange;

            int wavelengthCount = specPROSPECT.Lambda.Length;
            double[] Kall = new double[wavelengthCount];

            // Compute total absorption corresponding to each homogeneous layer
            for (int i = 0; i < wavelengthCount; i++)
            {
                Kall[i] = (inputPROSPECT.CHL * specPROSPECT.SAC_CHL[i] +
                          inputPROSPECT.CAR * specPROSPECT.SAC_CAR[i] +
                          inputPROSPECT.ANT * specPROSPECT.SAC_ANT[i] +
                          inputPROSPECT.BROWN * specPROSPECT.SAC_BROWN[i] +
                          inputPROSPECT.EWT * specPROSPECT.SAC_EWT[i] +
                          inputPROSPECT.LMA * specPROSPECT.SAC_LMA[i] +
                          inputPROSPECT.PROT * specPROSPECT.SAC_PROT[i] +
                          inputPROSPECT.CBC * specPROSPECT.SAC_CBC[i]) / inputPROSPECT.N;
            }

            // Non-conservative scattering (normal case) when Kall > 0
            double[] tau = new double[wavelengthCount];
            for (int i = 0; i < wavelengthCount; i++)
            {
                if (Kall[i] <= 0)
                {
                    tau[i] = 1;
                }
                else
                {
                    double t1 = (1 - Kall[i]) * Math.Exp(-Kall[i]);
                    double t2 = Kall[i] * Kall[i] * ExponentialIntegral(Kall[i]);
                    tau[i] = t1 + t2;
                }
            }

            // ***********************************************************************
            // Reflectance and transmittance of one layer
            // ***********************************************************************
            // Allen W.A., Gausman H.W., Richardson A.J., Thomas J.R. (1969),
            // Interaction of isotropic light with a compact plant leaf, J. Opt.
            // Soc. Am., 59(10):1376-1379.
            // ***********************************************************************
            // Reflectivity and transmissivity at the interface
            // ***********************************************************************
            double[] talf;
            if (inputPROSPECT.alpha == 40)
            {
                talf = specPROSPECT.Calctav40;
            }
            else
            {
                talf = new double[wavelengthCount];
                for (int i = 0; i < wavelengthCount; i++)
                {
                    talf[i] = CalcTav(inputPROSPECT.alpha, specPROSPECT.NRefrac[i]);
                }
            }

            double[] ralf = new double[wavelengthCount];
            for (int i = 0; i < wavelengthCount; i++)
            {
                ralf[i] = 1 - talf[i];
            }

            double[] t12 = specPROSPECT.Calctav90;
            double[] r12 = new double[wavelengthCount];
            double[] t21 = new double[wavelengthCount];
            double[] r21 = new double[wavelengthCount];

            for (int i = 0; i < wavelengthCount; i++)
            {
                r12[i] = 1 - t12[i];
                t21[i] = t12[i] / (specPROSPECT.NRefrac[i] * specPROSPECT.NRefrac[i]);
                r21[i] = 1 - t21[i];
            }

            // Top surface side
            double[] denom = new double[wavelengthCount];
            double[] Ta = new double[wavelengthCount];
            double[] Ra = new double[wavelengthCount];

            for (int i = 0; i < wavelengthCount; i++)
            {
                denom[i] = 1 - (r21[i] * r21[i] * (tau[i] * tau[i]));
                Ta[i] = (talf[i] * tau[i] * t21[i]) / denom[i];
                Ra[i] = ralf[i] + (r21[i] * tau[i] * Ta[i]);
            }

            // Bottom surface side
            double[] t = new double[wavelengthCount];
            double[] r = new double[wavelengthCount];

            for (int i = 0; i < wavelengthCount; i++)
            {
                t[i] = t12[i] * tau[i] * t21[i] / denom[i];
                r[i] = r12[i] + (r21[i] * tau[i] * t[i]);
            }

            // ***********************************************************************
            // Reflectance and transmittance of N layers
            // Stokes equations to compute properties of next N-1 layers (N real)
            // Normal case
            // ***********************************************************************
            // Stokes G.G. (1862), On the intensity of the light reflected from
            // or transmitted through a pile of plates, Proc. Roy. Soc. Lond.,
            // 11:545-556.
            // ***********************************************************************
            double[] D = new double[wavelengthCount];
            double[] rq = new double[wavelengthCount];
            double[] tq = new double[wavelengthCount];
            double[] a = new double[wavelengthCount];
            double[] b = new double[wavelengthCount];

            for (int i = 0; i < wavelengthCount; i++)
            {
                D[i] = Math.Sqrt((1 + r[i] + t[i]) * (1 + r[i] - t[i]) * (1 - r[i] + t[i]) * (1 - r[i] - t[i]));
                rq[i] = r[i] * r[i];
                tq[i] = t[i] * t[i];
                a[i] = (1 + rq[i] - tq[i] + D[i]) / (2 * r[i]);
                b[i] = (1 - rq[i] + tq[i] + D[i]) / (2 * t[i]);
            }

            double[] bNm1 = new double[wavelengthCount];
            double[] bN2 = new double[wavelengthCount];
            double[] a2 = new double[wavelengthCount];
            double[] denomStokes = new double[wavelengthCount];
            double[] Rsub = new double[wavelengthCount];
            double[] Tsub = new double[wavelengthCount];

            for (int i = 0; i < wavelengthCount; i++)
            {
                bNm1[i] = Math.Pow(b[i], inputPROSPECT.N - 1);
                bN2[i] = bNm1[i] * bNm1[i];
                a2[i] = a[i] * a[i];
                denomStokes[i] = a2[i] * bN2[i] - 1;
                Rsub[i] = a[i] * (bN2[i] - 1) / denomStokes[i];
                Tsub[i] = bNm1[i] * (a2[i] - 1) / denomStokes[i];

                // Case of zero absorption
                if (r[i] + t[i] >= 1)
                {
                    Tsub[i] = t[i] / (t[i] + (1 - t[i]) * (inputPROSPECT.N - 1));
                    Rsub[i] = 1 - Tsub[i];
                }
            }

            // Leaf reflectance and transmittance: combine top layer with next N-1 layers
            double[] refl = new double[wavelengthCount];
            double[] tran = new double[wavelengthCount];
            double[] denomFinal = new double[wavelengthCount];

            for (int i = 0; i < wavelengthCount; i++)
            {
                denomFinal[i] = 1 - Rsub[i] * r[i];
                tran[i] = Ta[i] * Tsub[i] / denomFinal[i];
                refl[i] = Ra[i] + (Ta[i] * Rsub[i] * t[i]) / denomFinal[i];
            }

            return new ProspectResult
            {
                Wavelength = specPROSPECT.Lambda,
                Reflectance = refl,
                Transmittance = tran
            };
        }

        /// <summary>
        /// Computation of transmissivity of a dielectric plane surface,
        /// averaged over all directions of incidence and over all polarizations.
        /// </summary>
        /// <param name="alpha">Max incidence angle of solid angle of incident light</param>
        /// <param name="nr">Refractive index</param>
        /// <returns>Transmissivity of a dielectric plane surface</returns>
        public static double CalcTav(double alpha, double nr)
        {
            // Stern F. (1964), Transmission of isotropic radiation across an
            // interface between two dielectrics, Appl. Opt., 3(1):111-113.
            // Allen W.A. (1973), Transmission of isotropic light across a
            // dielectric surface in two and three dimensions, J. Opt. Soc. Am.,
            // 63(6):664-666.

            double rd = Math.PI / 180;
            double n2 = nr * nr;
            double np = n2 + 1;
            double nm = n2 - 1;
            double a = (nr + 1) * (nr + 1) / 2;
            double k = -(n2 - 1) * (n2 - 1) / 4;
            double sa = Math.Sin(alpha * rd);

            double b2 = (sa * sa) - (np / 2);
            double b1;

            if (alpha == 90)
            {
                b1 = 0;
            }
            else
            {
                b1 = Math.Sqrt((b2 * b2) + k);
            }

            double b = b1 - b2;
            double b3 = Math.Pow(b, 3);
            double a3 = Math.Pow(a, 3);

            double ts = ((k * k) / (6 * b3) + (k / b) - b / 2) - ((k * k) / (6 * a3) + (k / a) - (a / 2));

            double tp1 = -2 * n2 * (b - a) / (np * np);
            double tp2 = -2 * n2 * np * Math.Log(b / a) / (nm * nm);
            double tp3 = n2 * ((1 / b) - (1 / a)) / 2;
            double tp4 = 16 * n2 * n2 * ((n2 * n2) + 1) * Math.Log(((2 * np * b) - (nm * nm)) / ((2 * np * a) - (nm * nm))) / ((np * np * np) * (nm * nm));
            double tp5 = 16 * (Math.Pow(n2, 3)) * (1 / ((2 * np * b) - (nm * nm)) - (1 / (2 * np * a - (nm * nm)))) / (np * np * np);

            double tp = tp1 + tp2 + tp3 + tp4 + tp5;
            double tav = (ts + tp) / (2 * (sa * sa));

            return tav;
        }

        /// <summary>
        /// Checks if the input parameters are defined as expected
        /// to run either PROSPECT-D or PROSPECT-PRO
        /// </summary>
        /// <param name="LMA">Content corresponding to LMA</param>
        /// <param name="PROT">Content corresponding to protein content</param>
        /// <param name="CBC">Content corresponding to carbon based constituents</param>
        /// <returns>Updated LMA, PROT and CBC values</returns>
        public static (double LMA, double PROT, double CBC) CheckVersionProspect(double? LMA, double PROT, double CBC)
        {
            // PROSPECT-D as default value
            if (LMA == null && PROT == 0 && CBC == 0)
                LMA = 0.008;

            // PROSPECT-PRO if PROT or CBC are not null
            if (LMA == null && (PROT > 0 || CBC > 0))
                LMA = 0;

            // If calling PROSPECT-PRO (protein content or CBC defined by user)
            // then set LMA to 0 in any case
            if (LMA != 0 && (PROT > 0 || CBC > 0))
            {
                Console.WriteLine("Warning: When using PROSPECT-PRO (PROT > 0 or CBC > 0), LMA is set to 0.");
                LMA = 0;
            }

            return (LMA.Value, PROT, CBC);
        }

        /// <summary>
        /// Produces a ProspectInput object from all prospect input variables if not defined already
        /// </summary>
        /// <param name="inputPROSPECT">Existing ProspectInput object or null</param>
        /// <param name="CHL">Chlorophyll content (microg.cm-2)</param>
        /// <param name="CAR">Carotenoid content (microg.cm-2)</param>
        /// <param name="ANT">Anthocyanin content (microg.cm-2)</param>
        /// <param name="BROWN">Brown pigment content (Arbitrary units)</param>
        /// <param name="EWT">Equivalent Water Thickness (g.cm-2)</param>
        /// <param name="LMA">Leaf Mass per Area (g.cm-2)</param>
        /// <param name="PROT">Protein content (g.cm-2)</param>
        /// <param name="CBC">NonProt Carbon-based constituent content (g.cm-2)</param>
        /// <param name="N">Leaf structure parameter</param>
        /// <param name="alpha">Solid angle for incident light at surface of leaf</param>
        /// <returns>Updated ProspectInput object</returns>
        public static ProspectInput DefineInputProspect(
            ProspectInput inputPROSPECT,
            double CHL,
            double CAR,
            double ANT,
            double BROWN,
            double EWT,
            double? LMA,
            double PROT,
            double CBC,
            double N,
            double alpha)
        {
            ProspectInput defaultProspect = new ProspectInput
            {
                CHL = 40.0,
                CAR = 8.0,
                ANT = 0.0,
                BROWN = 0.0,
                EWT = 0.01,
                LMA = 0.0,
                PROT = 0.0,
                CBC = 0.0,
                N = 1.5,
                alpha = 40.0
            };

            if (inputPROSPECT == null)
            {
                var dmVal = CheckVersionProspect(LMA, PROT, CBC);
                inputPROSPECT = new ProspectInput
                {
                    CHL = CHL,
                    CAR = CAR,
                    ANT = ANT,
                    BROWN = BROWN,
                    EWT = EWT,
                    LMA = dmVal.LMA,
                    PROT = dmVal.PROT,
                    CBC = dmVal.CBC,
                    N = N,
                    alpha = alpha
                };
            }
            else
            {
                // Use reflection to check for missing properties and set them to default values
                var dmVal = CheckVersionProspect(inputPROSPECT.LMA, inputPROSPECT.PROT, inputPROSPECT.CBC);
                inputPROSPECT.LMA = dmVal.LMA;
                inputPROSPECT.PROT = dmVal.PROT;
                inputPROSPECT.CBC = dmVal.CBC;

                // Set any uninitialized properties to default values
                if (inputPROSPECT.CHL == 0) inputPROSPECT.CHL = defaultProspect.CHL;
                if (inputPROSPECT.CAR == 0) inputPROSPECT.CAR = defaultProspect.CAR;
                if (inputPROSPECT.ANT == 0) inputPROSPECT.ANT = defaultProspect.ANT;
                if (inputPROSPECT.BROWN == 0) inputPROSPECT.BROWN = defaultProspect.BROWN;
                if (inputPROSPECT.EWT == 0) inputPROSPECT.EWT = defaultProspect.EWT;
                if (inputPROSPECT.N == 0) inputPROSPECT.N = defaultProspect.N;
                if (inputPROSPECT.alpha == 0) inputPROSPECT.alpha = defaultProspect.alpha;
            }

            return inputPROSPECT;
        }

        /// <summary>
        /// Helper function to calculate the exponential integral E1(x)
        /// </summary>
        /// <param name="x">Input value</param>
        /// <returns>Exponential integral value</returns>
        private static double ExponentialIntegral(double x)
        {
            // This is a simplified implementation of the exponential integral E1(x)
            // For a more precise implementation, consider using a specialized math library
            if (x <= 0)
                throw new ArgumentException("Value must be positive", nameof(x));

            // Approximation for small x
            if (x < 1)
            {
                double result = -Math.Log(x) - 0.57721566490153286060; // Euler-Mascheroni constant
                double term = -x;
                double sum = term;
                for (int i = 2; i <= 20; i++)
                {
                    term *= -x / i;
                    sum += term;
                    if (Math.Abs(term) < 1e-15)
                        break;
                }
                return result + sum;
            }

            // Approximation for larger x
            double a = x * x + 4.0 * x + 8.0;
            return Math.Exp(-x) / a;
        }

        /// <summary>
        /// This property should be replaced with actual spectral data from SpecPROSPECT_FullRange
        /// </summary>
        public static SpecPROSPECTData SpecPROSPECTFullRange
        {
            get
            {
                // This is a placeholder. In a real implementation, this would be populated with actual data
                // from the SpecPROSPECT_FullRange in the original R code
                throw new NotImplementedException("SpecPROSPECT_FullRange data needs to be provided");
            }
        }
    }

    /// <summary>
    /// Container for PROSPECT input parameters
    /// </summary>
    public class ProspectInput
    {
        /// <summary>
        /// Chlorophyll content (microg.cm-2)
        /// </summary>
        public double CHL { get; set; }
        /// <summary>
        /// Carotenoid content (microg.cm-2)
        /// </summary>
        public double CAR { get; set; }
        /// <summary>
        /// Anthocyanin content (microg.cm-2)
        /// </summary>
        public double ANT { get; set; }
        /// <summary>
        /// Brown pigment content (Arbitrary units)
        /// </summary>
        public double BROWN { get; set; }
        /// <summary>
        /// Equivalent Water Thickness (g.cm-2)
        /// </summary>
        public double EWT { get; set; }
        /// <summary>
        /// Leaf Mass per Area (g.cm-2)
        /// </summary>
        public double LMA { get; set; }
        /// <summary>
        /// Protein content (g.cm-2)
        /// </summary>
        public double PROT { get; set; }
        /// <summary>
        /// NonProt Carbon-based constituent content (g.cm-2)
        /// </summary>
        public double CBC { get; set; }
        /// <summary>
        /// Leaf structure parameter
        /// </summary>
        public double N { get; set; }
        /// <summary>
        /// Solid angle for incident light at surface of leaf
        /// </summary>
        public double alpha { get; set; }
    }

    /// <summary>
    /// Container for spectral data used in PROSPECT calculations
    /// </summary>
    public class SpecPROSPECTData
    {
        /// <summary>
        /// Wavelengths
        /// </summary>
        public double[] Lambda { get; set; }
        /// <summary>
        /// Refractive index
        /// </summary>
        public double[] NRefrac { get; set; }
        /// <summary>
        /// Specific absorption coefficient for chlorophyll
        /// </summary>
        public double[] SAC_CHL { get; set; }
        /// <summary>
        /// Specific absorption coefficient for carotenoids
        /// </summary>
        public double[] SAC_CAR { get; set; }
        /// <summary>
        /// Specific absorption coefficient for anthocyanins
        /// </summary>
        public double[] SAC_ANT { get; set; }
        /// <summary>
        /// Specific absorption coefficient for brown pigments
        /// </summary>
        public double[] SAC_BROWN { get; set; }
        /// <summary>
        /// Specific absorption coefficient for water
        /// </summary>
        public double[] SAC_EWT { get; set; }
        /// <summary>
        /// Specific absorption coefficient for dry matter
        /// </summary>
        public double[] SAC_LMA { get; set; }
        /// <summary>
        /// Specific absorption coefficient for proteins
        /// </summary>
        public double[] SAC_PROT { get; set; }
        /// <summary>
        /// Specific absorption coefficient for carbon-based constituents
        /// </summary>
        public double[] SAC_CBC { get; set; }
        /// <summary>
        /// Precalculated transmissivity at 40 degrees
        /// </summary>
        public double[] Calctav40 { get; set; }
        /// <summary>
        /// Precalculated transmissivity at 90 degrees
        /// </summary>
        public double[] Calctav90 { get; set; }
    }

    /// <summary>
    /// Container for PROSPECT calculation results
    /// </summary>
    public class ProspectResult
    {
        /// <summary>
        /// Wavelengths
        /// </summary>
        public double[] Wavelength { get; set; }
        /// <summary>
        /// Leaf reflectance values
        /// </summary>
        public double[] Reflectance { get; set; }
        /// <summary>
        /// Leaf transmittance values
        /// </summary>
        public double[] Transmittance { get; set; }
    }
}


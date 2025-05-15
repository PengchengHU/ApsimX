using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using APSIM.Shared.Utilities;
using Models.Sail;
using static Models.Sail.SailUtilities;
using static Models.Prospect.ProspectCore;
using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra;
using static Models.Prospect.ProsailCore;



namespace Models.Prospect
{
    /// <summary>
    /// Implements the PRO4SAIL model combining PROSPECT and SAIL for canopy reflectance simulation.
    /// </summary>
    /// <remarks>
    /// This class integrates PROSPECT for leaf optical properties and SAIL (4SAIL or 4SAIL2) for canopy reflectance.
    /// Based on the PRO4SAIL function from Lib_PROSAIL.R by Jean-Baptiste Feret and Florian de Boissieu.
    /// Reference: Feret, J.-B., et al. (2019). PROSAIL model.
    /// </remarks>
    public class ProsailCore
    {
        // Relative path from APSIM bin directory to Models\PROSAIL\PROSPECT
        private static readonly string RelativeSoilOpticalDataPath = Path.Combine("Models", "PROSAIL", "PROSPECT", "SpecPROSPECT_FullRange.json");
        private static string DefaultSoilOpticalDataPath => PathUtilities.GetAbsolutePath(RelativeSoilOpticalDataPath, AppDomain.CurrentDomain.BaseDirectory);


        //private static readonly string RelativeSoilOpticalDataPath = "..\\..\\..\\Models\\PROSAIL\\PROSPECT\\SpecPROSPECT_FullRange.json";
        //private static string DefaultSoilOpticalDataPath => PathUtilities.GetAbsolutePath(RelativeSoilOpticalDataPath, AppDomain.CurrentDomain.BaseDirectory);

        /// <summary>
        /// Contains reflectance data of wet and dry soil
        /// </summary>
        public struct WetDrySoilReflectance
        {
            /// <summary>Wavelength array in nanometers (nm)</summary>
            public Vector<double> Wavelength;
            /// <summary>Reflectance of dry soil</summary>
            public Vector<double> ReflectanceDry;
            /// <summary>Reflectance of dry soil</summary>
            public Vector<double> ReflectanceWet;
        }

        /// <summary>
        /// Runs the PRO4SAIL simulation to compute canopy reflectance factors.
        /// </summary>
        /// <param name="leafOpticalConstants">Leaf optical constants for PROSPECT. If null, loads default from file.</param>
        /// <param name="inputProspectList">List of PROSPECT input parameters from SailUtilities. First element for green leaves, second (optional) for brown leaves.</param>
        /// <param name="N">Leaf structure parameter (unitless). Default is 1.5.</param>
        /// <param name="CAB">Chlorophyll a + b content (μg/cm²). Default is 40.0.</param>
        /// <param name="CAR">Carotenoid content (μg/cm²). Default is 8.0.</param>
        /// <param name="ANT">Anthocyanin content (μg/cm²). Default is 0.0.</param>
        /// <param name="BROWN">Brown pigment content (arbitrary units). Default is 0.0.</param>
        /// <param name="EWT">Equivalent Water Thickness (g/cm²). Default is 0.01.</param>
        /// <param name="LMA">Leaf Mass per Area (g/cm²). Default is 0.008.</param>
        /// <param name="PROT">Protein content (g/cm²). Default is 0.0.</param>
        /// <param name="CBC">Non-protein carbon-based constituent content (g/cm²). Default is 0.0.</param>
        /// <param name="Alpha">Incidence angle in degrees. Default is 40.0.</param>
        /// <param name="Wavelengths">Array of specific wavelengths to simulate (subset of OpticalConstants.Wavelength, if null, all wavelengths used).</param>
        /// <param name="TypeLidf">Type of leaf inclination distribution function (1 for Verhoef, 2 for Campbell). Default is 2.</param>
        /// <param name="LIDFa">LIDF parameter a (average leaf slope if TypeLidf=1, angle if TypeLidf=2). Default is 60.</param>
        /// <param name="LIDFb">LIDF parameter b (bimodality if TypeLidf=1, null if TypeLidf=2). Default is null.</param>
        /// <param name="lai">Leaf Area Index. Default is 3.0.</param>
        /// <param name="q">Hot Spot parameter (0-1). Default is 0.1.</param>
        /// <param name="tts">Sun zenith angle in degrees (0-90). Default is 30.0.</param>
        /// <param name="tto">Observer zenith angle in degrees (0-90). Default is 0.0.</param>
        /// <param name="psi">Relative azimuth angle between sun and observer in degrees (0-360). Default is 60.0.</param>
        /// <param name="soilOptics">An object of SoilSpectra strcut containing wavelengths and soil reflectance spectrum. If null, uses psoil (Dry/Wet soil factor).</param>
        /// <param name="psoil">Dry/Wet soil factor (0 for wet soil and 1 for dry soil). Will be used when soilSpectra is null.</param>
        /// <param name="fractionBrown">Fraction of brown leaf area (0-1). Default is 0.0.</param>
        /// <param name="diss">Layer dissociation factor (0-1). Default is 0.0.</param>
        /// <param name="cv">Vertical crown cover percentage (0-1). Default is 1.0.</param>
        /// <param name="zeta">Tree shape factor (ratio of crown diameter to height, positive). Default is 1.0.</param>
        /// <param name="sailVersion">SAIL version to use ('4SAIL' or '4SAIL2'). Default is '4SAIL'.</param>
        /// <param name="brownLOP">Brown leaf optical properties. If null, generated by PROSPECT for 4SAIL2 if inputProspectList has two elements.if inputProspectList has two elements. Default is null.</param>
        /// <returns>A SailResult object containing canopy reflectance factors (rdot, rsot, rddt, rsdt, fCover, abs_dir, abs_hem, rsdstar, rddstar).</returns>
        /// <exception cref="ArgumentException">Thrown if input parameters are invalid or array lengths mismatch.</exception>
        /// <exception cref="ArgumentNullException">Thrown if required inputs are null for specific configurations.</exception>
        /// <exception cref="FileNotFoundException">Thrown if spectral data file is missing when leafOpticalConstants is null.</exception>
        public SailResult PRO4SAIL(
            LeafOpticalConsts? leafOpticalConstants = null,
            List<ProspectInputs> inputProspectList = null,
            double N = 1.5,
            double CAB = 40.0,
            double CAR = 8.0,
            double ANT = 0.0,
            double BROWN = 0.0,
            double EWT = 0.01,
            double LMA = 0.008,
            double PROT = 0.0,
            double CBC = 0.0,
            double Alpha = 40.0,
            double[] Wavelengths = null,
            int TypeLidf = 2,
            double LIDFa = 60.0,
            double? LIDFb = null,
            double lai = 3.0,
            double q = 0.1,
            double tts = 30.0,
            double tto = 0.0,
            double psi = 60.0,
            SoilOptics? soilOptics = null,
            double? psoil = null,
            double fractionBrown = 0.0,
            double diss = 0.0,
            double cv = 1.0,
            double zeta = 1.0,
            string sailVersion = "4SAIL",
            LeafOptics? brownLOP = null)
        {
            // Validate inputs
            if (!new[] { "4SAIL", "4SAIL2" }.Contains(sailVersion))
                throw new ArgumentException("SAILversion must be '4SAIL' or '4SAIL2'.");
            if (fractionBrown < 0 || fractionBrown > 1)
                throw new ArgumentOutOfRangeException(nameof(fractionBrown), "fractionBrown must be between 0 and 1.");
            if (diss < 0 || diss > 1)
                throw new ArgumentOutOfRangeException(nameof(diss), "diss must be between 0 and 1.");
            if (cv < 0 || cv > 1)
                throw new ArgumentOutOfRangeException(nameof(cv), "Cv must be between 0 and 1.");
            if (zeta < 0)
                throw new ArgumentOutOfRangeException(nameof(zeta), "Zeta cannot be negative.");
            if (TypeLidf != 1 && TypeLidf != 2)
                throw new ArgumentException("TypeLidf must be 1 (Verhoef) or 2 (Campbell).");
            if (TypeLidf == 1 && !LIDFb.HasValue)
                throw new ArgumentException("LIDFb is required when TypeLidf is 1.");

            // Load spectral constants if not provided
            LeafOpticalConsts leafOpticalData = leafOpticalConstants ?? LoadLocalLeafOpticalData();
            
            // Prepare soil data
            SoilOptics soilOpticalData;
            if (soilOptics.HasValue && soilOptics.Value.HasValue)
            {
                soilOpticalData = soilOptics.Value;
            }
            else if (psoil.HasValue && psoil.Value > 0 && psoil.Value < 1) // if no soil optical data provided, use psoil to calculate 
            {
                WetDrySoilReflectance wetDrySoilReflectance = LoadLocalWetDrySoilOpticalData();
                // Create the wavelength-to-index mapping for speeding up the subset of the specified wavelengths
                var wavelengthToIndex = new Dictionary<double, int>();
                for (int i = 0; i < wetDrySoilReflectance.Wavelength.Count; i++)
                {
                    wavelengthToIndex[wetDrySoilReflectance.Wavelength[i]] = i;
                }

                // Calculate the weighted reflectance vector
                Vector<double> weightedReflectance = psoil.Value * wetDrySoilReflectance.ReflectanceDry +
                                                     (1 - psoil.Value) * wetDrySoilReflectance.ReflectanceWet;

                soilOpticalData = new SoilOptics
                {
                    Wavelength = wetDrySoilReflectance.Wavelength,
                    Reflectance = weightedReflectance,
                    WavelengthToIndex = wavelengthToIndex
                };
            } else
            {
                throw new ArgumentException("Either soilOptics with valid data or psoil must be provided.");
            }

            // Handle custom wavelengths for soil optical data
            if (Wavelengths != null && Wavelengths.Length > 0)
            {
                // Validate that all specified wavelengths are in soilOpticalData.WavelengthToIndex
                foreach (double w in Wavelengths)
                {
                    if (!soilOpticalData.WavelengthToIndex.ContainsKey(w))
                    {
                        throw new ArgumentException($"Wavelength {w} nm is not in the soil optical data wavelengths.");
                    }
                }

                // Map wavelengths to their indices in the original array
                var indices = Wavelengths.Select(w => soilOpticalData.WavelengthToIndex[w]).ToArray();

                // Create a new SoilOptics with only the specified wavelengths               
                soilOpticalData = new SoilOptics
                {
                    Wavelength = Vector<double>.Build.DenseOfArray(Wavelengths),
                    Reflectance = Vector<double>.Build.DenseOfEnumerable(indices.Select(i => soilOpticalData.Reflectance[i])),
                    WavelengthToIndex = Wavelengths.Select((w, i) => new { Wavelength = w, Index = i })
                                                 .ToDictionary(x => x.Wavelength, x => x.Index)
                };
            }

            // Prepare PROSPECT inputs
            if (inputProspectList == null || inputProspectList.Count == 0)
            {
                inputProspectList = new List<ProspectInputs>
                {
                    new ProspectInputs(
                        n:N, cab:CAB, car:CAR, ant:ANT, brown:BROWN, ewt:EWT, lma:LMA, prot:PROT, cbc: CBC, alpha: Alpha, wavelengths:Wavelengths)
                };
            }

            // Adjust PROSPECT inputs for SAIL
            AdjustedProspectResult adjustedResults = AdjustProspectToSail(
                  sailVersion: sailVersion, 
                  leafOpticalConstants: leafOpticalData, 
                  inputProspectList: inputProspectList, 
                  fractionBrown: fractionBrown, 
                  brownLOP: brownLOP.HasValue ? brownLOP: null);

            // Run SAIL simulation based on version
            SailResult result;
            if (sailVersion == "4SAIL")
            {
                result = SailCore.FourSAIL(
                    leafOptics: adjustedResults.GreenLOP,
                    typeLidf: TypeLidf,
                    lidfA: LIDFa,
                    lidfB: LIDFb,
                    lai: lai,
                    q: q,
                    tts: tts,
                    tto: tto,
                    psi: psi,
                    soilOptics: soilOpticalData);
            }
            else // 4SAIL2
            {
                result = SailCore.FourSAIL2(
                    leafGreen: adjustedResults.GreenLOP,
                    leafBrown: (LeafOptics)adjustedResults.BrownLOP,
                    typeLidf: TypeLidf,
                    lidfA: LIDFa,
                    lidfB: LIDFb,
                    lai: lai,
                    q: q,
                    tts: tts,
                    tto: tto,
                    psi: psi,
                    soilOptics: soilOpticalData,
                    fractionBrown: fractionBrown,
                    diss: diss,
                    cv: cv,
                    zeta: zeta);
            }

            return result;
        }


        /// <summary>
        /// Load spectral data of wet and dry soil from a local JSON file
        /// </summary>
        public static WetDrySoilReflectance LoadLocalWetDrySoilOpticalData()
        {
            string path = DefaultSoilOpticalDataPath;
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Soil optical data file not found at {path}. Please provide a valid SoilOptics or ensure the file exists.");
            }

            try
            {
                string json = File.ReadAllText(path);
                var OpticalData = JsonConvert.DeserializeObject<WetDrySoilOpticalDataJason>(json);

                return new WetDrySoilReflectance
                {
                    Wavelength = Vector<double>.Build.DenseOfArray(OpticalData.Wavelength),
                    ReflectanceDry = Vector<double>.Build.DenseOfArray(OpticalData.Dry_Soil),
                    ReflectanceWet = Vector<double>.Build.DenseOfArray(OpticalData.Wet_Soil)
                };
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load soil optical data from {DefaultSoilOpticalDataPath}: {ex.Message}", ex);
            }
        }

        // Helper class for JSON deserialization
        private class WetDrySoilOpticalDataJason
        {
            public double[] Wavelength { get; set; }
            public double[] Dry_Soil { get; set; }
            public double[] Wet_Soil { get; set; }
        }

    }
}
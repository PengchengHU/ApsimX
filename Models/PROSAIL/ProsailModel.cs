using APSIM.Shared.Utilities;
using MathNet.Numerics.LinearAlgebra;
using Models.Core;
using Models;
using Models.Functions;
using Models.PMF;
using APSIM.Core;
using Models.Prosail;
using Models.PROSAIL.BSM;
using Models.PROSAIL.PROSPECT;
using Models.PROSAIL.SAIL;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using static Models.Prosail.ProsailCore;
using static Models.PROSAIL.PROSPECT.ProspectCore;
using static Models.PROSAIL.SAIL.SailUtilities;

namespace Models.PROSAIL
{
    /// <summary>
    /// Model implementing the PROSAIL radiative transfer model for canopy optical properties in APSIM
    /// with configurable parameter expressions and spectral data output to SQLite.
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Plant))]
    public class ProsailModel : Model, IStructureDependency
    {
        #region Links to other APSIM Components
        /// <summary>Link to the clock for daily outputs</summary>
        [Link]
        private Clock Clock = null;

        /// <summary>Link to summary file for outputs</summary>
        [Link]
        private ISummary Summary = null;

        /// <summary>Link to simulation for file paths and name</summary>
        [Link]
        private Simulation Simulation = null;

        /// <summary>Link to the parent Plant model to check IsAlive</summary>
        [Link(IsOptional = true)]
        private Plant ParentPlant = null;

        /// <summary>Structure instance supplied by APSIM.core.</summary>
        public IStructure Structure { private get; set; }

        #endregion

        #region Model Configuration
        /// <summary>SAIL model version selection</summary>
        [Separator("Model Configuration")]
        [Description("SAIL version")]
        [Tooltip("4SAIL: single layer canopy model. 4SAIL2: two layer canopy model (green + brown leaves).")]
        public SailVersionTypes SailVersion
        {
            get => sailVersion;
            set => sailVersion = value;
        }
        private SailVersionTypes sailVersion = SailVersionTypes.FourSAIL;

        /// <summary>Returns the SAIL version string used internally by the model core.</summary>
        private string SailVersionString => SailVersion == SailVersionTypes.FourSAIL2 ? "4SAIL2" : "4SAIL";

        /// <summary>True when the two-layer green/brown canopy model (4SAIL2) is selected.</summary>
        public bool IsFourSAIL2 => SailVersion == SailVersionTypes.FourSAIL2;

        /// <summary>Soil reflectance model selection.</summary>
        [Description("Soil reflectance model")]
        [Tooltip("WetDryMixing: linear mix of wet/dry soil reflectance spectra weighted by Psoil. BSM: Brightness Soil Model (Verhoef et al. 2018) driven by BsmBrightness/BsmLat/BsmLon/SMp.")]
        public SoilReflectanceModelTypes SoilReflectanceModel { get; set; } = SoilReflectanceModelTypes.WetDryMixing;

        /// <summary>Returns true when BSM is selected.</summary>
        public bool IsBSM => SoilReflectanceModel == SoilReflectanceModelTypes.BSM;

        /// <summary>Returns true when BSM is NOT selected.</summary>
        public bool IsNotBSM => !IsBSM;

        /// <summary>Spectral range to simulate (start-end in nm)</summary>
        [Description("Spectral range (nm)")]
        [Tooltip("Supports ranges (e.g., '400-500'), lists (e.g., '400, 500, 600'), and mixed formats (e.g., '400, 500-600, 700'). Default: 400-2500.")]
        public string InputWavelengthRange { get; set; } = "400-2500";
        #endregion

        #region PROSAIL Input Parameters (Expressions)
        /// <summary>The expression for N (Leaf structure parameter)</summary>
        [Separator("Leaf Properties (PROSPECT)")]
        [Separator("Green Leaf")]
        [Description("N - Leaf structure (unitless)")]
        [Tooltip("Leaf structure parameter. Can be a literal value or an APSIM expression" +
        "(e.g., IIF([Wheat].Leaf.SpecificAreaCanopy * 10 <= 0.1, 1.6, (0.9 * [Wheat].Leaf.SpecificAreaCanopy * 10 + 0.025) / ([Wheat].Leaf.SpecificAreaCanopy * 10 - 0.1)) )." +
        "Typical range: 1.0-2.6. Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 1.0, Upper = 2.6)]
        public string N { get; set; } = "1.5";

        /// <summary>The expression for CAB (Chlorophyll a + b content)</summary>
        [Description("CAB - Chlorophyll a+b (\u03BCg/cm\u00B2)")]
        [Tooltip("Chlorophyll a + b content. Can be a literal value or an APSIM expression (e.g., [Wheat].Leaf.SpecificNitrogen * 26 ). Typical range: 10-80 \u03BCg/cm\u00B2. Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 10.0, Upper = 80.0)]
        public string CAB { get; set; } = "40.0";

        /// <summary>The expression for CAR (Carotenoid content)</summary>
        [Description("CAR - Carotenoid (\u03BCg/cm\u00B2)")]
        [Tooltip("Carotenoid content. Can be a literal or APSIM expression (e.g., [Wheat].Leaf.SpecificNitrogen * 26 * 0.216 ). Typical range: 1-24 \u03BCg/cm\u00B2. Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 1.0, Upper = 24.0)]
        public string CAR { get; set; } = "8.0";

        /// <summary>The expression for EWT (Equivalent Water Thickness)</summary>
        [Description("EWT - Water thickness (cm)")]
        [Tooltip("Equivalent Water Thickness (CW). Can be a literal or APSIM expression. Typical range: 0.001-0.08 cm. Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 0.001, Upper = 0.08)]
        public string EWT { get; set; } = "0.01";

        /// <summary>The expression for LMA (Leaf Mass per Area)</summary>
        [Description("LMA - Dry matter (g/cm\u00B2)")]
        [Tooltip("Leaf Mass per Area (CM). Can be a literal or APSIM expression (e.g., (1 / ([Wheat].Leaf.SpecificAreaCanopy + 0.0001)) * 10^(-4) ). Typical range: 0.001-0.02 g/cm\u00B2. Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 0.001, Upper = 0.02)]
        public string LMA { get; set; } = "0.008";

        /// <summary>The expression for BROWN (Brown pigment content)</summary>
        [Description("BROWN - Brown pigment (unitless)")]
        [Tooltip("Brown pigment content. Can be a literal or APSIM expression. Typical range: 0-1. Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 0.0, Upper = 1.0)]
        public string BROWN { get; set; } = "0.0";

        /// <summary>The expression for ANT (Anthocyanin content)</summary>
        [Description("ANT - Anthocyanin (\u03BCg/cm\u00B2)")]
        [Tooltip("Anthocyanin content. Can be a literal or APSIM expression. Typical range: 0-10 \u03BCg/cm\u00B2. Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 0.0, Upper = 10.0)]
        public string ANT { get; set; } = "0.0";

        /// <summary>The expression for PROT (Protein content)</summary>
        [Description("PROT - Protein (g/cm\u00B2)")]
        [Tooltip("Protein content. Can be a literal or APSIM expression. Typical range: 0-10 g/cm\u00B2. Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 0.0, Upper = 10.0)]
        public string PROT { get; set; } = "0.0";

        /// <summary>The expression for CBC (NonProt Carbon-based constituent content)</summary>
        [Description("CBC - Carbon-based constituent (g/cm\u00B2)")]
        [Tooltip("Non-protein carbon-based constituent content. Can be a literal or APSIM expression. Typical range: 0-10 g/cm\u00B2. Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 0.0, Upper = 10.0)]
        public string CBC { get; set; } = "0.0";

        /// <summary>The expression for alpha (Incidence angle in degrees)</summary>
        [Description("Alpha - Incidence angle (\u00B0)")]
        [Tooltip("Incidence angle in degrees. Can be a literal or APSIM expression. Typical range: 0-90\u00B0. Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 0.0, Upper = 90.0)]
        public string Alpha { get; set; } = "40.0";

        /// <summary>The expression for N of the brown/senesced leaf class (4SAIL2 only).</summary>
        [Separator("Brown Leaf")]
        [Description("NBrown - Leaf structure of the brown leaf class (unitless)")]
        [Tooltip("Leaf structure parameter for the brown/senesced leaf class. Only used when SailVersion=4SAIL2 " +
        "and FractionBrown > 0. Can be a literal value or an APSIM expression. Typical range: 1.0-2.6. " +
        "Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 1.0, Upper = 2.6)]
        [Display(VisibleCallback = nameof(IsFourSAIL2))]
        public string NBrown { get; set; } = "1.5";

        /// <summary>The expression for CAB of the brown/senesced leaf class (4SAIL2 only).</summary>
        [Description("CABBrown - Chlorophyll a+b of the brown leaf class (\u03BCg/cm\u00B2)")]
        [Tooltip("Chlorophyll a + b content for the brown/senesced leaf class. Only used when SailVersion=4SAIL2 " +
        "and FractionBrown > 0. Can be a literal or APSIM expression. Typical range: 10-80 \u03BCg/cm\u00B2. " +
        "Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 10.0, Upper = 80.0)]
        [Display(VisibleCallback = nameof(IsFourSAIL2))]
        public string CABBrown { get; set; } = "40.0";

        /// <summary>The expression for CAR of the brown/senesced leaf class (4SAIL2 only).</summary>
        [Description("CARBrown - Carotenoid of the brown leaf class (\u03BCg/cm\u00B2)")]
        [Tooltip("Carotenoid content for the brown/senesced leaf class. Only used when SailVersion=4SAIL2 " +
        "and FractionBrown > 0. Can be a literal or APSIM expression. Typical range: 1-24 \u03BCg/cm\u00B2. " +
        "Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 1.0, Upper = 24.0)]
        [Display(VisibleCallback = nameof(IsFourSAIL2))]
        public string CARBrown { get; set; } = "8.0";

        /// <summary>The expression for EWT of the brown/senesced leaf class (4SAIL2 only).</summary>
        [Description("EWTBrown - Water thickness of the brown leaf class (cm)")]
        [Tooltip("Equivalent Water Thickness for the brown/senesced leaf class. Only used when SailVersion=4SAIL2 " +
        "and FractionBrown > 0. Can be a literal or APSIM expression. Typical range: 0.001-0.08 cm. " +
        "Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 0.001, Upper = 0.08)]
        [Display(VisibleCallback = nameof(IsFourSAIL2))]
        public string EWTBrown { get; set; } = "0.01";

        /// <summary>The expression for LMA of the brown/senesced leaf class (4SAIL2 only).</summary>
        [Description("LMABrown - Dry matter of the brown leaf class (g/cm\u00B2)")]
        [Tooltip("Leaf Mass per Area for the brown/senesced leaf class. Only used when SailVersion=4SAIL2 " +
        "and FractionBrown > 0. Can be a literal or APSIM expression. Typical range: 0.001-0.02 g/cm\u00B2. " +
        "Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 0.001, Upper = 0.02)]
        [Display(VisibleCallback = nameof(IsFourSAIL2))]
        public string LMABrown { get; set; } = "0.008";

        /// <summary>The expression for the brown pigment content of the brown/senesced leaf class itself (4SAIL2 only).</summary>
        [Description("BROWNBrown - Brown pigment of the brown leaf class (unitless)")]
        [Tooltip("Brown pigment content of the brown/senesced leaf class itself (a second, independent brown-pigment " +
        "loading on top of that leaf class already being the 'brown' one in the green/brown mix). Only used when " +
        "SailVersion=4SAIL2 and FractionBrown > 0. Can be a literal or APSIM expression. Typical range: 0-1. " +
        "Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 0.0, Upper = 1.0)]
        [Display(VisibleCallback = nameof(IsFourSAIL2))]
        public string BROWNBrown { get; set; } = "0.0";

        /// <summary>The expression for ANT of the brown/senesced leaf class (4SAIL2 only).</summary>
        [Description("ANTBrown - Anthocyanin of the brown leaf class (\u03BCg/cm\u00B2)")]
        [Tooltip("Anthocyanin content for the brown/senesced leaf class. Only used when SailVersion=4SAIL2 " +
        "and FractionBrown > 0. Can be a literal or APSIM expression. Typical range: 0-10 \u03BCg/cm\u00B2. " +
        "Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 0.0, Upper = 10.0)]
        [Display(VisibleCallback = nameof(IsFourSAIL2))]
        public string ANTBrown { get; set; } = "0.0";

        /// <summary>The expression for PROT of the brown/senesced leaf class (4SAIL2 only).</summary>
        [Description("PROTBrown - Protein of the brown leaf class (g/cm\u00B2)")]
        [Tooltip("Protein content for the brown/senesced leaf class. Only used when SailVersion=4SAIL2 " +
        "and FractionBrown > 0. Can be a literal or APSIM expression. Typical range: 0-10 g/cm\u00B2. " +
        "Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 0.0, Upper = 10.0)]
        [Display(VisibleCallback = nameof(IsFourSAIL2))]
        public string PROTBrown { get; set; } = "0.0";

        /// <summary>The expression for CBC of the brown/senesced leaf class (4SAIL2 only).</summary>
        [Description("CBCBrown - Carbon-based constituent of the brown leaf class (g/cm\u00B2)")]
        [Tooltip("Non-protein carbon-based constituent content for the brown/senesced leaf class. Only used when " +
        "SailVersion=4SAIL2 and FractionBrown > 0. Can be a literal or APSIM expression. Typical range: 0-10 g/cm\u00B2. " +
        "Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 0.0, Upper = 10.0)]
        [Display(VisibleCallback = nameof(IsFourSAIL2))]
        public string CBCBrown { get; set; } = "0.0";

        /// <summary>The expression for alpha of the brown/senesced leaf class (4SAIL2 only).</summary>
        [Description("AlphaBrown - Incidence angle of the brown leaf class (\u00B0)")]
        [Tooltip("Incidence angle in degrees for the brown/senesced leaf class. Only used when SailVersion=4SAIL2 " +
        "and FractionBrown > 0. Can be a literal or APSIM expression. Typical range: 0-90\u00B0. " +
        "Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 0.0, Upper = 90.0)]
        [Display(VisibleCallback = nameof(IsFourSAIL2))]
        public string AlphaBrown { get; set; } = "40.0";

        /// <summary>The expression for Leaf Area Index (LAI)</summary>
        [Separator("Canopy Properties (SAIL)")]
        [Description("LAI - Leaf Area Index (m\u00B2/m\u00B2)")]
        [Tooltip("Leaf Area Index. Can be a literal or APSIM expression (e.g., [Wheat].Leaf.LAI). Typical range: 0-10 m\u00B2/m\u00B2. Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 0.0, Upper = 10.0)]
        public string LAI { get; set; } = "3.0";

        /// <summary>The expression for the Hot Spot parameter (q).</summary>
        [Description("q - Hot Spot (unitless)")]
        [Tooltip("Hot Spot parameter. Can be a literal or APSIM expression. Typical range: 0-1. Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 0.0, Upper = 1.0)]
        public string HotSpot { get; set; } = "0.1";

        /// <summary>The expression for the LIDF type.</summary>
        [Description("TypeLidf - LIDF type")]
        [Tooltip("1 for Verhoef (uses LIDFa and LIDFb), 2 for Campbell (uses LIDFa only as mean leaf angle). Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 1.0, Upper = 2.0)]
        public string TypeLidf { get; set; } = "2";

        /// <summary>The expression for LIDF parameter 'a'.</summary>
        [Description("LIDFa - Average leaf slope/angle")]
        [Tooltip("Average leaf slope (TypeLidf=1, range -1 to 1) or mean leaf angle in degrees (TypeLidf=2, range -90 to 90). Can be a literal or APSIM expression. Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        public string LIDFa { get; set; } = "60.0";

        /// <summary>The expression for LIDF parameter 'b'.</summary>
        [Description("LIDFb - Bimodality")]
        [Tooltip("Bimodality parameter for Verhoef LIDF (TypeLidf=1 only). Ignored for Campbell. Typical range: -1 to 1. Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = -1.0, Upper = 1.0)]
        public string LIDFb { get; set; } = "-0.35";

        /// <summary>The expression for the fraction of brown leaf area.</summary>
        [Description("FractionBrown - Brown leaf fraction")]
        [Tooltip("Fraction of brown/senesced leaf area (unitless, 0-1). Used with 4SAIL2 version. Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 0.0, Upper = 1.0)]
        [Display(VisibleCallback = nameof(IsFourSAIL2))]
        public string FractionBrown { get; set; } = "0.0";

        /// <summary>The expression for the layer dissociation factor (diss).</summary>
        [Description("Diss - Dissociation factor")]
        [Tooltip("Layer dissociation factor for green/brown leaves (unitless, 0-1). Used with 4SAIL2 version. Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 0.0, Upper = 1.0)]
        [Display(VisibleCallback = nameof(IsFourSAIL2))]
        public string Dissociation { get; set; } = "0.0";

        /// <summary>The expression for the vertical crown cover percentage (cv).</summary>
        [Description("Cv - Crown cover")]
        [Tooltip("Vertical crown cover percentage (unitless, 0-1). Used with 4SAIL2 version. Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 0.0, Upper = 1.0)]
        [Display(VisibleCallback = nameof(IsFourSAIL2))]
        public string CrownCover { get; set; } = "1.0";

        /// <summary>The expression for the tree shape factor (zeta).</summary>
        [Description("Zeta - Tree shape factor")]
        [Tooltip("Tree shape factor: crown diameter to height ratio (unitless). Used with 4SAIL2 version. Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Bounds(Lower = 0.0, Upper = 10.0)]
        [Display(VisibleCallback = nameof(IsFourSAIL2))]
        public string TreeShape { get; set; } = "1.0";
        #endregion

        #region Sun-Observer Geometry
        /// <summary>Observation dates specified via the UI.</summary>
        [Separator("Sun-Observer Geometry")]
        [Description("Observation dates")]
        [Tooltip("Dates on which PROSAIL will run. If empty, PROSAIL runs every day the plant is alive and emerged. Dates outside the simulation range are ignored with a warning.")]
        public DateTime[] ObservationDates { get; set; }

        /// <summary>The expression or per-date list for the sun zenith angle (tts).</summary>
        [Description("TTS - Sun zenith angle (\u00B0)")]
        [Tooltip("Sun zenith angle (°, 0-90). Accepts: a single literal (e.g. 30) applied to all dates or a comma-separated list of one value per observation date (e.g. 25, 30, 35). Defaults to 90°.")]
        [Bounds(Lower = 0.0, Upper = 90.0)]
        public string SunZenithAngle { get; set; } = "90";

        /// <summary>The expression or per-date list for the observer zenith angle (tto).</summary>
        [Description("TTO - Observer zenith angle (\u00B0)")]
        [Tooltip("Observer (sensor) zenith angle (°, 0-90). Accepts: a single literal (e.g. 0) or a comma-separated list of one value per observation date (e.g. 0, 30, 60). Defaults to 0°.")]
        [Bounds(Lower = 0.0, Upper = 90.0)]
        public string ObserverZenithAngle { get; set; } = "0";

        /// <summary>The expression or per-date list for the relative azimuth angle (psi).</summary>
        [Description("PSI - Relative azimuth angle (\u00B0)")]
        [Tooltip("Relative azimuth angle between sun and observer (°, 0-360). Accepts: a single literal (e.g. 0) or a comma-separated list of one value per observation date (e.g. 0, 90, 180). Defaults to 0°.")]
        [Bounds(Lower = 0.0, Upper = 360.0)]
        public string RelativeAzimuthAngle { get; set; } = "0";

        /// <summary>True when observation dates are specified in the UI and at least one is set.</summary>
        public bool HasObservationDatesInUI => ObservationDates != null && ObservationDates.Length > 0;
        #endregion

        #region Soil Reflectance
        /// <summary>Path to wet/dry soil reflectance data file.</summary>
        [Separator("Soil Reflectance")]
        [Description("Wet/dry soil reflectance file")]
        [Tooltip("Optional CSV file (columns: Wavelength, Dry_Soil, Wet_Soil) to override the built-in SpecSOIL.json data. Leave empty to use the built-in default.")]
        [Display(Type = DisplayType.FileName, VisibleCallback = nameof(IsNotBSM))]
        public string WetDrySoilReflectancePath { get; set; }

        /// <summary>Psoil — dry-to-wet mixing factor, per-date list, or APSIM expression.</summary>
        [Description("Psoil - Soil dry-to-wet factor")]
        [Tooltip("Dry-to-wet mixing factor (0 = fully wet, 1 = fully dry). Accepts: a single literal (e.g., 0.5) applied to all dates; a comma-separated list of one value per observation date (e.g. 0.3, 0.5, 0.7); or an APSIM expression evaluated each day (e.g. 1 - [WaterBalance].SW[1]). Defaults to 1 \u2212 [WaterBalance].SW[1].")]
        [Display(VisibleCallback = nameof(IsNotBSM))]
        [Bounds(Lower = 0.0, Upper = 1.0)]
        public string Psoil { get; set; } = "1 - [WaterBalance].SW[1]";

        /// <summary>BSM soil brightness parameter.</summary>
        [Separator("Soil Reflectance")]
        [Description("BsmBrightness - Soil brightness (0-1)")]
        [Tooltip("Soil brightness scaling factor (0\u20131) for BSM. Scales the magnitude of the dry soil spectrum. Enter a literal (e.g., 0.5). Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Display(VisibleCallback = nameof(IsBSM))]
        public string BsmBrightness { get; set; } = "0.5";

        /// <summary>BSM spectral shape latitude.</summary>
        [Description("BsmLat - Spectral shape latitude (20-40\u00B0)")]
        [Tooltip("Spectral latitude for BSM (recommended 20\u201340\u00B0). Controls the spectral shape of the dry soil spectrum. Enter a literal (e.g., 25). Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Display(VisibleCallback = nameof(IsBSM))]
        public string BsmLat { get; set; } = "25";

        /// <summary>BSM spectral shape longitude.</summary>
        [Description("BsmLon - Spectral shape longitude (45-65\u00B0)")]
        [Tooltip("Spectral longitude for BSM (recommended 45\u201365\u00B0). Controls the spectral shape of the dry soil spectrum. Enter a literal (e.g., 45) or an APSIM expression. Evaluated daily only; does not support a per-observation-date list (see the Introduction node).")]
        [Display(VisibleCallback = nameof(IsBSM))]
        public string BsmLon { get; set; } = "45";

        /// <summary>SMp — soil moisture percentage, per-date list, or APSIM expression.</summary>
        [Description("SMp - Soil moisture percentage (5-55%)")]
        [Tooltip("Soil moisture volume percentage (5\u201355%) for BSM. Accepts: a single literal (e.g., 25) applied to all dates; a comma-separated list of one value per observation date (e.g. 20, 25, 30); or an APSIM expression evaluated each day (e.g. [WaterBalance].SW[1] * 100). Defaults to [WaterBalance].SW[1] * 100.")]
        [Display(VisibleCallback = nameof(IsBSM))]
        public string SMp { get; set; } = "[WaterBalance].SW[1] * 100";
        #endregion

        /// <summary>Holds the loaded spectral response function for the selected sensor.</summary>
        public SpectralResponseFunction SensorSRF { get; private set; }

        /// <summary>Mapping from sensor enum to local SRF file path.</summary>
        private readonly Dictionary<SensorTypes, string> sensorFileMap = new()
        {
            {SensorTypes.Landsat_7, "Landsat_7" },
            {SensorTypes.Landsat_8, "Landsat_8" },
            {SensorTypes.Landsat_9, "Landsat_9" },
            {SensorTypes.MODIS, "MODIS" },
            {SensorTypes.Pleiades_1A, "Pleiades_1A" },
            {SensorTypes.Pleiades_1B, "Pleiades_1B" },
            {SensorTypes.Sentinel_2, "Sentinel_2" },
            {SensorTypes.Sentinel_2A, "Sentinel_2A" },
            {SensorTypes.Sentinel_2B, "Sentinel_2B" },
            {SensorTypes.Sentinel_2C, "Sentinel_2C" },
            {SensorTypes.SPOT_6_7, "SPOT_6_7" },
            {SensorTypes.Venus, "Venus" }
        };

        /// <summary>Loads the SRF for the currently selected sensor.</summary>
        private void SetSensorSRF()
        {
            if (SensorType == SensorTypes.None || SensorType == SensorTypes.Custom)
            {
                SensorSRF = null; // None: not set; Custom: loaded in OnCommencing
                return;
            }
            if (sensorFileMap.TryGetValue(SensorType, out var filePath))
            {
                filePath = Path.Combine(AppContext.BaseDirectory, "PROSAIL", "InputProperties", "SpectralResponseFunctions", $"{filePath}.json");
                SensorSRF = LoadSpectralResponseFunction(filePath);
            }
            else
            {
                SensorSRF = null;
                throw new ArgumentException($"Unknown sensor: {SensorType}");
            }
        }

        #region Simulation and Output Control
        /// <summary>Whether to write the Parameters table to the database.</summary>
        [Separator("Simulation and Output Control")]
        [Description("Save input parameters")]
        [Tooltip("Save daily PROSAIL input parameters (leaf, canopy, soil, geometry, sensor) to the Parameters table of the database.")]
        public bool OutputParameters { get; set; } = true;

        /// <summary>Whether to write the CanopyOpticalVariable table to the database.</summary>
        [Description("Save canopy Optical variable")]
        [Tooltip("Save per-wavelength canopy optical variables to the CanopyOpticalVariable table of the database:\n" +
            "• Rdot – Hemispherical-directional reflectance factor (diffuse illumination → directional sensor)\n" +
            "• Rsot – Bi-directional reflectance factor (direct illumination → directional sensor)\n" +
            "• Rddt – Bi-hemispherical reflectance (diffuse illumination → hemisphere)\n" +
            "• Rsdt – Directional-hemispherical reflectance (direct illumination → hemisphere)\n" +
            "• FCover – Fractional canopy cover\n" +
            "• Abs_dir – Canopy absorptance under direct (beam) radiation\n" +
            "• Abs_hem – Canopy absorptance under diffuse (hemispherical) radiation\n" +
            "• Rsdstar – Canopy layer reflectance for direct illumination (excluding soil)\n" +
            "• Rddstar – Canopy layer reflectance for diffuse illumination (excluding soil)")]
        public bool OutputCanopyOpticalVariable { get; set; } = true;

        /// <summary>Whether to compute and save canopy state variables (fAPAR, fCover, albedo).</summary>
        [Description("Compute and save canopy state variable")]
        [Tooltip("Compute broadband canopy state variables and save them to the CanopyStateVariable table of the database:\n" +
            "• fAPAR – Fraction of Absorbed Photosynthetically Active Radiation\n" +
            "• fCover – Fractional green canopy cover\n" +
            "• albedo – Broadband canopy albedo")]
        public bool OutputCanopyStateVariable { get; set; } = true;

        /// <summary>Whether to compute and save canopy BRF.</summary>
        [Description("Compute and save canopy bidirectional reflectance factor (BRF)")]
        [Tooltip("Compute per-wavelength bidirectional reflectance factor (BRF) and save it to the CanopyBRF table of the database.")]
        public bool OutputCanopyBRF { get; set; } = true;

        /// <summary>Whether to compute and save reflectance resampled to a sensor.</summary>
        [Description("Compute and save reflectance resampled to sensor")]
        [Tooltip("Resample BRF to sensor bands and save to the ReflectanceResampledToSensor table of the database. Requires selecting a sensor type below.")]
        public bool OutputReflectanceResampledToSensor { get; set; } = true;

        /// <summary>Sensor type used for spectral resampling. Visible only when ReflectanceResampledToSensor output is enabled.</summary>
        [Description("Sensor for spectral resampling")]
        [Tooltip("Select a built-in sensor to use its spectral response function (SRF), or custom to provide a SRF CSV file.")]
        [Display(VisibleCallback = nameof(OutputReflectanceResampledToSensor))]
        public SensorTypes SensorType
        {
            get => sensorType;
            set
            {
                sensorType = value;
                SetSensorSRF();
            }
        }
        private SensorTypes sensorType;

        /// <summary>Path to a custom SRF CSV file (used when SensorType is Custom).</summary>
        [Description("Custom SRF CSV file")]
        [Tooltip("CSV file: first column is wavelength (nm), remaining columns are band SRF values. Column headers are used as band names.")]
        [Display(Type = DisplayType.FileName, VisibleCallback = nameof(IsCustomSensorAndOutputResampled))]
        public string CustomSRFPath { get; set; }

        /// <summary>Whether the user selected Custom sensor type and resampled output is enabled.</summary>
        public bool IsCustomSensorAndOutputResampled => OutputReflectanceResampledToSensor && SensorType == SensorTypes.Custom;

        /// <summary>Whether the user selected Custom sensor type.</summary>
        public bool IsCustomSensor => SensorType == SensorTypes.Custom;

        /// <summary>Logging verbosity level</summary>
        [Description("Logging level")]
        [Tooltip("Controls verbosity: Error (errors only), Warning (+ warnings), Info (+ informational), Debug (all messages).")]
        public LogLevel LoggingLevel { get; set; } = LogLevel.Info;

        #endregion

        #region Private Fields and Cached Data
        // Soil reflectance data
        private static string DefaultSpecSoilDataPath => Path.Combine(
            AppContext.BaseDirectory, "PROSAIL", "InputProperties", "SpectralData", "SpecSOIL.json");

        // Atmospheric reflectance data
        private static string DefaultSpecAtmDataPath => Path.Combine(
            AppContext.BaseDirectory, "PROSAIL", "InputProperties", "SpectralData", "SpecATM.json");

        // BSM spectral data
        private static string DefaultBsmDataPath => Path.Combine(
            AppContext.BaseDirectory, "PROSAIL", "InputProperties", "SpectralData", "BSM_GSV.json");

        /// <summary>Path to the SQLite database file (relative to simulation directory)</summary>
        private string ProsailSQLiteDatabasePath;

        /// <summary>The cached leaf spectral constants loaded at simulation start</summary>
        private LeafOpticalConsts? cachedLeafOpticalConstants = null;

        /// <summary>The cached atmospheric spectral data loaded at simulation start</summary>
        private AtmosphericSpectralData cachedAtmosphericSpectralData;

        /// <summary>The cached wet and dry soil reflectance at simulation start</summary>
        private WetDrySoilReflectance? cachedWetDrySoilReflectance = null;

        /// <summary>The cached BSM spectral data (loaded if SoilReflectanceModel = BSM)</summary>
        private BsmSpectralData? cachedBsmData = null;

        /// <summary>Cached PROSAIL results for the current day</summary>
        private CanopyOptics cachedProsailOutputs = null;

        /// <summary>
        /// Cache of parsed (but not yet variable-filled) expressions, keyed by expression text, so
        /// EvaluateExpression only re-parses a given expression string once per simulation instead
        /// of on every call. Variable values are always re-resolved fresh on every call regardless.
        /// </summary>
        private readonly Dictionary<string, ExpressionEvaluator> parsedExpressionCache = new Dictionary<string, ExpressionEvaluator>();

        /// <summary>The date of the last cached results</summary>
        private DateTime? lastCalculationDate = null;

        /// <summary>Database connection</summary>
        private SQLite dbConnection = null;

        /// <summary>Current simulation name for database records</summary>
        private string simulationName = null;

        /// <summary>Current parameter values after expression evaluation</summary>
        private Dictionary<string, object> CurrentParameterValues { get; set; } = new Dictionary<string, object>();

        /// <summary>Lookup: date -> index in the observation arrays (populated from CSV or UI). Null = daily mode.</summary>
        private Dictionary<DateTime, int> observationDateLookup;

        /// <summary>Resolved per-date Psoil values.</summary>
        private double[] resolvedPsoilValues;
        /// <summary>Resolved per-date SMp values (BSM path).</summary>
        private double[] resolvedSmpValues;
        /// <summary>Resolved per-date sun zenith values (parsed from SunZenithAngle string).</summary>
        private double[] resolvedSunZenithValues;
        /// <summary>Resolved per-date observer zenith values (parsed from ObserverZenithAngle string).</summary>
        private double[] resolvedObserverZenithValues;
        /// <summary>Resolved per-date relative azimuth values (parsed from RelativeAzimuthAngle string).</summary>
        private double[] resolvedRelativeAzimuthValues;
        #endregion

        /// <summary>Soil reflectance</summary>
        public SoilOptics SoilReflectance { get; set; } = new SoilOptics();
        /// <summary>Input wavelengths</summary>
        public double[] inputWavelengths;

        /// <summary>
        /// Helper method to write messages based on logging level.
        /// </summary>
        private void WriteMessage(LogLevel messageLevel, string message)
        {
            if ((int)messageLevel <= (int)LoggingLevel)
            {
                MessageType messageType = messageLevel switch
                {
                    LogLevel.Error => MessageType.Error,
                    LogLevel.Warning => MessageType.Warning,
                    _ => MessageType.Information
                };
                Summary.WriteMessage(this, message, messageType);
            }
        }

        /// <summary>
        /// Evaluates an expression and returns its value.
        /// </summary>
        private double EvaluateExpression(string expression)
        {
            try
            {
                if (double.TryParse(expression, out double result))
                {
                    WriteMessage(LogLevel.Debug, $"Parameter expression '{expression}' parsed as {result} on {Clock?.Today:yyyy-MM-dd}.");
                    return result;
                }

                if (!parsedExpressionCache.TryGetValue(expression, out ExpressionEvaluator fn))
                {
                    fn = new ExpressionEvaluator();
                    fn.Parse(expression.Trim());
                    fn.Infix2Postfix();
                    parsedExpressionCache[expression] = fn;
                }
                ExpressionFunction.FillVariableNames(fn, this, -1, Structure);
                ExpressionFunction.Evaluate(fn);
                object value = fn.Results != null ? (object)fn.Results : fn.Result;
                if (value == null)
                {
                    WriteMessage(LogLevel.Error, $"Parameter expression '{expression}' evaluated to null on {Clock?.Today:yyyy-MM-dd}.");
                    throw new InvalidOperationException($"Parameter expression '{expression}' evaluated to null.");
                }
                if (value is double d)
                {
                    WriteMessage(LogLevel.Debug, $"Parameter expression '{expression}' evaluated to {d} on {Clock?.Today:yyyy-MM-dd}.");
                    return d;
                }
                else if (value is double[] arr && arr.Length > 0)
                {
                    WriteMessage(LogLevel.Warning, $"Parameter expression '{expression}' evaluated to array, using first value {arr[0]} on {Clock?.Today:yyyy-MM-dd}.");
                    return arr[0];
                }
                else
                {
                    double resultValue = Convert.ToDouble(value);
                    WriteMessage(LogLevel.Debug, $"Parameter expression '{expression}' converted to {resultValue} on {Clock?.Today:yyyy-MM-dd}.");
                    return resultValue;
                }
            }
            catch (Exception ex)
            {
                WriteMessage(LogLevel.Error, $"Failed to evaluate parameter expression '{expression}' on {Clock?.Today:yyyy-MM-dd}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Tries to parse a string as a comma-separated list of doubles.
        /// Returns the parsed array if all tokens are valid numbers; otherwise returns null
        /// (indicating the string should be treated as an APSIM expression).
        /// A single-number string returns a length-1 array that broadcasts to all dates.
        /// </summary>
        private static double[] TryParseCommaDoubles(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var parts = s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;
            var result = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out result[i])) return null;
            return result;
        }

        /// <summary>
        /// Evaluate all PROSAIL parameters (except geometry and Psoil) and store them in CurrentParameterValues.
        /// </summary>
        private void EvaluateAllParameters()
        {
            // Leaf/PROSPECT parameters
            CurrentParameterValues["N"] = EvaluateExpression(N);
            CurrentParameterValues["CAB"] = EvaluateExpression(CAB);
            CurrentParameterValues["CAR"] = EvaluateExpression(CAR);
            CurrentParameterValues["EWT"] = EvaluateExpression(EWT);
            CurrentParameterValues["LMA"] = EvaluateExpression(LMA);
            CurrentParameterValues["BROWN"] = EvaluateExpression(BROWN);
            CurrentParameterValues["ANT"] = EvaluateExpression(ANT);
            CurrentParameterValues["PROT"] = EvaluateExpression(PROT);
            CurrentParameterValues["CBC"] = EvaluateExpression(CBC);
            CurrentParameterValues["Alpha"] = EvaluateExpression(Alpha);

            // SAIL/canopy parameters
            CurrentParameterValues["LAI"] = EvaluateExpression(LAI);
            CurrentParameterValues["HotSpot"] = EvaluateExpression(HotSpot);
            double typeLidfVal = EvaluateExpression(TypeLidf);
            int typeLidfInt = Convert.ToInt32(typeLidfVal);
            CurrentParameterValues["TypeLidf"] = typeLidfInt;

            CurrentParameterValues["LIDFa"] = EvaluateExpression(LIDFa);
            CurrentParameterValues["LIDFb"] = EvaluateExpression(LIDFb);

            // 4SAIL2-only parameters: only evaluated when actually selected, since they're hidden
            // in the UI and unused otherwise (see IsFourSAIL2).
            if (IsFourSAIL2)
            {
                CurrentParameterValues["FractionBrown"] = EvaluateExpression(FractionBrown);
                CurrentParameterValues["Dissociation"] = EvaluateExpression(Dissociation);
                CurrentParameterValues["CrownCover"] = EvaluateExpression(CrownCover);
                CurrentParameterValues["TreeShape"] = EvaluateExpression(TreeShape);

                CurrentParameterValues["NBrown"] = EvaluateExpression(NBrown);
                CurrentParameterValues["CABBrown"] = EvaluateExpression(CABBrown);
                CurrentParameterValues["CARBrown"] = EvaluateExpression(CARBrown);
                CurrentParameterValues["EWTBrown"] = EvaluateExpression(EWTBrown);
                CurrentParameterValues["LMABrown"] = EvaluateExpression(LMABrown);
                CurrentParameterValues["BROWNBrown"] = EvaluateExpression(BROWNBrown);
                CurrentParameterValues["ANTBrown"] = EvaluateExpression(ANTBrown);
                CurrentParameterValues["PROTBrown"] = EvaluateExpression(PROTBrown);
                CurrentParameterValues["CBCBrown"] = EvaluateExpression(CBCBrown);
                CurrentParameterValues["AlphaBrown"] = EvaluateExpression(AlphaBrown);
            }

            CurrentParameterValues["WetDrySoilReflectancePath"] = WetDrySoilReflectancePath;
        }

        /// <summary>
        /// Validate the ranges of all PROSAIL parameters.
        /// </summary>
        /// <summary>
        /// Names of the parameters checked by <see cref="ValidateParameterRanges"/>. Numeric bounds for
        /// all of these except LIDFa (whose valid range depends on TypeLidf - see below) live on the
        /// corresponding property's [Bounds] attribute, not here, so there is one source of truth per bound.
        /// </summary>
        private static readonly string[] ValidatedParameterNames =
        {
            "N", "CAB", "CAR", "EWT", "LMA", "BROWN", "ANT", "PROT", "CBC", "Alpha",
            "LAI", "HotSpot", "TypeLidf", "LIDFa", "LIDFb", "FractionBrown", "Dissociation",
            "CrownCover", "TreeShape", "Psoil", "SunZenithAngle", "ObserverZenithAngle", "RelativeAzimuthAngle",
            "NBrown", "CABBrown", "CARBrown", "EWTBrown", "LMABrown", "BROWNBrown", "ANTBrown", "PROTBrown",
            "CBCBrown", "AlphaBrown"
        };

        /// <summary>
        /// Names of parameters that are only populated in <see cref="CurrentParameterValues"/> (by
        /// <see cref="EvaluateAllParameters"/>) when <see cref="IsFourSAIL2"/> is true - the 10 brown-leaf
        /// properties plus the 4 existing 4SAIL2-only canopy properties. Skipped by
        /// <see cref="ValidateParameterRanges"/> otherwise, since they simply weren't evaluated.
        /// </summary>
        private static readonly HashSet<string> FourSAIL2OnlyParameterNames = new HashSet<string>
        {
            "FractionBrown", "Dissociation", "CrownCover", "TreeShape",
            "NBrown", "CABBrown", "CARBrown", "EWTBrown", "LMABrown", "BROWNBrown", "ANTBrown", "PROTBrown",
            "CBCBrown", "AlphaBrown"
        };

        /// <summary>
        /// Cached [Bounds]/[Description] attribute lookups for <see cref="ValidatedParameterNames"/>,
        /// built once since these are compile-time constants on the class - no need to re-resolve
        /// them via reflection every day. LIDFa is excluded (special-cased in ValidateParameterRanges).
        /// </summary>
        private static readonly Dictionary<string, (BoundsAttribute Bounds, string Description)> ParameterAttributeCache =
            BuildParameterAttributeCache();

        private static Dictionary<string, (BoundsAttribute, string)> BuildParameterAttributeCache()
        {
            var cache = new Dictionary<string, (BoundsAttribute, string)>();
            Type modelType = typeof(ProsailModel);
            foreach (string paramName in ValidatedParameterNames)
            {
                if (paramName == "LIDFa") continue; // special-cased in ValidateParameterRanges, no static Bounds
                PropertyInfo prop = modelType.GetProperty(paramName);
                cache[paramName] = (prop?.GetCustomAttribute<BoundsAttribute>(),
                                     prop?.GetCustomAttribute<DescriptionAttribute>()?.ToString() ?? paramName);
            }
            return cache;
        }

        private void ValidateParameterRanges()
        {
            foreach (string paramName in ValidatedParameterNames)
            {
                if (!IsFourSAIL2 && FourSAIL2OnlyParameterNames.Contains(paramName))
                    continue; // Not evaluated this call (see EvaluateAllParameters) - nothing to check.

                if (!CurrentParameterValues.TryGetValue(paramName, out object value))
                {
                    string missingMsg = $"Parameter '{paramName}' is missing from CurrentParameterValues on {Clock?.Today:yyyy-MM-dd}.";
                    WriteMessage(LogLevel.Error, missingMsg);
                    throw new InvalidOperationException(missingMsg);
                }

                double numericValue;
                try
                {
                    numericValue = Convert.ToDouble(value);
                }
                catch (Exception ex)
                {
                    string msg = $"Parameter '{paramName}' value '{value}' cannot be converted to a numeric value on {Clock?.Today:yyyy-MM-dd}. Error: {ex.Message}";
                    WriteMessage(LogLevel.Error, msg);
                    throw new InvalidOperationException(msg);
                }

                double lower, upper;
                if (paramName == "LIDFa")
                {
                    // LIDFa's valid range depends on TypeLidf: -1 to 1 for Verhoef (TypeLidf=1, average
                    // leaf slope), -90 to 90 for Campbell (TypeLidf=2, mean leaf angle in degrees). A
                    // single static [Bounds] attribute cannot express this, so it is special-cased here.
                    int typeLidfForLIDFa = Convert.ToInt32(CurrentParameterValues["TypeLidf"]);
                    (lower, upper) = typeLidfForLIDFa == 1 ? (-1.0, 1.0) : (-90.0, 90.0);
                }
                else
                {
                    BoundsAttribute bounds = ParameterAttributeCache[paramName].Bounds;
                    if (bounds == null)
                        continue; // No declared bounds for this parameter - nothing to check.
                    lower = bounds.Lower;
                    upper = bounds.Upper;
                }

                if (numericValue < lower || numericValue > upper)
                {
                    string description = ParameterAttributeCache[paramName].Description;
                    string msg = $"Parameter '{paramName}' value {numericValue} may be out of range [{lower}, {upper}] ({description}) on {Clock?.Today:yyyy-MM-dd}. Check this paper: https://doi.org/10.3390/rs10010085";
                    WriteMessage(LogLevel.Warning, msg);
                }
            }

            // TypeLidf as int
            if (CurrentParameterValues.TryGetValue("TypeLidf", out object typeLidfObj))
            {
                int typeLidf = Convert.ToInt32(typeLidfObj);
                if (typeLidf != 1 && typeLidf != 2)
                {
                    string msg = $"Parameter 'TypeLidf' value {typeLidf} is invalid (must be 1 or 2) on {Clock?.Today:yyyy-MM-dd}.";
                    WriteMessage(LogLevel.Error, msg);
                    throw new InvalidOperationException(msg);
                }
            }
            else
            {
                string msg = $"Parameter 'TypeLidf' is missing from CurrentParameterValues on {Clock?.Today:yyyy-MM-dd}.";
                WriteMessage(LogLevel.Error, msg);
                throw new InvalidOperationException(msg);
            }
        }

        /// <summary>
        /// Calculate canopy optical properties using the PROSAIL model.
        /// </summary>
        public CanopyOptics CalculateProsail()
        {
            if (cachedLeafOpticalConstants == null)
            {
                WriteMessage(LogLevel.Error, $"ProsailModel: CalculateProsail called without leaf optical constants on {Clock?.Today:yyyy-MM-dd}.");
                throw new InvalidOperationException("Leaf optical constants not loaded when CalculateProsail called.");
            }
            WriteMessage(LogLevel.Info, $"ProsailModel: CalculateProsail called on {Clock?.Today:yyyy-MM-dd}.");

            int TypeLidfValue = Convert.ToInt32(CurrentParameterValues["TypeLidf"]);
            string SailVersionValue = SailVersionString;

            // Always build the leaf-parameter list explicitly (rather than letting PRO4SAIL build
            // an implicit single-element one from the scalar args below), and always pass
            // wavelengths: inputWavelengths on every ProspectInputs. This matters even for plain
            // 4SAIL: AdjustProspectToSail (called unconditionally by PRO4SAIL regardless of
            // SailVersion) builds GreenLOP via the ProspectInputs-based Prospect() overload, which
            // uses ProspectInputs.Wavelengths - and that defaults to the full 400-2500nm range
            // whenever wavelengths isn't supplied, regardless of what's passed to PRO4SAIL's own
            // Wavelengths parameter (left null below). Without this, a user who narrows
            // InputWavelengthRange away from the full default would hit "Wavelength ... is not in
            // OpticalConstants.Wavelength" here even under plain 4SAIL.
            List<ProspectInputs> inputProspectList = new List<ProspectInputs>
            {
                new ProspectInputs(
                    n: Convert.ToDouble(CurrentParameterValues["N"]), cab: Convert.ToDouble(CurrentParameterValues["CAB"]),
                    car: Convert.ToDouble(CurrentParameterValues["CAR"]), ant: Convert.ToDouble(CurrentParameterValues["ANT"]),
                    brown: Convert.ToDouble(CurrentParameterValues["BROWN"]), ewt: Convert.ToDouble(CurrentParameterValues["EWT"]),
                    lma: Convert.ToDouble(CurrentParameterValues["LMA"]), prot: Convert.ToDouble(CurrentParameterValues["PROT"]),
                    cbc: Convert.ToDouble(CurrentParameterValues["CBC"]), alpha: Convert.ToDouble(CurrentParameterValues["Alpha"]),
                    wavelengths: inputWavelengths)
            };
            // When 4SAIL2 is selected, add a genuine second (brown) leaf-parameter set so
            // AdjustProspectToSail can actually produce a distinct BrownLOP instead of falling
            // back to green-only.
            if (IsFourSAIL2)
            {
                inputProspectList.Add(new ProspectInputs(
                    n: Convert.ToDouble(CurrentParameterValues["NBrown"]), cab: Convert.ToDouble(CurrentParameterValues["CABBrown"]),
                    car: Convert.ToDouble(CurrentParameterValues["CARBrown"]), ant: Convert.ToDouble(CurrentParameterValues["ANTBrown"]),
                    brown: Convert.ToDouble(CurrentParameterValues["BROWNBrown"]), ewt: Convert.ToDouble(CurrentParameterValues["EWTBrown"]),
                    lma: Convert.ToDouble(CurrentParameterValues["LMABrown"]), prot: Convert.ToDouble(CurrentParameterValues["PROTBrown"]),
                    cbc: Convert.ToDouble(CurrentParameterValues["CBCBrown"]), alpha: Convert.ToDouble(CurrentParameterValues["AlphaBrown"]),
                    wavelengths: inputWavelengths));
            }

            CanopyOptics results = ProsailCore.PRO4SAIL(
                leafOpticalConstants: cachedLeafOpticalConstants,
                inputProspectList: inputProspectList,
                N: Convert.ToDouble(CurrentParameterValues["N"]),
                CAB: Convert.ToDouble(CurrentParameterValues["CAB"]),
                CAR: Convert.ToDouble(CurrentParameterValues["CAR"]),
                EWT: Convert.ToDouble(CurrentParameterValues["EWT"]),
                LMA: Convert.ToDouble(CurrentParameterValues["LMA"]),
                ANT: Convert.ToDouble(CurrentParameterValues["ANT"]),
                BROWN: Convert.ToDouble(CurrentParameterValues["BROWN"]),
                PROT: Convert.ToDouble(CurrentParameterValues["PROT"]),
                CBC: Convert.ToDouble(CurrentParameterValues["CBC"]),
                Alpha: Convert.ToDouble(CurrentParameterValues["Alpha"]),
                // cachedLeafOpticalConstants is already subset to inputWavelengths (done once in
                // OnCommencing), so passing null here avoids ProspectCore.Prospect redundantly
                // rebuilding an identical wavelength subset from scratch every day.
                Wavelengths: null,
                SailVersion: SailVersionValue,
                LAI: Convert.ToDouble(CurrentParameterValues["LAI"]),
                HotSpot: Convert.ToDouble(CurrentParameterValues["HotSpot"]),
                TypeLidf: TypeLidfValue,
                LIDFa: Convert.ToDouble(CurrentParameterValues["LIDFa"]),
                LIDFb: Convert.ToDouble(CurrentParameterValues["LIDFb"]),
                // FractionBrown/Diss/Cv/Zeta are only populated in CurrentParameterValues when
                // IsFourSAIL2 (see EvaluateAllParameters); otherwise fall back to the same literal
                // defaults these properties and PRO4SAIL itself already use - safe because
                // AdjustProspectToSail's BrownLOP output is simply unused when SailVersion is 4SAIL.
                FractionBrown: IsFourSAIL2 ? Convert.ToDouble(CurrentParameterValues["FractionBrown"]) : 0.0,
                Diss: IsFourSAIL2 ? Convert.ToDouble(CurrentParameterValues["Dissociation"]) : 0.0,
                Cv: IsFourSAIL2 ? Convert.ToDouble(CurrentParameterValues["CrownCover"]) : 1.0,
                Zeta: IsFourSAIL2 ? Convert.ToDouble(CurrentParameterValues["TreeShape"]) : 1.0,
                SoilReflectance: SoilReflectance,
                TTS: Convert.ToDouble(CurrentParameterValues["SunZenithAngle"]),
                TTO: Convert.ToDouble(CurrentParameterValues["ObserverZenithAngle"]),
                PSI: Convert.ToDouble(CurrentParameterValues["RelativeAzimuthAngle"]),
                BrownLOP: null
            );

            WriteMessage(LogLevel.Info, $"ProspectModel: CalculateProspect completed, Wavelengths[{inputWavelengths.Length}]");

            if (results.Rdot.Length != inputWavelengths.Length ||
                results.Rsot.Length != inputWavelengths.Length ||
                results.Rddt.Length != inputWavelengths.Length ||
                results.Rsdt.Length != inputWavelengths.Length ||
                results.Abs_dir.Length != inputWavelengths.Length ||
                results.Abs_hem.Length != inputWavelengths.Length ||
                results.Rsdstar.Length != inputWavelengths.Length ||
                results.Rddstar.Length != inputWavelengths.Length)
            {
                WriteMessage(LogLevel.Error, "ProsailModel: Mismatch between PROSAIL output and input wavelengths.");
                throw new InvalidOperationException("Mismatch between PROSAIL output and input wavelengths.");
            }

            return new CanopyOptics
            {
                Rdot = results.Rdot,
                Rsot = results.Rsot,
                Rddt = results.Rddt,
                Rsdt = results.Rsdt,
                FCover = results.FCover,
                Abs_dir = results.Abs_dir,
                Abs_hem = results.Abs_hem,
                Rsdstar = results.Rsdstar,
                Rddstar = results.Rddstar,
                Wavelength = inputWavelengths
            };
        }

        #region Event Handlers
        /// <summary>Adds an Introduction memo child on first creation if one does not already exist.</summary>
        public override void OnCreated()
        {
            base.OnCreated();
            if (!Children.Exists(c => c is Memo && c.Name == "\U0001F4D6 Start Here - Introduction"))
            {
                Children.Insert(0, new Memo
                {
                    Name = "\U0001F4D6 Start Here - Introduction",
                    Text = LoadIntroductionText()
                });
            }
        }

        /// <summary>Loads the Introduction memo markdown from the embedded resource file.</summary>
        private static string LoadIntroductionText()
        {
            const string resourceName = "Models.Resources.PROSAIL.Introduction.md";
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException(
                        $"Embedded resource '{resourceName}' not found. Ensure Introduction.md exists " +
                        "under Models/Resources/PROSAIL and is packaged as an EmbeddedResource.");
                using (StreamReader reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }

        /// <summary>Called when [simulation commencing].</summary>
        [EventSubscribe("Commencing")]
        private void OnCommencing(object sender, EventArgs e)
        {
            WriteMessage(LogLevel.Info, "ProsailModel: Simulation commencing.");

            // Load leaf optical constants
            try
            {
                cachedLeafOpticalConstants = GetCachedLeafOpticalConstants();
                WriteMessage(LogLevel.Info, $"ProsailModel: Leaf optical constants loaded, Wavelengths count: {cachedLeafOpticalConstants.Value.Wavelength.Count}.");
            }
            catch (Exception ex)
            {
                WriteMessage(LogLevel.Error, $"ProsailModel: Failed to load leaf optical constants: {ex.Message}");
                throw;
            }

            // Load soil reflectance data
            if (IsBSM)
            {
                try
                {
                    cachedBsmData = BsmCore.LoadBsmData(DefaultBsmDataPath);
                    WriteMessage(LogLevel.Info, "ProsailModel: BSM spectral data loaded.");
                }
                catch (Exception ex)
                {
                    WriteMessage(LogLevel.Error, $"ProsailModel: Failed to load BSM spectral data: {ex.Message}");
                    throw;
                }
            }
            else
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(WetDrySoilReflectancePath))
                    {
                        string resolvedPath = ProsailInputLoader.ResolvePath(WetDrySoilReflectancePath, Simulation.FileName);
                        cachedWetDrySoilReflectance = ProsailInputLoader.LoadWetDrySoilReflectanceFromCsv(resolvedPath);
                        WriteMessage(LogLevel.Info, $"ProsailModel: Soil reflectance loaded from {resolvedPath}.");
                    }
                    else
                    {
                        cachedWetDrySoilReflectance = LoadWetDrySoilReflectanData(DefaultSpecSoilDataPath);
                        WriteMessage(LogLevel.Info, "ProsailModel: Using default soil reflectance data.");
                    }
                }
                catch (Exception ex)
                {
                    WriteMessage(LogLevel.Error, $"ProsailModel: Failed to load wet and dry soil reflectance data: {ex.Message}");
                    throw;
                }
            }

            // Parse wavelength range
            double[] wavelengths = ProsailInputLoader.ParseWavelengthRange(InputWavelengthRange, WriteMessage).ToArray();
            inputWavelengths = wavelengths.Length > 0 ? wavelengths : cachedLeafOpticalConstants.Value.Wavelength.ToArray();

            // Load atmospheric spectral data
            try
            {
                cachedAtmosphericSpectralData = LoadAtmosphericSpectralData(DefaultSpecAtmDataPath);
                WriteMessage(LogLevel.Info, $"ProsailModel: Atmospheric spectral data loaded from {DefaultSpecAtmDataPath}.");
            }
            catch (Exception ex)
            {
                WriteMessage(LogLevel.Error, $"ProsailModel: Failed to load atmospheric spectral data: {ex.Message}");
                throw;
            }

            // Subset spectral data to input wavelengths
            cachedLeafOpticalConstants = cachedLeafOpticalConstants.Value.SubsetByWavelengths(inputWavelengths);
            if (!IsBSM)
                cachedWetDrySoilReflectance = cachedWetDrySoilReflectance.Value.SubsetByWavelengths(inputWavelengths);
            cachedAtmosphericSpectralData = cachedAtmosphericSpectralData.SubsetByWavelengths(inputWavelengths);

            // Validate and load SRF if resampled output is enabled
            if (OutputReflectanceResampledToSensor)
            {
                if (SensorType == SensorTypes.None)
                    throw new InvalidOperationException(
                        "ProsailModel: ReflectanceResampledToSensor output is enabled but no sensor type has been selected.");

                if (SensorType == SensorTypes.Custom)
                {
                    if (string.IsNullOrWhiteSpace(CustomSRFPath))
                        throw new InvalidOperationException("Custom sensor selected but no SRF file specified.");

                    string resolvedSRFPath = ProsailInputLoader.ResolvePath(CustomSRFPath, Simulation.FileName);
                    SensorSRF = ProsailInputLoader.LoadSRFFromCsv(resolvedSRFPath);
                    WriteMessage(LogLevel.Info, $"ProsailModel: Custom SRF loaded from {resolvedSRFPath}.");
                }

                // Pre-process SRF against the simulation wavelength grid so ResampleReflectanceToSensor
                // can skip rebuilding the lookup dictionary on every daily call.
                SensorSRF?.Preprocess(inputWavelengths);
            }

            // Parse string inputs as comma-separated doubles if possible, else treat as APSIM expression each day
            resolvedPsoilValues = TryParseCommaDoubles(Psoil);
            resolvedSmpValues = TryParseCommaDoubles(SMp);
            resolvedSunZenithValues = TryParseCommaDoubles(SunZenithAngle);
            resolvedObserverZenithValues = TryParseCommaDoubles(ObserverZenithAngle);
            resolvedRelativeAzimuthValues = TryParseCommaDoubles(RelativeAzimuthAngle);

            // Build date lookup and validate
            if (ObservationDates != null && ObservationDates.Length > 0)
            {
                ObservationDates = ObservationDates.Select(d => d.Date).Distinct().OrderBy(d => d).ToArray();
                observationDateLookup = new Dictionary<DateTime, int>();
                for (int i = 0; i < ObservationDates.Length; i++)
                    observationDateLookup[ObservationDates[i]] = i;

                foreach (var d in ObservationDates.Where(d => d < Clock.StartDate || d > Clock.EndDate))
                    WriteMessage(LogLevel.Warning, $"ProsailModel: ObservationDate {d:yyyy-MM-dd} is outside simulation range.");

                ProsailInputLoader.ValidatePerDateArray(resolvedPsoilValues, "Psoil", ObservationDates.Length);
                ProsailInputLoader.ValidatePerDateArray(resolvedSmpValues, "SMp", ObservationDates.Length);
                ProsailInputLoader.ValidatePerDateArray(resolvedSunZenithValues, "SunZenithAngle", ObservationDates.Length);
                ProsailInputLoader.ValidatePerDateArray(resolvedObserverZenithValues, "ObserverZenithAngle", ObservationDates.Length);
                ProsailInputLoader.ValidatePerDateArray(resolvedRelativeAzimuthValues, "RelativeAzimuthAngle", ObservationDates.Length);
            }
            else
            {
                observationDateLookup = null; // Daily mode
            }

            // Initialize database
            string simulationFileName = Path.GetFileNameWithoutExtension(Simulation.FileName);
            ProsailSQLiteDatabasePath = $"{simulationFileName}_Prosail.db";
            string dbPath = ProsailDatabaseHelper.GetFullDatabasePath(ProsailSQLiteDatabasePath, Simulation.FileName);
            simulationName = Simulation.Name.Replace("'", "''");
            bool anyOutput = OutputParameters || OutputCanopyOpticalVariable || OutputCanopyStateVariable
                || OutputCanopyBRF || OutputReflectanceResampledToSensor;
            if (!anyOutput)
                throw new InvalidOperationException("ProsailModel: At least one output table must be selected.");
            dbConnection = ProsailDatabaseHelper.InitializeDatabase(dbPath, simulationName, Clock.StartDate, Clock.EndDate,
                OutputParameters, OutputCanopyOpticalVariable, OutputCanopyStateVariable,
                OutputCanopyBRF, OutputReflectanceResampledToSensor, WriteMessage);
        }

        /// <summary>Called when [do management calculations].</summary>
        [EventSubscribe("EndOfDay")]
        private void OnDoEndOfDay(object sender, EventArgs e)
        {
            if (ParentPlant?.IsAlive != true)
            {
                WriteMessage(LogLevel.Info, $"ProsailModel: Skipping calculations on {Clock.Today:yyyy-MM-dd} as Plant is not alive.");
                return;
            }

            if (ParentPlant?.IsEmerged != true)
            {
                WriteMessage(LogLevel.Info, $"ProsailModel: Skipping calculations on {Clock.Today:yyyy-MM-dd} as Plant has not emerged.");
                return;
            }

            // Check if today is an observation date (skip if dates specified and today isn't one)
            if (observationDateLookup != null && !observationDateLookup.ContainsKey(Clock.Today.Date))
            {
                WriteMessage(LogLevel.Debug, $"ProsailModel: Skipping {Clock.Today:yyyy-MM-dd} - not in observation dates.");
                return;
            }

            WriteMessage(LogLevel.Info, $"ProsailModel: OnDoEndOfDay called on {Clock.Today:yyyy-MM-dd}.");

            // Compute soil reflectance (BSM or wet/dry interpolation)
            double psoilValue;
            if (IsBSM)
            {
                double bVal = EvaluateExpression(BsmBrightness);
                double latVal = EvaluateExpression(BsmLat);
                double lonVal = EvaluateExpression(BsmLon);
                double smpVal = ProsailInputLoader.ResolveObservationParameter(
                    resolvedSmpValues, SMp, "SMp", Clock.Today, observationDateLookup,
                    EvaluateExpression, writeMessage: WriteMessage).Value;
                SoilReflectance = BsmCore.BSM(bVal, latVal, lonVal, smpVal, cachedBsmData.Value)
                                         .SubsetByWavelengths(inputWavelengths);
                psoilValue = smpVal;
            }
            else
            {
                // Resolve Psoil: per-date array > expression (defaults to "1 - [WaterBalance].SW[1]")
                double psoilResolved = ProsailInputLoader.ResolveObservationParameter(
                    resolvedPsoilValues, Psoil, "Psoil", Clock.Today, observationDateLookup,
                    EvaluateExpression, writeMessage: WriteMessage).Value;
                psoilValue = psoilResolved;
                SoilReflectance = CalculateSoilReflectanceFromWetDry((WetDrySoilReflectance)cachedWetDrySoilReflectance, psoilValue);
            }

            // Evaluate PROSPECT/SAIL expression parameters
            CurrentParameterValues.Clear();
            EvaluateAllParameters();

            // Resolve geometry: per-date array > expression > hardcoded defaults
            CurrentParameterValues["SunZenithAngle"] = ProsailInputLoader.ResolveObservationParameter(
                resolvedSunZenithValues, SunZenithAngle, "SunZenithAngle", Clock.Today, observationDateLookup,
                EvaluateExpression, defaultValue: 90.0, writeMessage: WriteMessage);
            CurrentParameterValues["ObserverZenithAngle"] = ProsailInputLoader.ResolveObservationParameter(
                resolvedObserverZenithValues, ObserverZenithAngle, "ObserverZenithAngle", Clock.Today, observationDateLookup,
                EvaluateExpression, defaultValue: 0.0, writeMessage: WriteMessage);
            CurrentParameterValues["RelativeAzimuthAngle"] = ProsailInputLoader.ResolveObservationParameter(
                resolvedRelativeAzimuthValues, RelativeAzimuthAngle, "RelativeAzimuthAngle", Clock.Today, observationDateLookup,
                EvaluateExpression, defaultValue: 0.0, writeMessage: WriteMessage);
            CurrentParameterValues["Psoil"] = psoilValue;

            // Validate parameter ranges
            ValidateParameterRanges();
            // Clear cached results to force recalculation
            cachedProsailOutputs = null;
            lastCalculationDate = null;

            try
            {
                // Calculate PROSAIL outputs
                var canopyOpticalVariables = CalculateProsail();
                cachedProsailOutputs = canopyOpticalVariables;
                lastCalculationDate = Clock.Today;

                WriteMessage(LogLevel.Info, $"ProsailModel: PROSAIL calculation completed, Wavelength[{canopyOpticalVariables.Wavelength.Length}]");

                double tts = Convert.ToDouble(CurrentParameterValues["SunZenithAngle"]);

                // Compute BRF (needed for CanopyBRF and resampling outputs)
                CanopyBRF? canopyBRF = null;
                if (OutputCanopyBRF || OutputReflectanceResampledToSensor)
                {
                    canopyBRF = ComputeBRF(
                        wavelength: canopyOpticalVariables.Wavelength,
                        rdot: canopyOpticalVariables.Rdot,
                        rsot: canopyOpticalVariables.Rsot,
                        tts: tts,
                        atmosphericSpectralData: cachedAtmosphericSpectralData);
                }

                // Compute canopy state variables (fAPAR, fCover, albedo)
                CanopyStateVariables? canopyStateVariables = null;
                if (OutputCanopyStateVariable)
                {
                    double fAPAR = ComputeFAPAR(
                        abs_dir: canopyOpticalVariables.Abs_dir,
                        abs_hem: canopyOpticalVariables.Abs_hem,
                        tts: tts,
                        atmosphericSpectralData: cachedAtmosphericSpectralData);

                    double albedo = ComputeAlbedo(
                        rddstar: canopyOpticalVariables.Rddstar,
                        rsdstar: canopyOpticalVariables.Rsdstar,
                        tts: tts,
                        atmosphericSpectralData: cachedAtmosphericSpectralData);

                    canopyStateVariables = new CanopyStateVariables
                    {
                        fAPAR = fAPAR,
                        fcover = canopyOpticalVariables.FCover[0],
                        albedo = albedo
                    };
                }

                // Spectral resampling to sensor
                SpectralResamplingResult resampledReflectance = null;
                if (OutputReflectanceResampledToSensor && canopyBRF.HasValue)
                {
                    resampledReflectance = ResampleReflectanceToSensor(
                        wavelength: canopyBRF.Value.Wavelength,
                        reflectance: canopyBRF.Value.BRF,
                        srf: SensorSRF);
                }

                // Save to database
                if (dbConnection != null)
                {
                    string sensorTypeStr = OutputReflectanceResampledToSensor
                        ? (SensorType == SensorTypes.Custom ? $"Custom:{CustomSRFPath}" : SensorType.ToString())
                        : string.Empty;

                    ProsailDatabaseHelper.WriteToDatabase(dbConnection, simulationName, Clock.Today,
                        CurrentParameterValues, WetDrySoilReflectancePath, SailVersionString, sensorTypeStr,
                        canopyOpticalVariables, canopyStateVariables ?? default, canopyBRF ?? default, resampledReflectance,
                        OutputParameters, OutputCanopyOpticalVariable, OutputCanopyStateVariable,
                        OutputCanopyBRF, OutputReflectanceResampledToSensor, WriteMessage);
                    WriteMessage(LogLevel.Info, $"ProsailModel: Wrote results to database for {Clock.Today:yyyy-MM-dd}.");
                }
            }
            catch (Exception ex)
            {
                WriteMessage(LogLevel.Error, $"ProsailModel: Error in OnDoEndOfDay: {ex.Message}");
                throw;
            }
        }

        /// <summary>Called when [simulation completed].</summary>
        [EventSubscribe("Completed")]
        private void OnCompleted(object sender, EventArgs e)
        {
            if (dbConnection != null)
            {
                dbConnection.CloseDatabase();
                string dbPath = ProsailDatabaseHelper.GetFullDatabasePath(ProsailSQLiteDatabasePath, Simulation.FileName);
                WriteMessage(LogLevel.Info, $"PROSAIL results database saved to: {dbPath}");
                dbConnection = null;
            }
        }
        #endregion
    }
}

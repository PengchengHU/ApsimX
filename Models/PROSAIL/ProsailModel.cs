using APSIM.Shared.Utilities;
using MathNet.Numerics.LinearAlgebra;
using Models.Core;
using Models.Functions;
using Models.Interfaces;
using Models.PMF;
using APSIM.Core;
using Models.Prosail;
using Models.PROSAIL.PROSPECT;
using Models.PROSAIL.SAIL;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
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

        /// <summary>Link to the soil water model to soil water content of the top layer</summary>
        [Link] private ISoilWater waterBalance = null;
        #endregion

        #region PROSAIL Input Parameters (Expressions)
        /// <summary>The expression for N (Leaf structure parameter)</summary>
        [Separator("Expressions to link APSIM variables and PROSPECT inputs")]
        [Description("N - Leaf structure (unitless)")]
        [Tooltip("Leaf structure parameter. Can be a literal value or an APSIM expression (e.g., 1.5). Typical range: 1.0-2.6.")]
        public string N { get; set; } = "1.5";

        /// <summary>The expression for CAB (Chlorophyll a + b content)</summary>
        [Description("CAB - Chlorophyll a+b (\u03BCg/cm\u00B2)")]
        [Tooltip("Chlorophyll a + b content. Can be a literal value or an APSIM expression (e.g., [Wheat].Leaf.ChlorophyllContent). Typical range: 10-80 \u03BCg/cm\u00B2.")]
        public string CAB { get; set; } = "40.0";

        /// <summary>The expression for CAR (Carotenoid content)</summary>
        [Description("CAR - Carotenoid (\u03BCg/cm\u00B2)")]
        [Tooltip("Carotenoid content. Can be a literal or APSIM expression. Typical range: 1-24 \u03BCg/cm\u00B2.")]
        public string CAR { get; set; } = "8.0";

        /// <summary>The expression for EWT (Equivalent Water Thickness)</summary>
        [Description("EWT - Water thickness (cm)")]
        [Tooltip("Equivalent Water Thickness (CW). Can be a literal or APSIM expression. Typical range: 0.001-0.08 cm.")]
        public string EWT { get; set; } = "0.01";

        /// <summary>The expression for LMA (Leaf Mass per Area)</summary>
        [Description("LMA - Dry matter (g/cm\u00B2)")]
        [Tooltip("Leaf Mass per Area (CM). Can be a literal or APSIM expression. Typical range: 0.001-0.02 g/cm\u00B2.")]
        public string LMA { get; set; } = "0.008";

        /// <summary>The expression for BROWN (Brown pigment content)</summary>
        [Description("BROWN - Brown pigment (unitless)")]
        [Tooltip("Brown pigment content. Can be a literal or APSIM expression. Typical range: 0-1.")]
        public string BROWN { get; set; } = "0.0";

        /// <summary>The expression for ANT (Anthocyanin content)</summary>
        [Description("ANT - Anthocyanin (\u03BCg/cm\u00B2)")]
        [Tooltip("Anthocyanin content. Can be a literal or APSIM expression. Typical range: 0-10 \u03BCg/cm\u00B2.")]
        public string ANT { get; set; } = "0.0";

        /// <summary>The expression for PROT (Protein content)</summary>
        [Description("PROT - Protein (g/cm\u00B2)")]
        [Tooltip("Protein content. Can be a literal or APSIM expression. Typical range: 0-10 g/cm\u00B2.")]
        public string PROT { get; set; } = "0.0";

        /// <summary>The expression for CBC (NonProt Carbon-based constituent content)</summary>
        [Description("CBC - Carbon-based constituent (g/cm\u00B2)")]
        [Tooltip("Non-protein carbon-based constituent content. Can be a literal or APSIM expression. Typical range: 0-10 g/cm\u00B2.")]
        public string CBC { get; set; } = "0.0";

        /// <summary>The expression for alpha (Incidence angle in degrees)</summary>
        [Description("Alpha - Incidence angle (\u00B0)")]
        [Tooltip("Incidence angle in degrees. Can be a literal or APSIM expression. Typical range: 0-90\u00B0.")]
        public string Alpha { get; set; } = "40.0";

        /// <summary>Spectral range to simulate (start-end in nm)</summary>
        [Description("Spectral range (nm)")]
        [Tooltip("Supports ranges (e.g., '400-500'), lists (e.g., '400, 500, 600'), and mixed formats (e.g., '400, 500-600, 700'). Default: 400-2500.")]
        public string InputWavelengthRange { get; set; } = "400-2500";

        /// <summary>SAIL model version selection</summary>
        [Separator("Canopy Properties (SAIL)")]
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

        /// <summary>The expression for Leaf Area Index (LAI)</summary>
        [Description("LAI - Leaf Area Index (m\u00B2/m\u00B2)")]
        [Tooltip("Leaf Area Index. Can be a literal or APSIM expression (e.g., [Wheat].Leaf.LAI). Typical range: 0-10 m\u00B2/m\u00B2.")]
        public string LAI { get; set; } = "3.0";

        /// <summary>The expression for the Hot Spot parameter (q).</summary>
        [Description("q - Hot Spot (unitless)")]
        [Tooltip("Hot Spot parameter. Can be a literal or APSIM expression. Typical range: 0-1.")]
        public string HotSpot { get; set; } = "0.1";

        /// <summary>The expression for the LIDF type.</summary>
        [Description("TypeLidf - LIDF type")]
        [Tooltip("1 for Verhoef (uses LIDFa and LIDFb), 2 for Campbell (uses LIDFa only as mean leaf angle).")]
        public string TypeLidf { get; set; } = "2";

        /// <summary>The expression for LIDF parameter 'a'.</summary>
        [Description("LIDFa - Average leaf slope/angle")]
        [Tooltip("Average leaf slope (TypeLidf=1) or mean leaf angle in degrees (TypeLidf=2). Can be a literal or APSIM expression.")]
        public string LIDFa { get; set; } = "60.0";

        /// <summary>The expression for LIDF parameter 'b'.</summary>
        [Description("LIDFb - Bimodality")]
        [Tooltip("Bimodality parameter for Verhoef LIDF (TypeLidf=1 only). Ignored for Campbell. Typical range: -1 to 1.")]
        public string LIDFb { get; set; } = "-0.35";

        /// <summary>The expression for the fraction of brown leaf area.</summary>
        [Description("FractionBrown - Brown leaf fraction")]
        [Tooltip("Fraction of brown/senesced leaf area (unitless, 0-1). Used with 4SAIL2 version.")]
        public string FractionBrown { get; set; } = "0.0";

        /// <summary>The expression for the layer dissociation factor (diss).</summary>
        [Description("Diss - Dissociation factor")]
        [Tooltip("Layer dissociation factor for green/brown leaves (unitless, 0-1). Used with 4SAIL2 version.")]
        public string Dissociation { get; set; } = "0.0";

        /// <summary>The expression for the vertical crown cover percentage (cv).</summary>
        [Description("Cv - Crown cover")]
        [Tooltip("Vertical crown cover percentage (unitless, 0-1).")]
        public string CrownCover { get; set; } = "1.0";

        /// <summary>The expression for the tree shape factor (zeta).</summary>
        [Description("Zeta - Tree shape factor")]
        [Tooltip("Tree shape factor: crown diameter to height ratio (unitless).")]
        public string TreeShape { get; set; } = "1.0";
        #endregion

        #region Soil Reflectance
        /// <summary>Path to wet/dry soil reflectance data file.</summary>
        [Separator("Soil reflectance")]
        [Description("Wet/dry soil reflectance file")]
        [Tooltip("CSV file with columns: Wavelength, Dry_Soil, Wet_Soil. If not specified, built-in default data is used.")]
        [Display(Type = DisplayType.FileName)]
        public string WetDrySoilReflectancePath { get; set; }

        /// <summary>The expression for the soil brightness parameter (psoil).</summary>
        [Description("Psoil - Soil water content (0=dry, 1=wet)")]
        [Tooltip("Soil water content of the top layer (unitless; 0 for dry, 1 for wet). Can be a literal or APSIM expression. If not specified, auto-calculated from APSIM soil water model.")]
        public string Psoil { get; set; }
        #endregion

        #region Observation Dates and Per-Date Parameters (CSV file option first)
        /// <summary>Path to a CSV file containing observation dates and per-date parameters.</summary>
        [Separator("Observation data from CSV file (specify ObservationDates, SunZenithAngle, ObserverZenithAngle, RelativeAzimuthAngle from a file)")]
        [Description("Observation data CSV file")]
        [Tooltip("CSV file with columns: Date (required), Psoil, SunZenithAngle, ObserverZenithAngle, RelativeAzimuthAngle (all optional). "
            + "If provided, overrides the UI arrays and expression fields below for the 3 geometry angles and ObservationDates. "
            + "Leave empty to use the UI arrays or expression fields below, or to run daily.")]
        [Display(Type = DisplayType.FileName)]
        public string ObservationDataFilePath { get; set; }
        #endregion

        #region Observation Dates and Per-Date Parameters (UI input option)
        /// <summary>Observation dates specified via the UI.</summary>
        [Separator("Observation data from UI (used when no CSV file is specified above)")]
        [Description("Observation dates")]
        [Tooltip("Dates on which PROSAIL should run. Leave empty to run daily (whenever plant is alive and emerged). Ignored if a CSV file is specified above.")]
        [Display(VisibleCallback = nameof(NoObservationDataFile))]
        public DateTime[] ObservationDates { get; set; }

        /// <summary>Per-date Psoil values from the UI.</summary>
        [Description("Per-date Psoil values")]
        [Tooltip("One value per observation date, or a single value for all dates. If empty, uses the Psoil expression or auto-calculates from soil water.")]
        [Display(VisibleCallback = nameof(HasObservationDatesInUI))]
        public double[] PsoilValues { get; set; }

        /// <summary>Per-date sun zenith angle values from the UI.</summary>
        [Description("Per-date Sun Zenith Angle (\u00B0)")]
        [Tooltip("One value per observation date, or a single value for all dates. If empty, uses the SunZenithAngle expression below or default (30\u00B0).")]
        [Display(VisibleCallback = nameof(HasObservationDatesInUI))]
        public double[] SunZenithAngleValues { get; set; }

        /// <summary>Per-date observer zenith angle values from the UI.</summary>
        [Description("Per-date Observer Zenith Angle (\u00B0)")]
        [Tooltip("One value per observation date, or a single value for all dates. If empty, uses the ObserverZenithAngle expression below or default (0\u00B0).")]
        [Display(VisibleCallback = nameof(HasObservationDatesInUI))]
        public double[] ObserverZenithAngleValues { get; set; }

        /// <summary>Per-date relative azimuth angle values from the UI.</summary>
        [Description("Per-date Relative Azimuth Angle (\u00B0)")]
        [Tooltip("One value per observation date, or a single value for all dates. If empty, uses the RelativeAzimuthAngle expression below or default (0\u00B0).")]
        [Display(VisibleCallback = nameof(HasObservationDatesInUI))]
        public double[] RelativeAzimuthAngleValues { get; set; }

        /// <summary>True when no CSV file is specified (show UI arrays).</summary>
        public bool NoObservationDataFile => string.IsNullOrWhiteSpace(ObservationDataFilePath);

        /// <summary>True when observation dates are in the UI (not CSV) and at least one is set.</summary>
        public bool HasObservationDatesInUI => NoObservationDataFile
            && ObservationDates != null && ObservationDates.Length > 0;
        #endregion

        #region Sun-Observer Geometry (expression fallback)
        /// <summary>The expression for the sun zenith angle (tts).</summary>
        [Separator("Sun-Observer Geometry expressions (used when no CSV file or per-date UI array is specified)")]
        [Description("TTS - Sun zenith angle (\u00B0)")]
        [Tooltip("Sun zenith angle in degrees (0-90). Can be a literal or APSIM expression. Used as a fallback when neither the CSV file nor the per-date UI array is specified. Default: 30\u00B0 if left empty.")]
        public string SunZenithAngle { get; set; }

        /// <summary>The expression for the observer zenith angle (tto).</summary>
        [Description("TTO - Observer zenith angle (\u00B0)")]
        [Tooltip("Observer zenith angle in degrees (0-90). Can be a literal or APSIM expression. Used as a fallback when neither the CSV file nor the per-date UI array is specified. Default: 0\u00B0 if left empty.")]
        public string ObserverZenithAngle { get; set; }

        /// <summary>The expression for the relative azimuth angle (psi).</summary>
        [Description("PSI - Relative azimuth angle (\u00B0)")]
        [Tooltip("Relative azimuth angle between sun and observer in degrees (0-360). Can be a literal or APSIM expression. Used as a fallback when neither the CSV file nor the per-date UI array is specified. Default: 0\u00B0 if left empty.")]
        public string RelativeAzimuthAngle { get; set; }
        #endregion

        #region Sensor Selection
        /// <summary>List of available sensors for the drop-down.</summary>
        [Separator("Sensor selection")]
        [Description("Sensor type")]
        [Tooltip("Select a built-in sensor to use its spectral response function (SRF), or Custom to provide your own SRF CSV file.")]
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

        /// <summary>Holds the loaded spectral response function for the selected sensor.</summary>
        public SpectralResponseFunction SensorSRF { get; private set; }

        /// <summary>Path to a custom SRF CSV file (used when SensorType is Custom).</summary>
        [Description("Custom SRF CSV file")]
        [Tooltip("CSV file: first column = wavelength (nm), remaining columns = band SRF values. Column headers are used as band names.")]
        [Display(Type = DisplayType.FileName, VisibleCallback = nameof(IsCustomSensor))]
        public string CustomSRFPath { get; set; }

        /// <summary>Whether the user selected Custom sensor type.</summary>
        public bool IsCustomSensor => SensorType == SensorTypes.Custom;

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
            if (SensorType == SensorTypes.Custom)
            {
                SensorSRF = null; // Loaded in OnCommencing
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
        #endregion

        #region Logging
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

        /// <summary>Path to the SQLite database file (relative to simulation directory)</summary>
        private string ProsailSQLiteDatabasePath;

        /// <summary>The cached leaf spectral constants loaded at simulation start</summary>
        private LeafOpticalConsts? cachedLeafOpticalConstants = null;

        /// <summary>The cached atmospheric spectral data loaded at simulation start</summary>
        private AtmosphericSpectralData cachedAtmosphericSpectralData;

        /// <summary>The cached wet and dry soil reflectance at simulation start</summary>
        private WetDrySoilReflectance? cachedWetDrySoilReflectance = null;

        /// <summary>Cached PROSAIL results for the current day</summary>
        private CanopyOptics cachedProsailOutputs = null;

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

        /// <summary>Resolved per-date Psoil values (from CSV or UI).</summary>
        private double[] resolvedPsoilValues;
        /// <summary>Resolved per-date sun zenith values (from CSV or UI).</summary>
        private double[] resolvedSunZenithValues;
        /// <summary>Resolved per-date observer zenith values (from CSV or UI).</summary>
        private double[] resolvedObserverZenithValues;
        /// <summary>Resolved per-date relative azimuth values (from CSV or UI).</summary>
        private double[] resolvedRelativeAzimuthValues;
        #endregion

        /// <summary>Soil reflectance</summary>
        public SoilOptics SoilReflectance { get; set; } = new SoilOptics();
        /// <summary>Flag to enable daily SQLite database output</summary>
        public bool EnableSQLiteOutput { get; set; } = true;
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

                object value = ExpressionFunction.Evaluate(expression, this, Structure);
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
            CurrentParameterValues["FractionBrown"] = EvaluateExpression(FractionBrown);
            CurrentParameterValues["Dissociation"] = EvaluateExpression(Dissociation);
            CurrentParameterValues["CrownCover"] = EvaluateExpression(CrownCover);
            CurrentParameterValues["TreeShape"] = EvaluateExpression(TreeShape);
            CurrentParameterValues["WetDrySoilReflectancePath"] = WetDrySoilReflectancePath;
        }

        /// <summary>
        /// Validate the ranges of all PROSAIL parameters.
        /// </summary>
        private void ValidateParameterRanges()
        {
            var ranges = new Dictionary<string, (double min, double max, string description)>
            {
                { "N", (1.0, 2.6, "Leaf structure parameter (unitless)") },
                { "CAB", (10.0, 80.0, "Chlorophyll a+b content (\u03BCg/cm\u00B2)") },
                { "CAR", (1.0, 24.0, "Carotenoid content (\u03BCg/cm\u00B2)") },
                { "EWT", (0.001, 0.08, "Equivalent Water Thickness (cm)") },
                { "LMA", (0.001, 0.02, "Leaf Mass per Area (g/cm\u00B2)") },
                { "BROWN", (0.0, 1.0, "Brown pigment content (unitless)") },
                { "ANT", (0.0, 10.0, "Anthocyanin content (\u03BCg/cm\u00B2)") },
                { "PROT", (0.0, 10.0, "Protein content (g/cm\u00B2)") },
                { "CBC", (0.0, 10.0, "NonProt Carbon-based constituent content (g/cm\u00B2)") },
                { "Alpha", (0.0, 90.0, "Incidence angle (degrees)") },
                { "LAI", (0.0, 10.0, "Leaf Area Index (m\u00B2/m\u00B2)") },
                { "HotSpot", (0.0, 1.0, "Hot Spot parameter (unitless)") },
                { "TypeLidf", (1.0, 2.0, "LIDF type (1 or 2)") },
                { "LIDFa", (-90.0, 90.0, "LIDF parameter a") },
                { "LIDFb", (-1.0, 1.0, "LIDF parameter b") },
                { "FractionBrown", (0.0, 1.0, "Fraction of brown leaf area") },
                { "Dissociation", (0.0, 1.0, "Layer dissociation factor") },
                { "CrownCover", (0.0, 1.0, "Vertical crown cover") },
                { "TreeShape", (0.0, 10.0, "Tree shape factor") },
                { "Psoil", (0.0, 1.0, "Soil water content factor") },
                { "SunZenithAngle", (0.0, 90.0, "Sun zenith angle (degrees)") },
                { "ObserverZenithAngle", (0.0, 90.0, "Observer zenith angle (degrees)") },
                { "RelativeAzimuthAngle", (0.0, 360.0, "Relative azimuth angle (degrees)") }
            };

            foreach (var param in ranges)
            {
                if (CurrentParameterValues.TryGetValue(param.Key, out object value))
                {
                    double numericValue;
                    try
                    {
                        numericValue = Convert.ToDouble(value);
                    }
                    catch (Exception ex)
                    {
                        string msg = $"Parameter '{param.Key}' value '{value}' cannot be converted to a numeric value on {Clock?.Today:yyyy-MM-dd}. Error: {ex.Message}";
                        WriteMessage(LogLevel.Error, msg);
                        throw new InvalidOperationException(msg);
                    }

                    if (numericValue < param.Value.min || numericValue > param.Value.max)
                    {
                        string msg = $"Parameter '{param.Key}' value {numericValue} may be out of range [{param.Value.min}, {param.Value.max}] ({param.Value.description}) on {Clock?.Today:yyyy-MM-dd}. Check this paper: https://doi.org/10.3390/rs10010085";
                        WriteMessage(LogLevel.Warning, msg);
                    }
                }
                else
                {
                    string msg = $"Parameter '{param.Key}' is missing from CurrentParameterValues on {Clock?.Today:yyyy-MM-dd}.";
                    WriteMessage(LogLevel.Error, msg);
                    throw new InvalidOperationException(msg);
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

            CanopyOptics results = ProsailCore.PRO4SAIL(
                leafOpticalConstants: cachedLeafOpticalConstants,
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
                Wavelengths: inputWavelengths,
                SailVersion: SailVersionValue,
                LAI: Convert.ToDouble(CurrentParameterValues["LAI"]),
                HotSpot: Convert.ToDouble(CurrentParameterValues["HotSpot"]),
                TypeLidf: TypeLidfValue,
                LIDFa: Convert.ToDouble(CurrentParameterValues["LIDFa"]),
                LIDFb: Convert.ToDouble(CurrentParameterValues["LIDFb"]),
                FractionBrown: Convert.ToDouble(CurrentParameterValues["FractionBrown"]),
                Diss: Convert.ToDouble(CurrentParameterValues["Dissociation"]),
                Cv: Convert.ToDouble(CurrentParameterValues["CrownCover"]),
                Zeta: Convert.ToDouble(CurrentParameterValues["TreeShape"]),
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

            // Load wet/dry soil reflectance data (CSV or default JSON)
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
            cachedWetDrySoilReflectance = cachedWetDrySoilReflectance.Value.SubsetByWavelengths(inputWavelengths);
            cachedAtmosphericSpectralData = cachedAtmosphericSpectralData.SubsetByWavelengths(inputWavelengths);

            // Load custom SRF if selected
            if (SensorType == SensorTypes.Custom)
            {
                if (string.IsNullOrWhiteSpace(CustomSRFPath))
                    throw new InvalidOperationException("Custom sensor selected but no SRF file specified.");

                string resolvedSRFPath = ProsailInputLoader.ResolvePath(CustomSRFPath, Simulation.FileName);
                SensorSRF = ProsailInputLoader.LoadSRFFromCsv(resolvedSRFPath);
                WriteMessage(LogLevel.Info, $"ProsailModel: Custom SRF loaded from {resolvedSRFPath}.");
            }

            // Load observation dates and per-date parameters
            if (!string.IsNullOrWhiteSpace(ObservationDataFilePath))
            {
                string resolvedObsPath = ProsailInputLoader.ResolvePath(ObservationDataFilePath, Simulation.FileName);
                var obsData = ProsailInputLoader.LoadObservationDataFromFile(resolvedObsPath);
                ObservationDates = obsData.Dates;
                resolvedPsoilValues = obsData.PsoilValues;
                resolvedSunZenithValues = obsData.SunZenithAngleValues;
                resolvedObserverZenithValues = obsData.ObserverZenithAngleValues;
                resolvedRelativeAzimuthValues = obsData.RelativeAzimuthAngleValues;
                WriteMessage(LogLevel.Info, $"ProsailModel: Loaded {ObservationDates.Length} observation dates from {ObservationDataFilePath}.");
            }
            else
            {
                // Use UI arrays directly
                resolvedPsoilValues = PsoilValues;
                resolvedSunZenithValues = SunZenithAngleValues;
                resolvedObserverZenithValues = ObserverZenithAngleValues;
                resolvedRelativeAzimuthValues = RelativeAzimuthAngleValues;
            }

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
            dbConnection = ProsailDatabaseHelper.InitializeDatabase(dbPath, simulationName, Clock.StartDate, Clock.EndDate, WriteMessage);
            if (dbConnection == null)
                EnableSQLiteOutput = false;
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

            // Resolve Psoil: per-date array > expression > auto-calc from soil water
            double? psoilResolved = ProsailInputLoader.ResolveObservationParameter(
                resolvedPsoilValues, Psoil, "Psoil", Clock.Today, observationDateLookup,
                EvaluateExpression, allowAutoCalc: true, writeMessage: WriteMessage);
            double psoilValue = psoilResolved ?? waterBalance.SW[0];
            if (!psoilResolved.HasValue)
                WriteMessage(LogLevel.Info, "ProsailModel: Psoil auto-calculated from soil water content.");

            SoilReflectance = CalculateSoilReflectanceFromWetDry((WetDrySoilReflectance)cachedWetDrySoilReflectance, psoilValue);

            // Evaluate PROSPECT/SAIL expression parameters
            CurrentParameterValues.Clear();
            EvaluateAllParameters();

            // Resolve geometry: per-date array > expression > hardcoded defaults
            CurrentParameterValues["SunZenithAngle"] = ProsailInputLoader.ResolveObservationParameter(
                resolvedSunZenithValues, SunZenithAngle, "SunZenithAngle", Clock.Today, observationDateLookup,
                EvaluateExpression, defaultValue: 30.0, writeMessage: WriteMessage);
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

                // Compute BRF
                CanopyBRF canopyBRF = ComputeBRF(
                    wavelength: canopyOpticalVariables.Wavelength,
                    rdot: canopyOpticalVariables.Rdot,
                    rsot: canopyOpticalVariables.Rsot,
                    tts: Convert.ToDouble(CurrentParameterValues["SunZenithAngle"]),
                    atmosphericSpectralData: cachedAtmosphericSpectralData);

                // Compute fAPAR
                double fAPAR = ComputeFAPAR(
                    abs_dir: canopyOpticalVariables.Abs_dir,
                    abs_hem: canopyOpticalVariables.Abs_hem,
                    tts: Convert.ToDouble(CurrentParameterValues["SunZenithAngle"]),
                    atmosphericSpectralData: cachedAtmosphericSpectralData);

                // Compute broadband albedo
                double albedo = ComputeAlbedo(
                    rddstar: canopyOpticalVariables.Rddstar,
                    rsdstar: canopyOpticalVariables.Rsdstar,
                    tts: Convert.ToDouble(CurrentParameterValues["SunZenithAngle"]),
                    atmosphericSpectralData: cachedAtmosphericSpectralData);

                // Spectral resampling to sensor
                SpectralResamplingResult resampledReflectance = ResampleReflectanceToSensor(
                    wavelength: canopyBRF.Wavelength.ToArray(),
                    reflectance: canopyBRF.BRF.ToArray(),
                    srf: SensorSRF);

                var canopyStateVariables = new CanopyStateVariables
                {
                    fAPAR = fAPAR,
                    fcover = canopyOpticalVariables.FCover.Distinct().First(),
                    albedo = albedo
                };

                // Save to database
                if (EnableSQLiteOutput && dbConnection != null)
                {
                    string sensorTypeStr = SensorType == SensorTypes.Custom
                        ? $"Custom:{CustomSRFPath}"
                        : SensorType.ToString();

                    ProsailDatabaseHelper.WriteToDatabase(dbConnection, simulationName, Clock.Today,
                        CurrentParameterValues, WetDrySoilReflectancePath, SailVersionString, sensorTypeStr,
                        canopyOpticalVariables, canopyStateVariables, canopyBRF, resampledReflectance, WriteMessage);
                    WriteMessage(LogLevel.Info, $"ProsailModel: Wrote results to database for {Clock.Today:yyyy-MM-dd}.");
                }
                else
                {
                    WriteMessage(LogLevel.Info, "ProsailModel: SQLite output disabled, results not saved to database.");
                }
            }
            catch (Exception ex)
            {
                WriteMessage(LogLevel.Error, $"ProsailModel: Error in OnDoEndOfDay: {ex.Message}");
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

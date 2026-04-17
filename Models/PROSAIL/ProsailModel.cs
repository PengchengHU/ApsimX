using APSIM.Shared.Utilities;
using MathNet.Numerics.LinearAlgebra;
using Models.Core;
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

        #region Sun-Observer Geometry
        /// <summary>Observation dates specified via the UI.</summary>
        [Separator("Sun-Observer Geometry")]
        [Description("Observation dates")]
        [Tooltip("Dates on which PROSAIL will run. If empty, PROSAIL runs every day the plant is alive and emerged. Dates outside the simulation range are ignored with a warning.")]
        public DateTime[] ObservationDates { get; set; }

        /// <summary>The expression or per-date list for the sun zenith angle (tts).</summary>
        [Description("TTS - Sun zenith angle (\u00B0)")]
        [Tooltip("Sun zenith angle (°, 0–90). Accepts: a single literal (e.g. 30) applied to all dates; a comma-separated list of one value per observation date (e.g. 25, 30, 35); or an APSIM expression evaluated each day. Defaults to 30° if left unchanged.")]
        public string SunZenithAngle { get; set; } = "30";

        /// <summary>The expression or per-date list for the observer zenith angle (tto).</summary>
        [Description("TTO - Observer zenith angle (\u00B0)")]
        [Tooltip("Observer (sensor) zenith angle (°, 0–90). Accepts: a single literal (e.g. 0); a comma-separated list of one value per observation date; or an APSIM expression evaluated each day. Defaults to 0°.")]
        public string ObserverZenithAngle { get; set; } = "0";

        /// <summary>The expression or per-date list for the relative azimuth angle (psi).</summary>
        [Description("PSI - Relative azimuth angle (\u00B0)")]
        [Tooltip("Relative azimuth angle between sun and observer (°, 0–360). Accepts: a single literal (e.g. 0); a comma-separated list of one value per observation date; or an APSIM expression evaluated each day. Defaults to 0°.")]
        public string RelativeAzimuthAngle { get; set; } = "0";

        /// <summary>True when observation dates are specified in the UI and at least one is set.</summary>
        public bool HasObservationDatesInUI => ObservationDates != null && ObservationDates.Length > 0;
        #endregion

        #region Soil Reflectance
        /// <summary>Use BSM (Brightness Soil Model) for soil reflectance instead of wet/dry interpolation.</summary>
        [Separator("Soil reflectance")]
        [Description("Use BSM for soil reflectance")]
        [Tooltip("If enabled, uses the Brightness Soil Model (BSM; Verhoef et al. 2018) to simulate soil reflectance from BsmBrightness, BsmLat, BsmLon, and SMp. If disabled, reflectance is a linear mix of dry and wet spectra weighted by Psoil.")]
        public bool UseBSM { get; set; } = false;

        /// <summary>Returns true when BSM is NOT selected.</summary>
        public bool IsNotBSM => !UseBSM;

        /// <summary>Path to wet/dry soil reflectance data file.</summary>
        [Description("Wet/dry soil reflectance file")]
        [Tooltip("Optional CSV file (columns: Wavelength, Dry_Soil, Wet_Soil) to override the built-in SpecSOIL.json data. Leave empty to use the built-in default.")]
        [Display(Type = DisplayType.FileName, VisibleCallback = nameof(IsNotBSM))]
        public string WetDrySoilReflectancePath { get; set; }

        /// <summary>Psoil — dry-to-wet mixing factor, per-date list, or APSIM expression.</summary>
        [Description("Psoil - Soil dry-to-wet factor (0=wet, 1=dry)")]
        [Tooltip("Dry-to-wet mixing factor (0 = fully wet, 1 = fully dry). Accepts: a single literal (e.g. 0.5) applied to all dates; a comma-separated list of one value per observation date (e.g. 0.3, 0.5, 0.7); or an APSIM expression evaluated each day (e.g. 1 - [WaterBalance].SW[1]). Defaults to 1 \u2212 [WaterBalance].SW[1].")]
        [Display(VisibleCallback = nameof(IsNotBSM))]
        public string Psoil { get; set; } = "1 - [WaterBalance].SW[1]";

        /// <summary>BSM soil brightness parameter.</summary>
        [Description("BsmBrightness - Soil brightness (0-1)")]
        [Tooltip("Soil brightness scaling factor (0\u20131) for BSM. Scales the magnitude of the dry soil spectrum. Enter a literal (e.g. 0.5) or an APSIM expression.")]
        [Display(VisibleCallback = nameof(UseBSM))]
        public string BsmBrightness { get; set; } = "0.5";

        /// <summary>BSM spectral shape latitude.</summary>
        [Description("BsmLat - Spectral shape latitude (20-40\u00B0)")]
        [Tooltip("Spectral latitude for BSM (recommended 20\u201340\u00B0). Controls the spectral shape of the dry soil spectrum. Enter a literal or APSIM expression.")]
        [Display(VisibleCallback = nameof(UseBSM))]
        public string BsmLat { get; set; } = "25";

        /// <summary>BSM spectral shape longitude.</summary>
        [Description("BsmLon - Spectral shape longitude (45-65\u00B0)")]
        [Tooltip("Spectral longitude for BSM (recommended 45\u201365\u00B0). Controls the spectral shape of the dry soil spectrum. Enter a literal or APSIM expression.")]
        [Display(VisibleCallback = nameof(UseBSM))]
        public string BsmLon { get; set; } = "45";

        /// <summary>SMp — soil moisture percentage, per-date list, or APSIM expression.</summary>
        [Description("SMp - Soil moisture percentage (5-55%)")]
        [Tooltip("Soil moisture volume percentage (5\u201355%) for BSM. Accepts: a single literal (e.g. 25) applied to all dates; a comma-separated list of one value per observation date (e.g. 20, 25, 30); or an APSIM expression evaluated each day (e.g. [WaterBalance].SW[1] * 100). Defaults to [WaterBalance].SW[1] * 100.")]
        [Display(VisibleCallback = nameof(UseBSM))]
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
        [Description("Save Parameters to database")]
        [Tooltip("Save daily PROSAIL input parameters (leaf, canopy, soil, geometry, sensor) to the Parameters table.")]
        public bool OutputParameters { get; set; } = true;

        /// <summary>Whether to write the CanopyOpticalVariable table to the database.</summary>
        [Description("Save CanopyOpticalVariable to database")]
        [Tooltip("Save per-wavelength canopy optical variables (Rdot, Rsot, Rddt, Rsdt, FCover, Abs_dir, Abs_hem, Rsdstar, Rddstar) to the CanopyOpticalVariable table.")]
        public bool OutputCanopyOpticalVariable { get; set; } = true;

        /// <summary>Whether to compute and save canopy state variables (fAPAR, fCover, albedo).</summary>
        [Description("Compute and save CanopyStateVariable to database")]
        [Tooltip("Compute broadband fAPAR, fCover, and albedo and save them to the CanopyStateVariable table.")]
        public bool OutputCanopyStateVariable { get; set; } = true;

        /// <summary>Whether to compute and save canopy BRF.</summary>
        [Description("Compute and save CanopyBRF to database")]
        [Tooltip("Compute per-wavelength bidirectional reflectance factor (BRF) and save it to the CanopyBRF table.")]
        public bool OutputCanopyBRF { get; set; } = true;

        /// <summary>Whether to compute and save reflectance resampled to a sensor.</summary>
        [Description("Compute and save ReflectanceResampledToSensor to database")]
        [Tooltip("Resample BRF to sensor bands and save to the ReflectanceResampledToSensor table. Requires selecting a sensor type below.")]
        public bool OutputReflectanceResampledToSensor { get; set; } = true;

        /// <summary>Sensor type used for spectral resampling. Visible only when ReflectanceResampledToSensor output is enabled.</summary>
        [Description("Sensor type")]
        [Tooltip("Select a built-in sensor to use its spectral response function (SRF), or Custom to provide your own SRF CSV file.")]
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
        [Tooltip("CSV file: first column = wavelength (nm), remaining columns = band SRF values. Column headers are used as band names.")]
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

        /// <summary>The cached BSM spectral data (loaded if UseBSM = true)</summary>
        private BsmSpectralData? cachedBsmData = null;

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

            // Load soil reflectance data
            if (UseBSM)
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
            if (!UseBSM)
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
            if (UseBSM)
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
                        fcover = canopyOpticalVariables.FCover.Distinct().First(),
                        albedo = albedo
                    };
                }

                // Spectral resampling to sensor
                SpectralResamplingResult resampledReflectance = null;
                if (OutputReflectanceResampledToSensor && canopyBRF.HasValue)
                {
                    resampledReflectance = ResampleReflectanceToSensor(
                        wavelength: canopyBRF.Value.Wavelength.ToArray(),
                        reflectance: canopyBRF.Value.BRF.ToArray(),
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

using APSIM.Shared.APSoil;
using APSIM.Shared.Utilities;
using MathNet.Numerics.LinearAlgebra;
using Microsoft.CodeAnalysis;
using Models.Core;
using Models.Functions;
using Models.Interfaces;
using Models.PMF;
using Models.PMF.Organs;
using APSIM.Core;
using Models.Prosail;
using Models.PROSAIL.PROSPECT;
using Models.PROSAIL.SAIL;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using static Models.Prosail.ProsailCore;
using static Models.PROSAIL.PROSPECT.ProspectCore;
using static Models.PROSAIL.SAIL.SailUtilities;

namespace Models.PROSAIL
{
    /// <summary>
    /// Model implementing the PROSAIL radiative transfer model for canopy optical properties in APSIM
    /// with configurable parameter expressions and spectral data output to SQLite
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

        /// <summary> Link to simulation for file paths and name</summary>
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
        /// <summary>APSIM-PROSPECT: leaf optics</summary>
        [Separator("Expressions to link APSIM variables and PROSPECT inputs")]

        // <summary>The expression for N (Leaf structure parameter)</summary>
        [Description("N - Leaf structure parameter (unitless)")]
        public string N { get; set; } = "1.5";

        /// <summary>The expression for CAB (Chlorophyll a + b content)</summary>
        [Description("CAB - Chlorophyll a + b content (μg/cm²)")]
        public string CAB { get; set; } = "40.0";

        /// <summary>The expression for CAR (Carotenoid content)</summary>
        [Description("CAR - Carotenoid content (μg/cm²)")]
        public string CAR { get; set; } = "8.0";

        /// <summary>The expression for EWT (Equivalent Water Thickness)</summary>
        [Description("EWT or CW - Equivalent Water Thickness or Water depth (cm)")]
        public string EWT { get; set; } = "0.01";

        /// <summary>The expression for LMA (Leaf Mass per Area)</summary>
        [Description("LMA or CM - Leaf Mass per Area or Dry matter content (g/cm²)")]
        public string LMA { get; set; } = "0.008";

        /// <summary>The expression for BROWN (Brown pigment content)</summary>
        [Description("BROWN - Brown pigment content (unitless)")]
        public string BROWN { get; set; } = "0.0";

        /// <summary>The expression for ANT (Anthocyanin content)</summary>
        [Description("ANT - Anthocyanin content (μg/cm²)")]
        public string ANT { get; set; } = "0.0";

        /// <summary>The expression for PROT (Protein content)</summary>
        [Description("PROT - Protein content (g/cm²)")]
        public string PROT { get; set; } = "0.0";

        /// <summary>The expression for CBC (NonProt Carbon-based constituent content)</summary>
        [Description("CBC - NonProt Carbon-based constituent content (g/cm²)")]
        public string CBC { get; set; } = "0.0";

        /// <summary>The expression for alpha (Incidence angle in degrees)</summary>
        [Description("Alpha - Incidence angle in degrees (°}")]
        public string Alpha { get; set; } = "40.0";

        /// <summary> Spectral range to simulate (start-end in nm)</summary>
        [Description("Spectral range to simulate in nm supports ranges (e.g., '400-500'),\nlists (e.g., '400, 500, 600'), \nand mixed formats (e.g., '400, 500-600, 700')")]
        public string InputWavelengthRange { get; set; } = "400-2500";


        /// <summary>Enum for supported SAIL model versions.</summary>
        public enum SailVersionTypes
        {
            /// <summary>4SAIL - single layer canopy model</summary>
            FourSAIL,
            /// <summary>4SAIL2 - two layer canopy model (green + brown)</summary>
            FourSAIL2
        }

        /// <summary>SAIL: Canopy optics </summary>
        [Separator("Canopy Properties (SAIL)")]
        // <summary>SAIL model version selection</summary>
        [Description("SAIL model version (4SAIL: single layer, 4SAIL2: green + brown layers)")]
        public SailVersionTypes SailVersion
        {
            get => sailVersion;
            set => sailVersion = value;
        }
        private SailVersionTypes sailVersion = SailVersionTypes.FourSAIL;

        /// <summary>Returns the SAIL version string used internally by the model core.</summary>
        private string SailVersionString => SailVersion == SailVersionTypes.FourSAIL2 ? "4SAIL2" : "4SAIL";

        /// <summary>The expression for Leaf Area Index (LAI)</summary>
        [Description("LAI - Leaf Area Index (m²/m²)")]
        public string LAI { get; set; } = "3.0";

        /// <summary>The expression for the Hot Spot parameter (q).</summary>
        [Description("q - Hot Spot parameter (unitless, 0-1)")]
        public string HotSpot { get; set; } = "0.1";

        /// <summary>The expression for the LIDF type.</summary>
        [Description("TypeLidf - LIDF type (1 for Verhoef, 2 for Campbell)")]
        public string TypeLidf { get; set; } = "2";

        /// <summary>The expression for LIDF parameter 'a'.</summary>
        [Description("LIDFa - Average leaf slope (TypeLidf=1) or angle (TypeLidf=2)")]
        public string LIDFa { get; set; } = "60.0";

        /// <summary>The expression for LIDF parameter 'b'.</summary>
        [Description("LIDFb - Bimodality (TypeLidf=1 only)")]
        public string LIDFb { get; set; } = "-0.35";

        /// <summary>The expression for the fraction of brown leaf area.</summary>
        [Description("FractionBrown - Fraction of brown/senesced leaf area (unitless, 0-1)")]
        public string FractionBrown { get; set; } = "0.0";

        /// <summary>The expression for the layer dissociation factor (diss).</summary>
        [Description("Diss - Layer dissociation factor for green/brown leaves (unitless, 0-1)")]
        public string Dissociation { get; set; } = "0.0";

        /// <summary>The expression for the vertical crown cover percentage (cv).</summary>
        [Description("Cv - Vertical crown cover percentage (unitless, 0-1)")]
        public string CrownCover { get; set; } = "1.0";

        /// <summary>The expression for the tree shape factor (zeta).</summary>
        [Description("Zeta - Tree shape factor (crown diameter to height ratio; unitless)")]
        public string TreeShape { get; set; } = "1.0";


        /// <summary>Soil reflectance </summary>
        [Separator("Soil reflectance")]
        // <summary>The expression for the soil brightness parameter (rsoil).</summary>
        [Description("Path to .Json file containing the data of wet and dry soil reflectance.\nIt must have the Wavelength, Dry_Soil and Wet_Soil lists.\nIf not specificed, a defualt file will be used!")]
        public string WetDrySoilReflectanceJsonPath { get; set; }

        /// <summary>The expression for the soil brightness parameter (psoil).</summary>
        [Description("psoil - Dry to Wet soil factor (unitless; 0 for wet, 1 for dry).\n If it is not specified, APSIM calculated soil water content of the top soil layer will be used.")]
        public string Psoil { get; set; }

        /// <summary>Sun-Observer Geometry</summary>
        [Separator("Sun-Observer Geometry")]
        // <summary>The expression for the sun zenith angle (tts).</summary>
        [Description("TTS - Sun zenith angle in degrees (0-90)")]
        public string SunZenithAngle { get; set; } = "30.0";

        /// <summary>The expression for the observer zenith angle (tto).</summary>
        [Description("TTO - Observer zenith angle in degrees (0-90)")]
        public string ObserverZenithAngle { get; set; } = "0.0";

        /// <summary>The expression for the relative azimuth angle (psi).</summary>
        [Description("psi - Relative azimuth angle between sun and observer (0-360)")]
        public string RelativeAzimuthAngle { get; set; } = "0.0";

        /// <summary>
        /// List of available sensors for the drop-down.
        /// </summary>
        [Separator("Sensor selection")]
        [Description("Select a sensor to use its spectral response function (SRF)")]
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

        /// <summary>
        /// Enum for supported sensors.
        /// </summary>
        public enum SensorTypes
        {
            /// <summary>Landsat_7</summary>
            Landsat_7,
            /// <summary>Landsat_8</summary>
            Landsat_8,
            /// <summary>Landsat_9</summary>
            Landsat_9,
            /// <summary>MODIS</summary>
            MODIS,
            /// <summary>Pleiades_1A</summary>
            Pleiades_1A,
            /// <summary>Pleiades_1B</summary>
            Pleiades_1B,
            /// <summary>Sentinel_2</summary>
            Sentinel_2,
            /// <summary>Sentinel_2A</summary>
            Sentinel_2A,
            /// <summary>Sentinel_2B</summary>
            Sentinel_2B,
            /// <summary>Sentinel_2C</summary>
            Sentinel_2C,
            /// <summary>SPOT_6_7</summary>
            SPOT_6_7,
            /// <summary>Venus</summary>
            Venus
        }

        /// <summary>
        /// Holds the loaded spectral response function for the selected sensor.
        /// </summary>
        public SpectralResponseFunction SensorSRF { get; private set; }

        /// <summary>
        /// Mapping from sensor enum to local SRF file path.
        /// </summary>
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

        /// <summary>
        /// Loads the SRF for the currently selected sensor.
        /// </summary>
        private void SetSensorSRF()
        {
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

        /// <summary>
        /// Defines the logging verbosity levels
        /// </summary>
        public enum LogLevel
        {
            /// <summary>Log only errors</summary>
            Error,
            /// <summary>Log errors and warnings</summary>
            Warning,
            /// <summary>Log errors, warnings, and informational messages</summary>
            Info,
            /// <summary>Log all messages, including debug details</summary>
            Debug
        }

        /// <summary>Logging verbosity level</summary>
        [Description("Logging verbosity level (Error, Warning, Info, Debug)")]
        public LogLevel LoggingLevel { get; set; } = LogLevel.Info;
        #endregion

        #region Private Fields and Cached Data
        // Soil reflectance data
        private static string DefaultSpecSoilDataPath => Path.Combine(
            AppContext.BaseDirectory,
            "PROSAIL",
            "InputProperties",
            "SpectralData",
            "SpecSOIL.json");

        // Atmospheric reflectance data
        private static string DefaultSpecAtmDataPath => Path.Combine(
            AppContext.BaseDirectory,
            "PROSAIL",
            "InputProperties",
            "SpectralData",
            "SpecATM.json");

        /// <summary>Path to the SQLite database file (relative to simulation directory)</summary>
        private string ProsailSQLiteDatabasePath;

        /// <summary>The cached leaf spectral constants loaded at simulation start</summary>
        private LeafOpticalConsts? cachedLeafOpticalConstants = null;

        /// <summary>The cached atmoospheric spectral data loaded at simulation start</summary>
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
        #endregion

        /// <summary>Soil reflectance</summary>
        public SoilOptics SoilReflectance { get; set; } = new SoilOptics();
        /// <summary> Flag to enable daily SQLite database output</summary>
        public bool EnableSQLiteOutput { get; set; } = true;
        /// <summary> Input wavelengths</summary>
        public double[] inputWavelengths;


        /// <summary>
        /// Helper method to write messages based on logging level
        /// </summary>
        private void WriteMessage(LogLevel messageLevel, string message)
        {
            if ((int)messageLevel <= (int)LoggingLevel)
            {
                MessageType messageType = messageLevel switch
                {
                    LogLevel.Error => MessageType.Error,
                    LogLevel.Warning => MessageType.Warning,
                    _ => MessageType.Information // Info and Debug map to Information
                };
                Summary.WriteMessage(this, message, messageType);
            }
        }

        /// <summary>
        /// Get the full path to the database file
        /// </summary>
        private string GetFullDatabasePath()
        {
            string simDir = Path.GetDirectoryName(Simulation.FileName);
            if (Path.IsPathRooted(ProsailSQLiteDatabasePath))
                return ProsailSQLiteDatabasePath;
            else
                return Path.Combine(simDir, ProsailSQLiteDatabasePath);
        }

        /// <summary>
        /// Initialize the SQLite database for PROSAIL results
        /// </summary>
        private void InitializeDatabase()
        {
            try
            {
                string dbPath = GetFullDatabasePath();
                string dbDir = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
                {
                    Directory.CreateDirectory(dbDir);
                }

                dbConnection = new SQLite();
                dbConnection.OpenDatabase(dbPath, false);

                simulationName = Simulation.Name.Replace("'", "''");

                // Clear existing tables
                dbConnection.ExecuteNonQuery("DROP TABLE IF EXISTS CanopyOpticalVariable;");
                dbConnection.ExecuteNonQuery("DROP TABLE IF EXISTS CanopyStateVariable;");
                dbConnection.ExecuteNonQuery("DROP TABLE IF EXISTS CanopyBRF;");
                dbConnection.ExecuteNonQuery("DROP TABLE IF EXISTS resampledReflectance; ");
                dbConnection.ExecuteNonQuery("DROP TABLE IF EXISTS Parameters;");
                dbConnection.ExecuteNonQuery("DROP TABLE IF EXISTS Simulations;");

                // Create new tables
                dbConnection.ExecuteNonQuery(@"
                    CREATE TABLE IF NOT EXISTS Simulations (
                        SimulationName TEXT PRIMARY KEY,
                        StartDate TEXT,
                        EndDate TEXT,
                        CreatedAt TEXT
                    )");

                dbConnection.ExecuteNonQuery(@"
                    CREATE TABLE IF NOT EXISTS Parameters (
                        SimulationName TEXT,
                        Date TEXT,
                        N REAL,
                        CAB REAL,
                        CAR REAL,
                        EWT REAL,
                        LMA REAL,
                        ANT REAL,
                        BROWN REAL,
                        PROT REAL,
                        CBC REAL,
                        Alpha REAL,
                        LAI REAL,
                        HotSpot REAL,
                        TypeLidf REAL,
                        LIDFa REAL,
                        LIDFb REAL,
                        FractionBrown REAL,
                        Dissociation REAL,
                        CrownCover REAL,
                        TreeShape REAL,
                        WetDrySoilReflectanceJsonPath TEXT,
                        Psoil REAL,
                        SunZenithAngle REAL,
                        ObserverZenithAngle REAL,
                        RelativeAzimuthAngle REAL,
                        SailVersion TEXT,
                        SensorType TEXT,
                        PRIMARY KEY (SimulationName, Date),
                        FOREIGN KEY (SimulationName) REFERENCES Simulations(SimulationName)
                    )");

                dbConnection.ExecuteNonQuery(@"
                    CREATE TABLE IF NOT EXISTS CanopyOpticalVariable (
                        SimulationName TEXT,
                        Date TEXT,
                        Wavelength REAL,
                        Rdot REAL,  
                        Rsot REAL, 
                        Rddt REAL,
                        Rsdt REAL,
                        FCover REAL,
                        Abs_dir REAL,
                        Abs_hem REAL,
                        Rsdstar REAL,
                        Rddstar REAL,
                        PRIMARY KEY (SimulationName, Date, Wavelength),
                        FOREIGN KEY (SimulationName, Date) REFERENCES Parameters(SimulationName, Date)
                    )");

                dbConnection.ExecuteNonQuery(@"
                    CREATE TABLE IF NOT EXISTS CanopyStateVariable (
                        SimulationName TEXT,
                        Date TEXT,
                        fAPAR REAL,  
                        fCover REAL,
                        albedo REAL,
                        PRIMARY KEY (SimulationName, Date),
                        FOREIGN KEY (SimulationName, Date) REFERENCES Parameters(SimulationName, Date)
                    )");

                dbConnection.ExecuteNonQuery(@"
                    CREATE TABLE IF NOT EXISTS CanopyBRF (
                        SimulationName TEXT,
                        Date TEXT,
                        Wavelength REAL,
                        BRF REAL, 
                        PRIMARY KEY (SimulationName, Date, Wavelength),
                        FOREIGN KEY (SimulationName, Date) REFERENCES Parameters(SimulationName, Date)
                    )");

                dbConnection.ExecuteNonQuery(@"
                    CREATE TABLE IF NOT EXISTS ReflectanceResampledToSensor (
                        SimulationName TEXT,
                        Date TEXT,
                        Wavelength REAL,
                        BandName TEXT,
                        Reflectance REAL, 
                        PRIMARY KEY (SimulationName, Date, Wavelength),
                        FOREIGN KEY (SimulationName, Date) REFERENCES Parameters(SimulationName, Date)
                    )");


                string sql = $@"
                    INSERT INTO Simulations (SimulationName, StartDate, EndDate, CreatedAt)
                    VALUES ('{simulationName}', '{Clock.StartDate:yyyy-MM-dd}', '{Clock.EndDate:yyyy-MM-dd}', '{DateTime.Now:yyyy-MM-dd HH:mm:ss}')";
                dbConnection.ExecuteNonQuery(sql);

                WriteMessage(LogLevel.Info, $"PROSAIL database initialized: {dbPath}");
            }
            catch (Exception ex)
            {
                if (dbConnection != null)
                {
                    dbConnection.CloseDatabase();
                    dbConnection = null;
                }
                WriteMessage(LogLevel.Error, $"Failed to initialize PROSAIL database: {ex.Message}");
                EnableSQLiteOutput = false;
            }
        }

        /// <summary>
        /// Write PROSAIL results to the database
        /// </summary>
        private void WriteToDatabase(DateTime date, CanopyOptics canopyOptics, 
            CanopyStateVariables canopyStateVariables, CanopyBRF canopyBRF, 
            SpectralResamplingResult spectralResamplingResult)
        {
            // Quick check on key properties
            if (dbConnection == null || canopyOptics?.Wavelength == null)
            {
                WriteMessage(LogLevel.Error, "ProsailModel: WriteToDatabase skipped due to null dbConnection or canopy properties.");
                throw new InvalidOperationException("ProsailModel: WriteToDatabase skipped due to null dbConnection or canopy properties.");
            }

            double[] Rdot = canopyOptics.Rdot;
            double[] Rsot = canopyOptics.Rsot;
            double[] Rddt = canopyOptics.Rddt;
            double[] Rsdt = canopyOptics.Rsdt;
            double[] FCover = canopyOptics.FCover;
            double[] Abs_dir = canopyOptics.Abs_dir;
            double[] Abs_hem = canopyOptics.Abs_hem;
            double[] Rsdstar = canopyOptics.Rsdstar;
            double[] Rddstar = canopyOptics.Rddstar;
            double[] usedWavelength = canopyOptics.Wavelength;

            // Validation for all required arrays
            if (Rdot == null || Rsot == null || Rddt == null || Rsdt == null ||
                FCover == null || Abs_dir == null || Abs_hem == null || Rsdstar == null || Rddstar == null)
            {
                WriteMessage(LogLevel.Error, "ProsailModel: WriteToDatabase skipped due to one or more null canopy radiative property arrays.");
                throw new InvalidOperationException("ProsailModel: WriteToDatabase skipped due to one or more null canopy radiative property arrays.");
            }

            // Validate array lengths
            if (Rdot.Length != usedWavelength.Length ||
                Rsot.Length != usedWavelength.Length ||
                Rddt.Length != usedWavelength.Length ||
                Rsdt.Length != usedWavelength.Length ||
                FCover.Length != usedWavelength.Length ||
                Abs_dir.Length != usedWavelength.Length ||
                Abs_hem.Length != usedWavelength.Length ||
                Rsdstar.Length != usedWavelength.Length ||
                Rddstar.Length != usedWavelength.Length)
            {
                WriteMessage(LogLevel.Error, $"ProsailModel: Array length mismatch in WriteToDatabase");
                throw new InvalidOperationException("ProsailModel: Array length mismatch in WriteToDatabase.");
            }

            try
            {
                dbConnection.ExecuteNonQuery("BEGIN TRANSACTION;");
                string dateStr = date.ToString("yyyy-MM-dd");

                // Parameters INSERT
                string paramSql = $@"
            INSERT OR REPLACE INTO Parameters (
                SimulationName, Date, N, CAB, CAR, EWT, LMA, ANT, BROWN, PROT, CBC, Alpha,
                LAI, HotSpot, TypeLidf, LIDFa, LIDFb, FractionBrown, Dissociation, CrownCover, TreeShape,
                WetDrySoilReflectanceJsonPath, Psoil, SunZenithAngle, ObserverZenithAngle, RelativeAzimuthAngle, SailVersion,
                SensorType
            ) VALUES (
                '{simulationName}', '{dateStr}', {CurrentParameterValues["N"]}, {CurrentParameterValues["CAB"]}, 
                {CurrentParameterValues["CAR"]}, {CurrentParameterValues["EWT"]}, {CurrentParameterValues["LMA"]}, 
                {CurrentParameterValues["ANT"]}, {CurrentParameterValues["BROWN"]}, {CurrentParameterValues["PROT"]}, 
                {CurrentParameterValues["CBC"]}, {CurrentParameterValues["Alpha"]},
                {CurrentParameterValues["LAI"]}, {CurrentParameterValues["HotSpot"]}, {CurrentParameterValues["TypeLidf"]},
                {CurrentParameterValues["LIDFa"]}, {CurrentParameterValues["LIDFb"]}, {CurrentParameterValues["FractionBrown"]},
                {CurrentParameterValues["Dissociation"]}, {CurrentParameterValues["CrownCover"]}, {CurrentParameterValues["TreeShape"]},
                '{WetDrySoilReflectanceJsonPath?.Replace("'", "''") ?? ""}', {CurrentParameterValues["Psoil"]},
                {CurrentParameterValues["SunZenithAngle"]}, {CurrentParameterValues["ObserverZenithAngle"]}, 
                {CurrentParameterValues["RelativeAzimuthAngle"]}, '{SailVersionString}', '{SensorType.ToString()}'
            )";
                dbConnection.ExecuteNonQuery(paramSql);

                // CanopyOpticalVariable INSERT
                StringBuilder spectraSql = new StringBuilder("INSERT OR REPLACE INTO CanopyOpticalVariable (SimulationName, Date, Wavelength, Rdot, Rsot, Rddt, Rsdt, fCover, Abs_dir, Abs_hem, Rsdstar, Rddstar) VALUES ");
                bool firstSpectra = true;

                // Process all wavelengths
                for (int i = 0; i < usedWavelength.Length; i++)
                {
                    if (!firstSpectra)
                        spectraSql.Append(",");

                    spectraSql.Append($"('{simulationName}', '{dateStr}', {usedWavelength[i]}, {Rdot[i]}, {Rsot[i]}, {Rddt[i]}, {Rsdt[i]}, {FCover[i]}," +
                        $"{Abs_dir[i]}, {Abs_hem[i]}, {Rsdstar[i]}, {Rddstar[i]})");
                    firstSpectra = false;
                }

                if (!firstSpectra)
                {
                    spectraSql.Append(";");
                    WriteMessage(LogLevel.Debug, $"ProsailModel: Executing CanopyOpticalVariable INSERT.");
                    dbConnection.ExecuteNonQuery(spectraSql.ToString());
                }

                // CanopyStateVariable INSERT
                string stateSql = $@"
            INSERT OR REPLACE INTO CanopyStateVariable (
                SimulationName, Date, fAPAR, fCover, albedo
            ) VALUES (
                '{simulationName}', '{dateStr}', {canopyStateVariables.fAPAR}, {canopyStateVariables.fcover}, {canopyStateVariables.albedo}
            )";
                dbConnection.ExecuteNonQuery(stateSql);

                // ResampledReflectance INSERT 
                if (spectralResamplingResult != null && spectralResamplingResult.Reflectance != null)
                {
                    try
                    {
                        StringBuilder resampledSql = new StringBuilder("INSERT OR REPLACE INTO ReflectanceResampledToSensor (SimulationName, Date, Wavelength, BandName, Reflectance) VALUES ");
                        bool firstRow = true;

                        // Iterate through each band's resampled reflectance values
                        for (int bandIndex = 0; bandIndex < spectralResamplingResult.Reflectance.Count; bandIndex++)
                        {
                            double[] bandReflectance = spectralResamplingResult.Reflectance[bandIndex];
                            string bandName = spectralResamplingResult.BandNames[bandIndex];
                            double wavelength = spectralResamplingResult.Wavelength[bandIndex];

                            if (bandReflectance == null || bandReflectance.Length == 0)
                            {
                                WriteMessage(LogLevel.Warning, $"ProsailModel: No reflectance data for band '{bandName}' on {dateStr} when resampling reflectance to sensor.");
                                continue; // Skip empty bands
                            }

                            // For each reflectance value in the band
                            foreach (double reflectance in bandReflectance)
                            {
                                if (!firstRow)
                                    resampledSql.Append(",");

                                // Escape the band name with single quotes and handle any quotes within the band name
                                string escapedBandName = bandName.Replace("'", "''");
                                resampledSql.Append($"('{simulationName}', '{dateStr}', {wavelength}, '{escapedBandName}', {reflectance})");
                                firstRow = false;
                            }
                        }

                        if (!firstRow) // Only execute if we have data to insert
                        {
                            resampledSql.Append(";");
                            dbConnection.ExecuteNonQuery(resampledSql.ToString());
                            WriteMessage(LogLevel.Debug, $"ProsailModel: Successfully wrote resampled reflectance data to database for {dateStr}");
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteMessage(LogLevel.Error, $"ProsailModel: Failed to write resampled reflectance data: {ex.Message}");
                        throw;
                    }
                }

                // CanopyBRF INSERT
                if (canopyBRF.Wavelength != null && canopyBRF.BRF != null)
                {
                    StringBuilder brfSql = new StringBuilder("INSERT OR REPLACE INTO CanopyBRF (SimulationName, Date, Wavelength, BRF) VALUES ");
                    bool firstBRF = true;
                    for (int i = 0; i < canopyBRF.Wavelength.Count; i++)
                    {
                        if (!firstBRF)
                            brfSql.Append(",");
                        brfSql.Append($"('{simulationName}', '{dateStr}', {canopyBRF.Wavelength[i]}, {canopyBRF.BRF[i]})");
                        firstBRF = false;
                    }
                    if (!firstBRF)
                    {
                        brfSql.Append(";");
                        dbConnection.ExecuteNonQuery(brfSql.ToString());
                        WriteMessage(LogLevel.Debug, $"ProsailModel: Successfully wrote CanopyBRF data to database for {dateStr}");
                    }
                }

                dbConnection.ExecuteNonQuery("COMMIT;");

                WriteMessage(LogLevel.Info, $"ProsailModel: Wrote results for {date:yyyy-MM-dd} to database.");
            }
            catch (Exception ex)
            {
                dbConnection.ExecuteNonQuery("ROLLBACK;");
                WriteMessage(LogLevel.Error, $"ProsailModel: Failed to write to database: {ex.Message}");
                throw;
            }
        }


        /// <summary>
        /// Parses the wavelength range from the specified string and returns the list of wavelengths.
        /// </summary>
        /// <returns>A sorted list of wavelengths (in nm) parsed from the input string. Returns an empty list if parsing fails.</returns>
        private List<double> ParseWavelengthRange()
        {
            List<double> wavelengths = new List<double>();

            // Default range if input is empty
            if (string.IsNullOrWhiteSpace(InputWavelengthRange))
            {
                WriteMessage(LogLevel.Info, "ProsailModel: InputWavelengthRange is empty, using default range 400-2500 nm.");
                for (int wl = 400; wl <= 2500; wl++)
                {
                    wavelengths.Add(wl);
                }
                return wavelengths;
            }

            // Split by commas to handle multiple parts (e.g., "500-600, 700-800")
            string[] parts = InputWavelengthRange.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                WriteMessage(LogLevel.Warning, "ProsailModel: InputWavelengthRange is empty after splitting.");
                return wavelengths; // Empty list
            }

            foreach (string part in parts)
            {
                // Check if the part is a range (contains a "-")
                if (part.Contains('-'))
                {
                    string[] rangeParts = part.Split('-', StringSplitOptions.TrimEntries);
                    if (rangeParts.Length != 2)
                    {
                        WriteMessage(LogLevel.Warning, $"ProsailModel: Invalid wavelength range format: {part}.");
                        continue;
                    }

                    if (!double.TryParse(rangeParts[0], out double startWavelength) || !double.TryParse(rangeParts[1], out double endWavelength))
                    {
                        WriteMessage(LogLevel.Warning, $"ProsailModel: Failed to parse wavelength range values: {part}.");
                        continue;
                    }

                    if (startWavelength < 0 || endWavelength < startWavelength)
                    {
                        WriteMessage(LogLevel.Warning, $"ProsailModel: Invalid wavelength range values (start < 0 or end < start): {part}.");
                        continue;
                    }

                    // Add all integer wavelengths in the range (inclusive)
                    for (int wl = (int)Math.Ceiling(startWavelength); wl <= (int)Math.Floor(endWavelength); wl++)
                    {
                        wavelengths.Add(wl);
                    }
                    WriteMessage(LogLevel.Info, $"ProsailModel: Parsed wavelength range: {startWavelength}-{endWavelength} nm.");
                }
                else
                {
                    // Parse as a single wavelength
                    if (!double.TryParse(part, out double wavelength))
                    {
                        WriteMessage(LogLevel.Warning, $"ProsailModel: Failed to parse wavelength value: {part}.");
                        continue;
                    }

                    if (wavelength < 0)
                    {
                        WriteMessage(LogLevel.Warning, $"ProsailModel: Invalid wavelength value (wavelength < 0): {part}.");
                        continue;
                    }

                    wavelengths.Add(wavelength);
                    WriteMessage(LogLevel.Info, $"ProsailModel: Parsed single wavelength: {wavelength} nm.");
                }
            }

            // Remove duplicates and sort
            wavelengths = wavelengths.Distinct().OrderBy(w => w).ToList();

            if (wavelengths.Count == 0)
            {
                WriteMessage(LogLevel.Warning, $"ProsailModel: No valid wavelengths parsed from: {InputWavelengthRange}.");
            }
            else
            {
                WriteMessage(LogLevel.Info, $"ProsailModel: Total wavelengths parsed: {wavelengths.Count}.");
            }

            return wavelengths;
        }        

        /// <summary>
        /// Evaluates an expression and returns its value
        /// </summary>
        /// <param name="expression">The expression to evaluate</param>
        /// <returns>The evaluated expression value</returns>
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
        /// Evaluate all PROSAIL parameters and store them in CurrentParameterValues
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
            //CurrentParameterValues["Wavelengths"] = inputWavelengths;

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
            //CurrentParameterValues["SoilReflectance"] = SoilReflectance;
            CurrentParameterValues["WetDrySoilReflectanceJsonPath"] = WetDrySoilReflectanceJsonPath;
            //CurrentParameterValues["Psoil"] = EvaluateExpression(Psoil); //Removed Psoil evaluation from here. It is handled in OnDoEndOfDay.

            // Soil/geometry parameters
            CurrentParameterValues["SunZenithAngle"] = EvaluateExpression(SunZenithAngle);
            CurrentParameterValues["ObserverZenithAngle"] = EvaluateExpression(ObserverZenithAngle);
            CurrentParameterValues["RelativeAzimuthAngle"] = EvaluateExpression(RelativeAzimuthAngle);
        }

        /// <summary>
        /// Validate the ranges of all PROSAIL parameters
        /// </summary>
        private void ValidateParameterRanges()
        {
            // Example ranges based on literature and your comments
            var ranges = new Dictionary<string, (double min, double max, string description)>
            {
                { "N", (1.0, 2.6, "Leaf structure parameter (unitless)") },
                { "CAB", (10.0, 80.0, "Chlorophyll a+b content (μg/cm²)") },
                { "CAR", (1.0, 24.0, "Carotenoid content (μg/cm²)") },
                { "EWT", (0.001, 0.08, "Equivalent Water Thickness (cm)") },
                { "LMA", (0.001, 0.02, "Leaf Mass per Area (g/cm²)") },
                { "BROWN", (0.0, 1.0, "Brown pigment content (unitless)") },
                { "ANT", (0.0, 10.0, "Anthocyanin content (μg/cm²)") },
                { "PROT", (0.0, 10.0, "Protein content (g/cm²)") },
                { "CBC", (0.0, 10.0, "NonProt Carbon-based constituent content (g/cm²)") },
                { "Alpha", (0.0, 90.0, "Incidence angle (degrees)") },
                { "LAI", (0.0, 10.0, "Leaf Area Index (m²/m²)") },
                { "HotSpot", (0.0, 1.0, "Hot Spot parameter (unitless)") },
                { "TypeLidf", (1.0, 2.0, "LIDF type (1 or 2)") },
                { "LIDFa", (-90.0, 90.0, "LIDF parameter a") },
                { "LIDFb", (-1.0, 1.0, "LIDF parameter b") },
                { "FractionBrown", (0.0, 1.0, "Fraction of brown leaf area") },
                { "Dissociation", (0.0, 1.0, "Layer dissociation factor") },
                { "CrownCover", (0.0, 1.0, "Vertical crown cover") },
                { "TreeShape", (0.0, 10.0, "Tree shape factor") },
                { "Psoil", (0.0, 1.0, "Dry to Wet soil factor") },
                { "SunZenithAngle", (0.0, 90.0, "Sun zenith angle (degrees)") },
                { "ObserverZenithAngle", (0.0, 90.0, "Observer zenith angle (degrees)") },
                { "RelativeAzimuthAngle", (0.0, 360.0, "Relative azimuth angle (degrees)") }
            };

            foreach (var param in ranges)
            {
                if (CurrentParameterValues.TryGetValue(param.Key, out object value))
                {
                    // Convert object to double for comparison
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
                        string msg = $"Parameter '{param.Key}' value {numericValue} may be out of range [{param.Value.min}, {param.Value.max}] ({param.Value.description}) on {Clock?.Today:yyyy-MM-dd}. Check this paper: https://doi.org/10.3390/rs10010085\"";
                        WriteMessage(LogLevel.Warning, msg);
                        //throw new InvalidOperationException(msg);
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
        /// Calculate canopy optical properties using the PROSAIL model
        /// </summary>
        /// <returns>A tuple containing reflectance and transmittance vectors</returns>
        public CanopyOptics CalculateProsail()
        {
            if (cachedLeafOpticalConstants == null)
            {
                WriteMessage(LogLevel.Error, $"ProsailModel: CalculateProsail called without leaf optical constants on {Clock?.Today:yyyy-MM-dd}.");
                throw new InvalidOperationException("Leaf optical constants not loaded when CalculateProsail called.");
            }
            WriteMessage(LogLevel.Info, $"ProsailModel: CalculateProsail called on {Clock?.Today:yyyy-MM-dd}.");

            // Retrieve parameters
            int TypeLidfValue = Convert.ToInt32(CurrentParameterValues["TypeLidf"]);
            string SailVersionValue = SailVersionString;

            CanopyOptics results = ProsailCore.PRO4SAIL(
                // leaf
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
                // canopy and soil
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
                // sun-observer geometry
                TTS: Convert.ToDouble(CurrentParameterValues["SunZenithAngle"]),
                TTO: Convert.ToDouble(CurrentParameterValues["ObserverZenithAngle"]),
                PSI: Convert.ToDouble(CurrentParameterValues["RelativeAzimuthAngle"]),
                BrownLOP: null
            );

            WriteMessage(LogLevel.Info, $"ProspectModel: CalculateProspect completed, Wavelengths[{inputWavelengths.Length}]");

            // Validate that the results match the input wavelengths
            if (results.Rdot.Length != inputWavelengths.Length ||
                results.Rsot.Length != inputWavelengths.Length ||
                results.Rddt.Length != inputWavelengths.Length ||
                results.Rsdt.Length != inputWavelengths.Length ||
                results.Abs_dir.Length != inputWavelengths.Length ||
                results.Abs_hem.Length != inputWavelengths.Length ||
                results.Rsdstar.Length != inputWavelengths.Length ||
                results.Rddstar.Length != inputWavelengths.Length)
            {
                WriteMessage(LogLevel.Error, $"ProsailModel: Mismatch between PROSAIL output and input wavelengths.");
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
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Commencing")]
        private void OnCommencing(object sender, EventArgs e)
        {
            WriteMessage(LogLevel.Info, "ProsailModel: Simulation commencing.");
            // Load leaf optical constants from local file
            try
            {
                cachedLeafOpticalConstants = GetCachedLeafOpticalConstants();
                WriteMessage(LogLevel.Info, $"ProsailModel: Leaf optical constants loaded, Wavelengths count: {cachedLeafOpticalConstants.Value.Wavelength.Count}.");
            }
            catch (Exception ex)
            {
                WriteMessage(LogLevel.Error, $"ProsailModel: Failed to load leaf optical constants: {ex.Message}");
                throw; // Halt simulation if data is missing
            }

            // Load wet and dry soil reflectance data
            try
            {
                if (string.IsNullOrWhiteSpace(WetDrySoilReflectanceJsonPath))
                {
                    // Use default soil reflectance data if not specified
                    cachedWetDrySoilReflectance = LoadWetDrySoilReflectanData(DefaultSpecSoilDataPath);
                    WriteMessage(LogLevel.Info, "ProsailModel: Using default wet and dry soil reflectance data.");
                }
                else
                {
                    cachedWetDrySoilReflectance = LoadWetDrySoilReflectanData(WetDrySoilReflectanceJsonPath);
                    WriteMessage(LogLevel.Info, $"ProsailModel: Wet and dry soil reflectance data loaded from {WetDrySoilReflectanceJsonPath}.");
                }
            }
            catch (Exception ex)
            {
                WriteMessage(LogLevel.Error, $"ProsailModel: Failed to load wet and dry soil reflectance data: {ex.Message}");
                throw; // Halt simulation if data is missing
            }

            // Parse the wavelength range from InputWavelengthRange
            double[] wavelengths = ParseWavelengthRange().ToArray();
            inputWavelengths = wavelengths.Length > 0 ? wavelengths : cachedLeafOpticalConstants.Value.Wavelength.ToArray();

            // Load atmospheric spectral data
            try 
            {
                cachedAtmosphericSpectralData = LoadAtmosphericSpectralData(DefaultSpecAtmDataPath);
                WriteMessage(LogLevel.Info, $"ProsailModel: Using default wet and dry soil reflectance data from {DefaultSpecAtmDataPath}.");
            }
            catch (Exception ex)
            {
                WriteMessage(LogLevel.Error, $"ProsailModel: Failed to load atmospheric spectral data: {ex.Message}");
                throw; // Halt simulation if data is missing
            }

            // Deal with the costom wavelengths for leaf optical constants and soil reflectance
            cachedLeafOpticalConstants = cachedLeafOpticalConstants.Value.SubsetByWavelengths(inputWavelengths);
            cachedWetDrySoilReflectance = cachedWetDrySoilReflectance.Value.SubsetByWavelengths(inputWavelengths);
            cachedAtmosphericSpectralData = cachedAtmosphericSpectralData.SubsetByWavelengths(inputWavelengths);

            // Set default ProsailSQLiteDatabasePath based on simulation file name
            string simulationFileName = Path.GetFileNameWithoutExtension(Simulation.FileName);
            ProsailSQLiteDatabasePath = $"{simulationFileName}_Prosail.db";
            InitializeDatabase();
        }

        /// <summary>Called when [do management calculations].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
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

            WriteMessage(LogLevel.Info, $"ProsailModel: OnDoEndOfDay called on {Clock.Today:yyyy-MM-dd}.");

            // Calculte soil reflectance based on wet/dry factor
            Double psoilValue;
            if (string.IsNullOrWhiteSpace(Psoil))
            {
                // Use APSIM calculated soil water content of top layer if Psoil is not specified
                psoilValue = waterBalance.SW[0];
                WriteMessage(LogLevel.Info, "ProsailModel: Psoil is not specified, using APSIM calculated soil water content of soil top layer.");
            }
            else
            {
                psoilValue = EvaluateExpression(Psoil);
            }
            SoilReflectance = CalculateSoilReflectanceFromWetDry((WetDrySoilReflectance)cachedWetDrySoilReflectance, psoilValue);

            // Initialize 
            CurrentParameterValues.Clear();
            // Evaluate all parameters
            EvaluateAllParameters();
            // Add the psoil value that was actually used for the calculation to the dictionary for logging.
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
                cachedProsailOutputs = canopyOpticalVariables; // Cache results
                lastCalculationDate = Clock.Today;

                WriteMessage(LogLevel.Info, message: $"ProsailModel: PROSAIL calculation completed, Wavelength[{canopyOpticalVariables.Wavelength.Length}]");

                // Compute BRF (Bidirectional Reflectance Factor) for each wavelength
                CanopyBRF canopyBRF = ComputeBRF(wavelength: canopyOpticalVariables.Wavelength, 
                    rdot: canopyOpticalVariables.Rdot, 
                    rsot: canopyOpticalVariables.Rsot, 
                    tts: Convert.ToDouble(CurrentParameterValues["SunZenithAngle"]),
                    atmosphericSpectralData: cachedAtmosphericSpectralData);

                // Compute fraction of absorbed photosyntehtically active radiation (fAPAR)
                double fAPAR = ComputeFAPAR(abs_dir: canopyOpticalVariables.Abs_dir,
                    abs_hem: canopyOpticalVariables.Abs_hem, 
                    tts: Convert.ToDouble(CurrentParameterValues["SunZenithAngle"]), 
                    atmosphericSpectralData: cachedAtmosphericSpectralData);

                // Compute broadband albedo
                double albedo = ComputeAlbedo(rddstar: canopyOpticalVariables.Rddstar,
                    rsdstar: canopyOpticalVariables.Rsdstar,
                    tts: Convert.ToDouble(CurrentParameterValues["SunZenithAngle"]),
                    atmosphericSpectralData: cachedAtmosphericSpectralData);

                // Spectral resampling to sensor
                SpectralResamplingResult resampledReflectance = ResampleReflectanceToSensor(wavelength: canopyBRF.Wavelength.ToArray(), 
                    reflectance: canopyBRF.BRF.ToArray(),
                    srf: SensorSRF);

                var canopyStateVariables = new CanopyStateVariables
                {
                    fAPAR = fAPAR,
                    fcover = canopyOpticalVariables.FCover.Distinct().First(),
                    albedo = albedo
                };

                // Save to database only if enabled
                if (EnableSQLiteOutput)
                {
                    WriteToDatabase(date: Clock.Today, 
                        canopyOptics: canopyOpticalVariables, 
                        canopyStateVariables: canopyStateVariables, 
                        canopyBRF: canopyBRF, 
                        spectralResamplingResult: resampledReflectance);
                    WriteMessage(LogLevel.Info, $"ProsailModel: Wrote results to database for {Clock.Today:yyyy-MM-dd}.");
                }
                else
                {
                    WriteMessage(LogLevel.Info, $"ProsailModel: SQLite output disabled, results not saved to database.");
                }
            }
            catch (Exception ex)
            {
                WriteMessage(LogLevel.Error, $"ProsailModel: Error in OnDoEndOfDay: {ex.Message}");
            }
        }

        /// <summary>Called when [simulation completed].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Completed")]
        private void OnCompleted(object sender, EventArgs e)
        {
            if (dbConnection != null)
            {
                dbConnection.CloseDatabase();
                dbConnection = null;
                string dbPath = GetFullDatabasePath();
                WriteMessage(LogLevel.Info, $"PROSAIL results database saved to: {dbPath}");
            }
        }
    }
}
#endregion
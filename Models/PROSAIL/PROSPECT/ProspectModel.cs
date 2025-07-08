using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Data;
using MathNet.Numerics.LinearAlgebra;
using Models.Core;
using Models.Functions;
using APSIM.Shared.Utilities;
using Models.PMF;
using System.Threading;
using static Models.PROSAIL.PROSPECT.ProspectCore;

namespace Models.PROSAIL.PROSPECT
{
    /// <summary>
    /// Model implementing the PROSPECT radiative transfer model for leaf optical properties in APSIM
    /// with configurable parameter expressions and spectral data output to SQLite
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Plant))]
    public class ProspectModel : Model
    {
        /// <summary> Link to the clock for daily outputs </summary>
        [Link]
        private Clock Clock = null;

        /// <summary> Link to summary file for outputs </summary>
        [Link]
        private ISummary Summary = null;

        /// <summary> Link to simulation for file paths and name </summary>
        [Link]
        private Simulation Simulation = null;

        /// <summary> Link to the parent Plant model to check IsAlive</summary>
        [Link(IsOptional = true)]
        private Plant ParentPlant = null;

        /// <summary>Integration</summary>
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

        /// <summary>Control</summary>
        [Separator("Control the outputs")]

        // <summary> Flag to enable daily SQLite database output</summary>
        [Description("Enable output to SQLite database")]
        public bool EnableSQLiteOutput { get; set; } = true;
        

        /// <summary> Spectral range to simulate (start-end in nm) </summary>
        [Description("Spectral range to simulate (in nm; supports ranges (e.g., '400-500'), lists (e.g., '400, 500, 600'), and mixed formats (e.g., '400, 500-600, 700')")]
        public string OutputWavelengthRange { get; set; } = "400-2500";

        /// <summary> Defines the logging verbosity levels</summary>
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

        /// <summary> Logging verbosity level</summary>
        [Description("Logging verbosity level (Error, Warning, Info, Debug)")]
        public LogLevel LoggingLevel { get; set; } = LogLevel.Info;

        /// <summary> Path to the SQLite database file (relative to simulation directory) </summary>
        private string ProspectSQLiteDatabasePath;

        /// <summary>The cached spectral constants loaded at simulation start</summary>
        private LeafOpticalConsts? cachedOpticalConstants = null;

        /// <summary>Cached PROSPECT results for the current day</summary>
        private LeafOptics? cachedProspectResults = null;

        /// <summary>The date of the last cached results</summary>
        private DateTime? lastCalculationDate = null;

        /// <summary>Database connection</summary>
        private SQLite dbConnection = null;

        /// <summary>Current simulation name for database records</summary>
        private string simulationName = null;

        /// <summary>Current parameter values after expression evaluation</summary>
        private Dictionary<string, double> CurrentParameterValues { get; set; } = new Dictionary<string, double>();

        /// <summary>Helper method to write messages based on logging level</summary>
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

        /// <summary>Called when [simulation commencing].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Commencing")]
        private void OnCommencing(object sender, EventArgs e)
        {
            WriteMessage(LogLevel.Info, "ProspectModel: Simulation commencing.");
            try
            {
                cachedOpticalConstants = GetCachedLeafOpticalConstants();
                WriteMessage(LogLevel.Info, $"ProspectModel: Leaf optical constants loaded, Wavelengths count: {cachedOpticalConstants.Value.Wavelength.Count}.");
            }
            catch (Exception ex)
            {
                WriteMessage(LogLevel.Error, $"ProspectModel: Failed to load leaf optical  constants: {ex.Message}");
                throw; // Halt simulation if data is missing
            }

            if (EnableSQLiteOutput)
            {
                // Set default ProspectSQLiteDatabasePath based on simulation file name
                string simulationFileName = Path.GetFileNameWithoutExtension(Simulation.FileName);
                ProspectSQLiteDatabasePath = $"{simulationFileName}_Prospect.db";
                
                InitializeDatabase();
            }
        }

        /// <summary>Called when [do management calculations].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("EndOfDay")]
        private void OnDoEndOfDay(object sender, EventArgs e)
        {
            if (ParentPlant?.IsAlive != true)
            {
                WriteMessage(LogLevel.Info, $"ProspectModel: Skipping calculations on {Clock.Today:yyyy-MM-dd} as Plant is not alive.");
                return;
            }

            if (ParentPlant?.IsEmerged != true)
            {
                WriteMessage(LogLevel.Info, $"ProspectModel: Skipping calculations on {Clock.Today:yyyy-MM-dd} as Plant has not emerged.");
                return;
            }

            WriteMessage(LogLevel.Info, $"ProspectModel: OnDoEndOfDay called on {Clock.Today:yyyy-MM-dd}.");
            // Initialize 
            CurrentParameterValues.Clear();
            // Clear cached results to force recalculation
            cachedProspectResults = null;
            lastCalculationDate = null;
            try
            {
                // Calculate PROSPECT outputs
                var results = CalculateProspect();
                cachedProspectResults = results; // Cache results
                lastCalculationDate = Clock.Today;

                WriteMessage(LogLevel.Info, message: $"ProspectModel: PROSPECT calculation completed, Reflectance[{results.Reflectance.Length}], Transmittance[{results.Transmittance.Length}]");

                // Save to database only if enabled
                if (EnableSQLiteOutput)
                {
                    WriteToDatabase(Clock.Today, results);
                    WriteMessage(LogLevel.Info, $"ProspectModel: Wrote results to database for {Clock.Today:yyyy-MM-dd}.");
                }
                else
                {
                    WriteMessage(LogLevel.Info, $"ProspectModel: SQLite output disabled, results not saved to database.");
                }
            }
            catch (Exception ex)
            {
                WriteMessage(LogLevel.Error, $"ProspectModel: Error in OnDoEndOfDay: {ex.Message}");
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
                WriteMessage(LogLevel.Info, $"PROSPECT results database saved to: {dbPath}");
            }
        }

        /// <summary>Initialize the SQLite database for PROSPECT results</summary>
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
                dbConnection.ExecuteNonQuery("DROP TABLE IF EXISTS Spectra;");
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
                        PRIMARY KEY (SimulationName, Date),
                        FOREIGN KEY (SimulationName) REFERENCES Simulations(SimulationName)
                    )");

                dbConnection.ExecuteNonQuery(@"
                    CREATE TABLE IF NOT EXISTS Spectra (
                        SimulationName TEXT,
                        Date TEXT,
                        WavelengthNM REAL,
                        Reflectance REAL,
                        Transmittance REAL,
                        PRIMARY KEY (SimulationName, Date, WavelengthNM),
                        FOREIGN KEY (SimulationName, Date) REFERENCES Parameters(SimulationName, Date)
                    )");

                string sql = $@"
                    INSERT INTO Simulations (SimulationName, StartDate, EndDate, CreatedAt)
                    VALUES ('{simulationName}', '{Clock.StartDate:yyyy-MM-dd}', '{Clock.EndDate:yyyy-MM-dd}', '{DateTime.Now:yyyy-MM-dd HH:mm:ss}')";
                dbConnection.ExecuteNonQuery(sql);

                WriteMessage(LogLevel.Info, $"PROSPECT database initialized: {dbPath}");
            }
            catch (Exception ex)
            {
                if (dbConnection != null)
                {
                    dbConnection.CloseDatabase();
                    dbConnection = null;
                }
                WriteMessage(LogLevel.Error, $"Failed to initialize PROSPECT database: {ex.Message}");
                EnableSQLiteOutput = false;
            }
        }

        /// <summary>Get the full path to the database file</summary>
        private string GetFullDatabasePath()
        {
            string simDir = Path.GetDirectoryName(Simulation.FileName);
            if (Path.IsPathRooted(ProspectSQLiteDatabasePath))
                return ProspectSQLiteDatabasePath;
            else
                return Path.Combine(simDir, ProspectSQLiteDatabasePath);
        }

        /// <summary>
        /// Parses the wavelength range from the specified string and returns the list of wavelengths.
        /// </summary>
        /// <returns>A sorted list of wavelengths (in nm) parsed from the input string. Returns an empty list if parsing fails.</returns>
        private List<double> ParseWavelengthRange()
        {
            List<double> wavelengths = new List<double>();

            // Default range if input is empty
            if (string.IsNullOrWhiteSpace(OutputWavelengthRange))
            {
                WriteMessage(LogLevel.Info, "ProspectModel: OutputWavelengthRange is empty, using default range 400-2500 nm.");
                for (int wl = 400; wl <= 2500; wl++)
                {
                    wavelengths.Add(wl);
                }
                return wavelengths;
            }

            // Split by commas to handle multiple parts (e.g., "500-600, 700-800")
            string[] parts = OutputWavelengthRange.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                WriteMessage(LogLevel.Warning, "ProspectModel: OutputWavelengthRange is empty after splitting.");
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
                        WriteMessage(LogLevel.Warning, $"ProspectModel: Invalid wavelength range format: {part}.");
                        continue;
                    }

                    if (!double.TryParse(rangeParts[0], out double startWavelength) || !double.TryParse(rangeParts[1], out double endWavelength))
                    {
                        WriteMessage(LogLevel.Warning, $"ProspectModel: Failed to parse wavelength range values: {part}.");
                        continue;
                    }

                    if (startWavelength < 0 || endWavelength < startWavelength)
                    {
                        WriteMessage(LogLevel.Warning, $"ProspectModel: Invalid wavelength range values (start < 0 or end < start): {part}.");
                        continue;
                    }

                    // Add all integer wavelengths in the range (inclusive)
                    for (int wl = (int)Math.Ceiling(startWavelength); wl <= (int)Math.Floor(endWavelength); wl++)
                    {
                        wavelengths.Add(wl);
                    }
                    WriteMessage(LogLevel.Info, $"ProspectModel: Parsed wavelength range: {startWavelength}-{endWavelength} nm.");
                }
                else
                {
                    // Parse as a single wavelength
                    if (!double.TryParse(part, out double wavelength))
                    {
                        WriteMessage(LogLevel.Warning, $"ProspectModel: Failed to parse wavelength value: {part}.");
                        continue;
                    }

                    if (wavelength < 0)
                    {
                        WriteMessage(LogLevel.Warning, $"ProspectModel: Invalid wavelength value (wavelength < 0): {part}.");
                        continue;
                    }

                    wavelengths.Add(wavelength);
                    WriteMessage(LogLevel.Info, $"ProspectModel: Parsed single wavelength: {wavelength} nm.");
                }
            }

            // Remove duplicates and sort
            wavelengths = wavelengths.Distinct().OrderBy(w => w).ToList();

            if (wavelengths.Count == 0)
            {
                WriteMessage(LogLevel.Warning, $"ProspectModel: No valid wavelengths parsed from: {OutputWavelengthRange}.");
            }
            else
            {
                WriteMessage(LogLevel.Info, $"ProspectModel: Total wavelengths parsed: {wavelengths.Count}.");
            }

            return wavelengths;
        }

        /// <summary>
        /// Write PROSPECT results to the database
        /// </summary>
        private void WriteToDatabase(DateTime date, LeafOptics leafOptics)
        {
            double[] reflectance = leafOptics.Reflectance;
            double[] transmittance = leafOptics.Transmittance;
            double[] usedWavelength = leafOptics.Wavelength;

            if (dbConnection == null || reflectance == null || transmittance == null || usedWavelength == null)
            {
                WriteMessage(LogLevel.Warning, "ProspectModel: WriteToDatabase skipped due to null dbConnection, reflectance, transmittance, or wavelength.");
                return;
            }

            // Validate array lengths
            if (reflectance.Length != usedWavelength.Length ||
                transmittance.Length != usedWavelength.Length)
            {
                WriteMessage(LogLevel.Error, $"ProspectModel: Array length mismatch in WriteToDatabase: reflectance[{reflectance.Length}], transmittance[{transmittance.Length}], wavelengths[{usedWavelength.Length}].");
                throw new InvalidOperationException("Array length mismatch in WriteToDatabase.");
            }

            try
            {
                dbConnection.ExecuteNonQuery("BEGIN TRANSACTION;");

                // Parse wavelength range
                List<double> outputWavelengths = ParseWavelengthRange();
                if (outputWavelengths.Count == 0)
                {
                    WriteMessage(LogLevel.Warning, $"ProspectModel: No valid wavelengths to save, skipping database write.");
                    return;
                }

                // Create a dictionary for fast lookup of output wavelengths
                var outputWavelengthSet = new HashSet<double>(outputWavelengths);

                string dateStr = date.ToString("yyyy-MM-dd");

                // Parameters INSERT
                string paramSql = $"INSERT OR REPLACE INTO Parameters (SimulationName, Date, N, CAB, CAR, EWT, LMA, ANT, BROWN, PROT, CBC, Alpha) VALUES " +
                                 $"('{simulationName}', '{dateStr}', {CurrentParameterValues["N"]}, {CurrentParameterValues["CAB"]}, {CurrentParameterValues["CAR"]}, " +
                                 $"{CurrentParameterValues["EWT"]}, {CurrentParameterValues["LMA"]}, {CurrentParameterValues["ANT"]}, {CurrentParameterValues["BROWN"]}, " +
                                 $"{CurrentParameterValues["PROT"]}, {CurrentParameterValues["CBC"]}, {CurrentParameterValues["Alpha"]})";
                dbConnection.ExecuteNonQuery(paramSql);

                // Spectra INSERT
                StringBuilder spectraSql = new StringBuilder("INSERT OR REPLACE INTO Spectra (SimulationName, Date, WavelengthNM, Reflectance, Transmittance) VALUES ");
                bool firstSpectra = true;

                // Process all wavelengths
                for (int i = 0; i < usedWavelength.Length; i++)
                {
                    double wavelength = usedWavelength[i];
                    if (outputWavelengthSet.Contains(wavelength))
                    {
                        if (!firstSpectra)
                            spectraSql.Append(",");
                        spectraSql.Append($"('{simulationName}', '{dateStr}', {wavelength}, {reflectance[i]}, {transmittance[i]})");
                        firstSpectra = false;
                    }
                }

                if (!firstSpectra)
                {
                    spectraSql.Append(";");
                    WriteMessage(LogLevel.Debug, $"ProspectModel: Executing Spectra INSERT with {spectraSql.Length} characters.");
                    dbConnection.ExecuteNonQuery(spectraSql.ToString());
                }

                dbConnection.ExecuteNonQuery("COMMIT;");

                WriteMessage(LogLevel.Info, $"ProspectModel: Wrote results for {date:yyyy-MM-dd} to database.");
            }
            catch (Exception ex)
            {
                dbConnection.ExecuteNonQuery("ROLLBACK;");
                WriteMessage(LogLevel.Error, $"ProspectModel: Failed to write to database: {ex.Message}");
                throw;
            }
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
                    WriteMessage(LogLevel.Debug, $"ProspectModel: Expression '{expression}' parsed to {result} on {Clock?.Today:yyyy-MM-dd}.");
                    return result;
                }

                object value = ExpressionFunction.Evaluate(expression, this);
                if (value == null)
                {
                    WriteMessage(LogLevel.Error, $"ProspectModel: Expression '{expression}' evaluated to null on {Clock?.Today:yyyy-MM-dd}.");
                    throw new InvalidOperationException($"ProspectModel: Expression '{expression}' evaluated to null on {Clock?.Today:yyyy-MM-dd}.");
                }
                if (value is double)
                {
                    double resultValue = (double)value;
                    WriteMessage(LogLevel.Debug, $"ProspectModel: Expression '{expression}' evaluated to {resultValue} on {Clock?.Today:yyyy-MM-dd}.");
                    return resultValue;
                }
                else if (value is double[] && ((double[])value).Length > 0)
                {
                    double resultValue = ((double[])value)[0];
                    WriteMessage(LogLevel.Warning, $"ProspectModel: Expression '{expression}' evaluated to array, using first value {resultValue} on {Clock?.Today:yyyy-MM-dd}.");
                    return resultValue;
                }
                else
                {
                    double resultValue = Convert.ToDouble(value);
                    WriteMessage(LogLevel.Debug, $"ProspectModel: Expression '{expression}' converted to {resultValue} on {Clock?.Today:yyyy-MM-dd}.");
                    return resultValue;
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("Divide by Zero", StringComparison.OrdinalIgnoreCase))
                {
                    WriteMessage(LogLevel.Warning, $"ProspectModel: Divide by Zero in expression '{expression}' on {Clock?.Today:yyyy-MM-dd}. Setting the expression to 0.");
                    return 0.0;
                }

                //WriteMessage(LogLevel.Error, $"ProspectModel: Failed to evaluate expression '{expression}' on {Clock?.Today:yyyy-MM-dd}: {ex.Message}.");
                throw new InvalidOperationException($"ProspectModel: Failed to evaluate expression '{expression}' on {Clock?.Today:yyyy-MM-dd}: {ex.Message}.");
            }
        }

        /// <summary>
        /// Calculate leaf optical properties using the PROSPECT model
        /// </summary>
        /// <returns>A tuple containing reflectance and transmittance vectors</returns>
        public LeafOptics CalculateProspect()
        {
            if (cachedOpticalConstants == null)
            {
                WriteMessage(LogLevel.Error, $"ProspectModel: CalculateProspect called without leaf optical constants on {Clock?.Today:yyyy-MM-dd}.");
                throw new InvalidOperationException("Leaf optical constants not loaded when CalculateProspect called.");
            }
            WriteMessage(LogLevel.Info, $"ProspectModel: CalculateProspect called on {Clock?.Today:yyyy-MM-dd}.");

            // Evaluate expressions and validate parameter values
            double nValue = EvaluateExpression(N);
            if (double.IsNaN(nValue) || nValue <= 0)
            {
                throw new InvalidOperationException($"Invalid N value ({nValue}) from expression '{N}' on {Clock?.Today:yyyy-MM-dd}. Must be positive and not NaN.");
            }
            CurrentParameterValues["N"] = nValue;
            // Check the range of N (1.0-2.6 unitless)
            if (nValue < 1 || nValue > 2.6)
            {
                WriteMessage(LogLevel.Warning, $"ProspectModel: N value ({nValue}) out of range [1.0, 2.6] on {Clock?.Today:yyyy-MM-dd}. Check this paper: https://doi.org/10.3390/rs10010085");
            }

            double cabValue = EvaluateExpression(CAB);
            if (double.IsNaN(cabValue) || cabValue < 0)
            {
                throw new InvalidOperationException($"Invalid CAB value ({cabValue}) from expression '{CAB}' on {Clock?.Today:yyyy-MM-dd}. Must be non-negative and not NaN.");
            }
            CurrentParameterValues["CAB"] = cabValue;
            // Check CAB range (10-80 μg/cm²)
            if (cabValue < 10 || cabValue > 80)
            {
                WriteMessage(LogLevel.Warning, $"ProspectModel: CAB value ({cabValue}) out of range [10, 80] on {Clock?.Today:yyyy-MM-dd}. Check this paper: https://doi.org/10.3390/rs10010085");
            }

            double carValue = EvaluateExpression(CAR);
            if (double.IsNaN(carValue) || carValue < 0)
            {
                throw new InvalidOperationException($"Invalid CAR value ({carValue}) from expression '{CAR}' on {Clock?.Today:yyyy-MM-dd}. Must be non-negative and not NaN.");
            }
            CurrentParameterValues["CAR"] = carValue;
            // Check CAR range (1-24 μg/cm²)
            if (carValue < 1 || carValue > 24)
            {
                WriteMessage(LogLevel.Warning, $"ProspectModel: CAR value ({carValue}) out of range [1, 24] on {Clock?.Today:yyyy-MM-dd}. Check this paper: https://doi.org/10.3390/rs10010085");
            }

            double ewtValue = EvaluateExpression(EWT);
            if (double.IsNaN(ewtValue) || ewtValue < 0)
            {
                throw new InvalidOperationException($"Invalid EWT value ({ewtValue}) from expression '{EWT}' on {Clock?.Today:yyyy-MM-dd}. Must be non-negative and not NaN.");
            }
            CurrentParameterValues["EWT"] = ewtValue;
            // Check EWT range (0.001-0.08 cm)
            if (ewtValue < 0.001 || ewtValue > 0.08)
            {
                WriteMessage(LogLevel.Warning, $"ProspectModel: EWT value ({ewtValue}) out of range [0.001, 0.08] on {Clock?.Today:yyyy-MM-dd}. Check this paper: https://doi.org/10.3390/rs10010085");
            }

            double lmaValue = EvaluateExpression(LMA);
            if (double.IsNaN(lmaValue) || lmaValue < 0)
            {
                throw new InvalidOperationException($"Invalid LMA value ({lmaValue}) from expression '{LMA}' on {Clock?.Today:yyyy-MM-dd}. Must be non-negative and not NaN.");
            }
            CurrentParameterValues["LMA"] = lmaValue;
            // Check LMA range (0.001-0.02 g/cm²)
            if (lmaValue < 0.001 || lmaValue > 0.02)
            {
                WriteMessage(LogLevel.Warning, $"ProspectModel: LMA value ({lmaValue}) out of range [0.001, 0.02] on {Clock?.Today:yyyy-MM-dd}. Check this paper: https://doi.org/10.3390/rs10010085");
            }

            double antValue = EvaluateExpression(ANT);
            if (double.IsNaN(antValue) || antValue < 0)
            {
                throw new InvalidOperationException($"Invalid ANT value ({antValue}) from expression '{ANT}' on {Clock?.Today:yyyy-MM-dd}. Must be non-negative and not NaN.");
            }
            CurrentParameterValues["ANT"] = antValue;

            double brownValue = EvaluateExpression(BROWN);
            if (double.IsNaN(brownValue) || brownValue < 0)
            {
                throw new InvalidOperationException($"Invalid BROWN value ({brownValue}) from expression '{BROWN}' on {Clock?.Today:yyyy-MM-dd}. Must be non-negative and not NaN.");
            }
            CurrentParameterValues["BROWN"] = brownValue;
            // Check BROWN range (0-1 unitless)
            if (brownValue < 0 || brownValue > 1)
            {
                WriteMessage(LogLevel.Warning, $"ProspectModel: BROWN value ({brownValue}) out of range [0, 1] on {Clock?.Today:yyyy-MM-dd}.");
            }

            double protValue = EvaluateExpression(PROT);
            if (double.IsNaN(protValue) || protValue < 0)
            {
                throw new InvalidOperationException($"Invalid PROT value ({protValue}) from expression '{PROT}' on {Clock?.Today:yyyy-MM-dd}. Must be non-negative and not NaN.");
            }
            CurrentParameterValues["PROT"] = protValue;

            double cbcValue = EvaluateExpression(CBC);
            if (double.IsNaN(cbcValue) || cbcValue < 0)
            {
                throw new InvalidOperationException($"Invalid CBC value ({cbcValue}) from expression '{CBC}' on {Clock?.Today:yyyy-MM-dd}. Must be non-negative and not NaN.");
            }
            CurrentParameterValues["CBC"] = cbcValue;

            double alphaValue = EvaluateExpression(Alpha);
            if (double.IsNaN(alphaValue))
            {
                throw new InvalidOperationException($"Invalid Alpha value ({alphaValue}) from expression '{Alpha}' on {Clock?.Today:yyyy-MM-dd}. Must not be NaN.");
            }
            CurrentParameterValues["Alpha"] = alphaValue;

            // Parse the wavelength range from OutputWavelengthRange
            double[] wavelengths = ParseWavelengthRange().ToArray();

            // Determine the wavelengths to use for PROSPECT calculation
            double[] inputWavelengths = wavelengths.Length > 0 ? wavelengths : cachedOpticalConstants.Value.Wavelength.ToArray();

            // Run the PROSPECT model with the selected wavelengths
            LeafOptics results = ProspectCore.Prospect(
                LeafOpticalConstants: cachedOpticalConstants,
                N: nValue, CAB: cabValue, CAR: carValue, EWT: ewtValue, LMA: lmaValue,
                ANT: antValue, BROWN: brownValue, PROT: protValue, CBC: cbcValue, Alpha: alphaValue,
                Wavelengths: inputWavelengths);

            WriteMessage(LogLevel.Info, $"ProspectModel: CalculateProspect completed, Reflectance[{results.Reflectance.Length}], Transmittance[{results.Transmittance.Length}], Wavelengths[{inputWavelengths.Length}]");

            // Validate that the results match the input wavelengths
            if (results.Reflectance.Length != inputWavelengths.Length || results.Transmittance.Length != inputWavelengths.Length)
            {
                WriteMessage(LogLevel.Error, $"ProspectModel: Mismatch between PROSPECT output and input wavelengths: Reflectance[{results.Reflectance.Length}], Transmittance[{results.Transmittance.Length}], InputWavelengths[{inputWavelengths.Length}].");
                throw new InvalidOperationException("Mismatch between PROSPECT output and input wavelengths.");
            }
            return results;
        }
    }
}
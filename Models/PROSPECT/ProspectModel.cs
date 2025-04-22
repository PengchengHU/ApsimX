using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Data;
using MathNet.Numerics.LinearAlgebra;
using Models.Core;
using Models.Functions;
using APSIM.Shared.Utilities;
using Models.PMF;

namespace Models.Prospect
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
        /// <summary>
        /// Link to the clock for daily outputs
        /// </summary>
        [Link]
        private Clock Clock = null;

        /// <summary>
        /// Link to summary file for outputs
        /// </summary>
        [Link]
        private ISummary Summary = null;

        /// <summary>
        /// Link to simulation for file paths
        /// </summary>
        [Link]
        private Simulation Simulation = null;

        /// <summary>
        /// Link to the parent Plant model to check IsAlive
        /// </summary>
        [Link(IsOptional = true)]
        private Plant ParentPlant = null;

        /// <summary>
        /// Current simulation name for database records
        /// </summary>
        private string simulationName = null;

        /// <summary>The expression for N (Leaf structure parameter)</summary>
        [Description("N - Leaf structure parameter")]
        public string N { get; set; } = "1.5";

        /// <summary>The expression for CHL (Chlorophyll content)</summary>
        [Description("CHL - Chlorophyll content (μg/cm²)")]
        public string CHL { get; set; } = "40.0";

        /// <summary>The expression for CAR (Carotenoid content)</summary>
        [Description("CAR - Carotenoid content (μg/cm²)")]
        public string CAR { get; set; } = "8.0";

        /// <summary>The expression for EWT (Equivalent Water Thickness)</summary>
        [Description("EWT - Equivalent Water Thickness (g/cm²)")]
        public string EWT { get; set; } = "0.01";

        /// <summary>The expression for LMA (Leaf Mass per Area)</summary>
        [Description("LMA - Leaf Mass per Area (g/cm²)")]
        public string LMA { get; set; } = "0.008";

        /// <summary>The expression for ANT (Anthocyanin content)</summary>
        [Description("ANT - Anthocyanin content (μg/cm²)")]
        public string ANT { get; set; } = "0.0";

        /// <summary>The expression for BROWN (Brown pigment content)</summary>
        [Description("BROWN - Brown pigment content (Arbitrary units)")]
        public string BROWN { get; set; } = "0.0";

        /// <summary>The expression for PROT (Protein content)</summary>
        [Description("PROT - Protein content (g/cm²)")]
        public string PROT { get; set; } = "0.0";

        /// <summary>The expression for CBC (NonProt Carbon-based constituent content)</summary>
        [Description("CBC - NonProt Carbon-based constituent content (g/cm²)")]
        public string CBC { get; set; } = "0.0";

        /// <summary>The expression for alpha (Incidence angle in degrees)</summary>
        [Description("Alpha - Incidence angle in degrees")]
        public string Alpha { get; set; } = "40.0";

        /// <summary>
        /// Flag to enable daily SQLite database output
        /// </summary>
        [Description("Enable daily output to SQLite database")]
        public bool EnableSQLiteOutput { get; set; } = true;

        /// <summary>
        /// Path to the SQLite database file (relative to simulation directory)
        /// </summary>
        [Description("Path to SQLite database file (relative to simulation directory)")]
        public string SQLiteDatabasePath { get; set; } = "ProspectResults.db";

        /// <summary>
        /// Spectral range to save (start-end in nm)
        /// </summary>
        [Description("Spectral range to save (start-end in nm, e.g. '400-2500')")]
        public string OutputWavelengthRange { get; set; } = "400-2500";

        /// <summary>
        /// The cached spectral constants loaded at simulation start
        /// </summary>
        private ProspectCore.SpectralConstants? cachedSpectralConstants = null;

        /// <summary>
        /// Cached PROSPECT results for the current day
        /// </summary>
        private (Vector<double> Reflectance, Vector<double> Transmittance)? cachedResults = null;

        /// <summary>
        /// The date of the last cached results
        /// </summary>
        private DateTime? lastCalculationDate = null;

        /// <summary>
        /// Database connection
        /// </summary>
        private SQLite dbConnection = null;               

        /// <summary>
        /// Cached wavelengths (nm) from the spectral constants
        /// </summary>
        [Description("Wavelengths (nm)")]
        public double[] Wavelengths
        {
            get
            {
                if (cachedSpectralConstants == null)
                {
                    Summary.WriteMessage(this, "ProspectModel: Wavelengths accessed before spectral constants loaded.", MessageType.Warning);
                    return Array.Empty<double>();
                }
                return cachedSpectralConstants.Value.Wavelengths.ToArray();
            }
        }

        /// <summary>
        /// Current parameter values after expression evaluation
        /// </summary>
        private Dictionary<string, double> CurrentParameterValues { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Gets the leaf reflectance spectrum calculated by PROSPECT
        /// </summary>
        [Units("unitless")]
        [Description("Leaf reflectance (0-1)")]
        public double[] LeafReflectance
        {
            get
            {
                if (Clock == null || cachedResults == null || lastCalculationDate != Clock.Today)
                {
                    Summary.WriteMessage(this, $"ProspectModel: LeafReflectance accessed without valid cached results on {Clock?.Today:yyyy-MM-dd}. Recalculating.", MessageType.Warning);
                    var results = CalculateProspect();
                    return results.Reflectance.ToArray();
                }
                return cachedResults.Value.Reflectance.ToArray();
            }
        }

        /// <summary>
        /// Gets the leaf transmittance spectrum calculated by PROSPECT
        /// </summary>
        [Units("unitless")]
        [Description("Leaf transmittance (0-1)")]
        public double[] LeafTransmittance
        {
            get
            {
                if (Clock == null || cachedResults == null || lastCalculationDate != Clock.Today)
                {
                    Summary.WriteMessage(this, $"ProspectModel: LeafTransmittance accessed without valid cached results on {Clock?.Today:yyyy-MM-dd}. Recalculating.", MessageType.Warning);
                    var results = CalculateProspect();
                    return results.Transmittance.ToArray();
                }
                return cachedResults.Value.Transmittance.ToArray();
            }
        }

        /// <summary>
        /// Gets the leaf absorptance spectrum calculated by PROSPECT
        /// </summary>
        [Units("unitless")]
        [Description("Leaf absorptance (0-1)")]
        public double[] LeafAbsorptance
        {
            get
            {
                if (Clock == null || cachedResults == null || lastCalculationDate != Clock.Today)
                {
                    Summary.WriteMessage(this, $"ProspectModel: LeafAbsorptance accessed without valid cached results on {Clock?.Today:yyyy-MM-dd}. Recalculating.", MessageType.Warning);
                    var results = CalculateProspect();
                    Vector<double> reflectance = results.Reflectance;
                    Vector<double> transmittance = results.Transmittance;
                    Vector<double> absorptance = Vector<double>.Build.Dense(reflectance.Count, i => Math.Round(1.0 - reflectance[i] - transmittance[i], 4));
                    return absorptance.ToArray();
                }
                Vector<double> cachedReflectance = cachedResults.Value.Reflectance;
                Vector<double> cachedTransmittance = cachedResults.Value.Transmittance;
                Vector<double> cachedAbsorptance = Vector<double>.Build.Dense(cachedReflectance.Count, i => Math.Round(1.0 - cachedReflectance[i] - cachedTransmittance[i], 4));
                return cachedAbsorptance.ToArray();
            }
        }

        /// <summary>Called when [simulation commencing].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Commencing")]
        private void OnCommencing(object sender, EventArgs e)
        {
            Summary.WriteMessage(this, $"ProspectModel: Subscribed to events - Commencing, DoDailyInitialisation, DoDaily, Completed.", MessageType.Information);
            Summary.WriteMessage(this, $"ProspectModel: Clock is {(Clock == null ? "null" : "not null")}.", MessageType.Information);
            Summary.WriteMessage(this, $"ProspectModel: Simulation is {(Simulation == null ? "null" : "not null")}.", MessageType.Information);
            var parent = Parent as IModel;
            Summary.WriteMessage(this, $"ProspectModel: Parent model is {(parent == null ? "null" : parent.GetType().Name)}.", MessageType.Information);

            // Load spectral constants at simulation start
            Summary.WriteMessage(this, "ProspectModel: Loading spectral constants.", MessageType.Information);
            try
            {
                cachedSpectralConstants = ProspectCore.LoadLocalSpectralData();
                Summary.WriteMessage(this, $"ProspectModel: Spectral constants loaded, Wavelengths count: {cachedSpectralConstants.Value.Wavelengths.Count}.", MessageType.Information);
            }
            catch (Exception ex)
            {
                Summary.WriteMessage(this, $"ProspectModel: Failed to load spectral constants: {ex.Message}", MessageType.Error);
                throw; // Halt simulation if data is missing
            }

            if (EnableSQLiteOutput)
            {
                InitializeDatabase();
            }
        }

        /// <summary>Called when [do daily initialization].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoDailyInitialisation")]
        private void OnDoDailyInitialisation(object sender, EventArgs e)
        {
            Summary.WriteMessage(this, $"ProspectModel.OnDoDailyInitialisation called on {Clock?.Today:yyyy-MM-dd}.", MessageType.Information);
            CurrentParameterValues.Clear();
            // Clear cached results to force recalculation
            cachedResults = null;
            lastCalculationDate = null;
        }

        /// <summary>Called when [do daily calculations].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoManagementCalculations")]
        private void OnDoManagementCalculations(object sender, EventArgs e)
        {
            if (ParentPlant?.IsAlive != true)
            {
                Summary.WriteMessage(this, $"ProspectModel: Skipping calculations on {Clock.Today:yyyy-MM-dd} as Plant is not alive.", MessageType.Information);
                return;
            }

            Summary.WriteMessage(this, $"ProspectModel.OnDoDaily called on {Clock.Today:yyyy-MM-dd}.", MessageType.Information);

            if (!EnableSQLiteOutput)
            {
                Summary.WriteMessage(this, "ProspectModel: SQLite output is disabled (EnableSQLiteOutput is false).", MessageType.Warning);
                return;
            }

            if (Clock == null)
            {
                Summary.WriteMessage(this, "ProspectModel: Clock is null, cannot proceed with daily calculations.", MessageType.Error);
                return;
            }

            try
            {
                // Calculate PROSPECT outputs
                Summary.WriteMessage(this, $"ProspectModel: Starting PROSPECT calculation with parameters: N={N}, CHL={CHL}, CAR={CAR}, EWT={EWT}, LMA={LMA}", MessageType.Information);
                var results = CalculateProspect();
                cachedResults = results; // Cache results
                lastCalculationDate = Clock.Today;

                Summary.WriteMessage(this, $"ProspectModel: PROSPECT calculation completed, Reflectance[{results.Reflectance.Count}], Transmittance[{results.Transmittance.Count}]", MessageType.Information);

                // Get wavelength range
                if (ParseWavelengthRange(out double startWavelength, out double endWavelength))
                {
                    // Save results to database
                    dbConnection?.ExecuteNonQuery("BEGIN TRANSACTION;");
                    try
                    {
                        SaveResultsToDatabase(results.Reflectance.ToArray(), results.Transmittance.ToArray(), startWavelength, endWavelength);
                        dbConnection?.ExecuteNonQuery("COMMIT;");
                        Summary.WriteMessage(this, $"PROSPECT results for {Clock.Today:yyyy-MM-dd} saved to database.", MessageType.Information);
                        Summary.WriteMessage(this, $"  Wavelength range: {startWavelength}-{endWavelength} nm", MessageType.Information);
                        Summary.WriteMessage(this, $"  Parameters: N={CurrentParameterValues["N"]:F2}, CHL={CurrentParameterValues["CHL"]:F1}, CAR={CurrentParameterValues["CAR"]:F1}, EWT={CurrentParameterValues["EWT"]:F4}, LMA={CurrentParameterValues["LMA"]:F4}", MessageType.Information);
                    }
                    catch (Exception ex)
                    {
                        dbConnection?.ExecuteNonQuery("ROLLBACK;");
                        Summary.WriteMessage(this, $"ProspectModel: Failed to save results: {ex.Message}", MessageType.Error);
                        throw; // Rethrow to halt simulation
                    }
                }
                else
                {
                    Summary.WriteMessage(this, $"Invalid wavelength range specified: {OutputWavelengthRange}. Using full spectrum.", MessageType.Warning);
                    dbConnection?.ExecuteNonQuery("BEGIN TRANSACTION;");
                    try
                    {
                        SaveResultsToDatabase(results.Reflectance.ToArray(), results.Transmittance.ToArray(), 0, 10000);
                        dbConnection?.ExecuteNonQuery("COMMIT;");
                        Summary.WriteMessage(this, $"PROSPECT results for {Clock.Today:yyyy-MM-dd} saved to database (full spectrum).", MessageType.Information);
                    }
                    catch (Exception ex)
                    {
                        dbConnection?.ExecuteNonQuery("ROLLBACK;");
                        Summary.WriteMessage(this, $"ProspectModel: Failed to save results: {ex.Message}", MessageType.Error);
                        throw; // Rethrow to halt simulation
                    }
                }
            }
            catch (Exception ex)
            {
                Summary.WriteMessage(this, $"ProspectModel: Error in OnDoDaily: {ex.Message}", MessageType.Error);
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
                Summary.WriteMessage(this, $"PROSPECT results database saved to: {dbPath}", MessageType.Information);
            }
        }

        /// <summary>
        /// Initialize the SQLite database for PROSPECT results
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

                // Clear existing tables
                dbConnection.ExecuteNonQuery("DROP TABLE IF EXISTS Spectra;");
                dbConnection.ExecuteNonQuery("DROP TABLE IF EXISTS Parameters;");
                dbConnection.ExecuteNonQuery("DROP TABLE IF EXISTS Simulations;");

                //simulationID = Guid.NewGuid().ToString();
                simulationName = Simulation.Name.Replace("'", "''");

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
                        CHL REAL,
                        CAR REAL,
                        EWT REAL,
                        LMA REAL,
                        ANT REAL,
                        BROWN REAL,
                        PROT REAL,
                        CBC REAL,
                        Alpha REAL,
                        PRIMARY KEY (Date, SimulationName),
                        FOREIGN KEY (SimulationName) REFERENCES Simulations(SimulationName)
                    )");

                dbConnection.ExecuteNonQuery(@"
                    CREATE TABLE IF NOT EXISTS Spectra (
                        SimulationName TEXT,
                        Date TEXT,
                        WavelengthNM REAL,
                        Reflectance REAL,
                        Transmittance REAL,
                        Absorptance REAL,
                        PRIMARY KEY (WavelengthNM, Date, SimulationName),
                        FOREIGN KEY (Date, SimulationName) REFERENCES Parameters(Date, SimulationName)
                    )");

                string sql = $@"
                    INSERT INTO Simulations (SimulationName, StartDate, EndDate, CreatedAt)
                    VALUES ('{simulationName}', '{Clock.StartDate:yyyy-MM-dd}', '{Clock.EndDate:yyyy-MM-dd}', '{DateTime.Now:yyyy-MM-dd HH:mm:ss}')";
                dbConnection.ExecuteNonQuery(sql);

                Summary.WriteMessage(this, $"PROSPECT database initialized: {dbPath}", MessageType.Information);
            }
            catch (Exception ex)
            {
                if (dbConnection != null)
                {
                    dbConnection.CloseDatabase();
                    dbConnection = null;
                }
                Summary.WriteMessage(this, $"Failed to initialize PROSPECT database: {ex.Message}", MessageType.Error);
                EnableSQLiteOutput = false;
            }
        }

        /// <summary>
        /// Get the full path to the database file
        /// </summary>
        private string GetFullDatabasePath()
        {
            string simDir = Path.GetDirectoryName(Simulation.FileName);
            if (Path.IsPathRooted(SQLiteDatabasePath))
                return SQLiteDatabasePath;
            else
                return Path.Combine(simDir, SQLiteDatabasePath);
        }

        /// <summary>
        /// Parse the wavelength range from the specified string
        /// </summary>
        /// <param name="startWavelength">Output start wavelength</param>
        /// <param name="endWavelength">Output end wavelength</param>
        /// <returns>True if parsing was successful</returns>
        private bool ParseWavelengthRange(out double startWavelength, out double endWavelength)
        {
            startWavelength = 400;
            endWavelength = 2500;

            if (string.IsNullOrWhiteSpace(OutputWavelengthRange))
            {
                Summary.WriteMessage(this, "ProspectModel: OutputWavelengthRange is empty, using default range 400-2500 nm.", MessageType.Information);
                return true;
            }

            string[] parts = OutputWavelengthRange.Split('-');
            if (parts.Length != 2)
            {
                Summary.WriteMessage(this, $"ProspectModel: Invalid wavelength range format: {OutputWavelengthRange}.", MessageType.Warning);
                return false;
            }

            if (!double.TryParse(parts[0], out startWavelength) || !double.TryParse(parts[1], out endWavelength))
            {
                Summary.WriteMessage(this, $"ProspectModel: Failed to parse wavelength range values: {OutputWavelengthRange}.", MessageType.Warning);
                return false;
            }

            if (startWavelength < 0 || endWavelength < startWavelength)
            {
                Summary.WriteMessage(this, $"ProspectModel: Invalid wavelength range values (start < 0 or end < start): {OutputWavelengthRange}.", MessageType.Warning);
                return false;
            }

            Summary.WriteMessage(this, $"ProspectModel: Parsed wavelength range: {startWavelength}-{endWavelength} nm.", MessageType.Information);
            return true;
        }

        /// <summary>
        /// Save PROSPECT results to SQLite database
        /// </summary>
        /// <param name="reflectance">Reflectance spectrum</param>
        /// <param name="transmittance">Transmittance spectrum</param>
        /// <param name="startWavelength">Start wavelength in nm</param>
        /// <param name="endWavelength">End wavelength in nm</param>
        private void SaveResultsToDatabase(double[] reflectance, double[] transmittance, double startWavelength, double endWavelength)
        {
            if (dbConnection == null || reflectance == null || transmittance == null || Wavelengths == null)
            {
                Summary.WriteMessage(this, "ProspectModel: SaveResultsToDatabase skipped due to null dbConnection, reflectance, transmittance, or Wavelengths.", MessageType.Warning);
                return;
            }

            try
            {
                string date = Clock.Today.ToString("yyyy-MM-dd");

                // Save parameter values
                string paramSql = $@"
                    INSERT OR REPLACE INTO Parameters 
                    (SimulationName, Date, N, CHL, CAR, EWT, LMA, ANT, BROWN, PROT, CBC, Alpha)
                    VALUES ('{date}', '{simulationName}', {CurrentParameterValues["N"]}, {CurrentParameterValues["CHL"]}, 
                            {CurrentParameterValues["CAR"]}, {CurrentParameterValues["EWT"]}, {CurrentParameterValues["LMA"]}, 
                            {CurrentParameterValues["ANT"]}, {CurrentParameterValues["BROWN"]}, {CurrentParameterValues["PROT"]}, 
                            {CurrentParameterValues["CBC"]}, {CurrentParameterValues["Alpha"]})";
                dbConnection.ExecuteNonQuery(paramSql);

                // Save spectral data
                for (int i = 0; i < Wavelengths.Length; i++)
                {
                    double wavelength = Wavelengths[i];
                    if (wavelength >= startWavelength && wavelength <= endWavelength)
                    {
                        string spectraSql = $@"
                            INSERT OR REPLACE INTO Spectra
                            (SimulationName, WavelengthNM, Date, Reflectance, Transmittance, Absorptance)
                            VALUES ({wavelength}, '{date}', '{simulationName}', {reflectance[i]}, {transmittance[i]}, 
                                    {1.0 - reflectance[i] - transmittance[i]})";
                        dbConnection.ExecuteNonQuery(spectraSql);
                    }
                }
            }
            catch (Exception ex)
            {
                Summary.WriteMessage(this, $"ProspectModel: Failed to save PROSPECT results to database: {ex.Message}", MessageType.Error);
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
            if (double.TryParse(expression, out double result))
                return result;

            object value = ExpressionFunction.Evaluate(expression, this);
            if (value is double)
                return (double)value;
            else if (value is double[] && ((double[])value).Length > 0)
                return ((double[])value)[0];
            else
                return Convert.ToDouble(value);
        }

        /// <summary>
        /// Calculate leaf optical properties using the PROSPECT model
        /// </summary>
        /// <returns>A tuple containing reflectance and transmittance vectors</returns>
        public (Vector<double> Reflectance, Vector<double> Transmittance) CalculateProspect()
        {
            if (cachedSpectralConstants == null)
            {
                Summary.WriteMessage(this, $"ProspectModel: CalculateProspect called without spectral constants on {Clock?.Today:yyyy-MM-dd}.", MessageType.Error);
                throw new InvalidOperationException("Spectral constants not loaded.");
            }

            Summary.WriteMessage(this, $"ProspectModel: CalculateProspect called on {Clock?.Today:yyyy-MM-dd}.", MessageType.Information);

            // Evaluate expressions and store current parameter values
            double nValue = EvaluateExpression(N);
            CurrentParameterValues["N"] = nValue;
            double chlValue = EvaluateExpression(CHL);
            CurrentParameterValues["CHL"] = chlValue;
            double carValue = EvaluateExpression(CAR);
            CurrentParameterValues["CAR"] = carValue;
            double ewtValue = EvaluateExpression(EWT);
            CurrentParameterValues["EWT"] = ewtValue;
            double lmaValue = EvaluateExpression(LMA);
            CurrentParameterValues["LMA"] = lmaValue;
            double antValue = EvaluateExpression(ANT);
            CurrentParameterValues["ANT"] = antValue;
            double brownValue = EvaluateExpression(BROWN);
            CurrentParameterValues["BROWN"] = brownValue;
            double protValue = EvaluateExpression(PROT);
            CurrentParameterValues["PROT"] = protValue;
            double cbcValue = EvaluateExpression(CBC);
            CurrentParameterValues["CBC"] = cbcValue;
            double alphaValue = EvaluateExpression(Alpha);
            CurrentParameterValues["Alpha"] = alphaValue;

            // Run the PROSPECT model with current parameters
            var results = ProspectCore.Run(
                cachedSpectralConstants,
                nValue, chlValue, carValue, ewtValue, lmaValue,
                antValue, brownValue, protValue, cbcValue, alphaValue);

            Summary.WriteMessage(this, $"ProspectModel: CalculateProspect completed, Reflectance[{results.Reflectance.Count}], Transmittance[{results.Transmittance.Count}]", MessageType.Information);

            return results;
        }

        /// <summary>
        /// Document the PROSPECT model inputs and outputs
        /// </summary>
        /// <param name="tags">The xml tags</param>
        /// <param name="headingLevel">The heading level</param>
        /// <param name="indent">The indentation level</param>
        public void Document(List<AutoDocumentation.ITag> tags, int headingLevel, int indent)
        {
            tags.Add(new AutoDocumentation.Heading(Name, headingLevel));
            tags.Add(new AutoDocumentation.Paragraph("The PROSPECT model simulates leaf optical properties (reflectance, transmittance, and absorptance) " +
                                                      "based on leaf biochemical and structural properties.", indent));

            tags.Add(new AutoDocumentation.Heading("Input Parameters", headingLevel + 1));
            tags.Add(new AutoDocumentation.Paragraph("The following parameters control the leaf optical properties:", indent));
            tags.Add(new AutoDocumentation.Paragraph($"N (Leaf structure parameter): {N}", indent));
            tags.Add(new AutoDocumentation.Paragraph($"CHL (Chlorophyll content, μg/cm²): {CHL}", indent));
            tags.Add(new AutoDocumentation.Paragraph($"CAR (Carotenoid content, μg/cm²): {CAR}", indent));
            tags.Add(new AutoDocumentation.Paragraph($"EWT (Equivalent Water Thickness, g/cm²): {EWT}", indent));
            tags.Add(new AutoDocumentation.Paragraph($"LMA (Leaf Mass per Area, g/cm²): {LMA}", indent));
            if (!string.IsNullOrEmpty(ANT) && ANT != "0.0")
                tags.Add(new AutoDocumentation.Paragraph($"ANT (Anthocyanin content, μg/cm²): {ANT}", indent));
            if (!string.IsNullOrEmpty(BROWN) && BROWN != "0.0")
                tags.Add(new AutoDocumentation.Paragraph($"BROWN (Brown pigment content): {BROWN}", indent));
            if (!string.IsNullOrEmpty(PROT) && PROT != "0.0")
                tags.Add(new AutoDocumentation.Paragraph($"PROT (Protein content, g/cm²): {PROT}", indent));
            if (!string.IsNullOrEmpty(CBC) && CBC != "0.0")
                tags.Add(new AutoDocumentation.Paragraph($"CBC (Carbon-based constituent content, g/cm²): {CBC}", indent));
            tags.Add(new AutoDocumentation.Paragraph($"Alpha (Incidence angle, degrees): {Alpha}", indent));

            tags.Add(new AutoDocumentation.Heading("Outputs", headingLevel + 1));
            tags.Add(new AutoDocumentation.Paragraph("The model provides the following outputs:", indent));
            tags.Add(new AutoDocumentation.Paragraph("- Full spectrum leaf reflectance, transmittance, and absorptance", indent));

            if (EnableSQLiteOutput)
            {
                tags.Add(new AutoDocumentation.Heading("Database Output", headingLevel + 1));
                tags.Add(new AutoDocumentation.Paragraph("Spectral data is saved to a SQLite database with the following details:", indent));
                tags.Add(new AutoDocumentation.Paragraph($"- Database file: {SQLiteDatabasePath}", indent));
                tags.Add(new AutoDocumentation.Paragraph($"- Wavelength range: {OutputWavelengthRange} nm", indent));
                tags.Add(new AutoDocumentation.Paragraph("The database contains full-resolution (1 nm) spectral data for each simulation day, including reflectance, transmittance, and absorptance values.", indent));
            }
        }
    }
}
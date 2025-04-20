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
    //[ValidParent(ParentType = typeof(Simulation))]
    //[ValidParent(ParentType = typeof(Zone))]
    //[ValidParent(ParentType = typeof(Model))]
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
        public bool EnableSQLiteOutput { get; set; } = false;

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
        /// The optional cached spectral constants to avoid reloading for each calculation
        /// </summary>
        private ProspectCore.SpectralConstants? cachedSpectralConstants = null;

        /// <summary>
        /// Database connection
        /// </summary>
        private SQLite dbConnection = null;

        /// <summary>
        /// Current simulation ID for database records
        /// </summary>
        private string simulationID = null;

        /// <summary>
        /// Cached wavelengths (nm) from the spectral constants
        /// </summary>
        [Description("Wavelengths (nm)")]
        public double[] Wavelengths
        {
            get
            {
                EnsureSpectralConstantsLoaded();
                return cachedSpectralConstants?.Wavelengths.ToArray();
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
                var results = CalculateProspect();
                return results.Reflectance.ToArray();
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
                var results = CalculateProspect();
                return results.Transmittance.ToArray();
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
                var results = CalculateProspect();
                Vector<double> reflectance = results.Reflectance;
                Vector<double> transmittance = results.Transmittance;

                // Calculate absorptance as 1 - reflectance - transmittance
                Vector<double> absorptance = Vector<double>.Build.Dense(reflectance.Count);
                for (int i = 0; i < reflectance.Count; i++)
                {
                    absorptance[i] = Math.Round(1.0 - reflectance[i] - transmittance[i], 4);
                }

                return absorptance.ToArray();
            }
        }

        /// <summary>Called when [simulation commencing].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Commencing")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {
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
            // Clear previous parameter values
            CurrentParameterValues.Clear();
        }

        /// <summary>Called when [do daily calculations].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoDaily")]
        private void OnDoDaily(object sender, EventArgs e)
        {
            // Calculate PROSPECT outputs
            var results = CalculateProspect();

            // Get wavelength range
            if (ParseWavelengthRange(out double startWavelength, out double endWavelength))
            {
                // Save results to database
                if (EnableSQLiteOutput)
                {
                    SaveResultsToDatabase(results.Reflectance.ToArray(), results.Transmittance.ToArray(), startWavelength, endWavelength);
                }                   
                // Report to summary
                Summary.WriteMessage(this, $"PROSPECT results for {Clock.Today:yyyy-MM-dd} saved to database.", MessageType.Information);
                Summary.WriteMessage(this, $"  Wavelength range: {startWavelength}-{endWavelength} nm", MessageType.Information);
                Summary.WriteMessage(this, $"  Parameters: N={CurrentParameterValues["N"]:F2}, CHL={CurrentParameterValues["CHL"]:F1}, CAR={CurrentParameterValues["CAR"]:F1}, EWT={CurrentParameterValues["EWT"]:F4}, LMA={CurrentParameterValues["LMA"]:F4}", MessageType.Information);
            }
            else
            {
                Summary.WriteMessage(this, $"Invalid wavelength range specified: {OutputWavelengthRange}. Using full spectrum.", MessageType.Warning);
                // Save results to database
                if (EnableSQLiteOutput)
                {
                    SaveResultsToDatabase(results.Reflectance.ToArray(), results.Transmittance.ToArray(), 0, 10000); // Full range
                }
            }
        }

        /// <summary>Called when [simulation completed].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Completed")]
        private void OnSimulationCompleted(object sender, EventArgs e)
        {
            // Close database connection if open
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

                // Create directory if it doesn't exist
                string dbDir = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
                {
                    Directory.CreateDirectory(dbDir);
                }

                // Create or open database
                dbConnection = new SQLite();
                dbConnection.OpenDatabase(dbPath, false); // false for read/write mode

                // Generate a unique simulation ID
                simulationID = Guid.NewGuid().ToString();

                // Create tables if they don't exist
                dbConnection.ExecuteNonQuery(@"
            CREATE TABLE IF NOT EXISTS Simulations (
                SimulationID TEXT PRIMARY KEY,
                SimulationName TEXT,
                StartDate TEXT,
                EndDate TEXT,
                CreatedAt TEXT
            )");

                dbConnection.ExecuteNonQuery(@"
            CREATE TABLE IF NOT EXISTS Parameters (
                Date TEXT,
                SimulationID TEXT,
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
                PRIMARY KEY (Date, SimulationID),
                FOREIGN KEY (SimulationID) REFERENCES Simulations(SimulationID)
            )");

                dbConnection.ExecuteNonQuery(@"
            CREATE TABLE IF NOT EXISTS Spectra (
                WavelengthNM REAL,
                Date TEXT,
                SimulationID TEXT,
                Reflectance REAL,
                Transmittance REAL,
                Absorptance REAL,
                PRIMARY KEY (WavelengthNM, Date, SimulationID),
                FOREIGN KEY (Date, SimulationID) REFERENCES Parameters(Date, SimulationID)
            )");

                // Insert simulation record
                string sql = $@"
            INSERT INTO Simulations (SimulationID, SimulationName, StartDate, EndDate, CreatedAt)
            VALUES ('{simulationID}', '{Simulation.Name.Replace("'", "''")}', '{Clock.StartDate:yyyy-MM-dd}', '{Clock.EndDate:yyyy-MM-dd}', '{DateTime.Now:yyyy-MM-dd HH:mm:ss}')";
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
            // Get the simulation directory
            string simDir = Path.GetDirectoryName(Simulation.FileName);
            // If path is absolute, use it directly, otherwise combine with simulation directory
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
                return true; // Use defaults

            string[] parts = OutputWavelengthRange.Split('-');
            if (parts.Length != 2)
                return false;

            if (!double.TryParse(parts[0], out startWavelength) || !double.TryParse(parts[1], out endWavelength))
                return false;

            if (startWavelength < 0 || endWavelength < startWavelength)
                return false;

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
                return;

            try
            {
                string date = Clock.Today.ToString("yyyy-MM-dd");

                dbConnection.BeginTransaction();

                // Save parameter values
                string paramSql = $@"
            INSERT OR REPLACE INTO Parameters 
            (Date, SimulationID, N, CHL, CAR, EWT, LMA, ANT, BROWN, PROT, CBC, Alpha)
            VALUES ('{date}', '{simulationID}', {CurrentParameterValues["N"]}, {CurrentParameterValues["CHL"]}, 
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
                    (WavelengthNM, Date, SimulationID, Reflectance, Transmittance, Absorptance)
                    VALUES ({wavelength}, '{date}', '{simulationID}', {reflectance[i]}, {transmittance[i]}, 
                            {1.0 - reflectance[i] - transmittance[i]})";
                        dbConnection.ExecuteNonQuery(spectraSql);
                    }
                }

                // Transaction is committed when connection is closed
            }
            catch (Exception ex)
            {
                Summary.WriteMessage(this, $"Failed to save PROSPECT results to database: {ex.Message}", MessageType.Error);
            }
        }

        /// <summary>
        /// Ensure the spectral constants are loaded
        /// </summary>
        private void EnsureSpectralConstantsLoaded()
        {
            if (cachedSpectralConstants == null)
            {
                // The null value will trigger ProspectCore to load the constants from the default file
                var results = ProspectCore.Run();
                // Store the loaded spectral constants for future use
                cachedSpectralConstants = new ProspectCore.SpectralConstants();
            }
        }

        /// <summary>
        /// Evaluates an expression and returns its value
        /// </summary>
        /// <param name="expression">The expression to evaluate</param>
        /// <returns>The evaluated expression value</returns>
        private double EvaluateExpression(string expression)
        {
            // First check if expression is a simple number
            if (double.TryParse(expression, out double result))
                return result;

            // Otherwise, evaluate the expression using the ExpressionFunction mechanism
            object value = ExpressionFunction.Evaluate(expression, this);

            if (value is double)
                return (double)value;
            else if (value is double[] && ((double[])value).Length > 0)
                return ((double[])value)[0]; // Take first element if it's an array
            else
                return Convert.ToDouble(value);
        }

        /// <summary>
        /// Calculate leaf optical properties using the PROSPECT model
        /// </summary>
        /// <returns>A tuple containing reflectance and transmittance vectors</returns>
        public (Vector<double> Reflectance, Vector<double> Transmittance) CalculateProspect()
        {
            EnsureSpectralConstantsLoaded();

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
            return ProspectCore.Run(
                cachedSpectralConstants,
                nValue, chlValue, carValue, ewtValue, lmaValue,
                antValue, brownValue, protValue, cbcValue, alphaValue);
        }

        /// <summary>
        /// Document the PROSPECT model inputs and outputs
        /// </summary>
        /// <param name="tags">The xml tags</param>
        /// <param name="headingLevel">The heading level</param>
        /// <param name="indent">The indentation level</param>
        public void Document(List<AutoDocumentation.ITag> tags, int headingLevel, int indent)
        {
            // Model description
            tags.Add(new AutoDocumentation.Heading(Name, headingLevel));
            tags.Add(new AutoDocumentation.Paragraph("The PROSPECT model simulates leaf optical properties (reflectance, transmittance, and absorptance) " +
                                                      "based on leaf biochemical and structural properties.", indent));

            // Parameters section
            tags.Add(new AutoDocumentation.Heading("Input Parameters", headingLevel + 1));
            tags.Add(new AutoDocumentation.Paragraph("The following parameters control the leaf optical properties:", indent));

            // List parameters and their expressions
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

            // Outputs section
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
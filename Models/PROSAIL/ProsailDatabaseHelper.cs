using APSIM.Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using static Models.PROSAIL.SAIL.SailUtilities;

namespace Models.PROSAIL
{
    /// <summary>
    /// Handles SQLite database initialization, writing, and path resolution for ProsailModel.
    /// </summary>
    /// <remarks>
    /// Physical storage tables are prefixed with an underscore (matching the convention used by
    /// Models.Storage.DataStoreWriter for its own _Simulations/_Checkpoints/_Units tables) and key
    /// rows by a compact SimulationID/WavelengthID instead of repeating SimulationName/Wavelength on
    /// every row. Plain views named after the original tables (Parameters, CanopyOpticalVariable, ...)
    /// reconstruct the original flat shape so existing queries/scripts are unaffected.
    /// </remarks>
    public static class ProsailDatabaseHelper
    {
        private static readonly List<string> ParameterColumns = new List<string> {
            "SimulationID", "Date", "N", "CAB", "CAR", "EWT", "LMA", "ANT", "BROWN", "PROT", "CBC", "Alpha",
            "LAI", "HotSpot", "TypeLidf", "LIDFa", "LIDFb", "FractionBrown", "Dissociation", "CrownCover", "TreeShape",
            "WetDrySoilReflectancePath", "Psoil", "SunZenithAngle", "ObserverZenithAngle", "RelativeAzimuthAngle",
            "SailVersion", "SensorType"
        };
        private static readonly List<string> CanopyOpticalVariableColumns = new List<string> {
            "SimulationID", "Date", "WavelengthID", "Rdot", "Rsot", "Rddt", "Rsdt", "fCover", "Abs_dir", "Abs_hem", "Rsdstar", "Rddstar"
        };
        private static readonly List<string> CanopyStateVariableColumns = new List<string> {
            "SimulationID", "Date", "fAPAR", "fCover", "albedo"
        };
        private static readonly List<string> CanopyBRFColumns = new List<string> {
            "SimulationID", "Date", "WavelengthID", "BRF"
        };
        private static readonly List<string> ResampledColumns = new List<string> {
            "SimulationID", "Date", "WavelengthID", "BandName", "Reflectance"
        };

        /// <summary>
        /// Gets the full path to the database file, resolving relative paths against the simulation directory.
        /// </summary>
        /// <param name="databasePath">Database path (may be relative).</param>
        /// <param name="simulationFileName">Full path to the simulation file.</param>
        /// <returns>Absolute database file path.</returns>
        public static string GetFullDatabasePath(string databasePath, string simulationFileName)
        {
            string simDir = Path.GetDirectoryName(simulationFileName);
            if (Path.IsPathRooted(databasePath))
                return databasePath;
            else
                return Path.Combine(simDir, databasePath);
        }

        /// <summary>
        /// Initializes the SQLite database: creates the lookup/storage tables and compatibility views for
        /// selected PROSAIL outputs, and resolves this simulation's compact SimulationID. Unlike a fresh
        /// database, an existing one is not dropped - only this simulation's own previous rows (identified
        /// by SimulationID) are cleared, so other simulations sharing the same database file are untouched.
        /// </summary>
        /// <param name="dbPath">Full path to the database file.</param>
        /// <param name="simulationName">Sanitized simulation name (single quotes escaped).</param>
        /// <param name="outputParameters">Whether to create/clear the Parameters table.</param>
        /// <param name="outputCanopyOpticalVariable">Whether to create/clear the CanopyOpticalVariable table.</param>
        /// <param name="outputCanopyStateVariable">Whether to create/clear the CanopyStateVariable table.</param>
        /// <param name="outputCanopyBRF">Whether to create/clear the CanopyBRF table.</param>
        /// <param name="outputReflectanceResampledToSensor">Whether to create/clear the ReflectanceResampledToSensor table.</param>
        /// <param name="simulationID">This simulation's resolved compact ID.</param>
        /// <param name="writeMessage">Logging callback.</param>
        /// <returns>An open SQLite connection.</returns>
        public static SQLite InitializeDatabase(string dbPath, string simulationName,
            bool outputParameters, bool outputCanopyOpticalVariable, bool outputCanopyStateVariable,
            bool outputCanopyBRF, bool outputReflectanceResampledToSensor,
            out int simulationID,
            Action<LogLevel, string> writeMessage)
        {
            try
            {
                string dbDir = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
                    Directory.CreateDirectory(dbDir);

                var db = new SQLite();
                db.OpenDatabase(dbPath, false);

                db.ExecuteNonQuery(@"
                    CREATE TABLE IF NOT EXISTS _Simulations (
                        SimulationID INTEGER PRIMARY KEY,
                        SimulationName TEXT UNIQUE
                    )");
                db.ExecuteNonQuery(@"
                    CREATE TABLE IF NOT EXISTS _Wavelengths (
                        WavelengthID INTEGER PRIMARY KEY,
                        Wavelength REAL UNIQUE
                    )");

                if (outputParameters)
                    db.ExecuteNonQuery(@"
                        CREATE TABLE IF NOT EXISTS _Parameters (
                            SimulationID INTEGER,
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
                            WetDrySoilReflectancePath TEXT,
                            Psoil REAL,
                            SunZenithAngle REAL,
                            ObserverZenithAngle REAL,
                            RelativeAzimuthAngle REAL,
                            SailVersion TEXT,
                            SensorType TEXT,
                            PRIMARY KEY (SimulationID, Date)
                        )");

                if (outputCanopyOpticalVariable)
                    db.ExecuteNonQuery(@"
                        CREATE TABLE IF NOT EXISTS _CanopyOpticalVariable (
                            SimulationID INTEGER,
                            Date TEXT,
                            WavelengthID INTEGER,
                            Rdot REAL,
                            Rsot REAL,
                            Rddt REAL,
                            Rsdt REAL,
                            FCover REAL,
                            Abs_dir REAL,
                            Abs_hem REAL,
                            Rsdstar REAL,
                            Rddstar REAL,
                            PRIMARY KEY (SimulationID, Date, WavelengthID)
                        )");

                if (outputCanopyStateVariable)
                    db.ExecuteNonQuery(@"
                        CREATE TABLE IF NOT EXISTS _CanopyStateVariable (
                            SimulationID INTEGER,
                            Date TEXT,
                            fAPAR REAL,
                            fCover REAL,
                            albedo REAL,
                            PRIMARY KEY (SimulationID, Date)
                        )");

                if (outputCanopyBRF)
                    db.ExecuteNonQuery(@"
                        CREATE TABLE IF NOT EXISTS _CanopyBRF (
                            SimulationID INTEGER,
                            Date TEXT,
                            WavelengthID INTEGER,
                            BRF REAL,
                            PRIMARY KEY (SimulationID, Date, WavelengthID)
                        )");

                if (outputReflectanceResampledToSensor)
                    db.ExecuteNonQuery(@"
                        CREATE TABLE IF NOT EXISTS _ReflectanceResampledToSensor (
                            SimulationID INTEGER,
                            Date TEXT,
                            WavelengthID INTEGER,
                            BandName TEXT,
                            Reflectance REAL,
                            PRIMARY KEY (SimulationID, Date, WavelengthID)
                        )");

                // Compatibility views: same names/columns as before normalization, so existing
                // queries against Parameters/CanopyOpticalVariable/etc. keep working unchanged.
                if (outputParameters)
                    db.ExecuteNonQuery(@"
                        CREATE VIEW IF NOT EXISTS Parameters AS
                        SELECT s.SimulationName, p.Date, p.N, p.CAB, p.CAR, p.EWT, p.LMA, p.ANT, p.BROWN, p.PROT, p.CBC, p.Alpha,
                               p.LAI, p.HotSpot, p.TypeLidf, p.LIDFa, p.LIDFb, p.FractionBrown, p.Dissociation, p.CrownCover, p.TreeShape,
                               p.WetDrySoilReflectancePath, p.Psoil, p.SunZenithAngle, p.ObserverZenithAngle, p.RelativeAzimuthAngle,
                               p.SailVersion, p.SensorType
                        FROM _Parameters p JOIN _Simulations s ON p.SimulationID = s.SimulationID");

                if (outputCanopyOpticalVariable)
                    db.ExecuteNonQuery(@"
                        CREATE VIEW IF NOT EXISTS CanopyOpticalVariable AS
                        SELECT s.SimulationName, t.Date, w.Wavelength, t.Rdot, t.Rsot, t.Rddt, t.Rsdt, t.FCover, t.Abs_dir, t.Abs_hem, t.Rsdstar, t.Rddstar
                        FROM _CanopyOpticalVariable t
                        JOIN _Simulations s ON t.SimulationID = s.SimulationID
                        JOIN _Wavelengths w ON t.WavelengthID = w.WavelengthID");

                if (outputCanopyStateVariable)
                    db.ExecuteNonQuery(@"
                        CREATE VIEW IF NOT EXISTS CanopyStateVariable AS
                        SELECT s.SimulationName, t.Date, t.fAPAR, t.fCover, t.albedo
                        FROM _CanopyStateVariable t JOIN _Simulations s ON t.SimulationID = s.SimulationID");

                if (outputCanopyBRF)
                    db.ExecuteNonQuery(@"
                        CREATE VIEW IF NOT EXISTS CanopyBRF AS
                        SELECT s.SimulationName, t.Date, w.Wavelength, t.BRF
                        FROM _CanopyBRF t
                        JOIN _Simulations s ON t.SimulationID = s.SimulationID
                        JOIN _Wavelengths w ON t.WavelengthID = w.WavelengthID");

                if (outputReflectanceResampledToSensor)
                    db.ExecuteNonQuery(@"
                        CREATE VIEW IF NOT EXISTS ReflectanceResampledToSensor AS
                        SELECT s.SimulationName, t.Date, w.Wavelength, t.BandName, t.Reflectance
                        FROM _ReflectanceResampledToSensor t
                        JOIN _Simulations s ON t.SimulationID = s.SimulationID
                        JOIN _Wavelengths w ON t.WavelengthID = w.WavelengthID");

                simulationID = GetOrCreateSimulationID(db, simulationName);

                // Clear only this simulation's own previous rows (if re-running), leaving any other
                // simulations sharing this database file untouched.
                if (outputParameters)            db.ExecuteNonQuery($"DELETE FROM _Parameters WHERE SimulationID = {simulationID};");
                if (outputCanopyOpticalVariable)  db.ExecuteNonQuery($"DELETE FROM _CanopyOpticalVariable WHERE SimulationID = {simulationID};");
                if (outputCanopyStateVariable)    db.ExecuteNonQuery($"DELETE FROM _CanopyStateVariable WHERE SimulationID = {simulationID};");
                if (outputCanopyBRF)              db.ExecuteNonQuery($"DELETE FROM _CanopyBRF WHERE SimulationID = {simulationID};");
                if (outputReflectanceResampledToSensor) db.ExecuteNonQuery($"DELETE FROM _ReflectanceResampledToSensor WHERE SimulationID = {simulationID};");

                writeMessage(LogLevel.Info, $"PROSAIL database initialized: {dbPath}");
                return db;
            }
            catch (Exception ex)
            {
                writeMessage(LogLevel.Error, $"Failed to initialize PROSAIL database: {ex.Message}");
                throw;
            }
        }

        /// <summary>Looks up this simulation's compact ID in _Simulations, creating it if it doesn't already exist.</summary>
        private static int GetOrCreateSimulationID(SQLite db, string simulationName)
        {
            db.ExecuteNonQuery($"INSERT OR IGNORE INTO _Simulations (SimulationName) VALUES ('{simulationName}');");
            return db.ExecuteQueryReturnInt($"SELECT SimulationID FROM _Simulations WHERE SimulationName = '{simulationName}';", 0);
        }

        /// <summary>
        /// Registers every wavelength this simulation will write (the full simulation grid, plus sensor
        /// band center wavelengths if resampled output is enabled) in _Wavelengths, and returns a
        /// wavelength-to-WavelengthID lookup covering the whole table (shared across all simulations
        /// writing to this database file) so each day's write can translate a wavelength to its ID with a
        /// plain dictionary lookup instead of a per-row database query.
        /// </summary>
        /// <param name="db">Open SQLite connection.</param>
        /// <param name="wavelengths">Distinct wavelengths to ensure are registered.</param>
        public static Dictionary<double, int> RegisterWavelengths(SQLite db, IEnumerable<double> wavelengths)
        {
            List<double> distinct = wavelengths.Distinct().ToList();
            if (distinct.Count > 0)
            {
                db.ExecuteNonQuery("BEGIN TRANSACTION;");
                try
                {
                    foreach (double wavelength in distinct)
                        db.ExecuteNonQuery($"INSERT OR IGNORE INTO _Wavelengths (Wavelength) VALUES ({wavelength.ToString("G17", CultureInfo.InvariantCulture)});");
                    db.ExecuteNonQuery("COMMIT;");
                }
                catch
                {
                    db.ExecuteNonQuery("ROLLBACK;");
                    throw;
                }
            }

            var lookup = new Dictionary<double, int>();
            DataTable rows = db.ExecuteQuery("SELECT WavelengthID, Wavelength FROM _Wavelengths;");
            foreach (DataRow row in rows.Rows)
                lookup[Convert.ToDouble(row["Wavelength"])] = Convert.ToInt32(row["WavelengthID"]);
            return lookup;
        }

        /// <summary>
        /// Writes PROSAIL results for one day to the database.
        /// </summary>
        /// <param name="db">Open SQLite connection.</param>
        /// <param name="simulationID">This simulation's compact ID (from InitializeDatabase).</param>
        /// <param name="wavelengthIdLookup">Wavelength-to-WavelengthID lookup (from RegisterWavelengths).</param>
        /// <param name="date">Simulation date.</param>
        /// <param name="parameterValues">Current parameter values dictionary.</param>
        /// <param name="wetDrySoilReflectancePath">Soil reflectance file path (for logging in DB).</param>
        /// <param name="sailVersionString">SAIL version string ("4SAIL" or "4SAIL2").</param>
        /// <param name="sensorTypeString">Sensor type string for the DB record.</param>
        /// <param name="canopyOptics">Canopy optical variable results.</param>
        /// <param name="canopyStateVariables">Canopy state variable results.</param>
        /// <param name="canopyBRF">Canopy BRF results.</param>
        /// <param name="spectralResamplingResult">Resampled reflectance results (may be null).</param>
        /// <param name="outputParameters">Whether to write the Parameters table.</param>
        /// <param name="outputCanopyOpticalVariable">Whether to write the CanopyOpticalVariable table.</param>
        /// <param name="outputCanopyStateVariable">Whether to write the CanopyStateVariable table.</param>
        /// <param name="outputCanopyBRF">Whether to write the CanopyBRF table.</param>
        /// <param name="outputReflectanceResampledToSensor">Whether to write the ReflectanceResampledToSensor table.</param>
        /// <param name="writeMessage">Logging callback.</param>
        public static void WriteToDatabase(SQLite db, int simulationID, Dictionary<double, int> wavelengthIdLookup, DateTime date,
            Dictionary<string, object> parameterValues,
            string wetDrySoilReflectancePath, string sailVersionString, string sensorTypeString,
            CanopyOptics canopyOptics, CanopyStateVariables canopyStateVariables,
            CanopyBRF canopyBRF, SpectralResamplingResult spectralResamplingResult,
            bool outputParameters, bool outputCanopyOpticalVariable, bool outputCanopyStateVariable,
            bool outputCanopyBRF, bool outputReflectanceResampledToSensor,
            Action<LogLevel, string> writeMessage)
        {
            if (db == null)
            {
                writeMessage(LogLevel.Error, "ProsailModel: WriteToDatabase skipped: null dbConnection.");
                throw new InvalidOperationException("ProsailModel: WriteToDatabase: null dbConnection.");
            }

            try
            {
                db.ExecuteNonQuery("BEGIN TRANSACTION;");
                string dateStr = date.ToString("yyyy-MM-dd");

                // Parameters INSERT
                if (outputParameters)
                {
                    db.InsertRows("_Parameters", ParameterColumns, new List<object[]> { new object[] {
                        simulationID, dateStr,
                        parameterValues["N"], parameterValues["CAB"], parameterValues["CAR"],
                        parameterValues["EWT"], parameterValues["LMA"], parameterValues["ANT"],
                        parameterValues["BROWN"], parameterValues["PROT"], parameterValues["CBC"],
                        parameterValues["Alpha"], parameterValues["LAI"], parameterValues["HotSpot"],
                        parameterValues["TypeLidf"], parameterValues["LIDFa"], parameterValues["LIDFb"],
                        parameterValues["FractionBrown"], parameterValues["Dissociation"],
                        parameterValues["CrownCover"], parameterValues["TreeShape"],
                        wetDrySoilReflectancePath ?? "",
                        parameterValues["Psoil"], parameterValues["SunZenithAngle"],
                        parameterValues["ObserverZenithAngle"], parameterValues["RelativeAzimuthAngle"],
                        sailVersionString, sensorTypeString
                    }});
                }

                // CanopyOpticalVariable INSERT
                if (outputCanopyOpticalVariable && canopyOptics?.Wavelength != null)
                {
                    double[] usedWavelength = canopyOptics.Wavelength;
                    double[] Rdot    = canopyOptics.Rdot;
                    double[] Rsot    = canopyOptics.Rsot;
                    double[] Rddt    = canopyOptics.Rddt;
                    double[] Rsdt    = canopyOptics.Rsdt;
                    double[] FCover  = canopyOptics.FCover;
                    double[] Abs_dir = canopyOptics.Abs_dir;
                    double[] Abs_hem = canopyOptics.Abs_hem;
                    double[] Rsdstar = canopyOptics.Rsdstar;
                    double[] Rddstar = canopyOptics.Rddstar;

                    var rows = new List<object[]>(usedWavelength.Length);
                    for (int i = 0; i < usedWavelength.Length; i++)
                        rows.Add(new object[] {
                            simulationID, dateStr, wavelengthIdLookup[usedWavelength[i]],
                            Rdot[i], Rsot[i], Rddt[i], Rsdt[i], FCover[i],
                            Abs_dir[i], Abs_hem[i], Rsdstar[i], Rddstar[i]
                        });

                    writeMessage(LogLevel.Debug, "ProsailModel: Executing CanopyOpticalVariable INSERT.");
                    db.InsertRows("_CanopyOpticalVariable", CanopyOpticalVariableColumns, rows);
                }

                // CanopyStateVariable INSERT
                if (outputCanopyStateVariable)
                {
                    db.InsertRows("_CanopyStateVariable", CanopyStateVariableColumns, new List<object[]> { new object[] {
                        simulationID, dateStr,
                        canopyStateVariables.fAPAR, canopyStateVariables.fcover, canopyStateVariables.albedo
                    }});
                }

                // ReflectanceResampledToSensor INSERT
                if (outputReflectanceResampledToSensor && spectralResamplingResult?.Reflectance != null)
                {
                    try
                    {
                        var rsRows = new List<object[]>();
                        for (int bandIndex = 0; bandIndex < spectralResamplingResult.Reflectance.Count; bandIndex++)
                        {
                            double[] bandReflectance = spectralResamplingResult.Reflectance[bandIndex];
                            string bandName = spectralResamplingResult.BandNames[bandIndex];
                            double wavelength = spectralResamplingResult.Wavelength[bandIndex];

                            if (bandReflectance == null || bandReflectance.Length == 0)
                            {
                                writeMessage(LogLevel.Warning, $"ProsailModel: No reflectance data for band '{bandName}' on {dateStr} when resampling reflectance to sensor.");
                                continue;
                            }
                            int wavelengthID = wavelengthIdLookup[wavelength];
                            foreach (double reflectance in bandReflectance)
                                rsRows.Add(new object[] { simulationID, dateStr, wavelengthID, bandName, reflectance });
                        }
                        if (rsRows.Count > 0)
                        {
                            db.InsertRows("_ReflectanceResampledToSensor", ResampledColumns, rsRows);
                            writeMessage(LogLevel.Debug, $"ProsailModel: Successfully wrote resampled reflectance data to database for {dateStr}");
                        }
                    }
                    catch (Exception ex)
                    {
                        writeMessage(LogLevel.Error, $"ProsailModel: Failed to write resampled reflectance data: {ex.Message}");
                        throw;
                    }
                }

                // CanopyBRF INSERT
                if (outputCanopyBRF && canopyBRF.Wavelength != null && canopyBRF.BRF != null)
                {
                    var brfRows = new List<object[]>(canopyBRF.Wavelength.Length);
                    for (int i = 0; i < canopyBRF.Wavelength.Length; i++)
                        brfRows.Add(new object[] { simulationID, dateStr, wavelengthIdLookup[canopyBRF.Wavelength[i]], canopyBRF.BRF[i] });

                    db.InsertRows("_CanopyBRF", CanopyBRFColumns, brfRows);
                    writeMessage(LogLevel.Debug, $"ProsailModel: Successfully wrote CanopyBRF data to database for {dateStr}");
                }

                db.ExecuteNonQuery("COMMIT;");
                writeMessage(LogLevel.Info, $"ProsailModel: Wrote results for {date:yyyy-MM-dd} to database.");
            }
            catch (Exception ex)
            {
                db.ExecuteNonQuery("ROLLBACK;");
                writeMessage(LogLevel.Error, $"ProsailModel: Failed to write to database: {ex.Message}");
                throw;
            }
        }
    }
}

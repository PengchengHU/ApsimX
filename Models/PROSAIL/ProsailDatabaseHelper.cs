using APSIM.Shared.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using static Models.PROSAIL.SAIL.SailUtilities;

namespace Models.PROSAIL
{
    /// <summary>
    /// Handles SQLite database initialization, writing, and path resolution for ProsailModel.
    /// </summary>
    public static class ProsailDatabaseHelper
    {
        private static readonly List<string> ParameterColumns = new List<string> {
            "SimulationName", "Date", "N", "CAB", "CAR", "EWT", "LMA", "ANT", "BROWN", "PROT", "CBC", "Alpha",
            "LAI", "HotSpot", "TypeLidf", "LIDFa", "LIDFb", "FractionBrown", "Dissociation", "CrownCover", "TreeShape",
            "WetDrySoilReflectancePath", "Psoil", "SunZenithAngle", "ObserverZenithAngle", "RelativeAzimuthAngle",
            "SailVersion", "SensorType"
        };
        private static readonly List<string> CanopyOpticalVariableColumns = new List<string> {
            "SimulationName", "Date", "Wavelength", "Rdot", "Rsot", "Rddt", "Rsdt", "fCover", "Abs_dir", "Abs_hem", "Rsdstar", "Rddstar"
        };
        private static readonly List<string> CanopyStateVariableColumns = new List<string> {
            "SimulationName", "Date", "fAPAR", "fCover", "albedo"
        };
        private static readonly List<string> CanopyBRFColumns = new List<string> {
            "SimulationName", "Date", "Wavelength", "BRF"
        };
        private static readonly List<string> ResampledColumns = new List<string> {
            "SimulationName", "Date", "Wavelength", "BandName", "Reflectance"
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
        /// Initializes the SQLite database: creates tables for selected PROSAIL outputs.
        /// </summary>
        /// <param name="dbPath">Full path to the database file.</param>
        /// <param name="simulationName">Sanitized simulation name (single quotes escaped).</param>
        /// <param name="startDate">Simulation start date.</param>
        /// <param name="endDate">Simulation end date.</param>
        /// <param name="outputParameters">Whether to create the Parameters table.</param>
        /// <param name="outputCanopyOpticalVariable">Whether to create the CanopyOpticalVariable table.</param>
        /// <param name="outputCanopyStateVariable">Whether to create the CanopyStateVariable table.</param>
        /// <param name="outputCanopyBRF">Whether to create the CanopyBRF table.</param>
        /// <param name="outputReflectanceResampledToSensor">Whether to create the ReflectanceResampledToSensor table.</param>
        /// <param name="writeMessage">Logging callback.</param>
        /// <returns>An open SQLite connection, or null if initialization fails.</returns>
        public static SQLite InitializeDatabase(string dbPath, string simulationName,
            DateTime startDate, DateTime endDate,
            bool outputParameters, bool outputCanopyOpticalVariable, bool outputCanopyStateVariable,
            bool outputCanopyBRF, bool outputReflectanceResampledToSensor,
            Action<LogLevel, string> writeMessage)
        {
            try
            {
                string dbDir = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
                    Directory.CreateDirectory(dbDir);

                var db = new SQLite();
                db.OpenDatabase(dbPath, false);

                // Drop existing tables (dependents first)
                if (outputCanopyOpticalVariable) db.ExecuteNonQuery("DROP TABLE IF EXISTS CanopyOpticalVariable;");
                if (outputCanopyStateVariable)   db.ExecuteNonQuery("DROP TABLE IF EXISTS CanopyStateVariable;");
                if (outputCanopyBRF)             db.ExecuteNonQuery("DROP TABLE IF EXISTS CanopyBRF;");
                if (outputReflectanceResampledToSensor) db.ExecuteNonQuery("DROP TABLE IF EXISTS ReflectanceResampledToSensor;");
                if (outputParameters)            db.ExecuteNonQuery("DROP TABLE IF EXISTS Parameters;");

                if (outputParameters)
                    db.ExecuteNonQuery(@"
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
                            WetDrySoilReflectancePath TEXT,
                            Psoil REAL,
                            SunZenithAngle REAL,
                            ObserverZenithAngle REAL,
                            RelativeAzimuthAngle REAL,
                            SailVersion TEXT,
                            SensorType TEXT,
                            PRIMARY KEY (SimulationName, Date)
                        )");

                if (outputCanopyOpticalVariable)
                    db.ExecuteNonQuery(@"
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
                            PRIMARY KEY (SimulationName, Date, Wavelength)
                        )");

                if (outputCanopyStateVariable)
                    db.ExecuteNonQuery(@"
                        CREATE TABLE IF NOT EXISTS CanopyStateVariable (
                            SimulationName TEXT,
                            Date TEXT,
                            fAPAR REAL,
                            fCover REAL,
                            albedo REAL,
                            PRIMARY KEY (SimulationName, Date)
                        )");

                if (outputCanopyBRF)
                    db.ExecuteNonQuery(@"
                        CREATE TABLE IF NOT EXISTS CanopyBRF (
                            SimulationName TEXT,
                            Date TEXT,
                            Wavelength REAL,
                            BRF REAL,
                            PRIMARY KEY (SimulationName, Date, Wavelength)
                        )");

                if (outputReflectanceResampledToSensor)
                    db.ExecuteNonQuery(@"
                        CREATE TABLE IF NOT EXISTS ReflectanceResampledToSensor (
                            SimulationName TEXT,
                            Date TEXT,
                            Wavelength REAL,
                            BandName TEXT,
                            Reflectance REAL,
                            PRIMARY KEY (SimulationName, Date, Wavelength)
                        )");

                writeMessage(LogLevel.Info, $"PROSAIL database initialized: {dbPath}");
                return db;
            }
            catch (Exception ex)
            {
                writeMessage(LogLevel.Error, $"Failed to initialize PROSAIL database: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Writes PROSAIL results for one day to the database.
        /// </summary>
        /// <param name="db">Open SQLite connection.</param>
        /// <param name="simulationName">Sanitized simulation name.</param>
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
        public static void WriteToDatabase(SQLite db, string simulationName, DateTime date,
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
                    db.InsertRows("Parameters", ParameterColumns, new List<object[]> { new object[] {
                        simulationName, dateStr,
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
                            simulationName, dateStr, usedWavelength[i],
                            Rdot[i], Rsot[i], Rddt[i], Rsdt[i], FCover[i],
                            Abs_dir[i], Abs_hem[i], Rsdstar[i], Rddstar[i]
                        });

                    writeMessage(LogLevel.Debug, "ProsailModel: Executing CanopyOpticalVariable INSERT.");
                    db.InsertRows("CanopyOpticalVariable", CanopyOpticalVariableColumns, rows);
                }

                // CanopyStateVariable INSERT
                if (outputCanopyStateVariable)
                {
                    db.InsertRows("CanopyStateVariable", CanopyStateVariableColumns, new List<object[]> { new object[] {
                        simulationName, dateStr,
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
                            foreach (double reflectance in bandReflectance)
                                rsRows.Add(new object[] { simulationName, dateStr, wavelength, bandName, reflectance });
                        }
                        if (rsRows.Count > 0)
                        {
                            db.InsertRows("ReflectanceResampledToSensor", ResampledColumns, rsRows);
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
                        brfRows.Add(new object[] { simulationName, dateStr, canopyBRF.Wavelength[i], canopyBRF.BRF[i] });

                    db.InsertRows("CanopyBRF", CanopyBRFColumns, brfRows);
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

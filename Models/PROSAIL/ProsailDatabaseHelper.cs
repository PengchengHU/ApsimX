using APSIM.Shared.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using static Models.PROSAIL.SAIL.SailUtilities;

namespace Models.PROSAIL
{
    /// <summary>
    /// Handles SQLite database initialization, writing, and path resolution for ProsailModel.
    /// </summary>
    public static class ProsailDatabaseHelper
    {
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
        /// Initializes the SQLite database: creates tables for PROSAIL results and inserts the simulation record.
        /// </summary>
        /// <param name="dbPath">Full path to the database file.</param>
        /// <param name="simulationName">Sanitized simulation name (single quotes escaped).</param>
        /// <param name="startDate">Simulation start date.</param>
        /// <param name="endDate">Simulation end date.</param>
        /// <param name="writeMessage">Logging callback.</param>
        /// <returns>An open SQLite connection, or null if initialization fails.</returns>
        public static SQLite InitializeDatabase(string dbPath, string simulationName,
            DateTime startDate, DateTime endDate, Action<LogLevel, string> writeMessage)
        {
            try
            {
                string dbDir = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
                    Directory.CreateDirectory(dbDir);

                var db = new SQLite();
                db.OpenDatabase(dbPath, false);

                // Clear existing tables
                db.ExecuteNonQuery("DROP TABLE IF EXISTS CanopyOpticalVariable;");
                db.ExecuteNonQuery("DROP TABLE IF EXISTS CanopyStateVariable;");
                db.ExecuteNonQuery("DROP TABLE IF EXISTS CanopyBRF;");
                db.ExecuteNonQuery("DROP TABLE IF EXISTS resampledReflectance;");
                db.ExecuteNonQuery("DROP TABLE IF EXISTS Parameters;");
                db.ExecuteNonQuery("DROP TABLE IF EXISTS Simulations;");

                db.ExecuteNonQuery(@"
                    CREATE TABLE IF NOT EXISTS Simulations (
                        SimulationName TEXT PRIMARY KEY,
                        StartDate TEXT,
                        EndDate TEXT,
                        CreatedAt TEXT
                    )");

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
                        PRIMARY KEY (SimulationName, Date),
                        FOREIGN KEY (SimulationName) REFERENCES Simulations(SimulationName)
                    )");

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
                        PRIMARY KEY (SimulationName, Date, Wavelength),
                        FOREIGN KEY (SimulationName, Date) REFERENCES Parameters(SimulationName, Date)
                    )");

                db.ExecuteNonQuery(@"
                    CREATE TABLE IF NOT EXISTS CanopyStateVariable (
                        SimulationName TEXT,
                        Date TEXT,
                        fAPAR REAL,
                        fCover REAL,
                        albedo REAL,
                        PRIMARY KEY (SimulationName, Date),
                        FOREIGN KEY (SimulationName, Date) REFERENCES Parameters(SimulationName, Date)
                    )");

                db.ExecuteNonQuery(@"
                    CREATE TABLE IF NOT EXISTS CanopyBRF (
                        SimulationName TEXT,
                        Date TEXT,
                        Wavelength REAL,
                        BRF REAL,
                        PRIMARY KEY (SimulationName, Date, Wavelength),
                        FOREIGN KEY (SimulationName, Date) REFERENCES Parameters(SimulationName, Date)
                    )");

                db.ExecuteNonQuery(@"
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
                    VALUES ('{simulationName}', '{startDate:yyyy-MM-dd}', '{endDate:yyyy-MM-dd}', '{DateTime.Now:yyyy-MM-dd HH:mm:ss}')";
                db.ExecuteNonQuery(sql);

                writeMessage(LogLevel.Info, $"PROSAIL database initialized: {dbPath}");
                return db;
            }
            catch (Exception ex)
            {
                writeMessage(LogLevel.Error, $"Failed to initialize PROSAIL database: {ex.Message}");
                return null;
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
        /// <param name="writeMessage">Logging callback.</param>
        public static void WriteToDatabase(SQLite db, string simulationName, DateTime date,
            Dictionary<string, object> parameterValues,
            string wetDrySoilReflectancePath, string sailVersionString, string sensorTypeString,
            CanopyOptics canopyOptics, CanopyStateVariables canopyStateVariables,
            CanopyBRF canopyBRF, SpectralResamplingResult spectralResamplingResult,
            Action<LogLevel, string> writeMessage)
        {
            if (db == null || canopyOptics?.Wavelength == null)
            {
                writeMessage(LogLevel.Error, "ProsailModel: WriteToDatabase skipped due to null dbConnection or canopy properties.");
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

            if (Rdot == null || Rsot == null || Rddt == null || Rsdt == null ||
                FCover == null || Abs_dir == null || Abs_hem == null || Rsdstar == null || Rddstar == null)
            {
                writeMessage(LogLevel.Error, "ProsailModel: WriteToDatabase skipped due to one or more null canopy radiative property arrays.");
                throw new InvalidOperationException("ProsailModel: WriteToDatabase skipped due to one or more null canopy radiative property arrays.");
            }

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
                writeMessage(LogLevel.Error, "ProsailModel: Array length mismatch in WriteToDatabase");
                throw new InvalidOperationException("ProsailModel: Array length mismatch in WriteToDatabase.");
            }

            try
            {
                db.ExecuteNonQuery("BEGIN TRANSACTION;");
                string dateStr = date.ToString("yyyy-MM-dd");

                // Parameters INSERT
                string paramSql = $@"
            INSERT OR REPLACE INTO Parameters (
                SimulationName, Date, N, CAB, CAR, EWT, LMA, ANT, BROWN, PROT, CBC, Alpha,
                LAI, HotSpot, TypeLidf, LIDFa, LIDFb, FractionBrown, Dissociation, CrownCover, TreeShape,
                WetDrySoilReflectancePath, Psoil, SunZenithAngle, ObserverZenithAngle, RelativeAzimuthAngle, SailVersion,
                SensorType
            ) VALUES (
                '{simulationName}', '{dateStr}', {parameterValues["N"]}, {parameterValues["CAB"]},
                {parameterValues["CAR"]}, {parameterValues["EWT"]}, {parameterValues["LMA"]},
                {parameterValues["ANT"]}, {parameterValues["BROWN"]}, {parameterValues["PROT"]},
                {parameterValues["CBC"]}, {parameterValues["Alpha"]},
                {parameterValues["LAI"]}, {parameterValues["HotSpot"]}, {parameterValues["TypeLidf"]},
                {parameterValues["LIDFa"]}, {parameterValues["LIDFb"]}, {parameterValues["FractionBrown"]},
                {parameterValues["Dissociation"]}, {parameterValues["CrownCover"]}, {parameterValues["TreeShape"]},
                '{wetDrySoilReflectancePath?.Replace("'", "''") ?? ""}', {parameterValues["Psoil"]},
                {parameterValues["SunZenithAngle"]}, {parameterValues["ObserverZenithAngle"]},
                {parameterValues["RelativeAzimuthAngle"]}, '{sailVersionString}', '{sensorTypeString}'
            )";
                db.ExecuteNonQuery(paramSql);

                // CanopyOpticalVariable INSERT
                StringBuilder spectraSql = new StringBuilder("INSERT OR REPLACE INTO CanopyOpticalVariable (SimulationName, Date, Wavelength, Rdot, Rsot, Rddt, Rsdt, fCover, Abs_dir, Abs_hem, Rsdstar, Rddstar) VALUES ");
                bool firstSpectra = true;
                for (int i = 0; i < usedWavelength.Length; i++)
                {
                    if (!firstSpectra) spectraSql.Append(",");
                    spectraSql.Append($"('{simulationName}', '{dateStr}', {usedWavelength[i]}, {Rdot[i]}, {Rsot[i]}, {Rddt[i]}, {Rsdt[i]}, {FCover[i]}," +
                        $"{Abs_dir[i]}, {Abs_hem[i]}, {Rsdstar[i]}, {Rddstar[i]})");
                    firstSpectra = false;
                }
                if (!firstSpectra)
                {
                    spectraSql.Append(";");
                    writeMessage(LogLevel.Debug, "ProsailModel: Executing CanopyOpticalVariable INSERT.");
                    db.ExecuteNonQuery(spectraSql.ToString());
                }

                // CanopyStateVariable INSERT
                string stateSql = $@"
            INSERT OR REPLACE INTO CanopyStateVariable (
                SimulationName, Date, fAPAR, fCover, albedo
            ) VALUES (
                '{simulationName}', '{dateStr}', {canopyStateVariables.fAPAR}, {canopyStateVariables.fcover}, {canopyStateVariables.albedo}
            )";
                db.ExecuteNonQuery(stateSql);

                // ReflectanceResampledToSensor INSERT
                if (spectralResamplingResult != null && spectralResamplingResult.Reflectance != null)
                {
                    try
                    {
                        StringBuilder resampledSql = new StringBuilder("INSERT OR REPLACE INTO ReflectanceResampledToSensor (SimulationName, Date, Wavelength, BandName, Reflectance) VALUES ");
                        bool firstRow = true;
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
                            {
                                if (!firstRow) resampledSql.Append(",");
                                string escapedBandName = bandName.Replace("'", "''");
                                resampledSql.Append($"('{simulationName}', '{dateStr}', {wavelength}, '{escapedBandName}', {reflectance})");
                                firstRow = false;
                            }
                        }
                        if (!firstRow)
                        {
                            resampledSql.Append(";");
                            db.ExecuteNonQuery(resampledSql.ToString());
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
                if (canopyBRF.Wavelength != null && canopyBRF.BRF != null)
                {
                    StringBuilder brfSql = new StringBuilder("INSERT OR REPLACE INTO CanopyBRF (SimulationName, Date, Wavelength, BRF) VALUES ");
                    bool firstBRF = true;
                    for (int i = 0; i < canopyBRF.Wavelength.Count; i++)
                    {
                        if (!firstBRF) brfSql.Append(",");
                        brfSql.Append($"('{simulationName}', '{dateStr}', {canopyBRF.Wavelength[i]}, {canopyBRF.BRF[i]})");
                        firstBRF = false;
                    }
                    if (!firstBRF)
                    {
                        brfSql.Append(";");
                        db.ExecuteNonQuery(brfSql.ToString());
                        writeMessage(LogLevel.Debug, $"ProsailModel: Successfully wrote CanopyBRF data to database for {dateStr}");
                    }
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

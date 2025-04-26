using System;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using Models.Prospect;
using Models.Sail;

namespace Models.Prosail
{
    /// <summary>
    /// Integrates PROSPECT leaf optical model with SAIL canopy reflectance model to simulate canopy reflectance
    /// </summary>
    /// <remarks>
    /// PROSAIL combines PROSPECT (Jacquemoud and Baret, 1990) with SAIL (Verhoef, 1984) models.
    /// Reference: Jacquemoud, S., et al. (2009). PROSPECT + SAIL models: A review of use for vegetation characterization.
    /// </remarks>
    public static class ProsailCore
    {
       /// <summary>
        /// Runs the PROSAIL model (PROSPECT + SAIL canopy reflectance model)
        /// </summary>
        /// <param name="prospectParams">PROSPECT input parameters</param>
        /// <param name="sailParams">SAIL canopy parameters</param>
        /// <param name="specSensor">Spectral properties (optional, uses default if null)</param>
        /// <param name="leafBrown">Optional leaf optical properties for brown vegetation (4SAIL2 only)</param>
        /// <returns>Canopy reflectance factors</returns>
        /// <exception cref="ArgumentException">Thrown if inputs are invalid</exception>
        public static ProsailResult RunProsail(
            ProspectParameters prospectParams,
            SailParameters sailParams,
            ProspectCore.SpectralConstants? specSensor = null,
            LeafOptics? leafBrown = null)
        {
            if (prospectParams.N < 1.0)
                throw new ArgumentException("Leaf structure parameter N must be >= 1.0");
            if (sailParams.LAI < 0)
                throw new ArgumentException("LAI must be non-negative");
            if (sailParams.TTS < 0 || sailParams.TTS > 90 || sailParams.TTO < 0 || sailParams.TTO > 90)
                throw new ArgumentException("Solar and observer zenith angles must be between 0 and 90 degrees");
            if (sailParams.FractionBrown < 0 || sailParams.FractionBrown > 1)
                throw new ArgumentException("FractionBrown must be between 0 and 1");
            if (sailParams.SoilReflectance == null)
                throw new ArgumentException("Soil reflectance cannot be null");

            // Run PROSPECT to get leaf optical properties
            var (reflectance, transmittance) = ProspectCore.Run(
                specSensor,
                prospectParams.N,
                prospectParams.CHL,
                prospectParams.CAR,
                prospectParams.EWT,
                prospectParams.LMA,
                prospectParams.ANT,
                prospectParams.BROWN,
                prospectParams.PROT,
                prospectParams.CBC,
                prospectParams.Alpha);

            var leafGreen = new LeafOptics
            {
                Reflectance = reflectance,
                Transmittance = transmittance,
                Wavelengths = specSensor?.Wavelengths ?? ProspectCore.LoadLocalSpectralData().Wavelengths
            };

            // Run appropriate SAIL version
            return sailParams.SAILVersion == SailVersion.FourSAIL2
                ? SailModel.FourSAIL2(leafGreen, sailParams, leafBrown)
                : SailModel.FourSAIL(leafGreen, sailParams);
        }
    }
}
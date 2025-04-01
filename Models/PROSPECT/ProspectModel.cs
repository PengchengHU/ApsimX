using System;
using System.Linq;
using Models.Core;
using Models.Prospect;
using Models.PMF;
using Models.PMF.Phen;
using Models.PMF.Organs;
using Models.Climate;
using System.Reflection;
using System.Collections.Generic;
using System.Text.Json.Serialization; // Modern .NET serialization
using System.Numerics;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace Models.Prospect
{
    /// <summary>
    /// APSIMX中的PROSPECT模型组件
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Plant))] // allowed to put under Plant
    public class ProspectModel : Model
    {
        [Link]
        private ISummary Summary = null;

        /// <summary>Linked parameter converter instance</summary>
        [Link] private ExpressionProspectConverter converter = null;

        /// <summary>Output wavelength array (nm)</summary>
        [System.Text.Json.Serialization.JsonIgnore] // Modern .NET serialization
        // [Newtonsoft.Json.JsonIgnore] // Alternative for Newtonsoft.Json
        public double[] Wavelengths { get; private set; }

        /// <summary>Output reflectance spectrum (0-1)</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        // [Newtonsoft.Json.JsonIgnore] // Alternative for Newtonsoft.Json
        public double[] Reflectance { get; private set; }

        /// <summary>Output transmittance spectrum (0-1)</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        // [Newtonsoft.Json.JsonIgnore] // Alternative for Newtonsoft.Json
        public double[] Transmittance { get; private set; }

        /// <summary>
        /// Daily simulation event handler
        /// </summary>
        [EventSubscribe("DoDailyInitialisation")]
        private void RunProspect(object sender, EventArgs e)
        {
            try
            {
                // 获取动态计算的参数
                double n = converter.CalculateStructureN();
                double chl = converter.CalculateChlorophyll();
                double ewt = converter.CalculateEWT();
                double car = converter.CalculateCarotenoids();
                double lma = converter.CalculateLMA();
                double alpha = converter.CalculateAlpha();

                // Input validation
                ValidateInputs(chl, ewt, n, car, lma, alpha);

                try
                {
                    var (refl, tran) = ProspectCore.Run(N: n, CHL: chl, CAR: car, EWT: ewt, LMA: lma, alpha: alpha);
                    Wavelengths = refl.Count > 0 ? Enumerable.Range(400, refl.Count).Select(i => (double)i).ToArray() : Array.Empty<double>();
                    Reflectance = refl.ToArray();
                    Transmittance = tran.ToArray();
                }
                catch (Exception ex)
                {
                    Summary?.WriteMessage(this, $"PROSPECT model failed: {ex.Message}", MessageType.Error);
                    // Set default values to avoid null references if simulation continues
                    Wavelengths = Array.Empty<double>();
                    Reflectance = Array.Empty<double>();
                    Transmittance = Array.Empty<double>();
                    throw; // Re-throw to halt simulation if desired
                }
            }
            catch (Exception ex)
            {
                // Log error or handle appropriately
                Console.WriteLine($"PROSPECT Model Error: {ex.Message}");

                // Reset outputs to prevent null reference issues
                Wavelengths = Array.Empty<double>();
                Reflectance = Array.Empty<double>();
                Transmittance = Array.Empty<double>();
            }
        }

        private void ValidateInputs(double chl, double ewt, double n, double car, double lma, double alpha)
        {
            if (chl < 0 || ewt < 0 || n <= 0 || car < 0 || lma < 0 || alpha < 0)
            {
                throw new ArgumentException("Invalid input parameters for PROSPECT model");
            }
        }
    }
}
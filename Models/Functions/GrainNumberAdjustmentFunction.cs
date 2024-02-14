using Models.Utilities;
using System;
using Models.Core;
using Models.PMF;
using Models.PMF.Phen;
using Models.PMF.Organs;
using Models.Climate;
using System.Collections.Generic;
using System.Linq;
using DocumentFormat.OpenXml.Drawing.Charts;
using DocumentFormat.OpenXml.Spreadsheet;
using static Models.Core.ScriptCompiler;
using System.IO;
using Models.DCAPST.Environment;

namespace Models.Functions
{
    /// <summary> Model the effects of frost and heat stress on grain number. </summary>
    [Serializable]
    [Description("Calculate the stress factors of frost and heat events to adjust wheat grain number.")]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Plant))]
    public class GrainNumberAdjustmentFunction : Model
    {
        //[Link]
        //Clock Clock;
        [Link]
        Weather Weather = null;
        [Link]
        Zone zone = null;
        [Link]
        Plant Plant = null;
        [Link] 
        HourlySinPpAdjusted hourlyTemperature = null;

        // Internal variables
        private string CropType;
        private double lowestSens;
        private double highestSens;

        // Define parameters

        /// <summary>Frost damage</summary>
        [Separator("Frost damage")]
        // <summary>Temperature thereshold</summary>
        [Description("Threshold temperature for frost damage")]
        public double frostTempThreshold { get; set; }

        /// <summary>Cultivar-specific factor of frost tolerance</summary>
        [Description("Cultivar-specific factor of frost tolerance")]
        public double frostToleranceFactor { get; set; }

        /// <summary>Inflection point of preflowering stage for frost sensitivity</summary>
        [Description("Preflowering inflection point of sensitivity for frost damage")]
        public double frostInflectionStagePriorFlower { get; set; }

        /// <summary>Frost sensitivity of preflowering inflection point</summary>
        [Description("Frost sensitivity of preflowering inflection point")]
        public double frostInflectionStagePriorFlowerSens { get; set; }

        /// <summary>Inflection point of postflowering stage for frost sensitivity</summary>
        [Description("Postflowering inflection point of sensitivity for frost damage")]
        public double frostInflectionStageAfterFlower { get; set; }

        /// <summary>Frost sensitivity of postflowering inflection point</summary>
        [Description("Frost sensitivity of postflowering inflection point")]
        public double frostInflectionStageAfterFlowerSens { get; set; }


        /// <summary>Heat damage</summary>
        [Separator("Heat damage")]
        // <summary>Threshold temperature for heat damage</summary>
        [Description("Threshold temperature for heat damage")]
        public double heatTempThreshold { get; set; }

        /// <summary>Cultivar-specific factor of heat tolerance</summary>
        [Description("Cultivar-specific factor of heat tolerance")]
        public double heatToleranceFactor { get; set; }

        /// <summary>Inflection point of preflowering stage for heat sensitivity</summary>
        [Description("Preflowering inflection point of sensitivity for heat damage")]
        public double heatInflectionStagePriorFlower { get; set; }

        /// <summary>Heat sensitivity of preflowering inflection point</summary>
        [Description("Heat sensitivity of preflowering inflection point")]
        public double heatInflectionStagePriorFlowerSens { get; set; }

        /// <summary>Inflection point of postflowering stage for heat sensitivity</summary>
        [Description("Postflowering inflection point of sensitivity for heat damage")]
        public double heatInflectionStageAfterFlower { get; set; }

        /// <summary>Heat sensitivity of postflowering inflection point</summary>
        [Description("Heat sensitivity of postflowering inflection point")]
        public double heatInflectionStageAfterFlowerSens { get; set; }

        // Output variables

        /// <summary>Daily multiplier of frost damage.</summary>
        public double FrostSurv { get; set; }

        /// <summary>Daily sensitivity of frost stress.</summary>
        public double FrostSens { get; set; }

        /// <summary>Daily multiplier of heat damage.</summary>
        public double HeatSurv { get; set; }

        /// <summary>Daily sensitivity of heat stress.</summary>
        public double HeatSens { get; set; }

        /// <summary>Daily multiplier of frost and heat combined damage.</summary>
        public double FrostHeatCombinedSurv { get; set; }

        /// <summary>Final multiplier of frost and heat damage.</summary>
        public double FinalStressMultiplier { get; set; }

        /// <summary>Final multiplier of frost damage.</summary>
        public double FinalFrostMultiplier { get; set; }

        /// <summary>Final multiplier of heat damage.</summary>
        public double FinalHeatMultiplier { get; set; }

        /// <summary>Frost- and heat-limiated yield.</summary>
        /// [Units("g/m2")]
        public double FrostHeatYield { get; set; }


        [EventSubscribe("Sowing")]
        private void OnDoSowing(object sender, EventArgs e)
        {
            // initialize
            CropType = Plant.PlantType;
            lowestSens = 0;
            highestSens = 1;

            FinalStressMultiplier = 1.0;
            FinalFrostMultiplier = 1.0;
            FinalHeatMultiplier = 1.0;
            FrostHeatYield = 0;

            FrostSurv = 1.0;
            FrostSens = 0;
            HeatSurv = 1.0;
            HeatSens = 0;
            FrostHeatCombinedSurv = 1.0;
        }

        ///// <summary>Caculate hourly temperature from daily minT and maxT using a cosine function.</summary>
        ///// <returns>list of 24 temperature estimates.</returns>
        //public List<double> HourlyAirTemperature(double minT, double maxT)
        //{
        //    if (minT >= maxT)
        //    {
        //        throw new Exception("maxT must be larger than minT!");
        //    }

        //    List<double> temp = new List<Double>();
        //    List<int> hours = Enumerable.Range(1, 24).ToList();
        //    foreach (int hour in hours)
        //    {
        //        temp.Add((minT + maxT) / 2 + ((maxT - minT) * (Math.Cos(Math.PI * (hour - 14) / 12))) / 2);
        //    }
        //    return temp;
        //}

        /// <summary>Calculate the day time mean temperature from sunrise to sunset.</summary>
        public double DaytimeMeanTemp(List<double> hourlyAirTemp, double sunriseHour, double sunsetHour)
        {
            int sunriseHour_t = (int)(Math.Floor(sunriseHour));
            int sunsetHour_t = (int)(Math.Floor(sunsetHour));
            int daytimeHours = sunsetHour_t - sunriseHour_t + 1;
            double avg = hourlyAirTemp.Skip(sunriseHour_t - 1).Take(daytimeHours).Average();
            return (avg);
        }

        /// <summary>Caculate the night time mean temperature.</summary>
        public double NighttimeMeanTemp(List<double> hourlyAirTemp, double sunriseHour, double sunsetHour)
        {
            int sunriseHour_t = (int)(Math.Floor(sunriseHour)) - 1;
            int sunsetHour_t = (int)(Math.Floor(sunsetHour)) - 1;
            List<int> daytimeHours = Enumerable.Range(sunriseHour_t, sunsetHour_t).ToList();
            foreach (int hour in daytimeHours.OrderByDescending(v => v))
            {
                hourlyAirTemp.RemoveAt(hour);
            }
            return (hourlyAirTemp.Average());
        }

        /// <summary>Caculate daily multiplier of heat stress using logistic function.</summary>
        public double FrostSurvFunLogistic(double temperature, double frostTempThreshold, double frostToleranceFactor)
        {
            double ratio = 1.0;

            if (temperature > frostTempThreshold)
            {
                ratio = 1 / (1 + frostToleranceFactor * Math.Exp(0.5 * (frostTempThreshold - temperature)));
            }
            return ratio;
        }

        /// <summary>
        /// Caculate daily multiplier of frost stress using piecewise linear function.
        /// The K (frostToleranceFactor argument) value of linear function can be used as a cultivar-specific frost tolerant factor.
        /// Note that the mortality threshold temperature (100% killed) is not specificed, which will be determined by K and frost threshold temperature.
        /// </summary>
        public double FrostSurvFunLinear(double temperature, double frostTempThreshold, double frostToleranceFactor)
        {
            double ratio = 0.0;

            if (temperature > frostTempThreshold)
            {
                ratio = 1.0d;
            }
            else if (temperature <= frostTempThreshold)
            {
                ratio = frostToleranceFactor * (temperature - frostTempThreshold) + 1;
            }

            if (ratio < 0)
            {
                ratio = 0;
            }
            return ratio;
        }

        /// <summary>Caculate daily multiplier of heat stress using logistic function.</summary>
        public double HeatSurvFunLogistic(double temperature, double heatTempThreshold, double heatToleranceFactor)
        {
            double ratio = 1.0;

            if (temperature > heatTempThreshold)
            {
                ratio = 1 / (1 + heatToleranceFactor * Math.Exp(0.5 * (temperature - heatTempThreshold)));
            }
            return ratio;
        }

        /// <summary>Caculate daily multiplier of heat stress using piecewise linear function.</summary>
        public double HeatSurvFunLinear(double temperature, double heatTempThreshold, double heatToleranceFactor)
        {
            double ratio = 1.0;
            if (temperature <= heatTempThreshold)
            {
                ratio = 1.0;
            }
            else if (temperature > heatTempThreshold)
            {
                ratio = heatToleranceFactor * (heatTempThreshold - temperature) + 1;
            }
            
            if (ratio < 0)
            {
                ratio = 0;
            }            
            return ratio;
        }

        /// <summary>
        /// Caculate sensitivity of frost and heat damage around flowering (presented by growth stage) using the double logistic function.
        /// The sensitivity ranges from 0 to 1, O means insensitive to stresses, 1 means the most sensitiv to stresses.
        /// The sensititve period of frost and heat damage for wheat starts from Zadok stage of 31 (terminal spikelet) to 79 (Start of grain filling). 
        /// Both the frost and heat damage use the same function but may with different parameter values.
        /// </summary>
        public double SensitivityFun(double growthStage, double lowestSens, double highestSens, 
            double inflectionStagePriorFlower, double inflectionStagePriorFlowerSens,
            double inflectionStageAfterFlower, double inflectionStageAfterFlowerSens)
        {
            if (inflectionStagePriorFlowerSens > highestSens | inflectionStageAfterFlowerSens > highestSens | inflectionStagePriorFlowerSens < lowestSens | 
                inflectionStageAfterFlowerSens < lowestSens | inflectionStagePriorFlower > inflectionStageAfterFlower) 
            {
                throw new Exception("Inflection points of growth stage not in range!");
            }

            double t1 = 1 / (1 + Math.Exp(-inflectionStagePriorFlowerSens * (growthStage - inflectionStagePriorFlower)));
            double t2 = 1 / (1 + Math.Exp(inflectionStageAfterFlowerSens * (growthStage - inflectionStageAfterFlower)));

            double ratio = lowestSens + (highestSens - lowestSens) * ((t1 + t2) - 1);
            return ratio;
        }

        /// <summary>
        /// Caculate the fraction of individuals reaching the each development stage, which follows a normal distribution.
        /// Return the frequency of individuals in each stage.
        /// </summary>
        public double PopulationFun(double growthStage, double stageMean, double stageStandDeviaton) 
        {       
            double frequency = (Math.Exp(-0.5 * Math.Pow((growthStage - stageMean) / stageStandDeviaton, 2))) /
                (stageStandDeviaton * Math.Sqrt(2 * Math.PI));

            return frequency;
        }

        /// <summary>Does the calculations of multiplers and sensitivities of frost and heat stresses.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoManagementCalculations")]
        private void OnDoManagementCalculations(object sender, EventArgs e)
        {
            if (!Plant.IsAlive)
            {
                return;
            }

            Phenology phen = (Phenology)zone.Get("[" + CropType + "].Phenology");
            ReproductiveOrgan grains = (ReproductiveOrgan)zone.Get("[" + CropType + "].Grain");
            double stageToday = phen.Stage;
            double Hsrise = Weather.CalculateSunRise();
            double Hsset = Weather.CalculateSunSet();

            // calculate hourly temperature
            // List<double> hourlyT = HourlyAirTemperature(Weather.MinT, Weather.MaxT);
            List<double> hourlyT = hourlyTemperature.SubDailyValues();

            // calucate daytime mean temperature
            double daytimeMeanT = DaytimeMeanTemp(hourlyT, Hsrise, Hsset);

            // calculate night-time mean temperature
            double nighttimeMeanT = NighttimeMeanTemp(hourlyT, Hsrise, Hsset);       

            // frost survival
            FrostSurv = FrostSurvFunLogistic(nighttimeMeanT, frostTempThreshold, frostToleranceFactor);

            // frost sensitivity
            if (stageToday < 31 | stageToday > 79)
            {
                FrostSens = 0;
            }
            else
            {
                FrostSens = SensitivityFun(stageToday, lowestSens, highestSens,
                    frostInflectionStagePriorFlower, frostInflectionStagePriorFlowerSens, 
                    frostInflectionStageAfterFlower, frostInflectionStageAfterFlowerSens);
            }
            
            // heat survival
            HeatSurv = HeatSurvFunLogistic(daytimeMeanT, heatTempThreshold, heatToleranceFactor);

            // heat sensitivity
            if (stageToday < 31 | stageToday > 79)
            {
                HeatSens = 0;
            }
            else
            {
                HeatSens = SensitivityFun(stageToday, lowestSens, highestSens, 
                    heatInflectionStagePriorFlower, heatInflectionStagePriorFlowerSens, 
                    heatInflectionStageAfterFlower, heatInflectionStageAfterFlowerSens);
            }

            // combined stress survival
            FrostHeatCombinedSurv = (1 - (1 - FrostSurv) * FrostSens)
                        * (1 - (1 - HeatSurv) * HeatSens);

            // final multiplier
            FinalStressMultiplier = FinalStressMultiplier * FrostHeatCombinedSurv;
            FinalHeatMultiplier = FinalHeatMultiplier * (1 - (1 - HeatSurv) * HeatSens);
            FinalFrostMultiplier = FinalFrostMultiplier * (1 - (1 - FrostSurv) * FrostSens);

            // frost and heat limited grain number
            grains.Number = grains.Number * FinalStressMultiplier;
        }
    }
}

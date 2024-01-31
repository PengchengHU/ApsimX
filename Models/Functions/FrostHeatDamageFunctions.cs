using Models.Utilities;
using System;
using System.Linq;
using Models.Core;
using Models.PMF;
using Models.PMF.Phen;
using Models.PMF.Organs;
using Models.Climate;
using System.Collections;
using System.Collections.Generic;
using Models.Interfaces;
using APSIM.Shared.Documentation;

namespace Models.Functions
{
    /// <summary> Damage functions of frost and heat stress. </summary>
    [Serializable]
    [Description("Damage functions of frost and heat stresses are provided for barley, canola and wheat by the GRDC FAHMA project.")]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Plant))]
    public class FrostHeatDamageFunctions : Model
    {
        //[Link]
        //Clock Clock;
        [Link]
        Weather Weather = null;
        [Link]
        Zone zone = null;
        [Link]
        Plant Plant = null;

        // Internal variables
        private int iDay;
        private int flowerIdx;
        private double flowerCumTT;
        //private double yield_x;

        private string CropType;

        private double[] minT; // default to 0
        private double[] maxT;
        private double[] cumTT;
        private double[] zadok;

        // Define parameters

        /// <summary>Frost damage</summary>
        [Separator("Frost damage")]
        // <summary>Lower thereshold</summary>
        [Description("Lower threshold of temperature for frost damage")] 
        public double FrostSurvLowT { get; set; }

        /// <summary>Upper threshold damage</summary>
        [Description("Multiplier of frost damage for lower threshold of temperature")]
        public double FrostSurvLowR { get; set; }

        /// <summary>Multiplier of lower threshold</summary>
        [Description("Upper threshold of temperature for frost damage")]
        public double FrostSurvUpT { get; set; }

        /// <summary>Heat damage</summary>
        [Description("Multiplier of frost damage for upper threshold of temperature")]
        public double FrostSurvUpR { get; set; }


        /// <summary>Sensitivity period of frost damage</summary>
        [Separator("Sensitivity period of frost damage")]
        // <summary>Multiplier of lower threshold</summary>
        [Description("Threshold #1 of thermal time for frost damage")]
        public double FrostSensTT1 { get; set; }

        /// <summary>Multiplier of lower threshold</summary>
        [Description("Threshold #2 of thermal time for frost damage")] 
        public double FrostSensTT2 { get; set; }

        /// <summary>Multiplier of lower threshold</summary>
        [Description("Threshold #3 of thermal time for frost damage")] 
        public double FrostSensTT3 { get; set; }

        /// <summary>Multiplier of lower threshold</summary>
        [Description("Threshold #4 of thermal time for frost damage")] 
        public double FrostSensTT4 { get; set; }


        /// <summary>Heat damage</summary>
        [Separator("Heat damage")]
        // <summary>Multiplier of lower threshold</summary>
        [Description("Lower threshold of temperature for heat damage")]
        public double HeatSurvLowT { get; set; }

        /// <summary>Multiplier of lower threshold</summary>
        [Description("Multiplier of heat damage for lower threshold of temperature")]
        public double HeatSurvLowR { get; set; }

        /// <summary>Multiplier of lower threshold</summary>
        [Description("Upper threshold of temperature for heat damage")]
        public double HeatSurvUpT { get; set; }

        /// <summary>Multiplier of lower threshold</summary>
        [Description("Multiplier of heat damage for lower threshold of temperature")]
        public double HeatSurvUpR { get; set; }


        /// <summary>Sensitivity period of heat damage</summary>
        [Separator("Sensitivity period of heat damage")]
        // <summary>Multiplier of lower threshold</summary>
        [Description("Threshold #1 of thermal time for heat damage")]
        public double HeatSensTT1 { get; set; }

        /// <summary>Multiplier of lower threshold</summary>
        [Description("Threshold #2 of thermal time for heat damage")]
        public double HeatSensTT2 { get; set; }

        /// <summary>Multiplier of lower threshold</summary>
        [Description("Threshold #3 of thermal time for heat damage")]
        public double HeatSensTT3 { get; set; }

        /// <summary>Multiplier of lower threshold</summary>
        [Description("Threshold #4 of thermal time for heat damage")]
        public double HeatSensTT4 { get; set; }


        // Output variables
        /// <summary>Cumulative thermal time around flowering.</summary>
        /// [Units("oC d")]
        public double[] TTAroundFL { get; set; }
        /// <summary>Daily multiplier of frost damage.</summary>
        public double[] FrostSurv { get; set; }
        /// <summary>Daily sensitivity of frost stress.</summary>
        public double[] FrostSens { get; set; }
        /// <summary>Daily multiplier of heat damage.</summary>
        public double[] HeatSurv { get; set; }
        /// <summary>Daily sensitivity of heat stress.</summary>
        public double[] HeatSens { get; set; }
        /// <summary>Daily multiplier of frost and heat combined damage.</summary>
        public double[] StressSurv { get; set; }
        /// <summary>Final multiplier of frost and heat damage.</summary>
        public double FinalStressMultiplier { get; set; }
        /// <summary>Frost- and heat-limiated yield.</summary>
        /// [Units("kg/ha")]
        public double FrostHeatYield { get; set; }

        /// <summary>Function for initialize arrary with the same value.</summary>
        public double[] FillArr(double[] arr, double value)
        {
            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = value;
            }
            return arr;
        }

        [EventSubscribe("Sowing")]
        private void OnDoSowing(object sender, EventArgs e)
        {
            iDay = 0;
            flowerIdx = 1;
            flowerCumTT = 0;

            CropType = Plant.PlantType;

            minT = new double[366]; // default to 0
            maxT = new double[366];
            cumTT = new double[366];
            zadok = new double[366];

            FinalStressMultiplier = 1.0;
            FrostHeatYield = 0;
            TTAroundFL = new double[366]; // default to 0
            FrostSurv = new double[366];
            FrostSens = new double[366];
            HeatSurv = new double[366];
            HeatSens = new double[366];
            StressSurv = new double[366];

            // initialize
            FillArr(FrostSurv, 1);
            FillArr(HeatSurv, 1);
        }

        /// <summary>Caculates daily multiplier of frost stress.</summary>
        private double FrostSurvFun(double t)
        {            
            double ratio = 0.0;

            if (t >= FrostSurvUpT)
            {
                ratio = 1.0d;
            }
            else if (t > FrostSurvLowT && t < FrostSurvUpT)
            {
                ratio =
                    t * ((FrostSurvUpR - FrostSurvLowR) / (FrostSurvUpT - FrostSurvLowT))
                    +
                        (FrostSurvLowR * FrostSurvUpT - FrostSurvLowT * FrostSurvUpR)
                        / (FrostSurvUpT - FrostSurvLowT);
            }
            else if (t <= FrostSurvLowT)
            {
                ratio = FrostSurvLowR;
            }
            return ratio;
        }

        /// <summary>Caculates daily sensitivity of frost stress.</summary>
        private double FrostSensFun(double ttAroundFL)
        {            
            double sens = 0.0;

            if (ttAroundFL <= FrostSensTT1)
            {
                sens = 0;
            }
            else if (ttAroundFL > FrostSensTT1 && ttAroundFL < FrostSensTT2)
            {
                sens =
                    ttAroundFL * ((1 - 0) / (FrostSensTT2 - FrostSensTT1))
                    + (0 * FrostSensTT2 - FrostSensTT1 * 1) / (FrostSensTT2 - FrostSensTT1);
            }
            else if (ttAroundFL >= FrostSensTT2 && ttAroundFL <= FrostSensTT3)
            {
                sens = 1.0;
            }
            else if (ttAroundFL > FrostSensTT3 && ttAroundFL < FrostSensTT4)
            {
                sens =
                    ttAroundFL * ((0 - 1) / (FrostSensTT4 - FrostSensTT3))
                    + (1 * FrostSensTT4 - FrostSensTT3 * 0) / (FrostSensTT4 - FrostSensTT3);
            }
            else if (ttAroundFL >= FrostSensTT4)
            {
                sens = 0;
            }
            return sens;
        }

        /// <summary>Caculates daily multiplier of heat stress.</summary>
        private double HeatSurvFun(double t)
        {
            double ratio = 1.0;

            if (t <= HeatSurvLowT)
            {
                ratio = 1.0;
            }
            else if (t > HeatSurvLowT && t < HeatSurvUpT)
            {
                ratio =
                    t * ((HeatSurvUpR - HeatSurvLowR) / (HeatSurvUpT - HeatSurvLowT))
                    +
                        (HeatSurvLowR * HeatSurvUpT - HeatSurvLowT * HeatSurvUpR)
                        / (HeatSurvUpT - HeatSurvLowT);
            }
            else if (t >= HeatSurvUpT)
            {
                ratio = HeatSurvUpR;
            }
            return ratio;
        }

        /// <summary>Caculates daily sensitivity of heat stress.</summary>
        private double HeatSensFun(double ttAroundFL)
        {
            double sens = 0;

            if (ttAroundFL <= HeatSensTT1)
            {
                sens = 0;
            }
            else if (ttAroundFL > HeatSensTT1 && ttAroundFL < HeatSensTT2)
            {
                sens =
                    ttAroundFL * ((1 - 0) / (HeatSensTT2 - HeatSensTT1))
                    + (0 * HeatSensTT2 - HeatSensTT1 * 1) / (HeatSensTT2 - HeatSensTT1);
            }
            else if (ttAroundFL >= HeatSensTT2 && ttAroundFL <= HeatSensTT3)
            {
                sens = 1.0;
            }
            else if (ttAroundFL > HeatSensTT3 && ttAroundFL < HeatSensTT4)
            {
                sens =
                    ttAroundFL * ((0 - 1) / (HeatSensTT4 - HeatSensTT3))
                    + (1 * HeatSensTT4 - HeatSensTT3 * 0) / (HeatSensTT4 - HeatSensTT3);
            }
            else if (ttAroundFL >= HeatSensTT4)
            {
                sens = 0;
            }
            return sens;
        }

        /// <summary>Get the index of the flowering date.</summary>
        private int FindArrIdx(double[] arr, double valueToSearch)
        {
            int idx = 0;
            for (var i = 0; i < arr.Length; i++)
            {
                if (arr[i] >= valueToSearch)
                {
                    idx = i;
                    break;
                }
            }
            return idx;
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
            ReproductiveOrgan organs = (ReproductiveOrgan)zone.Get("[" + CropType + "].Grain");
            double zadok_today;
            double stage_today;
            if (CropType == "Wheat" | CropType == "Barley")
            {
                zadok_today = (double)Plant.FindByPath("Phenology.Zadok.Stage").Value;
                if (zadok_today >= 5)
                {
                    minT[iDay] = Weather.MinT;
                    maxT[iDay] = Weather.MaxT;
                    cumTT[iDay] = phen.AccumulatedTT;
                    zadok[iDay] = zadok_today;
                    iDay = iDay + 1;
                }
            }
            else if (CropType == "Canola")
            {
                stage_today = phen.Stage;
                if (stage_today > 1)
                {
                    minT[iDay] = Weather.MinT;
                    maxT[iDay] = Weather.MaxT;
                    cumTT[iDay] = phen.AccumulatedTT;
                    zadok[iDay] = stage_today;
                    iDay = iDay + 1;
                }
            }

            if (Plant.IsReadyForHarvesting)
            {
                // cumulative TT around flowering
                if (CropType == "Barley")
                {
                    flowerIdx = FindArrIdx(zadok, 48.0);
                }
                else if (CropType == "Canola")
                {
                    flowerIdx = FindArrIdx(zadok, 6.0);
                }
                else if (CropType == "Wheat")
                {
                    flowerIdx = FindArrIdx(zadok, 65.0);

                }

                flowerCumTT = cumTT[flowerIdx];
                for (int i = 0; i < cumTT.Length; i++)
                {
                    TTAroundFL[i] = cumTT[i] - flowerCumTT;
                }

                // frost survival
                for (int i = 0; i < minT.Length; i++)
                {
                    FrostSurv[i] = FrostSurvFun(minT[i]);
                }

                // frost sensitivity
                for (int i = 0; i < TTAroundFL.Length; i++)
                {
                    FrostSens[i] = FrostSensFun(TTAroundFL[i]);
                }

                // heat survival
                for (int i = 0; i < maxT.Length; i++)
                {
                    HeatSurv[i] = HeatSurvFun(maxT[i]);
                }

                // heat sensitivity
                for (int i = 0; i < TTAroundFL.Length; i++)
                {
                    HeatSens[i] = HeatSensFun(TTAroundFL[i]);
                }

                // total stress survival
                for (int i = 0; i < TTAroundFL.Length; i++)
                {
                    StressSurv[i] =
                        (1 - (1 - FrostSurv[i]) * FrostSens[i])
                        * (1 - (1 - HeatSurv[i]) * HeatSens[i]);
                }

                // final multiplier
                for (int i = 0; i < StressSurv.Length; i++)
                {
                    FinalStressMultiplier = FinalStressMultiplier * StressSurv[i];
                }
                FrostHeatYield = organs.Wt * 10 * FinalStressMultiplier;
            }
        }
    }
}

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

namespace Models
{
    /// <summary> These damage functions of frost and heat stress. </summary>
    [Serializable]
    [Description("Damage functions of frost and heat stresses are provided for barley, canola and wheat by the GRDC FAHMA project. These functions are optimized using datasets from multiple eviroments and varieties for the three crops, respectively." +
        " These are not validated yet, please use them with cautious!!!")]
    [ValidParent(ParentType = typeof(Simulation))]
    [ViewName("UserInterface.Views.GridView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    public class FHDamageFunctions : Model
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

        private double[] minT_x; // default to 0
        private double[] maxT_x;
        private double[] cumTT_x;
        private double[] zadok_x;

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
        public double FHLyield { get; set; }

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

            minT_x = new double[366]; // default to 0
            maxT_x = new double[366];
            cumTT_x = new double[366];
            zadok_x = new double[366];

            FinalStressMultiplier = 1.0;
            FHLyield = 0;
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
        private double FrostSurvFun(double t, string cropType)
        {
            double frostSurvLowT;
            double frostSurvLowR;
            double frostSurvUpT;
            double frostSurvUpR;

            if (cropType == "Barley")
            {
                frostSurvLowT = -6.4715088;
                frostSurvLowR = 0.9437132;
                frostSurvUpT = 3.9290022;
                frostSurvUpR = 1;
            }
            else if (cropType == "Canola")
            {
                frostSurvLowT = -4.9;
                frostSurvLowR = 0.85;
                frostSurvUpT = 1.8;
                frostSurvUpR = 1;
            }
            else if (cropType == "Wheat")
            {
                frostSurvLowT = -5.5596890;
                frostSurvLowR = 0.8999303;
                frostSurvUpT = 3.9938798;
                frostSurvUpR = 1;
            }
            else
            {
                throw new ArgumentException("Damage functions are only for barley, canola and wheat:", nameof(cropType));
            }

            double ratio = 0.0;

            if (t >= frostSurvUpT)
            {
                ratio = 1.0d;
            }
            else if (t > frostSurvLowT && t < frostSurvUpT)
            {
                ratio =
                    t * ((frostSurvUpR - frostSurvLowR) / (frostSurvUpT - frostSurvLowT))
                    + (
                        (frostSurvLowR * frostSurvUpT - frostSurvLowT * frostSurvUpR)
                        / (frostSurvUpT - frostSurvLowT)
                    );
            }
            else if (t <= frostSurvLowT)
            {
                ratio = frostSurvLowR;
            }
            return ratio;
        }

        /// <summary>Caculates daily sensitivity of frost stress.</summary>
        private double FrostSensFun(double ttAroundFL, string cropType)
        {
            double frostSensTT1;
            double frostSensTT2;
            double frostSensTT3;
            double frostSensTT4;

            if (cropType == "Barley")
            {
                frostSensTT1 = -89.7869785;
                frostSensTT2 = -87.6759355;
                frostSensTT3 = 165.7052198;
                frostSensTT4 = 165.9875261;
            }
            else if (cropType == "Canola")
            {
                frostSensTT1 = 260;
                frostSensTT2 = 497;
                frostSensTT3 = 836;
                frostSensTT4 = 896;
            }
            else if (cropType == "Wheat")
            {
                frostSensTT1 = -166.8632213;
                frostSensTT2 = -166.6334562;
                frostSensTT3 = 144.7173302;
                frostSensTT4 = 144.9225310;
            }
            else
            {
                throw new ArgumentException("Damage functions are only for barley, canola and wheat:", nameof(cropType));
            }

            double sens = 0.0;

            if (ttAroundFL <= frostSensTT1)
            {
                sens = 0;
            }
            else if (ttAroundFL > frostSensTT1 && ttAroundFL < frostSensTT2)
            {
                sens =
                    ttAroundFL * ((1 - 0) / (frostSensTT2 - frostSensTT1))
                    + ((0 * frostSensTT2 - frostSensTT1 * 1) / (frostSensTT2 - frostSensTT1));
            }
            else if (ttAroundFL >= frostSensTT2 && ttAroundFL <= frostSensTT3)
            {
                sens = 1.0;
            }
            else if (ttAroundFL > frostSensTT3 && ttAroundFL < frostSensTT4)
            {
                sens =
                    ttAroundFL * ((0 - 1) / (frostSensTT4 - frostSensTT3))
                    + ((1 * frostSensTT4 - frostSensTT3 * 0) / (frostSensTT4 - frostSensTT3));
            }
            else if (ttAroundFL >= frostSensTT4)
            {
                sens = 0;
            }
            return sens;
        }

        /// <summary>Caculates daily multiplier of heat stress.</summary>
        private double HeatSurvFun(double t, string cropType)
        {
            double heatSurvLowT;
            double heatSurvLowR;
            double heatSurvUpT;
            double heatSurvUpR;

            if (cropType == "Barley")
            {
                heatSurvLowT = 31.8578159;
                heatSurvLowR = 1.0;
                heatSurvUpT = 37.6471652;
                heatSurvUpR = 0.1189986;
            }
            else if (cropType == "Canola")
            {
                heatSurvLowT = 31.5;
                heatSurvLowR = 1.0;
                heatSurvUpT = 35.5;
                heatSurvUpR = 0.54;
            }
            else if (cropType == "Wheat")
            {
                heatSurvLowT = 32.6192418;
                heatSurvLowR = 1.0;
                heatSurvUpT = 37.6124377;
                heatSurvUpR = 0.7117925;
            }
            else
            {
                throw new ArgumentException("Damage functions are only for barley, canola and wheat:", nameof(cropType));
            }
            double ratio = 1.0;

            if (t <= heatSurvLowT)
            {
                ratio = 1.0;
            }
            else if (t > heatSurvLowT && t < heatSurvUpT)
            {
                ratio =
                    t * ((heatSurvUpR - heatSurvLowR) / (heatSurvUpT - heatSurvLowT))
                    + (
                        (heatSurvLowR * heatSurvUpT - heatSurvLowT * heatSurvUpR)
                        / (heatSurvUpT - heatSurvLowT)
                    );
            }
            else if (t >= heatSurvUpT)
            {
                ratio = heatSurvUpR;
            }
            return ratio;
        }

        /// <summary>Caculates daily sensitivity of heat stress.</summary>
        private double HeatSensFun(double ttAroundFL, string cropType)
        {
            double heatSensTT1;
            double heatSensTT2;
            double heatSensTT3;
            double heatSensTT4;

            if (cropType == "Barley")
            {
                heatSensTT1 = -178.7831958;
                heatSensTT2 = -48.0749768;
                heatSensTT3 = 204.7985235;
                heatSensTT4 = 218.5135670;
            }
            else if (cropType == "Canola")
            {
                heatSensTT1 = -63;
                heatSensTT2 = 117;
                heatSensTT3 = 338;
                heatSensTT4 = 746;
            }
            else if (cropType == "Wheat")
            {
                heatSensTT1 = -92.4978649;
                heatSensTT2 = -13.3039786;
                heatSensTT3 = 216.9567100;
                heatSensTT4 = 242.4225241;
            }
            else
            {
                throw new ArgumentException("Damage functions are only for barley, canola and wheat:", nameof(cropType));
            }

            double sens = 0;

            if (ttAroundFL <= heatSensTT1)
            {
                sens = 0;
            }
            else if (ttAroundFL > heatSensTT1 && ttAroundFL < heatSensTT2)
            {
                sens =
                    ttAroundFL * ((1 - 0) / (heatSensTT2 - heatSensTT1))
                    + ((0 * heatSensTT2 - heatSensTT1 * 1) / (heatSensTT2 - heatSensTT1));
            }
            else if (ttAroundFL >= heatSensTT2 && ttAroundFL <= heatSensTT3)
            {
                sens = 1.0;
            }
            else if (ttAroundFL > heatSensTT3 && ttAroundFL < heatSensTT4)
            {
                sens =
                    ttAroundFL * ((0 - 1) / (heatSensTT4 - heatSensTT3))
                    + ((1 * heatSensTT4 - heatSensTT3 * 0) / (heatSensTT4 - heatSensTT3));
            }
            else if (ttAroundFL >= heatSensTT4)
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
        [EventSubscribe("DoManagement")]
        private void OnDoManagement(object sender, EventArgs e)
        {
            if (!(Plant.IsAlive && (CropType == "Barley" | CropType == "Canola" | CropType == "Wheat")))
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
                    minT_x[iDay] = Weather.MinT;
                    maxT_x[iDay] = Weather.MaxT;
                    cumTT_x[iDay] = phen.AccumulatedTT;
                    zadok_x[iDay] = zadok_today;
                    iDay = iDay + 1;
                }
            } 
            else if (CropType == "Canola")
            {
                stage_today = phen.Stage;
                if (stage_today > 1)
                {
                    minT_x[iDay] = Weather.MinT;
                    maxT_x[iDay] = Weather.MaxT;
                    cumTT_x[iDay] = phen.AccumulatedTT;
                    zadok_x[iDay] = stage_today;
                    iDay = iDay + 1;
                }
            }

            if (Plant.IsReadyForHarvesting)
            {
                // cumulative TT around flowering
                if (CropType == "Barley")
                {
                    flowerIdx = FindArrIdx(zadok_x, 48.0);
                }
                else if (CropType == "Canola")
                {
                    flowerIdx = FindArrIdx(zadok_x, 6.0);
                }
                else if (CropType == "Wheat")
                {
                    flowerIdx = FindArrIdx(zadok_x, 65.0);

                }

                flowerCumTT = cumTT_x[flowerIdx];
                for (int i = 0; i < cumTT_x.Length; i++)
                {
                    TTAroundFL[i] = cumTT_x[i] - flowerCumTT;
                }

                // frost survival
                for (int i = 0; i < minT_x.Length; i++)
                {
                    FrostSurv[i] = FrostSurvFun(minT_x[i], CropType);
                }

                // frost sensitivity
                for (int i = 0; i < TTAroundFL.Length; i++)
                {
                    FrostSens[i] = FrostSensFun(TTAroundFL[i], CropType);
                }

                // heat survival
                for (int i = 0; i < maxT_x.Length; i++)
                {
                    HeatSurv[i] = HeatSurvFun(maxT_x[i], CropType);
                }

                // heat sensitivity
                for (int i = 0; i < TTAroundFL.Length; i++)
                {
                    HeatSens[i] = HeatSensFun(TTAroundFL[i], CropType);
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
                FHLyield = organs.Wt * 10 * FinalStressMultiplier;
            }
        }
    }
}

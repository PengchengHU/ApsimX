using System;
using Models.Core;
using Models.PMF;
using Models.PMF.Phen;
using Models.PMF.Organs;
using Models.Climate;
using System.Linq;
using Models.PMF.Struct;
using MathNet.Numerics.Distributions;
using DocumentFormat.OpenXml.Vml.Office;
using DocumentFormat.OpenXml.Office2010.ExcelAc;
using System.Collections.Generic;
using static Models.Core.AutoDocumentation;
using DocumentFormat.OpenXml.ExtendedProperties;
using Models.PostSimulationTools;
using static ICSharpCode.SharpZipLib.Zip.ExtendedUnixData;

namespace Models.Functions
{
    /// <summary> Damage functions of frost and heat stress. </summary>
    [Serializable]
    [Description("New grian number model considering reproductive biology and the effects of frost and heat stresses on grain number.")]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Plant))]
    public class GrainNumberModel : Model
    {
        //[Link]
        //Clock Clock;
        //[Link]
        //Weather Weather = null;
        [Link]
        Zone zone = null;
        [Link]
        Plant Plant = null;
        //[Link]
        //Phenology Phenology = null;
        //[Link]
        //Structure Structure = null;

        // Define parameters

        /// <summary>Potential grain number</summary>
        [Separator("Potential grain number")]
        // <summary>Spikelet primordia plastochron</summary>
        [Description("Spikelet primordia plastochron")]
        // [Units("oCd/spikelet")]
        public double SpikeletPrimordiaPlastochron { get; set; }

        /// <summary>The number of floret primordia on proximal spikelets (third-fifth spikelet from the basal) </summary>
        [Description("The number of floret primordia on proximal spikelet")]
        // Normally 6-8
        public int FloretPrimordiaNoProximal { get; set; }

        /// <summary>The number of floret primordia on central spikelets (middle spikelets)</summary>
        [Description("The number of floret primordia on central spikelets")]
        // Normally 8-12
        public int FloretPrimordiaNoCentral { get; set; }

        /// <summary>The number of floret primordia on distal spikelets (third-fifth spikelet from the apical)</summary>
        [Description("The number of floret primordia on apical spikelet")]
        // Normally 6-8
        public int FloretPrimordiaNoApical { get; set; }

        /// <summary>Floret fertility accounting for floret survival to reach fertile floret stage</summary>
        [Description("Floret fertility rate")]
        // Normally < 0.5
        public double FloretFertilityRate { get; set; }

        /// <summary>Grain abortion accounting for fertilized florets obort</summary>
        [Description("Grain abortion rate")]
        // Normally > 0.9
        public double GrainAbortionRate { get; set; }


        /// <summary>Fertility of meiotic phase</summary>
        [Separator("Fertility of meiotic phase")]
        // <summary>Lambda of poisson distribution of dates of flag leaf fully emerged of shoots</summary>
        [Description("Lambda of Poission distribution of flag leaf fully emerged from shoots")]
        public double LambdaFlafLeaf { get; set; }

        /// <summary>Mean (peak) date of normal distribution of florets during the meiotic phase</summary>
        [Description("Mean date of normal distribution of meiosis dates of florets")]
        public double MeanFloretMeiosisDate { get; set; }

        /// <summary>Standard deviation in date of normal distribution of florets during the meiotic phase</summary>
        [Description("Standard deviation of normal distribution of meiosis dates of florets")]
        public double StddevFloretMeiosisDate { get; set; }

        /// <summary>Threshold temperature of heat stress</summary>
        [Description("Threshold temperature of heat stress")]
        public double HeatCriticalTemp { get; set; }

        /// <summary>Threshold temperature of frost stress</summary>
        [Description("Threshold temperature of frost stress")]
        public double FrostCriticalTemp { get; set; }

        /// <summary>Heat degree hours inducing 50% of sterility</summary>
        [Description("Heat degree hours to inducing 50% of sterility in logistic function")]
        public double HeatMeioticHalfLethalDegreeHours { get; set; }

        /// <summary>Kill factor to control shape of logistic curve</summary>
        [Description("Factor controlling shape of logistic curve of heat damage")]
        public double HeatKillFactor { get; set; }

        /// <summary>Frost degree hours inducing 50% of sterility</summary>
        [Description("Frost degree hours inducing 50% of sterility in logistic function")]
        public double FrostMeioticHalfLethalDegreeHours { get; set; }

        /// <summary>Kill factor to control shape of logistic curve of frost damage </summary>
        [Description("Factor controlling shape of logistic curve of frost damage")]
        public double FrostKillFactor { get; set; }

        /// <summary>Fertility of anthesis</summary>
        [Separator("Fertility of anthesis")]
        // <summary>Lambda of poisson distribution of heading dates of spikes</summary>
        [Description("Lambda of Poission distribution of heading dates of spikes")]
        public double LambdaHeading { get; set; }

        /// <summary>Mean (peak) date of normal distribution of flowering florets on spike</summary>
        [Description("Mean date of normal distribution of flowering florets on a spike")]
        public double MeanFloretFloweringDate { get; set; }

        /// <summary>Standard deviation of normal distribution of flowering florets on a spike</summary>
        [Description("Standard deviation of normal distribution of flowering spikelets on a spike")]
        public double StddevFloretFloweringDate { get; set; }

        /// <summary>Mean (peak) date of normal distribution of florets flowering at a time of a day</summary>
        [Description("Mean date of normal distribution of florets flowering at a time of a day")]
        public double MeanFloretFloweringTime { get; set; }

        /// <summary>Standard deviation of normal distribution of florets flowering at a time of a day</summary>
        [Description("Standard deviation of normal distribution of florets flowering at a time of a day")]
        public double StddevFloretFloweringTime { get; set; }


        // Internal variables
        /// <summary>Cumulative thermal time from floral iniation stage to termimal spikelet stage</summary>
        public double TTFITS { get; set; }

        /// <summary>Crop type</summary>
        string CropType;

        /// <summary>The start stage name in numeric values</summary>
        [Description("Numeric Stage to start accumulation")]
        Double StartStageName;

        /// <summary>The end stage name</summary>
        [Description("Numeric Stage to stop accumulation")]
        double EndStageName;

        /// <summary>Days after stage >= 6</summary>
        [Description("Days after numeric Stage >= 6")]
        int DaysAfterFlagLeafTip;

        /// <summary>Days after heading initiation >= 6</summary>
        [Description("Days after heading initiation")]
        int DaysAfterHeadingInitiation;


        // Output variables
        /// <summary>Number of spikelet primordia per spike</summary>
        public double SpikeletPrimordiaPerSpike { get; set; }

        /// <summary>Number of floret primordia per spike</summary>
        public double FloretPrimordiaPerSpike { get; set; }

        /// <summary>Number of fertile florets per spike</summary>
        public double FertileFloretsPerSpike { get; set; }

        /// <summary>Grain number per spike</summary>
        public double GrainsPerSpike { get; set; }

        /// <summary>Potential grain number per unit of area land</summary>
        [Units("#/m^2")]
        public double PotentialGrainNumberPerArea { get; set; }


        /// <summary>The probability density distribution of dates of flag leaf fully emerged of spikes</summary>
        public double SpikesFlagLeafEmergedFrequency { get; set; }

        /// <summary>The probability density distribution of meiosis dates of florets on a spike</summary>
        public double SpikeFloretsMeioticFrequency { get; set; }

        /// <summary>Relative frequency of florets in a field that reach meiotic phase after the appearence of flag leaf tip</summary>
        public double FloretsMeioticDateFrequency { get; set; }

        /// <summary>Daily floret fertilities in the population as affected by frost stress at meiosis</summary>
        public double DailyFrostFloretFertilityMeioticDate { get; set; }

        /// <summary>Caumulative floret fertilities in the population as affected by frost stress at meiosis</summary>
        public double CumulativeFrostFloretFertilityMeioticDate { get; set; }

        /// <summary>Daily floret fertilities in the population as affected by heat stress at meiosis</summary>
        public double DailyHeatFloretFertilityMeioticDate { get; set; }

        /// <summary>Cumulative floret fertilities in the population as affected by heat stress at meiosis</summary>
        public double CumulativeHeatFloretFertilityMeioticDate { get; set; }


        /// <summary>The probability density distribution of heading dates of spikes</summary>
        public double SpikesHeadingFrequency { get; set; }

        /// <summary>The probability density distribution of flowering florets on a spike</summary>
        public double SpikeFloretsFloweringFrequency { get; set; }

        /// <summary>The probability density distribution of florets flowering at time of a day</summary>
        public double FloretsFloweringFrequency { get; set; }


        // Functions 
        /// <summary>Poission distribution</summary>
        private double PoissionDistributor(double lambda, int k)
        {
            var Distr = new Poisson(lambda);
            double DistrToday = Distr.Probability(k);
            return DistrToday;
        }

        /// <summary>Normal distribution</summary>
        private double NormalDistributor(double mean, double stddev, double k)
        {
            var Distr = Normal.WithMeanStdDev(mean, stddev);
            double RateToday = Distr.Density(k);
            return RateToday;
        }

        /// <summary>Logistic distribution</summary>
        private double LogisticDistributor(double mean, double stddev, double k)
        {
            var Distr = Logistic.WithMeanStdDev(mean, stddev);
            double RateToday = Distr.Density(k);
            return RateToday;
        }

        /// <summary>Calculate heat degree hours</summary>
        private double HeatDegreeHours(double HeatCriticalTemp)
        {
            var HourlyTemp = new HourlySinPpAdjusted();
            List<double> HourlyTempList = HourlyTemp.SubDailyValues();
            double HeatDegree = 0;
            for (int Th = 0; Th <= 23; Th++)
            {
                if(HourlyTempList[Th] > HeatCriticalTemp)
                {
                    HeatDegree = HeatDegree + (HourlyTempList[Th] - HeatCriticalTemp);
                }
            }
            return (HeatDegree);
        }

        /// <summary>Calculate heat degree hours</summary>
        private double FrostDegreeHours(double FrostCriticalTemp)
        {
            var HourlyTemp = new HourlySinPpAdjusted();
            List<double> HourlyTempList = HourlyTemp.SubDailyValues();
            double FrostDegree = 0;
            for (int Th = 0; Th <= 23; Th++)
            {
                if (HourlyTempList[Th] < FrostCriticalTemp)
                {
                    FrostDegree = FrostDegree + (FrostCriticalTemp - HourlyTempList[Th]);
                }
            }
            return (FrostDegree);
        }

        /// <summary>The probability of florets to be fertile in response to heat or frost degree hours</summary>
        private double MeioticFloretFertility(double DegreeHours, double MeioticHalfLethalDegreeHours, double MeioticKillFactor)
        {
            double Fertility = 1 / (1 + Math.Exp(MeioticKillFactor * (DegreeHours - MeioticHalfLethalDegreeHours)));
            return (Fertility);
        }

        [EventSubscribe("Sowing")]
        private void OnDoSowing(object sender, EventArgs e)
        {
            // initialize
            //CropType = Plant.PlantType;
            CropType = "Wheat";
            StartStageName = 4; // VernalSaturation or floral initiation stage
            EndStageName = 5; // terminal spikelet stage
            DaysAfterFlagLeafTip = 0;

            TTFITS = 0;
            SpikeletPrimordiaPerSpike = 0;
            FloretPrimordiaPerSpike = 0;
            FertileFloretsPerSpike = 0;
            GrainsPerSpike = 0;
            PotentialGrainNumberPerArea = 0;

            SpikeFloretsMeioticFrequency = 0;
            SpikeFloretsMeioticFrequency = 0;
            FloretsMeioticDateFrequency = 0;
            DailyFrostFloretFertilityMeioticDate = 0;
            CumulativeFrostFloretFertilityMeioticDate = 0;
            DailyHeatFloretFertilityMeioticDate = 0;
            CumulativeHeatFloretFertilityMeioticDate = 0; 
        }
               

        /// <summary>Does the modelling of grain number and effects of frost and heat stresses.</summary>
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
            Structure stru = (Structure)zone.Get("[" + CropType + "].Structure");
            ReproductiveOrgan organs = (ReproductiveOrgan)zone.Get("[" + CropType + "].Grain");

            // Potential grain number
            // Cumulative thermal time from floral initiation to terminal spikelet stage
            if (CropType == "Wheat" | CropType == "wheat")
            {
                if (phen.Stage >= StartStageName && phen.Stage <= EndStageName) 
                { 
                    TTFITS += phen.thermalTime.Value(); 
                }
            }
            else
            {
                throw new Exception("Crop type not supported!");
            }

            // Calculate spikelet primordia floret primordia, fertile florets, and grains per spike from terminal spikelet
            if (phen.Stage >= EndStageName && phen.Stage < 8.1) 
            {
                SpikeletPrimordiaPerSpike = TTFITS / SpikeletPrimordiaPlastochron;
                FloretPrimordiaPerSpike = SpikeletPrimordiaPerSpike * (FloretPrimordiaNoApical + FloretPrimordiaNoCentral + FloretPrimordiaNoProximal) / 3;
                FertileFloretsPerSpike = FloretPrimordiaPerSpike * FloretFertilityRate;
                GrainsPerSpike = FertileFloretsPerSpike * (1 - GrainAbortionRate);
            }

            // Calculate potential grain number per unit of area
            PotentialGrainNumberPerArea = GrainsPerSpike * Plant.Population * (1 + stru.BranchNumber); // main shoot and tillers

            // Frost and heat damage on meiotic phase 
            // currently start from growth stage >= 6 to stage <= 7 (heading)
            if (phen.Stage >= 6 && phen.Stage <= 7)
            {
                // Probability of dates of flag leaf fully emerged(liguale appears) of shoots/ spikes
                SpikesFlagLeafEmergedFrequency = PoissionDistributor(LambdaFlafLeaf, DaysAfterFlagLeafTip);
                // Probability of meiosis dates of florets on a spike
                SpikeFloretsMeioticFrequency = NormalDistributor(MeanFloretMeiosisDate, StddevFloretMeiosisDate, DaysAfterFlagLeafTip);
                // Probability of meiosis dates of florets among the population in a field 
                FloretsMeioticDateFrequency = SpikesFlagLeafEmergedFrequency * SpikeFloretsMeioticFrequency;

                // Floret fertility in response to frost and heat stress on the meiosis date
                // Frost
                double DegreeHours = FrostDegreeHours(FrostCriticalTemp);
                double Fertility = MeioticFloretFertility(DegreeHours, FrostMeioticHalfLethalDegreeHours, FrostKillFactor);
                DailyFrostFloretFertilityMeioticDate = FloretsMeioticDateFrequency * Fertility;
                CumulativeFrostFloretFertilityMeioticDate += DailyFrostFloretFertilityMeioticDate;

                // Heat
                DegreeHours = HeatDegreeHours(HeatCriticalTemp);
                Fertility = MeioticFloretFertility(DegreeHours, HeatMeioticHalfLethalDegreeHours, HeatKillFactor);
                DailyHeatFloretFertilityMeioticDate = FloretsMeioticDateFrequency * Fertility;
                CumulativeHeatFloretFertilityMeioticDate += DailyHeatFloretFertilityMeioticDate;

                DaysAfterFlagLeafTip += 1;
            }
            
            // Frost and heat damage on flowering 
            if (phen.Stage > 7 && phen.Stage <= 9)
            {
                // Probability of heading dates of spikes
                SpikesHeadingFrequency = PoissionDistributor(LambdaHeading, DaysAfterHeadingInitiation);
                // Probability of flowering florets of a spike
                SpikeFloretsFloweringFrequency = NormalDistributor(MeanFloretFloweringDate, StddevFloretFloweringDate, DaysAfterHeadingInitiation);

                // Probability of florets that flower at the time t of a day
                List<double> sdts = new List<double>();

                for (int Th = 0; Th <= 23; Th++)
                { 
                
                
                
                
                }



                    DaysAfterHeadingInitiation += 1;

            }




            // Probability distrbution of meiotic, currently start from growth stage >= 6




        }
    }
}
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
using MathNet.Numerics;

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
        [Link]
        Weather Weather = null;
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
        public double LambdaFlagLeaf { get; set; }

        /// <summary>Mean (peak) date of normal distribution of florets during the meiotic phase</summary>
        [Description("Mean date of normal distribution of meiosis dates of florets")]
        public double FloretMeiosisDateMean { get; set; }

        /// <summary>Standard deviation in date of normal distribution of florets during the meiotic phase</summary>
        [Description("Standard deviation of normal distribution of meiosis dates of florets")]
        public double FloretMeiosisDateStddev { get; set; }

        /// <summary>Threshold temperature of heat stress</summary>
        [Description("Threshold temperature of heat stress")]
        public double HeatCriticalTemp { get; set; }

        /// <summary>Threshold temperature of frost stress</summary>
        [Description("Threshold temperature of frost stress")]
        public double FrostCriticalTemp { get; set; }

        /// <summary>Heat degree hours inducing 50% of sterility</summary>
        [Description("Heat degree hours to inducing 50% of sterility in logistic function")]
        public double MeiosisHalfKillHeatDegreeHours { get; set; }

        /// <summary>Kill factor to control shape of logistic curve</summary>
        [Description("Factor controlling shape of logistic curve of heat damage on meiotic florets")]
        public double MeiosisHeatKillFactor { get; set; }

        /// <summary>Frost degree hours inducing 50% of sterility</summary>
        [Description("Frost degree hours inducing 50% of sterility in logistic function")]
        public double MeiosisHalfKillFrostDegreeHours { get; set; }

        /// <summary>Kill factor to control shape of logistic curve of frost damage </summary>
        [Description("Factor controlling shape of logistic curve of frost damage on meiotic florets")]
        public double MeiosisFrostKillFactor { get; set; }

        /// <summary>Fertility of anthesis</summary>
        [Separator("Fertility of anthesis")]
        // <summary>Lambda of poisson distribution of heading dates of spikes</summary>
        [Description("Lambda of Poission distribution of heading dates of spikes")]
        public double LambdaSpikeHeading { get; set; }

        /// <summary>Mean (peak) date of normal distribution of flowering florets on spike</summary>
        [Description("Mean date of normal distribution of flowering florets on a spike")]
        public double FloretFloweringDateMean { get; set; }

        /// <summary>Standard deviation of normal distribution of flowering florets on a spike</summary>
        [Description("Standard deviation of normal distribution of flowering spikelets on a spike")]
        public double FloretFloweringDateStddev { get; set; }

        /// <summary>Mean (peak) date of normal distribution of florets flowering at a time of a day</summary>
        [Description("Mean date of normal distribution of florets flowering at a time of a day")]
        public double FloretFloweringTimeMean { get; set; }

        /// <summary>Standard deviation of normal distribution of florets flowering at a time of a day</summary>
        [Description("Standard deviation of normal distribution of florets flowering at a time of a day")]
        public double FloretFloweringTimeStddev { get; set; }


        /// <summary>High temperature inducing 50% of flowering florets sterility in logistic function</summary>
        [Description("High temperature inducing 50% of flowering florets sterility in logistic function")]
        // Normally > 30 oC
        public double FloweringHalfKillHeatTemp { get; set; }

        /// <summary>Kill factor to control shape of logistic curve of heat damage </summary>
        [Description("Factor controlling shape of logistic curve of heat damage on flowering florets")]
        public double FloweringHeatKillFactor { get; set; }

        /// <summary>Cold temperature inducing 50% of flowering florets sterility in logistic function</summary>
        [Description("Cold temperature inducing 50% of flowering florets sterility in logistic function")]
        // Normally < 2 oC
        public double FloweringFrostHalfKillTemp { get; set; }

        /// <summary>Kill factor to control shape of logistic curve of frost damage </summary>
        [Description("Factor controlling shape of logistic curve of frost damage on flowering florets")]
        public double FloweringFrostKillFactor { get; set; }


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
        [Units("grains/m^2")]
        public double PotentialGrainNumberPerArea { get; set; }

        /// <summary>Actual grain number per unit of area land</summary>
        [Units("grains/m^2")]
        public double ActualGrainNumberPerArea { get; set; }
        

        /// <summary>The probability of dates of flag leaf fully emerged of spikes</summary>
        public double DailyFlagLeafEmergedFreq { get; set; }

        /// <summary>The probability of meiosis dates of florets on a spike</summary>
        public double MeiosisFloretsOnSpikeFreq { get; set; }

        /// <summary>Relative frequency of florets in a population that reach meiotic phase after the appearence of flag leaf tip</summary>
        public double DailyMeiosisFloretFreq { get; set; }

        /// <summary>Daily floret fertilities in the population as affected by frost stress at meiosis</summary>
        public double DailyMeiosisFloretFertilityFrost { get; set; }

        /// <summary>Caumulative floret fertilities in the population as affected by frost stress at meiosis</summary>
        public double CumMeiosisFloretFertilityFrost { get; set; }

        /// <summary>Daily floret fertilities in the population as affected by heat stress at meiosis</summary>
        public double DailyMeiosisFloretFertilityHeat { get; set; }

        /// <summary>Cumulative floret fertilities in the population as affected by heat stress at meiosis</summary>
        public double CumMeiosisFloretFertilityHeat { get; set; }

        /// <summary>Cumulative floret fertilities in the population during meiotic phase</summary>
        public double CumMeiosisFloretFertility { get; set; }


        /// <summary>The probability of heading dates of spikes</summary>
        public double DailyHeadingSpikeFreq { get; set; }

        /// <summary>The probability of flowering florets on a spike</summary>
        public double FloweringFloretsOnSpikeFreq { get; set; }

        /// <summary>The probability of florets flowering at a time of the day</summary>
        public double[] DayHourFloweringFloretFreq { get; set; }

        /// <summary>The floret fertility after a heat event at a time of the day</summary>
        public double[] DayHourFloweringFloretFertilityHeat { get; set; }

        ///// <summary>The floret fertility after a frost event at a time of the day</summary>
        //public double[] DayHourFloweringFloretFertilityFrost { get; set; }

        /// <summary>The floret fertility after heat events at the day</summary>
        public double DailyFloweringFloretFertilityHeat { get; set; }

        /// <summary>The floret fertility after frost events at the day</summary>
        public double DailyFloweringFloretFertilityFrost { get; set; }

        /// <summary>The floret fertility after both frost and heat events at the day</summary>
        public double DailyFloweringFloretFertility { get; set; }

        /// <summary>Cumulative floret fertility during flowering</summary>
        public double CumFloweringFloretFertility { get; set; }

        /// <summary>Cumulative floret fertility during and meiotic and flowering phase</summary>
        public double CumFloretFertility { get; set; }
        

        // Functions 
        /// <summary>Poission distribution</summary>
        private double PoissionDistributor(double lambda, int k)
        {
            var Distr = new Poisson(lambda);
            double DistrToday = Distr.Probability(k);
            return DistrToday;
        }

        /// <summary>Gamma distribution</summary>
        private double GammaDistributor(double shape, double rate, double k)
        {
            var Distr = Gamma.WithShapeRate(shape, rate);
            double DistrToday = Distr.Density(k);
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
        private double MeioticFloretFertility(double DegreeHours, double MeioticHalfKillDegreeHours, double MeioticKillFactor)
        {
            double Fertility = 1 / (1 + Math.Exp(MeioticKillFactor * (DegreeHours - MeioticHalfKillDegreeHours)));
            return (Fertility);
        }

        /// <summary>The probability of flowering fertile florets in response to heat or frost degree hours</summary>
        private double FloweringFloretFertility(string StressType, double Temperature, double FloweringHalfKillTemperature, double FloweringKillFactor)
        {
            double Fertility = 1;
            if (StressType == "Heat")
            {
                Fertility = 1 / (1 + Math.Exp(FloweringKillFactor * (Temperature - FloweringHalfKillTemperature)));
            } 
            else if (StressType == "Frost")
            {
                Fertility = 1 / (1 + Math.Exp(FloweringKillFactor * (FloweringHalfKillTemperature - Temperature)));
            }
            else
            {
                throw new Exception("Set StressType to Heat or Frost");
            }

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

            MeiosisFloretsOnSpikeFreq = 0;
            MeiosisFloretsOnSpikeFreq = 0;
            DailyMeiosisFloretFreq = 0;
            DailyMeiosisFloretFertilityFrost = 0;
            CumMeiosisFloretFertilityFrost = 0;
            DailyMeiosisFloretFertilityHeat = 0;
            CumMeiosisFloretFertilityHeat = 0;

            DayHourFloweringFloretFreq = new double[24];
            DayHourFloweringFloretFertilityHeat = new double[24];
            //DayHourFloweringFloretFertilityFrost = new double[24];
            DailyFloweringFloretFertilityHeat = 0;
            DailyFloweringFloretFertilityFrost = 0;
            DailyFloweringFloretFertility = 0;
            CumFloweringFloretFertility = 0;
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

            // Modelling potential grain number
            // Cumulative thermal time from floral initiation (vernalSaturation in APSIM) to terminal spikelet stage
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

            // Calculating spikelet primordia, floret primordia, fertile florets, and grains per spike from terminal spikelet
            if (phen.Stage >= EndStageName && phen.Stage < 8.1) 
            {
                // Spikelet primordia per spike
                SpikeletPrimordiaPerSpike = TTFITS / SpikeletPrimordiaPlastochron;
                // Floret primordia per spike
                FloretPrimordiaPerSpike = SpikeletPrimordiaPerSpike * (FloretPrimordiaNoApical + FloretPrimordiaNoCentral + FloretPrimordiaNoProximal) / 3;
                // Fertile florets per spike
                FertileFloretsPerSpike = FloretPrimordiaPerSpike * FloretFertilityRate;
                // Grains per spike
                GrainsPerSpike = FertileFloretsPerSpike * (1 - GrainAbortionRate);
            }

            // Calculating potential grain number per unit of area
            PotentialGrainNumberPerArea = GrainsPerSpike * Plant.Population * (1 + stru.BranchNumber); // main shoot and tillers


            // Calculating floret fertility in response to frost and heat event on meiotic phase 
            // Step 1: Calculating the daily frequency of flag leaf fully emerged in a population as meiosis occurs around flag leaf fully emerged ro early booting
            // Step 2: Calculating the frequency of florets reached the meiotic pahse on a spike
            // Step 3: Calculating the frequency of florets reached the meiotic phase in the population
            // Step 4: Calculating the probability of florets to be fertile in response to heat and cold degree hour
            // Step 5: Calculating the floret fertility in the population 
            if (phen.Zadok >= 33 && phen.Zadok <= 50)
            {
                // Probability of flag leaf fully emerged (liguale appears) of shoots/ spikes at the day in a population,
                // which is a right-skewed Gamma distribution of Zadok stage from 33 (3rd node detectable) to 50 (first spikelet of spike just visible),
                // with the cumulative probability at ZS39 or ZS40 (50% of plants with fully emerged flag leaf) is 50%.
                // In APSIM, ZS is respectively interpolated based on the growth phases. For stages from ZS33 to ZS39, which are interpolated from GS5.9 (for ZS33) and GS6 (for ZS39).
                // For stages from ZS39 to ZS55, which are interpoloated from from GS6 (for ZS39) and GS7 (for ZS55; Heading - Ear half emerged).
                // It is reasonable to fit a curve with cumulative probility before ZS40 and after ZS40 equal to 50%.
                // Here we calculate the cumulative forst or heat degree hours of a day as it is assumed that the meiosis of floret will last for a day to finish.
                DailyFlagLeafEmergedFreq = GammaDistributor(9.99994, 1.381259, phen.Zadok - 33);

                // Probability of florets at the meiosis date on a spike
                MeiosisFloretsOnSpikeFreq = NormalDistributor(FloretMeiosisDateMean, FloretMeiosisDateStddev, DaysAfterFlagLeafTip);

                // Probability of florets at the meiotic phase among the population  
                DailyMeiosisFloretFreq = DailyFlagLeafEmergedFreq * MeiosisFloretsOnSpikeFreq;

                // Floret fertility in response to frost stress on the meiosis date
                double DegreeHours = FrostDegreeHours(FrostCriticalTemp);
                double Fertility = MeioticFloretFertility(DegreeHours, MeiosisHalfKillFrostDegreeHours, MeiosisFrostKillFactor);

                // Floret fertility of the day in the population 
                DailyMeiosisFloretFertilityFrost = DailyMeiosisFloretFreq * Fertility;
                // CumMeiosisFloretFertilityFrost += DailyMeiosisFloretFertilityFrost;

                // Floret fertility in response to heat stress on the meiosis date
                DegreeHours = HeatDegreeHours(HeatCriticalTemp);
                Fertility = MeioticFloretFertility(DegreeHours, MeiosisHalfKillHeatDegreeHours, MeiosisHeatKillFactor);

                // Floret fertility of the day in the population 
                DailyMeiosisFloretFertilityHeat = DailyMeiosisFloretFreq * Fertility;
                // CumMeiosisFloretFertilityHeat += DailyMeiosisFloretFertilityHeat;

                // Cumulative floret fertility of the population
                CumMeiosisFloretFertility += DailyMeiosisFloretFertilityFrost * DailyMeiosisFloretFertilityHeat;
            }

            // Calculating floret fertility in response to frost and heat event on flowering 
            // Step 1: Calculating the daily frequency of spikes headed on a day in a population
            // Step 2: Calculating the frequency of florets flowered on a spike
            // Step 3: Calculating the hourly frequency of florets flowered on the spike at the day
            // Step 4: Calculating the hourly frequency of florets flowered on the spike at the day in the population
            // Step 5: Calculating the hourly floret fertility in response to high and cold temperature
            // Step 6: Calculating the hourly floret fertility in repsonse to high and cold temperature at the day in the population
            if (phen.Zadok > 50 && phen.Zadok < 75)
            {
                // Probability of spikes reaching heading at the day in a population, 
                // which is a right-skewed Gamma distribution of Zadok stage from 50 (first spikelet of spike just visible) to 75 (early grain filling),
                // with the cumulative probability at ZS65 (50% of plants flowering) is 50%.
                // In APSIM, ZS is respectively interpolated based on the growth phases. 
                // For stages from ZS39 to ZS55, which are interpoloated from from GS6 (for ZS39) and GS7 (for ZS55; 50% of spike heading).
                // For stages from ZS55 to ZS65, which are interpolated from GS7 (for ZS33) and GS8 (for ZS65).
                // It is assumed that the flowering in in a population is initiated at ZS50 (first spikelet of spike just visible) and stopped at ZS75 (early grain filliing)
                // Here we calculate the cumulative forst or heat degree hours of a day as it is assumed that the meiosis of floret will last for a day to finish.
                DailyHeadingSpikeFreq = PoissionDistributor(LambdaSpikeHeading, DaysAfterHeadingInitiation);
                // Probability of flowering florets on a spike
                FloweringFloretsOnSpikeFreq = NormalDistributor(FloretFloweringDateMean, FloretFloweringDateStddev, DaysAfterHeadingInitiation);
                                
                // Sunrise and sunset hour of the day
                int SunriseHour = (int)Math.Floor(Weather.CalculateSunRise());
                int SunsetHour = (int)Math.Ceiling(Weather.CalculateSunSet()); 
                
                // Hourly temperature
                var HourlyTemp = new HourlySinPpAdjusted();
                List<double> HourlyTempList = HourlyTemp.SubDailyValues();

                DayHourFloweringFloretFreq = new double[24];
                DayHourFloweringFloretFertilityHeat = new double[24];
                //DayHourFloweringFloretFertilityFrost = new double[24];
                double HourlyFloweringFloretFreq = 0;
                double HourlyFloweringFloretsFertilityHeat = 0;
                double FrostFloweringFloretsFertilityHour = 0;
                DailyFloweringFloretFertilityHeat = 0;
                DailyFloweringFloretFertilityFrost = 1;

                // Flowering floret fertility in the population in response to heat stress on an hour of a day
                for (int Th = SunriseHour; Th <= SunsetHour; Th++)
                {
                    // Probability of florets that flower at the hour
                    HourlyFloweringFloretFreq = NormalDistributor(FloretFloweringTimeMean, FloretFloweringTimeStddev, Th);
                    // Probability of florets that flower at the hour of the day in the population
                    DayHourFloweringFloretFreq[Th] = DailyHeadingSpikeFreq * FloweringFloretsOnSpikeFreq * HourlyFloweringFloretFreq;

                    // Hourly floret fertility in reponse to high temperature
                    HourlyFloweringFloretsFertilityHeat = FloweringFloretFertility("Heat", HourlyTempList[Th], FloweringHalfKillHeatTemp, FloweringHeatKillFactor);
                    // Hourly fertility of flowering florets in repsonse to high temperature at the day in the population  
                    DayHourFloweringFloretFertilityHeat[Th] = DayHourFloweringFloretFreq[Th] * HourlyFloweringFloretsFertilityHeat;
                    // Cumulative floret fertility of the population at the day
                    DailyFloweringFloretFertilityHeat += DayHourFloweringFloretFertilityHeat[Th];
                }

                // Flowering floret fertility in the population in response to frost stress on an hour of a day
                // Sequence of night hours
                int[] NightHours = Enumerable.Range(0, SunriseHour - 1).Concat(Enumerable.Range(SunsetHour + 1, 23)).ToArray();
                for (int Th = 0; Th <= (NightHours.Length - 1); Th++)
                {
                    // Hourly floret fertility in reponse to cold temperature
                    FrostFloweringFloretsFertilityHour = FloweringFloretFertility("Frost", HourlyTempList[NightHours[Th]], FloweringFrostHalfKillTemp, FloweringFrostKillFactor);
                    DailyFloweringFloretFertilityFrost *= FrostFloweringFloretsFertilityHour;
                }
                DailyFloweringFloretFertilityFrost = DailyHeadingSpikeFreq * FloweringFloretsOnSpikeFreq * DailyFloweringFloretFertilityFrost;

                // Flowering floret fertility in the population in in response to frost and heat stress the day
                DailyFloweringFloretFertility = DailyFloweringFloretFertilityHeat * DailyFloweringFloretFertilityFrost;

                CumFloweringFloretFertility += DailyFloweringFloretFertility;
                DaysAfterHeadingInitiation += 1;
            }

            if (phen.Zadok >= 9)
            {
                // Floret fertility resulted from frost and heat damages on meiotic and flowering phases
                CumFloretFertility = CumMeiosisFloretFertility * CumFloweringFloretFertility;
                // Apply the fertility on potential grain number to get the actual one
                ActualGrainNumberPerArea = PotentialGrainNumberPerArea * CumFloretFertility;
            }
        }
    }
}
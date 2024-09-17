using System;
using System.Data;
using APSIM.Shared.Utilities;
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
using DocumentFormat.OpenXml.Spreadsheet;
using Models.Interfaces;
using DocumentFormat.OpenXml.Wordprocessing;
using static Models.Core.ScriptCompiler;
using ExcelDataReader;
using System.IO;
using System.Collections;
using DocumentFormat.OpenXml.Drawing.Charts;

namespace Models.Functions
{
    /// <summary> Damage functions of frost and heat stress. </summary>
    [Serializable]
    [Description("New grian number model considering reproductive and stress biology.")]
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


        #region Functions
        /// <summary>Poission distribution</summary>
        static private double PoissionDistributor(double lambda, int k)
        {
            var Distr = new Poisson(lambda);
            double DistrToday = Distr.Probability(k);
            return DistrToday;
        }

        /// <summary>Gamma distribution</summary>
        static private double GammaDistributor(double shape, double rate, double k)
        {
            var Distr = Gamma.WithShapeRate(shape, rate);
            double DistrToday = Distr.Density(k);
            return DistrToday;
        }

        /// <summary>Beta distribution</summary>
        private double BetaDistributor(double shape, double rate, double k)
        {
            var Distr = new Beta(shape, rate);
            double DistrToday = Distr.Density(k);
            return DistrToday;
        }

        /// <summary>Normal distribution</summary>
        static private double NormalDistributor(double mean, double stddev, double k)
        {
            var Distr = Normal.WithMeanStdDev(mean, stddev);
            double RateToday = Distr.Density(k);
            return RateToday;
        }

        /// <summary>Logistic distribution</summary>
        static private double LogisticDistributor(double mean, double stddev, double k)
        {
            var Distr = Logistic.WithMeanStdDev(mean, stddev);
            double RateToday = Distr.Density(k);
            return RateToday;
        }

        /// <summary>Double summation</summary>
        static double IntegrateProduct(int i, double lambda, double mean, double stddev)
        {
            double result = 0;

            for (int j = 0; j <= i; j += 1)
            {
                int k = i + 1 - j;
                double integrand = PoissionDistributor(lambda, j) * NormalDistributor(mean, stddev, k);
                result += integrand;
            }

            return result;
        }

        /// <summary>Estimate daily temperature from Min and Max temp</summary>
        public static List<double> HourlyTemperature(double DayLength, double MinT, double MaxT, double YesterdayMaxT, double TomorrowMinT, double SunRise, double SunSet)
        {
            double P = 1.5;
            double TC = 4.0;            
            double Tsset;

            List<double> sdts = new List<double>();

            for (int Th = 0; Th <= 23; Th++)
            {
                double Ta = 1.0;
                if (Th < SunRise)
                {
                    //  Hour between midnight and sunrise
                    //  PERIOD A MaxTB is max. temperature, before day considered

                    //this is the sunset temperature of based on the previous day
                    double n = 24 - DayLength;
                    Tsset = MinT + (YesterdayMaxT - MinT) *
                                    Math.Sin(Math.PI * (DayLength / (DayLength + 2 * P)));

                    Ta = (MinT - Tsset * Math.Exp(-n / TC) +
                            (Tsset - MinT) * Math.Exp(-(Th + 24 - SunSet) / TC)) /
                            (1 - Math.Exp(-n / TC));
                }
                else if (Th >= SunRise & Th < 12 + P)
                {
                    // PERIOD B Hour between sunrise and normal time of MaxT
                    Ta = MinT + (MaxT - MinT) *
                            Math.Sin(Math.PI * (Th - SunRise) / (DayLength + 2 * P));
                }
                else if (Th >= 12 + P & Th < SunSet)
                {
                    // PERIOD C Hour between normal time of MaxT and sunset
                    //  MinTA is min. temperature, after day considered

                    Ta = TomorrowMinT + (MaxT - TomorrowMinT) *
                        Math.Sin(Math.PI * (Th - SunRise) / (DayLength + 2 * P));
                }
                else
                {
                    // PERIOD D Hour between sunset and midnight
                    Tsset = TomorrowMinT + (MaxT - TomorrowMinT) * Math.Sin(Math.PI * (DayLength / (DayLength + 2 * P)));
                    double n = 24 - DayLength;
                    Ta = (TomorrowMinT - Tsset * Math.Exp(-n / TC) +
                            (Tsset - TomorrowMinT) * Math.Exp(-(Th - SunSet) / TC)) /
                            (1 - Math.Exp(-n / TC));
                }
                sdts.Add(Ta);
            }
            return sdts;
        }


        /// <summary>Calculate heat degree hours</summary>
        private double HeatDegreeHours(double HeatCriticalTemp, List<double> HourlyTempList)
        {
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
        private double FrostDegreeHours(double FrostCriticalTemp, List<double> HourlyTempList)
        {
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

        
        static Dictionary<string, List<double>> ReadPoissonParameter(string filePath)
        {
            // Create a dictionary to hold the column data as lists of doubles
            var columnData = new Dictionary<string, List<double>>();

            try
            {
                // Read all lines from the CSV file
                string[] lines = File.ReadAllLines(filePath);

                if (lines.Length > 0)
                {
                    // Get the headers from the first line
                    string[] headers = lines[0].Split(',');

                    // Initialize the dictionary with empty lists for each column
                    foreach (string header in headers)
                    {
                        columnData[header] = new List<double>();
                    }

                    // Process each row of data
                    for (int row = 1; row < lines.Length; row++)
                    {
                        string[] values = lines[row].Split(',');

                        if (values.Length == headers.Length)
                        {
                            for (int col = 0; col < headers.Length; col++)
                            {
                                if (double.TryParse(values[col], out double cellValue))
                                {
                                    columnData[headers[col]].Add(cellValue);
                                }
                                else
                                {
                                    Console.WriteLine($"Warning: Unable to convert cell value '{values[col]}' in column '{headers[col]}' to double. Row: {row + 1}");
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Warning: Skipping row {row + 1} due to incorrect number of values.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while reading the CSV file: {ex.Message}");
            }

            return columnData;
        }

        static Dictionary<string, double> DetermineLambda(Dictionary<string, List<double>> PoissonParameter, int RangeMid, int RangeEnd) 
        {

            List<double> MidValue = PoissonParameter["mid"];
            List<double> EndValue = PoissonParameter["end"];
            List<double> LambdaValue = PoissonParameter["lambda"];

            List<double> Dist = new List<double>();
            int ParameterIndex;
            for (int i = 0; i < MidValue.Count - 1; i++)
            {
                Dist.Add(Math.Pow(MidValue[i] - RangeMid, 2) + Math.Pow(EndValue[i] - RangeEnd, 2));
            }

            // Find the minimum value
            double DistMin = Dist.Min();

            // Find all indices of the minimum value
            List<int> MinIndices = Dist
                .Select((value, index) => new { value, index })
                .Where(x => x.value == DistMin)
                .Select(x => x.index)
                .ToList();

            List<double> Diff = new();
            if (MinIndices.Count > 1)
            {
                for (int j = 0; j < MinIndices.Count - 1; j++)
                {
                    Diff.Add(MidValue[MinIndices[j]] - RangeMid);
                }
                // Find the index of the minimum value
                int MinDiffIndex = Diff.IndexOf(Diff.Min());
                ParameterIndex = MinIndices[MinDiffIndex];
            }
            else
            {
                ParameterIndex = MinIndices[0];
            }

            // Create a dictionary
            var res = new Dictionary<string, double>
            {
                { "mid", MidValue[ParameterIndex] },
                { "end", EndValue[ParameterIndex] },
                { "lambda", LambdaValue[ParameterIndex] }
            };

            return res;
        }

        #endregion

        #region Define parameters

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
        //// <summary>Lambda of poisson distribution of dates of flag leaf fully emerged of shoots</summary>
        //[Description("Lambda of Poission distribution of flag leaf fully emerged from shoots")]
        //public double LambdaFlagLeaf { get; set; }

        ///// <summary>Mean (peak) date of normal distribution of florets during the meiotic phase</summary>
        //[Description("Mean date of normal distribution of meiosis dates of florets")]
        //public double FloretMeiosisDateMean { get; set; }

        ///// <summary>Standard deviation in date of normal distribution of florets during the meiotic phase</summary>
        //[Description("Standard deviation of normal distribution of meiosis dates of florets")]
        //public double FloretMeiosisDateStddev { get; set; }

        // <summary>Threshold temperature of heat stress</summary>
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
        [Separator("Fertility of flowering phase")]
        // <summary>Lambda of poisson distribution of heading dates of spikes</summary>
        //[Description("Lambda of Poission distribution of heading dates of spikes")]
        //public double LambdaSpikeHeading { get; set; }

        ///// <summary>Mean (peak) date of normal distribution of flowering florets on spike</summary>
        //[Description("Mean date of normal distribution of flowering florets on a spike")]
        //public double FloretFloweringDateMean { get; set; }

        ///// <summary>Standard deviation of normal distribution of flowering florets on a spike</summary>
        //[Description("Standard deviation of normal distribution of flowering spikelets on a spike")]
        //public double FloretFloweringDateStddev { get; set; }

        // <summary>Mean (peak) date of normal distribution of florets flowering at a time of a day</summary>
        [Description("Mean time of normal distribution of florets flowering during a day")]
        public double FloretFloweringTimeMean { get; set; }

        /// <summary>Standard deviation of normal distribution of florets flowering at a time of a day</summary>
        [Description("Standard deviation of normal distribution of florets flowering during a day")]
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

        #endregion

        #region Output variables
        /// <summary>Hourly temperature</summary>
        public List<double> HourlyTemp { get; set; }

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
        //public double DailyEmergedFlagLeafPerc { get; set; }
        //public double[] DailyEmergedFlagLeafPerc { get; set; }
        public List<double> DailyEmergedFlagLeafPerc { get; set; }
        
        /// <summary>The probability of meiosis dates of florets on a spike</summary>
        //public double DailyMeiosisFloretsOnSpikePerc { get; set; }
        public List<double> DailyMeiosisFloretsOnSpikePerc { get; set; }

        /// <summary>Relative frequency of florets in a population that reach meiotic phase after the appearence of flag leaf tip</summary>
        //public double DailyMeiosisFloretPerc { get; set; }
        public List<double> DailyMeiosisFloretPerc { get; set; }

        /// <summary>Daily floret fertilities in the population as affected by frost stress at meiosis</summary>
        //public double DailyMeiosisFloretFertilityFrost { get; set; }
        public List<double> DailyMeiosisFloretFertilityFrost { get; set; }

        /// <summary>Caumulative floret fertilities in the population as affected by frost stress at meiosis</summary>
        public double CumMeiosisFloretFertilityFrost { get; set; }

        /// <summary>Daily floret fertilities in the population as affected by heat stress at meiosis</summary>
        //public double DailyMeiosisFloretFertilityHeat { get; set; }
        public List<double> DailyMeiosisFloretFertilityHeat { get; set; }

        /// <summary>Cumulative floret fertilities in the population as affected by heat stress at meiosis</summary>
        public double CumMeiosisFloretFertilityHeat { get; set; }

        /// <summary>Daily floret fertility induced by frost and heat at meiosis</summary>
        public double DailyMeioisFloretFertility { get; set; }

        /// <summary>Cumulative floret fertilities in the population during meiotic phase</summary>
        public double FinalMeiosisFertileFloretPerc { get; set; }


        /// <summary>The probability of heading dates of spikes</summary>
        //public double DailyHeadingSpikeFreq { get; set; }
        public List<double> DailyFloweringSpikePerc { get; set; }

        /// <summary>The probability of flowering florets on a spike</summary>
        public List<double> DailyFloweringFloretsOnSpikePerc { get; set; }

        /// <summary>Relative frequency of flowering florets in a population</summary>
        public List<double> DailyFloweringFloretPerc { get; set; }

        /// <summary>The probability of florets flowering at a time of the day</summary>
        public List<double> DayHourFloweringFloretFreq { get; set; }

        /// <summary>The floret fertility after a heat event at a time of the day</summary>
        public List<double> DayHourFloweringFertileFloretPercHeat { get; set; }

        /// <summary>The floret fertility after heat events at the day</summary>
        public List<double> DailyFloweringFertileFloretPercHeat { get; set; }

        /// <summary>The floret fertility after heat events at the day</summary>
        public List<double> DailyFloweringFloretFertilityHeat { get; set; }

        /// <summary>The floret fertility after heat events at the day</summary>
        public List<double> DailyFloweringFertileFloretPercFrost { get; set; }

        /// <summary>The floret fertility after frost events at the day</summary>
        public List<double> DailyFloweringFloretFertilityFrost { get; set; }

        /// <summary>The floret fertility after both frost and heat events at the day</summary>
        public List<double> DailyFloweringFloretFertility { get; set; }

        /// <summary>The floret fertility after both frost and heat events at the day</summary>
        public List<double> DailyFloweringFertileFloretPerc { get; set; }

        /// <summary>Cumulative floret fertility during flowering</summary>
        public double FinalFloweringFloretFertility { get; set; }

        /// <summary>Cumulative floret fertility during and meiotic and flowering phase</summary>
        public double FinalFloweringFertileFloretPerc { get; set; }

        /// <summary> Final percentage of fertile floret </summary>
        public double FinalFertileFloretPerc { get; set; }

        #endregion

        #region Internal variables
        /// <summary>Cumulative thermal time from floral iniation stage to termimal spikelet stage</summary>
        public double TTFITS { get; set; }

        /// <summary>Crop type</summary>
        string CropType;

        //DataTable PoissonParameters { get; set; }

        /// <summary>The shape parameter for gamma distribution of flag leaf</summary>
        const double MeiosisShape = 9.99994;

        /// <summary>The rate parameter for gamma distribution of flag leaf</summary>
        const double MeiosisRate = 1.381259;

        /// <summary>The mean parameter for normal distribution of florets reached meiosis in a spike</summary>
        const double MeiosisMean = 2.5;

        /// <summary>The stddev parameter for normal distribution of florets reached meiosis in a spike</summary>
        const double MeiosisStddev = 1;

        /// <summary>The lambda parameter for poisson distribution of flag leaf emerged</summary>
        double MeiosisLambda = 8.655686;

        /// <summary>Days after ZS33</summary>
        public int DaysAfterZS33 { get; set; }

        /// <summary>Days after ZS33 for the flag leaf stage </summary>
        public int DaysAtFlagLeaf { get; set; }

        /// <summary>Days after ZS33 for ZS 50</summary>
        public int DaysAtZS50 { get; set; }

        /// <summary>Bool for reaching ZS50</summary>
        bool ReachedZS50;

        /// <summary>Bool for reaching start of grain filling</summary>
        bool ReachedStartGrainFill;

        /// <summary>Days after ZS33</summary>
        public int DaysAfterZS50 { get; set; }

        /// <summary>Days after ZS33 for the flowering stage</summary>
        public int DaysAtFlowering { get; set; }

        /// <summary>Days after ZS33 for the stage of start of grian filling</summary>
        public int DaysAtStartGrainFill { get; set; }

        double FloweringLambda = 6;
        const double FloretFloweringDateMean = 2.5;
        const double FloretFloweringDateStddev = 1;        

        List<double> MinT = new List<double>();
        List<double> MaxT = new List<double>();
        List<double> YesterdayMaxT = new List<double>();
        List<double> TomorrowMinT = new List<double>();
        List<double> Sunrise = new List<double>();
        List<double> Sunset = new List<double>();
        List<double> DayLength = new List<double>();
        List<int> SunriseHour = new List<int>();
        List<int> SunsetHour = new List<int>();

        Dictionary<string, List<double>> PoissonParameter;

        #endregion

        [EventSubscribe("Sowing")]
        private void OnDoSowing(object sender, EventArgs e)
        {
            // initialize
            //CropType = Plant.PlantType;
            CropType = "Wheat";

            TTFITS = 0;
            SpikeletPrimordiaPerSpike = 0;
            FloretPrimordiaPerSpike = 0;
            FertileFloretsPerSpike = 0;
            GrainsPerSpike = 0;
            PotentialGrainNumberPerArea = 0;
            ActualGrainNumberPerArea = 0;

            HourlyTemp = new List<double>();
            
            ReachedZS50 = false;
            DaysAfterZS33 = 0;
            DaysAtZS50 = 0;
            DailyEmergedFlagLeafPerc = new List<double>();
            DailyMeiosisFloretsOnSpikePerc = new List<double>();
            DailyMeiosisFloretPerc = new List<double>();
            DailyMeiosisFloretFertilityFrost = new List<double>();
            DailyMeiosisFloretFertilityHeat = new List<double>();
            DailyMeioisFloretFertility = 0;
            FinalMeiosisFertileFloretPerc = 0;

            ReachedStartGrainFill = false;
            DaysAfterZS50 = 0;
            DaysAtFlowering = 0;
            DaysAtStartGrainFill = 0;
            DailyFloweringSpikePerc = new List<double>();
            DailyFloweringFloretsOnSpikePerc = new List<double>();
            DailyFloweringFloretPerc = new List<double>();
            DailyFloweringFertileFloretPercHeat = new List<double>();
            DailyFloweringFloretFertilityHeat = new List<double>();
            DailyFloweringFertileFloretPercFrost = new List<double>();
            DailyFloweringFloretFertilityFrost = new List<double>();
            DailyFloweringFertileFloretPerc = new List<double>();
            DailyFloweringFloretFertility = new List<double>();
            FinalFloweringFloretFertility = 0;
            FinalFloweringFertileFloretPerc = 0;

            FinalFertileFloretPerc = 0;

            PoissonParameter = ReadPoissonParameter("./PoissonParameters.csv");
            if (PoissonParameter == null)
                throw new Exception("Data for Poisson parameter does not appear to be any data.");
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

            #region Modelling potential grain number
            // Cumulative thermal time from floral initiation (vernalSaturation in APSIM) to terminal spikelet stage
            if (CropType == "Wheat" | CropType == "wheat")
            {
                if (phen.Stage >= 4 && phen.Stage <= 5) 
                { 
                    TTFITS += phen.thermalTime.Value(); 
                }
            }
            else
            {
                throw new Exception("Crop type not supported!");
            }

            // Calculating spikelet primordia, floret primordia, fertile florets, and grains per spike from terminal spikelet stage
            if (phen.Stage >= 5 && phen.Stage < 8.1) 
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
            PotentialGrainNumberPerArea = GrainsPerSpike * Plant.Population * (1 + stru.BranchNumber); // main shoot and tillers, may need to consider the difference between main stem and tillers
            #endregion

            #region Calculating floret fertility in response to frost and heat event on meiotic phase 
            // Step 1: Calculating the daily frequency of flag leaf fully emerged in a population as meiosis occurs around flag leaf fully emerged ro early booting
            // Step 2: Calculating the frequency of florets reached the meiotic pahse on a spike
            // Step 3: Calculating the frequency of florets reached the meiotic phase in the population
            // Step 4: Calculating the probability of florets to be fertile in response to heat and cold degree hour
            // Step 5: Calculating the floret fertility in the population 

            if (phen.Zadok >= 37 && phen.Zadok <= 50)
            {
                DaysAfterZS33 = DaysAfterZS33 + 1;
                if (phen.CurrentStageName == "FlagLeaf")
                {
                    DaysAtFlagLeaf = DaysAfterZS33;
                }
                DaysAtZS50 = DaysAfterZS33;

                DayLength.Add(Weather.CalculateDayLength(-6));
                MinT.Add(Weather.MinT);
                MaxT.Add(Weather.MaxT);
                double tmp = (Weather.YesterdaysMetData == null) ? Weather.MaxT : Weather.YesterdaysMetData.MaxT;
                YesterdayMaxT.Add(tmp);
                tmp = (Weather.TomorrowsMetData == null) ? Weather.MinT : Weather.TomorrowsMetData.MinT;
                TomorrowMinT.Add(tmp);
                Sunrise.Add(Weather.CalculateSunRise());
                Sunset.Add(Weather.CalculateSunSet());
            }
            

            if (phen.Zadok > 50 && ReachedZS50 == false)
            {
                ReachedZS50 = true;

                // Determin parameter value
                Dictionary<string, double> PoissonPara = DetermineLambda(PoissonParameter, DaysAtFlagLeaf, DaysAtZS50);
                MeiosisLambda = PoissonPara["lambda"];

                for (int i = 0; i <= DaysAtZS50 - 1; i++)
                {
                    // Probability of flag leaf fully emerged (liguale appears) of shoots/ spikes at the day in a population,
                    // which is a right-skewed Gamma distribution of Zadok stage from 33 (3rd node detectable) to 50 (first spikelet of spike just visible),
                    // with the cumulative probability at ZS39 or ZS40 (50% of plants with fully emerged flag leaf) is 50%, i.e., cumulative probability reaches peak at ZS39.
                    // In APSIM, ZS is respectively interpolated based on the growth phases. For stages from ZS33 to ZS39, which are interpolated from GS5.9 (for ZS33) and GS6 (for ZS39).
                    // For stages from ZS39 to ZS55, which are interpoloated from from GS6 (for ZS39) and GS7 (for ZS55; Heading - Ear half emerged).
                    // It is reasonable to fit a curve with cumulative probility before ZS40 and after ZS40 equal to 50%.
                    // Here we calculate the cumulative forst or heat degree hours of a day as it is assumed that the meiosis of floret will last for a day to finish.

                    //DailyEmergedFlagLeafPerc = GammaDistributor(MeiosisShape, MeiosisRate, phen.Zadok - 33);
                    DailyEmergedFlagLeafPerc.Add(PoissionDistributor(MeiosisLambda, i + 1));

                    // Probability of florets at the meiosis date on a spike
                    //DailyMeiosisFloretsOnSpikePerc = NormalDistributor(FloretMeiosisDateMean, FloretMeiosisDateStddev, DaysAfterFlagLeafTip);
                    DailyMeiosisFloretsOnSpikePerc.Add(NormalDistributor(MeiosisMean, MeiosisStddev, i + 1)); // mean +- 2sd ~ 95%

                    // Probability of florets at the meiotic phase among the population  
                    //DailyMeiosisFloretPerc = DailyEmergedFlagLeafPerc * DailyMeiosisFloretsOnSpikePerc;
                    double MeiosisFloretPercToday = IntegrateProduct(i + 1, MeiosisLambda, MeiosisMean, MeiosisStddev);
                    DailyMeiosisFloretPerc.Add(MeiosisFloretPercToday);


                    // Floret fertility in response to frost stress on the meiosis date
                    HourlyTemp = HourlyTemperature(DayLength[i], MinT[i], MaxT[i], YesterdayMaxT[i], TomorrowMinT[i], Sunrise[i], Sunset[i]);
                    double DegreeHours = FrostDegreeHours(FrostCriticalTemp, HourlyTemp);
                    double FrostFertilityToday = MeioticFloretFertility(DegreeHours, MeiosisHalfKillFrostDegreeHours, MeiosisFrostKillFactor);
                    DailyMeiosisFloretFertilityFrost.Add(FrostFertilityToday);
                    
                    // Floret fertility in response to heat stress on the meiosis date
                    DegreeHours = HeatDegreeHours(HeatCriticalTemp, HourlyTemp);
                    double HeatFertilityToday = MeioticFloretFertility(DegreeHours, MeiosisHalfKillHeatDegreeHours, MeiosisHeatKillFactor);
                    DailyMeiosisFloretFertilityHeat.Add(HeatFertilityToday);

                    DailyMeioisFloretFertility = FrostFertilityToday * HeatFertilityToday;

                    // Cumulative floret fertility of the population
                    FinalMeiosisFertileFloretPerc += MeiosisFloretPercToday * DailyMeioisFloretFertility;
                }
            }
            #endregion

            #region Calculating floret fertility in response to frost and heat event on flowering 
            // Step 1: Calculating the daily frequency of spikes headed on a day in a population OR daily frequency of spikes flowered on a day in a population
            // Step 2: Calculating the frequency of florets flowered on a spike
            // Step 3: Calculating the hourly frequency of florets flowered on the spike at the day
            // Step 4: Calculating the hourly frequency of florets flowered on the spike at the day in the population
            // Step 5: Calculating the hourly floret fertility in response to high and cold temperature
            // Step 6: Calculating the hourly floret fertility in repsonse to high and cold temperature at the day in the population

            if (phen.Zadok > 50 && ReachedStartGrainFill == false)
            {
                DaysAfterZS50 = DaysAfterZS50 + 1;
                if (phen.CurrentStageName == "Flowering")
                {
                    DaysAtFlowering = DaysAfterZS50;
                }

                if (phen.CurrentStageName == "StartGrainFill")
                {
                    ReachedStartGrainFill = true;
                    DaysAtStartGrainFill = DaysAfterZS50;
                }

                SunriseHour.Add((int)Math.Floor(Weather.CalculateSunRise()));
                SunsetHour.Add((int)Math.Floor(Weather.CalculateSunSet()));
            }


            if (phen.CurrentStageName == "StartGrainFill")
            {
                Dictionary<string, double> PoissonPara = DetermineLambda(PoissonParameter, DaysAtFlowering, DaysAtStartGrainFill);
                FloweringLambda = PoissonPara["lambda"];

                for (int i = 0; i <= DaysAtStartGrainFill - 1; i++)
                {
                    // Probability of spikes reaching heading at the day in a population, 
                    // which is a right-skewed Gamma distribution of Zadok stage from 50 (first spikelet of spike just visible) to 75 (early grain filling),
                    // with the cumulative probability at ZS65 (50% of plants flowering) is 50%.
                    // In APSIM, ZS is respectively interpolated based on the growth phases. 
                    // For stages from ZS39 to ZS55, which are interpoloated from from GS6 (for ZS39) and GS7 (for ZS55; 50% of spike heading).
                    // For stages from ZS55 to ZS65, which are interpolated from GS7 (for ZS33) and GS8 (for ZS65).
                    // It is assumed that the flowering in in a population is initiated at ZS50 (first spikelet of spike just visible) and stopped at ZS75 (early grain filliing)
                    // Here we calculate the cumulative forst or heat degree hours of a day as it is assumed that the meiosis of floret will last for a day to finish.

                    //DailyHeadingSpikeFreq = GammaDistributor(2.632749, 0.3842951, phen.Zadok - 49);
                    // DailyFloweringSpikePerc.Add(BetaDistributor(3.172469, 1.645674, i + 1));
                    DailyFloweringSpikePerc.Add(PoissionDistributor(FloweringLambda, i + 1));

                    // Probability of flowering florets on a spike
                    DailyFloweringFloretsOnSpikePerc.Add(NormalDistributor(FloretFloweringDateMean, FloretFloweringDateStddev, i + 1));

                    // Probability of florets flowering among the population  
                    double FloweringFloretPercToday = IntegrateProduct(i + 1, FloweringLambda, FloretFloweringDateMean, FloretFloweringDateStddev);
                    DailyFloweringFloretPerc.Add(FloweringFloretPercToday);

                    DayHourFloweringFloretFreq = new List<double>();
                    DayHourFloweringFertileFloretPercHeat = new List<double>();
                    double HourlyFloweringFloretPerc;
                    double HourlyFloweringFloretsFertilityHeat;             

                    // Flowering floret fertility in the population in response to heat stress on an hour of a day
                    for (int Th = SunriseHour[i]; Th <= SunsetHour[i]; Th++)
                    {
                        // Probability of florets that flower at the hour
                        HourlyFloweringFloretPerc = NormalDistributor(FloretFloweringTimeMean, FloretFloweringTimeStddev, Th);

                        // Probability of florets that flower at the hour of the day in the population
                        double FloweringFloretPercTodayHour = FloweringFloretPercToday * HourlyFloweringFloretPerc;
                        DayHourFloweringFloretFreq.Add(FloweringFloretPercTodayHour);

                        // Hourly floret fertility in reponse to high temperature
                        HourlyFloweringFloretsFertilityHeat = FloweringFloretFertility("Heat", HourlyTemp[Th], FloweringHalfKillHeatTemp, FloweringHeatKillFactor);

                        // Hourly fertility of flowering florets in repsonse to high temperature at the day in the population  
                        DayHourFloweringFertileFloretPercHeat.Add(FloweringFloretPercTodayHour * HourlyFloweringFloretsFertilityHeat);
                    }

                    // Cumulative floret fertility of the population at the day
                    double FloweringFertileFloretPercHeatToday = DayHourFloweringFertileFloretPercHeat.Sum();
                    DailyFloweringFertileFloretPercHeat.Add(FloweringFertileFloretPercHeatToday);
                    double FloweringFloretFertilityHeatToday = FloweringFertileFloretPercHeatToday / FloweringFloretPercToday;
                    DailyFloweringFloretFertilityHeat.Add(FloweringFloretFertilityHeatToday);

                    // Flowering floret fertility in the population in response to frost stress on an hour of a day
                    // Sequence of night hours
                    //int[] NightHours = Enumerable.Range(0, SunriseHour - 1).Concat(Enumerable.Range(SunsetHour + 1, 23)).ToArray();
                    double FrostFloweringFloretsFertilityHour;
                    double FloweringFloretFertilityFrostToday = 1;
                    for (int Th = 0; Th <= 23; Th++)
                    {
                        // Hourly floret fertility in reponse to cold temperature
                        FrostFloweringFloretsFertilityHour = FloweringFloretFertility("Frost", HourlyTemp[Th], FloweringFrostHalfKillTemp, FloweringFrostKillFactor);
                        FloweringFloretFertilityFrostToday *= FrostFloweringFloretsFertilityHour;
                    }

                    // Cumulative floret fertility of the population at the day
                    //double FloretFertilityFrostToday = FloweringFloretPercToday * DailyFertilityFrost;
                    DailyFloweringFloretFertilityFrost.Add(FloweringFloretFertilityFrostToday);

                    // Flowering floret fertility in the population in response to frost and heat stress of the day
                    double FloweringFloretFertilityToday = FloweringFloretFertilityHeatToday * FloweringFloretFertilityFrostToday;
                    DailyFloweringFertileFloretPerc.Add(FloweringFloretPercToday * FloweringFloretFertilityToday);
                    DailyFloweringFloretFertility.Add(FloweringFloretFertilityToday);


                    FinalFloweringFertileFloretPerc += FloweringFloretPercToday * FloweringFloretFertilityToday;
                    FinalFloweringFloretFertility *= FloweringFloretFertilityToday;
                }
            }
            #endregion

            if (phen.Stage >= 9)
            {
                // Floret fertility resulted from frost and heat damages on meiotic and flowering phases
                FinalFertileFloretPerc = FinalMeiosisFertileFloretPerc * FinalFloweringFertileFloretPerc;

                // Apply the fertility on potential grain number to get the actual one
                ActualGrainNumberPerArea = PotentialGrainNumberPerArea * FinalFertileFloretPerc;
            }
        }
    }
}
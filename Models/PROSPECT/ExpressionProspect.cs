using System;
using System.Collections.Generic;
using Models.Core;
using Models.PMF;
using Models.PMF.Phen;
using Models.PMF.Organs;
using Models.Climate;
using System.Reflection;

namespace Models.Prospect
{
    /// <summary>
    /// 支持表达式编辑的高级参数转换器
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Plant))] // allowed to put under Plant
    public class ExpressionProspectConverter : Model, IProspectParameterConverter
    {
        //[Link] private Weather weather = null;
        //[Link] private Leaf leaf = null;
        //[Link] private Clock clock = null;

        #region 表达式配置属性

        /// <summary>
        /// Mathematical expression for chlorophyll calculation
        /// </summary>
        /// <remarks>
        /// Supports variables: [Leaf.N], [Leaf.Age], [Weather.Radn], [Weather.Temp]
        /// Example: "[Leaf.N] * 35 + Pow([Weather.Radn], 0.5)"
        /// </remarks>
        [Category("1. 叶绿素计算")]
        [Description("叶绿素计算公式(可使用[Leaf.N]、[Weather.Radn]等变量)")]
        [Display(Order = 10)]
        public string ChlorophyllExpression { get; set; } = "[Leaf.N] * 35 + Pow([Weather.Radn], 0.5)";

        /// <summary>
        /// Mathematical expression for equivalent water thickness calculation
        /// </summary>
        /// <remarks>
        /// Supports variables: [Leaf.WaterPotential], [Leaf.Age]
        /// Example: "0.01 + 0.005 * Exp(-0.2 * [Leaf.Age])"
        /// </remarks>
        [Category("2. 水分计算")]
        [Description("等效水厚度计算公式")]
        [Display(Order = 20)]
        public string EWTExpression { get; set; } = "0.01 + 0.005 * Exp(-0.2 * [Leaf.Age])";

        /// <summary>
        /// Mathematical expression for leaf structure parameter calculation
        /// </summary>
        /// <remarks>
        /// Supports variables: [Leaf.Thickness], [Leaf.SLA]
        /// Example: "1.2 + 0.3 * [Leaf.Thickness]"
        /// </remarks>
        [Category("3. 结构参数")]
        [Description("结构参数N计算公式")]
        [Display(Order = 30)]
        public string NStructureExpression { get; set; } = "1.2 + 0.3 * [Leaf.Thickness]";

        /// <summary>
        /// Mathematical expression for carotenoid parameter calculation
        /// </summary>
        /// <remarks>
        /// Supports variables: [Leaf.Thickness], [Leaf.SLA]
        /// Example: "1.2 + 0.3 * [Leaf.Thickness]"
        /// </remarks>
        [Category("4. 类胡萝卜素计算")]
        [Description("类胡萝卜素计算公式(μg/cm²)")]
        [Display(Order = 40)]
        public string CarotenoidExpression { get; set; } = "[Leaf.N] * 10";

        /// <summary>
        /// Mathematical expression for leaf mass per area calculation
        /// </summary>
        /// <remarks>
        /// Supports variables: [Leaf.Thickness], [Leaf.SLA]
        /// Example: "1.2 + 0.3 * [Leaf.Thickness]"
        /// </remarks>
        [Category("5. 比叶重计算")]
        [Description("比叶重计算公式(g/cm²)")]
        [Display(Order = 50)]
        public string LMAExpression { get; set; } = "0.008 * (1 + 0.1 * [Leaf.Age])";

        /// <summary>
        /// Mathematical expression for incidence angle in degrees calculation
        /// </summary>
        /// <remarks>
        /// Supports variables: [Leaf.Thickness], [Leaf.SLA]
        /// Example: "1.2 + 0.3 * [Leaf.Thickness]"
        /// </remarks>
        [Category("5. 入射角度计算")]
        [Description("入射角度计算公式(g/cm²)")]
        [Display(Order = 60)]
        public string AlphaExpression { get; set; } = "0.008 * (1 + 0.1 * [Leaf.Age])";

        /// <summary>
        /// Custom variables for additional parameter specification
        /// </summary>
        /// <remarks>
        /// Format: 'VariableName1=Value1;VariableName2=Value2'
        /// </remarks>
        [Category("6. 高级设置")]
        [Description("自定义变量(格式: '变量名=值;变量名2=值2')")]
        [Display(Order = 70)]
        public string CustomVariables { get; set; } = "ResistFactor=1.0;PhotoInhibit=0.5";

        #endregion

        #region 转换方法实现

        /// <summary>Calculates chlorophyll content from crop state</summary>
        /// <returns>Chlorophyll content (μg/cm²)</returns>
        public double CalculateChlorophyll()
        {
            var vars = ParseCustomVariables();
            return ExpressionHelper.Evaluate(ChlorophyllExpression, this, vars);
        }

        /// <summary>Calculates equivalent water thickness from crop state</summary>
        /// <returns>EWT (g/cm²)</returns>
        public double CalculateEWT()
        {
            var vars = ParseCustomVariables();
            return ExpressionHelper.Evaluate(EWTExpression, this, vars);
        }

        /// <summary>Calculates leaf structure parameter from crop state</summary>
        /// <returns>Structure parameter N (unitless)</returns>
        public double CalculateStructureN()
        {
            var vars = ParseCustomVariables();
            return ExpressionHelper.Evaluate(NStructureExpression, this, vars);
        }

        /// <summary>Calculates carotenoids content from crop state</summary>
        /// <returns>Carotenoids content (μg/cm²)</returns>
        public double CalculateCarotenoids()
        {
            var vars = ParseCustomVariables();
            double value = ExpressionHelper.Evaluate(CarotenoidExpression, this, vars);
            return Math.Max(0, Math.Min(500, value)); // Typical range: 0-500 μg/cm²
        }

        /// <summary>Calculates leaf mass per area parameter from crop state</summary>
        /// <returns>Leaf mass per area (g/cm²)</returns>
        public double CalculateLMA()
        {
            var vars = ParseCustomVariables();
            double value = ExpressionHelper.Evaluate(LMAExpression, this, vars);
            //return Math.Max(0, Math.Min(0.5, value)); // Typical range: 0-0.5 g/cm²
            return value;
        }

        /// <summary>Calculates incidence angle in degrees</summary>
        /// <returns>Incidence angle in degrees (degree)</returns>
        public double CalculateAlpha()
        {
            var vars = ParseCustomVariables();
            double value = ExpressionHelper.Evaluate(AlphaExpression, this, vars);
            //return Math.Max(0, Math.Min(0.5, value)); // Typical range: 0-0.5 g/cm²
            return value;
        }

        private Dictionary<string, double> ParseCustomVariables()
        {
            var vars = new Dictionary<string, double>();
            if (string.IsNullOrWhiteSpace(CustomVariables))
                return vars;

            foreach (var item in CustomVariables.Split(';'))
            {
                var parts = item.Split('=');
                if (parts.Length == 2 && double.TryParse(parts[1].Trim(), out double value))
                    vars.Add(parts[0].Trim(), value);
            }
            return vars;
        }

        #endregion

        #region 验证逻辑

        [EventSubscribe("StartOfSimulation")]
        private void OnSimulationStart(object sender, EventArgs e)
        {
            ValidateExpression(ChlorophyllExpression, "叶绿素");
            ValidateExpression(EWTExpression, "等效水厚度");
            ValidateExpression(NStructureExpression, "结构参数N");
            ValidateExpression(CarotenoidExpression, "类胡萝卜素");
            ValidateExpression(LMAExpression, "单位叶重");
            ValidateExpression(AlphaExpression, "入射角度");
        }

        private void ValidateExpression(string expression, string paramName)
        {
            if (!ExpressionHelper.Validate(expression, this, out string error))
                throw new Exception($"{paramName}公式验证失败: {error}");

            try
            {
                double testValue = paramName switch
                {
                    "叶绿素" => CalculateChlorophyll(),
                    "等效水厚度" => CalculateEWT(),
                    "N" => CalculateStructureN(),
                    "Car" => CalculateCarotenoids(),
                    "LMA" => CalculateLMA(),
                    _ => CalculateAlpha()
                };

                if (testValue < 0)
                    throw new Exception($"计算结果为负值: {testValue}");
            }
            catch (Exception ex)
            {
                throw new Exception($"{paramName}公式测试计算失败: {ex.Message}");
            }
        }

        #endregion
    }

    /// <summary>
    /// PROSPECT参数转换器接口
    /// </summary>
    public interface IProspectParameterConverter
    {
        /// <summary>Calculates chlorophyll content from crop state</summary>
        /// <returns>Chlorophyll content (μg/cm²)</returns>
        double CalculateChlorophyll();

        /// <summary>Calculates equivalent water thickness from crop state</summary>
        /// <returns>EWT (g/cm²)</returns>
        double CalculateEWT();

        /// <summary>Calculates leaf structure parameter from crop state</summary>
        /// <returns>Structure parameter N (unitless)</returns>
        double CalculateStructureN();

        /// <summary>Calculates carotenoids content from crop state</summary>
        /// <returns>Carotenoids content (μg/cm²)</returns>
        double CalculateCarotenoids();

        /// <summary>Calculates leaf mass per area parameter from crop state</summary>
        /// <returns>Leaf mass per area (g/cm²)</returns>
        double CalculateLMA();

        /// <summary>Calculates incidence angle in degrees</summary>
        /// <returns>Incidence angle in degrees (degree)</returns>
        double CalculateAlpha();
    }
}
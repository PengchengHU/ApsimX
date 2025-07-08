using System.Collections.Generic;
using APSIM.Documentation.Models.Types;
using APSIM.Shared.Documentation;
using Models.Core;
using Models.PROSAIL.PROSPECT;

class DocProspectModel: DocGeneric
{

    /// <summary>Initializes a new instance of the <see cref="DocProspectModel"/> class.</summary>
    /// <param name="model">The model to document.</param>
    public DocProspectModel(IModel model) : base(model) { }


    /// <summary>Document the model</summary>
    public override List<ITag> Document(int none = 0)
    {
        ProspectModel prospectModel = this.model as ProspectModel;
        List<ITag> newTags = new List<ITag>();
        Section mainSection = new Section("ProspectModel", new List<ITag>());
        mainSection.Children.Add(new Paragraph("The PROSPECT model simulates leaf optical properties (reflectance and transmittance) " +
                                                    "based on leaf biochemical and structural properties."));
        newTags.Add(mainSection);
        Section inputParamsSection = new Section("Input Parameters", new List<ITag>());
        inputParamsSection.Children.Add(new Paragraph("The following parameters control the leaf optical properties:"));
        inputParamsSection.Children.Add(new Paragraph($"N (Leaf structure parameter): {prospectModel.N}"));
        inputParamsSection.Children.Add(new Paragraph($"CAB (Chlorophyll a + b content, μg/cm²): {prospectModel.CAB}"));
        inputParamsSection.Children.Add(new Paragraph($"CAR (Carotenoid content, μg/cm²): {prospectModel.CAR}"));
        inputParamsSection.Children.Add(new Paragraph($"EWT (Equivalent Water Thickness, g/cm²): {prospectModel.EWT}"));
        inputParamsSection.Children.Add(new Paragraph($"LMA (Leaf Mass per Area, g/cm²): {prospectModel.LMA}"));
        if (!string.IsNullOrEmpty(prospectModel.ANT) && prospectModel.ANT != "0.0")
            inputParamsSection.Children.Add(new Paragraph($"ANT (Anthocyanin content, μg/cm²): {prospectModel.ANT}"));
        if (!string.IsNullOrEmpty(prospectModel.BROWN) && prospectModel.BROWN != "0.0")
            inputParamsSection.Children.Add(new Paragraph($"BROWN (Brown pigment content): {prospectModel.BROWN}"));
        if (!string.IsNullOrEmpty(prospectModel.PROT) && prospectModel.PROT != "0.0")
            inputParamsSection.Children.Add(new Paragraph($"PROT (Protein content, g/cm²): {prospectModel.PROT}"));
        if (!string.IsNullOrEmpty(prospectModel.CBC) && prospectModel.CBC != "0.0")
            inputParamsSection.Children.Add(new Paragraph($"CBC (NonProt Carbon-based constituent content, g/cm²): {prospectModel.CBC}"));
        inputParamsSection.Children.Add(new Paragraph($"Alpha (Incidence angle, degrees): {prospectModel.Alpha}"));

        newTags.Add(inputParamsSection);
        Section outputParamsSection = new Section("Outputs", new List<ITag>());
        outputParamsSection.Children.Add(new Paragraph("The model provides the following outputs:"));
        outputParamsSection.Children.Add(new Paragraph("- Full spectrum (400 - 2500 nm) leaf reflectance and transmittance"));
        newTags.Add(outputParamsSection);

        if (prospectModel.EnableSQLiteOutput)
        {
            Section databaseOutputSection = new Section("Database Output", new List<ITag>());
            databaseOutputSection.Children.Add(new Paragraph("Spectral data is saved to a SQLite database with the following details:"));
            // databaseOutputSection.Children.Add(new Paragraph($"- Database file: {prospectModel.ProspectSQLiteDatabasePath}")); // This property is private and not accessible here.
            databaseOutputSection.Children.Add(new Paragraph($"- Wavelengths: {prospectModel.OutputWavelengthRange} (supports ranges like '400-500', lists like '400, 500, 600', or mixed formats like '400, 500-600, 700')"));
            databaseOutputSection.Children.Add(new Paragraph($"- Logging level: {prospectModel.LoggingLevel} (controls verbosity of messages)"));
            databaseOutputSection.Children.Add(new Paragraph("The database contains spectral data for each simulation day when the plant is alive, including reflectance and transmittance values."));
            newTags.Add(databaseOutputSection);
        }

        return newTags;
    }
}
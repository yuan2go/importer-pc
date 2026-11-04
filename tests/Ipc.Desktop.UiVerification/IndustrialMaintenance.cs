static partial class Program
{
    static Task VerifyIndustrialMaintenanceUi(string evidence, List<string> lines, bool baseline)
        => VerifyUninstallResultActions(evidence, lines);
}

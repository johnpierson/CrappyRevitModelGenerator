using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

namespace CrappyRevitModelGenerator.Commands
{
    /// <summary>
    /// Manual trigger for <see cref="AutomationRunner"/> (Add-Ins &gt; External Tools &gt; CRMG
    /// Automation), reading the same <c>CRMG_AUTOMATION</c> environment variable that
    /// <see cref="App"/> checks automatically at startup. Kept for someone who wants to re-run
    /// automation by hand in an already-open Revit session without a restart.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    [Journaling(JournalingMode.NoCommandData)]
    public class AutomationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var parameters = AutomationRunner.ReadParametersFromEnvironment();
            if (parameters == null)
            {
                message = "The " + AutomationRunner.EnvironmentVariable + " environment variable is not set to an existing JSON parameters file.";
                return Result.Cancelled;
            }

            return AutomationRunner.Run(commandData.Application, parameters, out message);
        }
    }

    /// <summary>Lets the automation command run with no document open (it can create one from a template).</summary>
    public class AlwaysAvailable : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => true;
    }
}

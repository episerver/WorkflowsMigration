using System.Linq;

namespace EPiServer.Commerce.Tools.WorkflowMigration
{
    public class Program
    {
        static void Main(string[] args)
        {
            if (args == null || args.Count() < 1)
            {
                System.Console.Error.WriteLine("Invalid parameters");
                System.Console.Error.WriteLine("Executable <WorkflowProjectFolder> [<WorkflowConfigFilePath>] [<ActivityProjectFolder>]");
                System.Environment.Exit(1);
            }

            var workflowFolder = args[0];
            var workflowConfigFile = args.Count() > 1 ? args[1] : null;
            var activityFolder = args.Count() > 2 ? args[2] : workflowFolder;

            new WorkflowMigrator(workflowFolder, workflowConfigFile).Migrate();
            new ActivityMigrator(activityFolder).Migrate();
        }
    }
}

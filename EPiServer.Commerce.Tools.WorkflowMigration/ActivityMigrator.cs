using Mediachase.Commerce.Engine;
using Mediachase.Commerce.WorkflowCompatibility;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace EPiServer.Commerce.Tools.WorkflowMigration
{
    /// <summary>
    /// Represents class to migrate legacy activities to the new system.
    /// </summary>
    public class ActivityMigrator : BaseMigrator
    {
        private string _activityProjectPath;

        public ActivityMigrator(string activityProjectPath)
        {
            ThrowIfPathDoesNotExist(activityProjectPath);
            _activityProjectPath = EnsureDirectoryPath(activityProjectPath);
        }

        public override void Migrate()
        {
            RemoveProjectItems(_activityProjectPath, "*Activity*.Designer.cs");

            foreach (var file in Directory.GetFiles(_activityProjectPath, "*Activity*.cs", SearchOption.AllDirectories))
            {
                // source file is not expected be be large, so we could use ReadAllText
                // consider of using StreamReader when reading large file
                var textContent = File.ReadAllText(file);

                textContent = RemoveValidationOptionAttribute(textContent);
                textContent = RemoveWorkflowUsingStatements(textContent);
                textContent = AddUsingStatements(textContent);
                textContent = EnsureClassModifiers(textContent);

                File.WriteAllText(file, textContent);
            }
            DeleteLegacyReferences(_activityProjectPath);
        }

        public string EnsureClassModifiers(string textContent)
        {
            var sourceSyntaxTree = CSharpSyntaxTree.ParseText(textContent);
            var root = sourceSyntaxTree.GetRoot();
            var myClass = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
            if (myClass.Modifiers.Any(m => m.Text == "abstract"))
            {
                return textContent;
            }
             // remove partial modifier
            var partialModifier = myClass.Modifiers.FirstOrDefault(m => m.Text == "partial");
            if (partialModifier != null)
            {
                textContent = textContent.Replace(" partial class ", " class ");
            }

            // if there is not imeplementation of "Execute", mark the class as abstract
            if (!myClass.Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.Value.ToString() == "Execute" 
                    && m.ParameterList.Parameters.Count > 0 && m.ParameterList.Parameters[0].Type.ToString() == typeof(ActivityExecutionContext).Name))
            {
                textContent = textContent.Replace(" class ", " abstract class ");
            }
            return textContent;
        }

        public string RemoveValidationOptionAttribute(string textContent)
        {
            return Regex.Replace(textContent, @"\[.*?ValidationOption.*?\]\s*\n", string.Empty);
        }

        public string RemoveWorkflowUsingStatements(string textContent)
        {
            return Regex.Replace(textContent, @"using System.Workflow.*?;\s*\n", string.Empty);
        }

        public string AddUsingStatements(string textContent)
        {
            int position = textContent.IndexOf("using ");
            if (position == -1)
            {
                position = 0;
            }

            return textContent.Insert(position, string.Format("using {0};{1}using {2};{1}", typeof(ActivityFlow).Namespace, Environment.NewLine, typeof(Activity).Namespace));
        }
    }
}

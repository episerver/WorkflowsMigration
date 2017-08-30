using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EPiServer.Commerce.Tools.WorkflowMigration
{
    public abstract class BaseMigrator
    {
        public abstract void Migrate();

        protected void ThrowIfPathDoesNotExist(string path)
        {
            if (string.IsNullOrEmpty(path) ||  !Directory.Exists(path))
            {
                throw new DirectoryNotFoundException("The following directory could not be found: " + path);
            }
        }

        protected void RemoveProjectItems(string projectPath, string searchPattern)
        {
            var projectFile = FindProjectFile(projectPath);
            var xmlRoot = XElement.Load(projectFile);
            var rootFolder = new Uri(projectPath);
            foreach (var file in Directory.GetFiles(projectPath, searchPattern, SearchOption.AllDirectories))
            {
                File.Delete(file);
                var fileInfo = new FileInfo(file);
                var fileFolder = EnsureDirectoryPath(fileInfo.DirectoryName);
                var folder = rootFolder.MakeRelativeUri(new Uri(fileFolder)).ToString().Replace("/", "\\");
                var relativePath = Path.Combine(folder, fileInfo.Name);
                xmlRoot.Descendants().Where(t => t.Name.LocalName == "Compile" && t.Attribute("Include").Value == relativePath).Remove();
                xmlRoot.Descendants().Where(t => t.Name.LocalName == "Content" && t.Attribute("Include").Value == relativePath).Remove();
            }
            xmlRoot.Save(projectFile);
        }

        protected void AddFileToProject(string projectPath, string filePath)
        {
            var rootFolder = new Uri(projectPath);
            var fileInfo = new FileInfo(filePath);
            var fileFolder = EnsureDirectoryPath(fileInfo.DirectoryName);
            var folder = rootFolder.MakeRelativeUri(new Uri(fileFolder)).ToString().Replace("/", "\\");
            var relativePath = Path.Combine(folder, fileInfo.Name);

            var projectFile = FindProjectFile(projectPath);
            var xmlRoot = XElement.Load(projectFile);
            XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
            xmlRoot.Descendants().First(t => t.Name.LocalName == "ItemGroup").Add(new XElement(ns + "Compile", new XAttribute("Include", relativePath)));

            xmlRoot.Save(projectFile);
        }

        protected void DeleteLegacyReferences(string projectPath)
        {
            var projectFile = FindProjectFile(projectPath);
            var xmlRoot = XElement.Load(projectFile);

            xmlRoot.Descendants().Where(t => t.Name.LocalName == "Import" && t.Attribute("Project").Value == @"$(MSBuildToolsPath)\Workflow.Targets").Remove();
            xmlRoot.Descendants().Where(t => t.Name.LocalName == "Reference" &&
                                        (t.Attribute("Include").Value == "System.Workflow.Activities"
                                        || t.Attribute("Include").Value == "System.Workflow.ComponentModel"
                                        || t.Attribute("Include").Value == "System.Workflow.Runtime")
                                    ).Remove();

            xmlRoot.Save(projectFile);

            var assemblyInfoFile = FindFile(projectPath, "AssemblyInfo.cs");
            var textContent = File.ReadAllText(assemblyInfoFile);
            textContent = Regex.Replace(textContent, @"using System.Workflow.*?;", string.Empty);
            File.WriteAllText(assemblyInfoFile, textContent);
        }

        protected string EnsureDirectoryPath(string path)
        {
            if (!path.EndsWith("\\"))
            {
                path += "\\";
            }
            return path;
        }

        private string FindProjectFile(string projectPath)
        {
            return FindFile(projectPath, "*.csproj");
        }

        private string FindFile(string projectPath, string searchPattern)
        {
            return Directory.GetFiles(projectPath, searchPattern, SearchOption.AllDirectories).First();
        }
    }
}

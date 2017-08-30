using Mediachase.Commerce.Engine;
using Mediachase.Commerce.Orders.Managers;
using Mediachase.Commerce.WorkflowCompatibility;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace EPiServer.Commerce.Tools.WorkflowMigration
{
    /// <summary>
    /// Represents class to migrate legacy workflows.
    /// </summary>
    public class WorkflowMigrator : BaseMigrator
    {
        private string _workflowProjectPath;

        private CodeCompileUnit _compileUnit;
        private XElement _workflowConfig;

        private Regex _namespacePattern = new Regex(@"namespace (.*?)[\s{]", RegexOptions.Compiled);
        private Regex _classNamePattern = new Regex("partial class (.*?) :", RegexOptions.Compiled);

        public WorkflowMigrator(string workflowProjectPath)
            : this(workflowProjectPath, null)
        { }

        public WorkflowMigrator(string workflowProjectPath, string workflowConfig)
        {
            ThrowIfPathDoesNotExist(workflowProjectPath);
            _workflowProjectPath = EnsureDirectoryPath(workflowProjectPath);
            _compileUnit = new CodeCompileUnit();
            _workflowConfig = !string.IsNullOrEmpty(workflowConfig) ? XElement.Load(workflowConfig) : null;
        }

        public override void Migrate()
        {
            foreach (var fileInfo in Directory.GetFiles(_workflowProjectPath, "*.xoml.cs", SearchOption.AllDirectories).Select(f => new FileInfo(f)))
            {
                Migrate(fileInfo);
            }

            // Delete legacy workflow files
            RemoveProjectItems(_workflowProjectPath, "*.xoml*");
            DeleteLegacyReferences(_workflowProjectPath);
        }

        private void Migrate(FileInfo workflowCodeFile)
        {
            var textContent = File.ReadAllText(workflowCodeFile.FullName);
            var xomlFileName = Path.GetFileNameWithoutExtension(workflowCodeFile.Name);
            var matches = _namespacePattern.Match(textContent);
            var namespaceString = matches.Groups.Count > 1 ? matches.Groups[1].Value : "Mediachase.Commerce.Workflow";
            matches = _classNamePattern.Match(textContent);
            var className = matches.Groups.Count > 1 ? matches.Groups[1].Value : Path.GetFileNameWithoutExtension(xomlFileName);
            var wfName = className;
            var configItem = _workflowConfig != null ? _workflowConfig.Descendants().Where(t => t.Name.LocalName == "add" && t.Attribute("type").Value.Contains(string.Format("{0}.{1},", namespaceString, className))).FirstOrDefault()
                                            : null;
            if (configItem != null)
            {
                wfName = configItem.Attribute("name").Value;
            }

            var wfConfig = Directory.GetFiles(workflowCodeFile.Directory.FullName, xomlFileName, SearchOption.AllDirectories).Single();

            var model = new WorkflowModel()
            {
                CodeBehindContent = textContent,
                XomlFilePath = wfConfig,
                NamespaceString = namespaceString,
                Name = wfName,
                Directory = workflowCodeFile.Directory.FullName
            };

            Migrate(model);
        }

        private void Migrate(WorkflowModel workflowModel)
        {
            var sourceSyntaxTree = CSharpSyntaxTree.ParseText(workflowModel.CodeBehindContent);
            // create namespace
            var codeNamespace = new CodeNamespace(workflowModel.NamespaceString);
            _compileUnit.Namespaces.Add(codeNamespace);
            AddUsingStatements(codeNamespace, sourceSyntaxTree.GetCompilationUnitRoot().Usings);
            var targetClass = CreateClass(workflowModel.Name);
            codeNamespace.Types.Add(targetClass);

            // implement Configure method
            using (var reader = File.OpenText(workflowModel.XomlFilePath))
            {
                var configureMethod = CreateConfigureMethod(reader);
                targetClass.Members.Add(configureMethod);
            }

            // literal code
            MigrateLegacyWorkflowCode(sourceSyntaxTree, targetClass);

            var destinationFilePath = Path.Combine(workflowModel.Directory, string.Format("{0}.cs", workflowModel.Name));
            GenerateCSharpCode(destinationFilePath);
            AddFileToProject(_workflowProjectPath, destinationFilePath);
        }

        private void AddUsingStatements(CodeNamespace codeNamespace, SyntaxList<UsingDirectiveSyntax> usingLists)
        {
            // add all using statements
            foreach (var usingDirective in usingLists)
            {
                var val = usingDirective.Name.ToString();
                if (val.StartsWith("System.Workflow"))
                {
                    continue;
                }
                codeNamespace.Imports.Add(new CodeNamespaceImport(val));
            }
        }

        public CodeTypeDeclaration CreateClass(string wfName)
        {
            var targetClass = new CodeTypeDeclaration(wfName);
            targetClass.IsClass = true;
            targetClass.TypeAttributes = System.Reflection.TypeAttributes.Public;
            targetClass.BaseTypes.Add(typeof(ActivityFlow));

            // add ActivityFlowConfiguration attribute
            var attribute = new CodeAttributeDeclaration(new CodeTypeReference(typeof(ActivityFlowConfigurationAttribute)));
            attribute.Arguments.Add(new CodeAttributeArgument("Name", new CodePrimitiveExpression(GetConfiguredWorkflowName(wfName))));
            AddSettingLegacyMode(attribute, wfName);
            targetClass.CustomAttributes.Add(attribute);
            return targetClass;
        }

        public CodeMemberMethod CreateConfigureMethod(TextReader xomlFileReader)
        {
            // implement Configure method
            var configureMethod = new CodeMemberMethod();
            configureMethod.Attributes = MemberAttributes.Public | MemberAttributes.Override;
            configureMethod.Name = "Configure";
            configureMethod.ReturnType = new CodeTypeReference(typeof(ActivityFlowRunner));
            configureMethod.Parameters.Add(new CodeParameterDeclarationExpression(typeof(ActivityFlowRunner), "activityFlow"));

            // Configure method content
            var returnStatement = new CodeMethodReturnStatement();
            returnStatement.Expression = GenerateActivityFlowExpression(xomlFileReader);
            configureMethod.Statements.Add(returnStatement);
            return configureMethod;
        }

        public void MigrateLegacyWorkflowCode(string legacyWorkflowCode, CodeTypeDeclaration targetClass)
        {
            MigrateLegacyWorkflowCode(CSharpSyntaxTree.ParseText(legacyWorkflowCode), targetClass);
        }

        public void MigrateLegacyWorkflowCode(SyntaxTree syntaxTree, CodeTypeDeclaration targetClass)
        {
            var root = syntaxTree.GetRoot();
            var myClass = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
            foreach (var member in myClass.Members)
            {
                if (member is PropertyDeclarationSyntax)
                {
                    var property = member as PropertyDeclarationSyntax;
                    targetClass.Members.Add(TweakProperty(property));
                }
                else if (member is MethodDeclarationSyntax)
                {
                    // looking for condition method to tweak it, so it returns boolean
                    var methodDeclaration = member as MethodDeclarationSyntax;
                    if (methodDeclaration != null && methodDeclaration.ParameterList.Parameters.Count > 1 && methodDeclaration.ParameterList.Parameters[1].Type.ToString() == "ConditionalEventArgs")
                    {
                        targetClass.Members.Add(TweakConditionMethods(methodDeclaration));
                    }
                    else
                    {
                        targetClass.Members.Add(new CodeSnippetTypeMember(member.ToFullString()));
                    }
                }
            }
        }

        private CodeMemberField TweakProperty(PropertyDeclarationSyntax property)
        {
            //In the new workflow engine, workflow's properties must be decorated with the ActivityFlowContextProperty attribute, and they must have both public getter and setter.
            //The tool will convert workflow's properties with default getter and setter. If there is any logic implementation in the getter or setter, we need update the converted 
            //properties later. But it's officially not possible to generate default (empty) getter & setter with Codedom in C#. Here is a special treatment for this issue, however,
            //following tips on http://stackoverflow.com/questions/13679171/how-to-generate-empty-get-set-statements-using-codedom-in-c-sharp
            var attribute = new CodeAttributeDeclaration(new CodeTypeReference(typeof(ActivityFlowContextPropertyAttribute)));
            MemberAttributes modifier = property.Modifiers.Any(m => m.Text.ToLower() == "private") ? MemberAttributes.Private : MemberAttributes.Public;
            if (!property.Modifiers.Any(m => m.Text.ToLower() == "virtual"))
            {
                modifier |= MemberAttributes.Final;
            }
            var field = new CodeMemberField
            {
                Attributes = modifier,
                Name = property.Identifier.Text,
                Type = new CodeTypeReference(property.Type.ToFullString()),
            };
            field.CustomAttributes.Add(attribute);
            field.Name += " { get; set; }//";
            
            return field;
        }

        private string GetConfiguredWorkflowName(string wfName)
        {
            string result = wfName;
            switch (wfName)
            { 
                case "CartCheckoutWorkflow":
                    result = OrderGroupWorkflowManager.CartCheckOutWorkflowName;
                    break;
                case "LegacyCartPrepareWorkflow":
                case "CartPrepareWorkflow":
                    result = OrderGroupWorkflowManager.CartPrepareWorkflowName;
                    break;
                case "LegacyCartValidateWorkflow":
                case "CartValidateWorkflow":
                    result = OrderGroupWorkflowManager.CartValidateWorkflowName;
                    break;
                case "CheckAndReserveInstorePickupWorkflow":
                    result = OrderGroupWorkflowManager.CheckAndReserveInstorePickupWorkflowName;
                    break;
                case "LegacyPOCalculateTotalsWorkflow":
                case "POCalculateTotalsWorkflow":
                    result = OrderGroupWorkflowManager.OrderCalculateTotalsWorkflowName;
                    break;
                case "POCompleteShipmentWorkflow":
                    result = OrderGroupWorkflowManager.OrderCompleteShipmentWorkflowName;
                    break;
                case "LegacyPORecalculateWorkflow":
                case "PORecalculateWorkflow":
                    result = OrderGroupWorkflowManager.OrderRecalculateWorkflowName;
                    break;
                case "POSaveChangesWorkflow":
                    result = OrderGroupWorkflowManager.OrderSaveChangesWorkflowName;
                    break;
                case "POSplitShipmentsWorkflow":
                    result = OrderGroupWorkflowManager.OrderSplitShipmentsWorkflowName;
                    break;
                case "ReturnFormCompleteWorkflow":
                    result = OrderGroupWorkflowManager.ReturnFormCompleteWorkflowName;
                    break;
                case "ReturnFormRecalculateWorkflow":
                    result = OrderGroupWorkflowManager.ReturnFormRecalculateWorkflowName;
                    break;
            }

            return result;
        }

        private void AddSettingLegacyMode(CodeAttributeDeclaration attribute, string wfName)
        {
            if (wfName.StartsWith("Legacy"))
            {
                attribute.Arguments.Add(new CodeAttributeArgument("LegacyMode", new CodePrimitiveExpression(true)));
            }
        }

        private ExpressionStatementSyntax ParseExpressStatement(string content)
        {
            return SyntaxFactory.ExpressionStatement(SyntaxFactory.ParseExpression(content));
        }

        private CodeMemberMethod TweakConditionMethods(MethodDeclarationSyntax method)
        {
            var variableName = method.ParameterList.Parameters[1].Identifier.ToString();
            var result = new CodeMemberMethod();
            result.Attributes = MemberAttributes.Private;
            result.ReturnType = new CodeTypeReference(typeof(bool));
            result.Name = method.Identifier.ToString();
            result.Statements.Add(new CodeVariableDeclarationStatement(typeof(ConditionalEventArgs), variableName, new CodeObjectCreateExpression(typeof(ConditionalEventArgs))));
            result.Statements.Add(new CodeSnippetStatement(method.Body.Statements.ToFullString()));
            result.Statements.Add(new CodeMethodReturnStatement(new CodeArgumentReferenceExpression(variableName + ".Result")));

            return result;
        }

        private CodeExpression GenerateActivityFlowExpression(TextReader streamReader)
        {
            var codeSnippet = new StringBuilder();

            using (var reader = XmlReader.Create(streamReader))
            {
                while (reader.Read())
                {
                    switch (reader.NodeType)
                    {
                        case XmlNodeType.Element:
                        case XmlNodeType.EndElement:
                            switch (reader.Name)
                            {
                                case "SequentialWorkflowActivity":
                                    if (reader.IsStartElement())
                                    {
                                        StartWorkflow(codeSnippet, reader);
                                    }
                                    else
                                    {
                                        EndWorkflow(codeSnippet, reader);
                                    }
                                    break;
                                case "IfElseActivity":
                                    if (reader.IsStartElement())
                                    {
                                        StartIf(codeSnippet, reader);
                                    }
                                    else
                                    {
                                        EndIf(codeSnippet, reader);
                                    }
                                    break;
                                case "IfElseBranchActivity":
                                    if (reader.IsStartElement())
                                    {
                                        StartIfElseBranch(codeSnippet, reader);
                                    }
                                    else
                                    {
                                        EndIfElseBranch(codeSnippet, reader);
                                    }
                                    break;
                                case "IfElseBranchActivity.Condition":
                                case "CodeCondition":
                                    if (reader.IsStartElement())
                                    {
                                        StartIfElseCondition(codeSnippet, reader);
                                    }
                                    else
                                    {
                                        EndIfElseCondition(codeSnippet, reader);
                                    }
                                    break;
                                default:
                                    AddStep(codeSnippet, reader);
                                    break;
                            }
                            break;
                    }
                }
            }

            return new CodeSnippetExpression(codeSnippet.ToString());
        }

        private string Indent(int count)
        {
            return "".PadLeft(count * 4);
        }

        private int _indentCount = 4;

        private string NewLine()
        {
            return Environment.NewLine + Indent(_indentCount);
        }

        private void StartWorkflow(StringBuilder codeSnippet, XmlReader reader)
        {
            codeSnippet.Append("activityFlow");
        }

        private void EndWorkflow(StringBuilder codeSnippet, XmlReader reader)
        {
        }

        private bool _inIfCondition = false;

        private void StartIf(StringBuilder codeSnippet, XmlReader reader)
        {
            if (_inIfCondition)
            {
                // nested condition
                _inIfCondition = false;
            }
        }

        private void EndIf(StringBuilder codeSnippet, XmlReader reader)
        {
            _indentCount--;
            _inIfCondition = false;
            codeSnippet.Append(NewLine()).Append(".EndIf()");
        }

        private void StartIfElseBranch(StringBuilder codeSnippet, XmlReader reader)
        {
            if (_inIfCondition)
            {
                // this means we are in "else" branch if a previous "if"
                _indentCount--;
                codeSnippet.Append(NewLine()).Append(".Else()");
                _indentCount++;
            }
            else
            {
                // "if" branch
                _inIfCondition = true;
                codeSnippet.Append(NewLine()).Append(".If(() => ");
                _indentCount++;
            }
        }

        private void EndIfElseBranch(StringBuilder codeSnippet, XmlReader reader)
        {
            
        }

        private void StartIfElseCondition(StringBuilder codeSnippet, XmlReader reader)
        {
            var functionName = reader["Condition"];
            if (!string.IsNullOrEmpty(functionName))
            {
                codeSnippet.Append(string.Format("{0}())", functionName));
            }
        }

        private void EndIfElseCondition(StringBuilder codeSnippet, XmlReader reader)
        {

        }

        private void AddStep(StringBuilder codeSnippet, XmlReader reader)
        {
            var activityClassName = reader.LocalName;
            if (activityClassName == "FaultHandlersActivity")
            {
                return;
            }

            if (!string.IsNullOrEmpty(reader.Prefix))
            {
                var namespaceDeclarePrefix = "clr-namespace:";
                var namespaceDeclarative = reader.LookupNamespace(reader.Prefix);
                namespaceDeclarative = namespaceDeclarative.Substring(namespaceDeclarative.IndexOf(namespaceDeclarePrefix) + namespaceDeclarePrefix.Length);
                var activityNamespace = namespaceDeclarative.IndexOf(";") > 0 ? namespaceDeclarative.Substring(0, namespaceDeclarative.IndexOf(";"))
                                                                        : namespaceDeclarative;
                activityClassName = string.Format("{0}.{1}", activityNamespace, reader.LocalName);
            }

            codeSnippet.Append(NewLine()).Append(string.Format(".Do<{0}>()", activityClassName));
        }

        private void GenerateCSharpCode(string destinationFile)
        {
            var provider = CodeDomProvider.CreateProvider("CSharp");
            var options = new CodeGeneratorOptions();
            options.BracingStyle = "C";
            Directory.CreateDirectory(new FileInfo(destinationFile).Directory.FullName);
            using (var sourceWriter = new StreamWriter(destinationFile))
            {
                provider.GenerateCodeFromCompileUnit(_compileUnit, sourceWriter, options);
            }
            _compileUnit.Namespaces.Clear();
        }
    }
}

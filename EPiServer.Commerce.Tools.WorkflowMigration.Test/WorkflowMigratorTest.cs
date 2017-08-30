using Mediachase.Commerce.Engine;
using Mediachase.Commerce.WorkflowCompatibility;
using System;
using System.CodeDom;
using System.IO;
using Xunit;

namespace EPiServer.Commerce.Tools.WorkflowMigration.Test
{

    public class WorkflowMigratorTest
    {
        private WorkflowMigrator _subject;


        public WorkflowMigratorTest()
        {
            _subject = new WorkflowMigrator("C:\\");
        }

        [Fact]
        public void Constructor_WhenPathDoesNotExist_ShouldThrowException()
        {
            Assert.Throws<DirectoryNotFoundException>(() => new WorkflowMigrator("XX:\\"));
        }

        [Fact]
        public void Constructor_WhenPathIsNotValid_ShouldThrowException()
        {
            Assert.Throws<DirectoryNotFoundException>(() => new WorkflowMigrator("hello\\world"));
        }

        [Fact]
        public void CreateClass_WithWorkflowName_ShouldReturnActivityFlow()
        {
            var actual = _subject.CreateClass("Foo");

            Assert.True(actual.IsClass);
            Assert.Equal(System.Reflection.TypeAttributes.Public, actual.TypeAttributes);
            Assert.Equal(typeof(ActivityFlow).FullName, actual.BaseTypes[0].BaseType);
            Assert.Equal(typeof(ActivityFlowConfigurationAttribute).FullName, actual.CustomAttributes[0].AttributeType.BaseType);
            Assert.Equal("Name", actual.CustomAttributes[0].Arguments[0].Name);

            var attributeExpression = actual.CustomAttributes[0].Arguments[0].Value as CodePrimitiveExpression;
            Assert.Equal("Foo", attributeExpression.Value.ToString());
        }

        [Fact]
        public void CreateConfigureMethod_FromXomlFile_ShouldGenerateConfigureMethod()
        {
            var xomlFileContent = @"
                <SequentialWorkflowActivity x:Class='Legacy.Workflow.FooWorkflow' x:Name='FooWorkflow' xmlns:ns0='clr-namespace:Legacy.Workflow.Activities' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/workflow'>
	                <ns0:FirstActivity x:Name='firstActivity1' />

                  <IfElseActivity x:Name='ifElseActivity0'>
                    <IfElseBranchActivity x:Name='ifElseBranchActivity0'>
                      <IfElseBranchActivity.Condition>
                        <CodeCondition Condition='ShouldAdjustInventory' />
                      </IfElseBranchActivity.Condition>
                      <IfElseActivity x:Name='ifElseActivity1'>
                        <IfElseBranchActivity x:Name='ifElseBranchActivity1'>
                          <IfElseBranchActivity.Condition>
                            <CodeCondition Condition='CheckInstoreInventory' />
                          </IfElseBranchActivity.Condition>
                          <ns0:CheckInstoreInventoryActivity OrderGroup='{ActivityBind PurchaseOrderRecalculateVNextWorkflow,Path=OrderGroup}' x:Name='checkInstoreInventoryActivity1' />
                        </IfElseBranchActivity>
                        <IfElseBranchActivity x:Name='ifElseBranchActivity2'>
                          <ns0:CheckInventoryActivity x:Name='checkInventoryActivity1' />
                        </IfElseBranchActivity>
                      </IfElseActivity>
                    </IfElseBranchActivity>
                  </IfElseActivity>
  
	                <ns0:LastActivity x:Name='lastActivity1' />
                </SequentialWorkflowActivity>
                ";
            var actual = _subject.CreateConfigureMethod(new StringReader(xomlFileContent));

            Assert.Equal(MemberAttributes.Public | MemberAttributes.Override, actual.Attributes);
            Assert.Equal("Configure", actual.Name);
            Assert.Equal(typeof(ActivityFlowRunner).FullName, actual.ReturnType.BaseType);
            Assert.Equal(1, actual.Parameters.Count);
            Assert.Equal(typeof(ActivityFlowRunner).FullName, actual.Parameters[0].Type.BaseType);

            var returnStatement = actual.Statements[0] as CodeMethodReturnStatement;
            Assert.NotNull(returnStatement);
            Assert.True(returnStatement.Expression is CodeSnippetExpression);

            var expected = @"activityFlow
                .Do<Legacy.Workflow.Activities.FirstActivity>()
                .If(() => ShouldAdjustInventory())
                    .If(() => CheckInstoreInventory())
                        .Do<Legacy.Workflow.Activities.CheckInstoreInventoryActivity>()
                    .Else()
                        .Do<Legacy.Workflow.Activities.CheckInventoryActivity>()
                    .EndIf()
                .EndIf()
                .Do<Legacy.Workflow.Activities.LastActivity>()";
            Assert.Equal(RemoveSpaces(expected), RemoveSpaces((returnStatement.Expression as CodeSnippetExpression).Value));
        }

        private string RemoveSpaces(string text)
        {
            return text.Trim().Replace(" ", string.Empty).Replace("\r\n", string.Empty).Replace("\n", string.Empty);
        }
    
        [Fact]
        public void MigrateLegacyWorkflowCode_ShouldMigrateLegacyMethod()
        {
            var code = @"public class Foo { private bool Bar() { return true; } }";
            var typeDeclaration = _subject.CreateClass("Foo");
            _subject.MigrateLegacyWorkflowCode(code, typeDeclaration);

            var actual = typeDeclaration.Members[0] as CodeSnippetTypeMember;
            Assert.Equal("private bool Bar() { return true; }", actual.Text.Trim());
        }

        [Fact]
        public void MigrateLegacyWorkflowCode_ShouldMigrateLegacyProperty()
        {
            var code = @"public class Foo { private bool Bar { get; set; } }";
            var typeDeclaration = _subject.CreateClass("Foo");
            _subject.MigrateLegacyWorkflowCode(code, typeDeclaration);

            var actual = typeDeclaration.Members[0] as CodeMemberField;
            Assert.True(actual.Name.Contains("Bar { get; set; }"));
        }

        [Fact]
        public void MigrateLegacyWorkflowCode_ShouldMigrateConditionMethod()
        {
            var code = @"public class Foo 
                        { 
                            private void RunProcessPayment(object sender, ConditionalEventArgs e)
                            {
                                e.Result = !this.IsIgnoreProcessPayment;
                            }
                        }";
            var typeDeclaration = _subject.CreateClass("Foo");
            _subject.MigrateLegacyWorkflowCode(code, typeDeclaration);

            var actual = typeDeclaration.Members[0] as CodeMemberMethod;
            Assert.Equal("RunProcessPayment", actual.Name);
            Assert.Equal(typeof(Boolean).FullName, actual.ReturnType.BaseType);
            Assert.Equal(0, actual.Parameters.Count);
            Assert.Equal(MemberAttributes.Private, actual.Attributes);
            Assert.Equal(3, actual.Statements.Count);

            var firstStatement = actual.Statements[0] as CodeVariableDeclarationStatement;
            var secondStatement = actual.Statements[1] as CodeSnippetStatement;
            var lastStatement = actual.Statements[2] as CodeMethodReturnStatement;

            Assert.Equal(typeof(ConditionalEventArgs).FullName, firstStatement.Type.BaseType);
            Assert.Equal("e", firstStatement.Name);
            Assert.Equal("e.Result = !this.IsIgnoreProcessPayment;", secondStatement.Value.Trim());
            Assert.Equal("e.Result", (lastStatement.Expression as CodeArgumentReferenceExpression).ParameterName);
        }
    }
}

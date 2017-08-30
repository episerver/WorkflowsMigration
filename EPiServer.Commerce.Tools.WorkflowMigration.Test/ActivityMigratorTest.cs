using EPiServer.Commerce.Tools.WorkflowMigration;
using System.IO;
using Xunit;

namespace EPiServer.Commerce.Tools.WorkflowMigration.Test
{

    public class ActivityMigratorTest
    {
        private ActivityMigrator _subject;


        public ActivityMigratorTest()
        {
            _subject = new ActivityMigrator("C:\\");
        }

        [Fact]
        public void Constructor_WhenPathDoesNotExist_ShouldThrowException()
        {
            Assert.Throws<DirectoryNotFoundException>(() => new ActivityMigrator("XX:\\"));
        }

        [Fact]
        public void Constructor_WhenPathIsNotValid_ShouldThrowException()
        {
            Assert.Throws<DirectoryNotFoundException>(() => new ActivityMigrator("hello\\world"));
        }

        [Fact]
        public void EnsureClassModifiers_WhenClassIsAbstract_ShouldNotModifyAnything()
        {
            string textContent, expected;
            textContent = expected = "public abstract class Foo { }";
            var actual = _subject.EnsureClassModifiers(textContent);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void EnsureClassModifiers_WhenClassIsPartial_ShouldRemovePartial()
        {
            var textContent = @"
                public partial class Foo
                {
                    protected override ActivityExecutionStatus Execute(ActivityExecutionContext executionContext) { return null; }
                }";
            var expected = @"
                public class Foo
                {
                    protected override ActivityExecutionStatus Execute(ActivityExecutionContext executionContext) { return null; }
                }";
            var actual = _subject.EnsureClassModifiers(textContent);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void EnsureClassModifiers_WhenClassDidNotImplementExecuteMethod_ShouldBeMarkedAsAbstract()
        {
            var textContent = "public class Foo { }";
            var expected = "public abstract class Foo { }";
            var actual = _subject.EnsureClassModifiers(textContent);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void EnsureClassModifiers_WhenClassImplementedExecuteMethod_ShouldNotModifyAnything()
        {
            string textContent, expected;
            textContent = expected = @"
                public class Foo
                {
                    protected override ActivityExecutionStatus Execute(ActivityExecutionContext executionContext) { return null; }
                }";
            var actual = _subject.EnsureClassModifiers(textContent);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void RemoveValidationOptionAttribute_WhenClassHasPropertyWithValidationOption_ShouldRemoveIt()
        {
            var textContent = @"
                public class Foo
                {
                    [ValidationOption(ValidationOption.Optional)]
                    public bool Bar { get; set; }

                    protected override ActivityExecutionStatus Execute(ActivityExecutionContext executionContext) { return null; }
                }";
            var expected = @"
                public class Foo
                {
                                        public bool Bar { get; set; }

                    protected override ActivityExecutionStatus Execute(ActivityExecutionContext executionContext) { return null; }
                }";
            var actual = _subject.RemoveValidationOptionAttribute(textContent);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void RemoveWorkflowUsingStatements_WhenClassHasLegacyUsingStatements_ShouldRemoveThem()
        {
            var textContent = @"
using Mediachase.Commerce.WorkflowCompatibility;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.IO;
using System.Linq;
using System.Workflow.ComponentModel.Compiler;
using System.Workflow.ComponentModel.Serialization;
using System.Workflow.ComponentModel;
using System.Workflow.ComponentModel.Design;
using System.Workflow.Runtime;
using System.Text.RegularExpressions;

namespace Legacy.Workflows
{
    public class Foo
    {
        protected override ActivityExecutionStatus Execute(ActivityExecutionContext executionContext) { return null; }
    }
}";
            var expected = @"
using Mediachase.Commerce.WorkflowCompatibility;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Legacy.Workflows
{
    public class Foo
    {
        protected override ActivityExecutionStatus Execute(ActivityExecutionContext executionContext) { return null; }
    }
}";
            var actual = _subject.RemoveWorkflowUsingStatements(textContent);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void AddUsingStatements_ShouldAddNewUsingStatements()
        {
            var textContent = @"
                    using System;
                    using System.IO;
                    using System.Linq;
                    using System.Text.RegularExpressions;

                    namespace Legacy.Workflows
                    {
                        public class Foo
                        {
                            protected override ActivityExecutionStatus Execute(ActivityExecutionContext executionContext) { return null; }
                        }
                    }";

            var actual = _subject.AddUsingStatements(textContent);
            Assert.True(actual.Contains("using Mediachase.Commerce.Engine;"));
            Assert.True(actual.Contains("using Mediachase.Commerce.WorkflowCompatibility;"));
        }
    }
}

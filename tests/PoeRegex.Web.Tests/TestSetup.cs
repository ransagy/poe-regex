using System.Runtime.CompilerServices;
using Bunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PoeRegex.Web.Tests;

internal static class TestSetup
{
    /// <summary>
    /// The canonical planner can make the first compile of a family slower;
    /// under parallel test load bUnit's one-second default is too tight.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize() =>
        BunitContext.DefaultWaitTimeout = TimeSpan.FromSeconds(5);
}

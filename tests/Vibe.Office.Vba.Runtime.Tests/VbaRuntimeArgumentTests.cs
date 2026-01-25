using System.Collections.Generic;
using System.Threading.Tasks;
using Vibe.Office.Vba.Runtime;
using Xunit;

namespace Vibe.Office.Vba.Runtime.Tests;

public sealed class VbaRuntimeArgumentTests
{
    [Fact]
    public async Task ExecuteAsync_PassesArgumentsToProcedure()
    {
        var source = "Sub Entry(a, b)\n    Application.TestHook a, b\nEnd Sub";
        var host = new TestHost();
        var runtime = new VbaRuntime(host);
        var args = new[] { VbaValue.FromDouble(42d), VbaValue.FromString("alpha") };

        var result = await runtime.ExecuteAsync(source, "Entry", args);

        Assert.True(result.Success);
        Assert.Equal(2, host.Arguments.Count);
        Assert.Equal(42d, host.Arguments[0].AsDouble());
        Assert.Equal("alpha", host.Arguments[1].AsString());
    }

    private sealed class TestHost : IVbaHost
    {
        public List<VbaValue> Arguments { get; } = new();

        public bool TryInvokeMember(string name, IReadOnlyList<VbaValue> arguments, out VbaValue result)
        {
            if (string.Equals(name, "Application.TestHook", System.StringComparison.OrdinalIgnoreCase))
            {
                Arguments.Clear();
                Arguments.AddRange(arguments);
                result = VbaValue.Empty;
                return true;
            }

            result = VbaValue.Empty;
            return false;
        }

        public bool TryGetMember(string name, out VbaValue result)
        {
            result = VbaValue.Empty;
            return false;
        }

        public bool TrySetMember(string name, VbaValue value)
        {
            return false;
        }
    }
}

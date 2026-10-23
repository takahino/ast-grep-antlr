namespace AstGrepAntlr.Tests.Harness;

/// <summary>
/// Fixed-seed RNG for property / variation tests. Override with AG4_TEST_SEED.
/// </summary>
public static class TestSeed
{
    public static int Value
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("AG4_TEST_SEED");
            if (!string.IsNullOrEmpty(env) && int.TryParse(env, out var parsed))
            {
                return parsed;
            }

            return 20261024;
        }
    }

    public static Random Create() => new(Value);

    public static string FailMessage(int caseIndex, string detail) => $"seed={Value} case={caseIndex}: {detail}";
}

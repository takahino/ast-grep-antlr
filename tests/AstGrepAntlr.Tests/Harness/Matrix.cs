namespace AstGrepAntlr.Tests.Harness;

/// <summary>Cartesian product helpers that emit string case ids for xunit Theories.</summary>
public static class Matrix
{
    public static IEnumerable<object[]> Product(params IReadOnlyList<string>[] axes)
    {
        if (axes.Length == 0)
        {
            yield break;
        }

        foreach (var combo in ProductInner(axes, 0, []))
        {
            var id = string.Join('/', combo);
            yield return [id, .. combo];
        }
    }

    private static IEnumerable<string[]> ProductInner(IReadOnlyList<string>[] axes, int index, List<string> prefix)
    {
        if (index >= axes.Length)
        {
            yield return prefix.ToArray();
            yield break;
        }

        foreach (var value in axes[index])
        {
            prefix.Add(value);
            foreach (var rest in ProductInner(axes, index + 1, prefix))
            {
                yield return rest;
            }

            prefix.RemoveAt(prefix.Count - 1);
        }
    }
}

using Antlr4.Runtime;

namespace AstGrepAntlr.Parsing;

/// <summary>Bail strategy that honors a <see cref="CancellationToken"/> in <see cref="Sync"/>.</summary>
internal sealed class CancelableBailErrorStrategy : BailErrorStrategy
{
    private readonly CancellationToken _cancellationToken;

    public CancelableBailErrorStrategy(CancellationToken cancellationToken) => _cancellationToken = cancellationToken;

    public override void Sync(Parser recognizer)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        base.Sync(recognizer);
    }
}

/// <summary>Default recovery strategy that honors a <see cref="CancellationToken"/> in <see cref="Sync"/>.</summary>
internal sealed class CancelableDefaultErrorStrategy : DefaultErrorStrategy
{
    private readonly CancellationToken _cancellationToken;

    public CancelableDefaultErrorStrategy(CancellationToken cancellationToken) =>
        _cancellationToken = cancellationToken;

    public override void Sync(Parser recognizer)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        base.Sync(recognizer);
    }
}

using Xunit;

// KingsRowBoard.Layout is process-wide state that some tests change, so tests run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

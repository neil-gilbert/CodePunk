using Xunit;

// Disable parallel execution in this test assembly because several tests mutate
// process-wide environment variables (e.g., CODEPUNK_CONFIG_HOME) that influence
// file-system paths used by the session file store. Parallel runs can create
// nondeterministic path races causing intermittent failures.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

using Xunit;

// Disable parallel test execution across classes to prevent race conditions on WPF Application.Current / Dispatcher
[assembly: CollectionBehavior(DisableTestParallelization = true)]

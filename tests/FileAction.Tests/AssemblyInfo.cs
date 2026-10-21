using Xunit;

// Theme, language and the lock vault are process-wide statics. Running collections in parallel lets one
// test switch the theme while another captures a brush, which produced the 2.4.8 redact-theme flake.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

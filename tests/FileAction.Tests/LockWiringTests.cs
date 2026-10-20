using FileAction.App.Shell.Protection;

namespace FileAction.Tests;

/// <summary>
/// The app's Lock hooks are thin, but a missing call means the feature is dead. These tests replace the
/// delegates and prove each entry point is reachable and invoked exactly once.
/// </summary>
public class LockWiringTests
{
    [Fact]
    public void App_startup_exit_open_settings_and_self_test_hooks_invoke_the_lock_entry_points()
    {
        var handlers = LockWiring.TryHandleArgs;
        var startup = LockWiring.StartupRecovery;
        var exit = LockWiring.RelockOnExit;
        var settings = LockWiring.OpenSettings;
        var selfTest = LockWiring.SelfTest;
        try
        {
            var calls = 0;
            LockWiring.TryHandleArgs = args => args.Contains("--lock-open");
            LockWiring.StartupRecovery = () => calls++;
            LockWiring.RelockOnExit = () => calls += 10;
            LockWiring.OpenSettings = () => calls += 100;
            LockWiring.SelfTest = (_, _) => calls += 1000;

            Assert.True(LockWiring.HandleArgs(["--lock-open", "x.dwenc"]));
            Assert.False(LockWiring.HandleArgs(["--settings"]));
            LockWiring.Startup();
            LockWiring.Exit();
            LockWiring.Settings();
            LockWiring.RunSelfTest("out", []);
            Assert.Equal(1111, calls);
        }
        finally
        {
            LockWiring.TryHandleArgs = handlers;
            LockWiring.StartupRecovery = startup;
            LockWiring.RelockOnExit = exit;
            LockWiring.OpenSettings = settings;
            LockWiring.SelfTest = selfTest;
        }
    }
}

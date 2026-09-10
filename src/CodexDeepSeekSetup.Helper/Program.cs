using CodexDeepSeekSetup.Helper;

if (!OperatingSystem.IsWindows())
{
    return 1;
}

var runner = new HelperCommandRunner(new NativeCredentialReader(), new HelperSelfDeleteFinalizer());
return await runner.ExecuteAsync(args, Console.Out, Console.Error, CancellationToken.None);

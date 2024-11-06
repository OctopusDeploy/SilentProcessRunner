﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Octopus.Shellfish;
using Tests.Plumbing;
using Xunit;

// We disable this because we want to test the synchronous version of the method as well as the async version using the same test method
// ReSharper disable MethodHasAsyncOverload

namespace Tests;

public class ShellCommandExecutorFixture
{
    public enum SyncBehaviour
    {
        Sync,
        Async
    }

    // ReSharper disable InconsistentNaming
    const int SIG_TERM = 143;
    const int SIG_KILL = 137;
    const string Username = "test-shellexecutor";

    static readonly string Command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "bash";
    static readonly string CommandParam = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "/c" : "-c";

    // Mimic the cancellation behaviour from LoggedTest in Octopus Server; we can't reference it in this assembly
    static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(45);

    readonly CancellationTokenSource cancellationTokenSource = new(TestTimeout);
    CancellationToken CancellationToken => cancellationTokenSource.Token;

    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task ExitCode_ShouldBeReturned(SyncBehaviour behaviour)
    {
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var executor = new ShellCommandExecutor()
            .WithExecutable(Command)
            .WithRawArguments($"{CommandParam} \"exit 99\"")
            .CaptureStdOutTo(stdOut)
            .CaptureStdErrTo(stdErr);
        
        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(99, "our custom exit code should be reflected");

        // we're executing cmd.exe which writes a newline to stdout and stderr
        stdOut.ToString().Should().Be(Environment.NewLine, "no messages should be written to stdout");
        stdErr.ToString().Should().Be(Environment.NewLine, "no messages should be written to stderr");
    }

    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task RunningAsSameUser_ShouldCopySpecialEnvironmentVariables(SyncBehaviour behaviour)
    {
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var executor = new ShellCommandExecutor()
            .WithExecutable(Command)
            .WithRawArguments($"{CommandParam} \"echo {EchoEnvironmentVariable("customenvironmentvariable")}\"")
            .WithEnvironmentVariables(new Dictionary<string, string>
            {
                { "customenvironmentvariable", "customvalue" }
            })
            .CaptureStdOutTo(stdOut)
            .CaptureStdErrTo(stdErr);
        
        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().Be(Environment.NewLine, "no messages should be written to stderr");
        stdOut.ToString().Should().ContainEquivalentOf("customvalue", "the environment variable should have been copied to the child process");
    }
    
    // [WindowsFact]
    // public void RunningAsDifferentUser_ShouldCopySpecialEnvironmentVariables()
    // {
    //     var user = new TestUserPrincipal(Username);
    //
    //     var arguments = $"{CommandParam} \"echo {EchoEnvironmentVariable("customenvironmentvariable")}\"";
    //     // Target the CommonApplicationData folder since this is a place the particular user can get to
    //     var workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    //     var networkCredential = user.GetCredential();
    //     var customEnvironmentVariables = new Dictionary<string, string>
    //     {
    //         { "customenvironmentvariable", "customvalue" }
    //     };
    //
    //     var exitCode = Execute(Command,
    //         arguments,
    //         workingDirectory,
    //         out _,
    //         out var infoMessages,
    //         out var errorMessages,
    //         networkCredential,
    //         customEnvironmentVariables,
    //         CancellationToken);
    //
    //     exitCode.Should().Be(0, "the process should have run to completion");
    //     infoMessages.ToString().Should().ContainEquivalentOf("customvalue", "the environment variable should have been copied to the child process");
    //     errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
    // }
    //
    // [WindowsFact]
    // public void RunningAsDifferentUser_ShouldWorkLotsOfTimes()
    // {
    //     var user = new TestUserPrincipal(Username);
    //
    //     using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(240));
    //
    //     for (var i = 0; i < 20; i++)
    //     {
    //         var arguments = $"{CommandParam} \"echo {EchoEnvironmentVariable("customenvironmentvariable")}%\"";
    //         // Target the CommonApplicationData folder since this is a place the particular user can get to
    //         var workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    //         var networkCredential = user.GetCredential();
    //         var customEnvironmentVariables = new Dictionary<string, string>
    //         {
    //             { "customenvironmentvariable", $"customvalue-{i}" }
    //         };
    //
    //         var exitCode = Execute(Command,
    //             arguments,
    //             workingDirectory,
    //             out _,
    //             out var infoMessages,
    //             out var errorMessages,
    //             networkCredential,
    //             customEnvironmentVariables,
    //             cts.Token);
    //
    //         exitCode.Should().Be(0, "the process should have run to completion");
    //         infoMessages.ToString().Should().ContainEquivalentOf($"customvalue-{i}", "the environment variable should have been copied to the child process");
    //         errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
    //     }
    // }
    //
    // [WindowsFact]
    // public void RunningAsDifferentUser_CanWriteToItsOwnTempPath()
    // {
    //     var user = new TestUserPrincipal(Username);
    //
    //     var arguments = $"{CommandParam} \"echo hello > %temp%hello.txt\"";
    //     // Target the CommonApplicationData folder since this is a place the particular user can get to
    //     var workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    //     var networkCredential = user.GetCredential();
    //     var customEnvironmentVariables = new Dictionary<string, string>();
    //
    //     var exitCode = Execute(Command,
    //         arguments,
    //         workingDirectory,
    //         out _,
    //         out _,
    //         out var errorMessages,
    //         networkCredential,
    //         customEnvironmentVariables,
    //         CancellationToken);
    //
    //     exitCode.Should().Be(0, "the process should have run to completion after writing to the temp folder for the other user");
    //     errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
    // }

    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task CancellationToken_ShouldForceKillTheProcess(SyncBehaviour behaviour)
    {
        // Terminate the process after a very short time so the test doesn't run forever
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        // Starting a new instance of cmd.exe will run indefinitely waiting for user input
        var executor = new ShellCommandExecutor()
            .WithExecutable(Command)
            .CaptureStdOutTo(stdOut)
            .CaptureStdErrTo(stdErr);
            
        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(cts.Token)
            : executor.Execute(cts.Token);

        var exitCode = result.ExitCode;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            exitCode.Should().BeLessOrEqualTo(0, "the process should have been terminated");
            stdOut.ToString().Should().ContainEquivalentOf("Microsoft Windows", "the default command-line header would be written to stdout");
        }
        else
        {
            exitCode.Should().BeOneOf(SIG_KILL, SIG_TERM, 0, -1);
        }

        stdErr.ToString().Should().Be(string.Empty, "no messages should be written to stderr, and the process was terminated before the trailing newline got there");
    }

    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task EchoHello_ShouldWriteToStdOut(SyncBehaviour behaviour)
    {
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var executor = new ShellCommandExecutor()
            .WithExecutable(Command)
            .WithRawArguments($"{CommandParam} \"echo hello\"")
            .CaptureStdOutTo(stdOut)
            .CaptureStdErrTo(stdErr);
        
        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().Be(Environment.NewLine, "no messages should be written to stderr");
        stdOut.ToString().Should().ContainEquivalentOf("hello");
    }

    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task EchoError_ShouldWriteToStdErr(SyncBehaviour behaviour)
    {
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var executor = new ShellCommandExecutor()
            .WithExecutable(Command)
            .WithRawArguments($"{CommandParam} \"echo Something went wrong! 1>&2\"")
            .CaptureStdOutTo(stdOut)
            .CaptureStdErrTo(stdErr);
        
        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdOut.ToString().Should().Be(Environment.NewLine, "no messages should be written to stdout");
        stdErr.ToString().Should().ContainEquivalentOf("Something went wrong!");
    }

    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task RunAsCurrentUser_ShouldWork(SyncBehaviour behaviour)
    {
        var arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"{CommandParam} \"echo {EchoEnvironmentVariable("username")}\""
            : $"{CommandParam} \"whoami\"";

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var executor = new ShellCommandExecutor()
            .WithExecutable(Command)
            .WithRawArguments(arguments)
            .CaptureStdOutTo(stdOut)
            .CaptureStdErrTo(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().Be(Environment.NewLine, "no messages should be written to stderr");
        stdOut.ToString().Should().ContainEquivalentOf($@"{Environment.UserName}");
    }

    [WindowsTheory]
    [InlineData("cmd.exe", "/c \"echo %userdomain%\\%username%\"", SyncBehaviour.Sync)]
    [InlineData("cmd.exe", "/c \"echo %userdomain%\\%username%\"", SyncBehaviour.Async)]
    [InlineData("powershell.exe", "-command \"Write-Host $env:userdomain\\$env:username\"", SyncBehaviour.Sync)]
    [InlineData("powershell.exe", "-command \"Write-Host $env:userdomain\\$env:username\"", SyncBehaviour.Async)]
    public async Task RunAsCurrentUser_CmdAndPowerShell_ShouldWork(string command, string arguments, SyncBehaviour behaviour)
    {
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var executor = new ShellCommandExecutor()
            .WithExecutable(command)
            .WithRawArguments(arguments)
            .CaptureStdOutTo(stdOut)
            .CaptureStdErrTo(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().Be(Environment.NewLine, "no messages should be written to stderr");
        stdOut.ToString().Should().ContainEquivalentOf($@"{Environment.UserDomainName}\{Environment.UserName}");
    }
    
    // [WindowsTheory]
    // [InlineData("cmd.exe", "/c \"echo %userdomain%\\%username%\"")]
    // [InlineData("powershell.exe", "-command \"Write-Host $env:userdomain\\$env:username\"")]
    // public void RunAsDifferentUser_ShouldWork(string command, string arguments)
    // {
    //     var user = new TestUserPrincipal(Username);
    //
    //     // Target the CommonApplicationData folder since this is a place the particular user can get to
    //     var workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    //     var networkCredential = user.GetCredential();
    //     var customEnvironmentVariables = new Dictionary<string, string>();
    //
    //     var exitCode = Execute(command,
    //         arguments,
    //         workingDirectory,
    //         out _,
    //         out var infoMessages,
    //         out var errorMessages,
    //         networkCredential,
    //         customEnvironmentVariables,
    //         CancellationToken);
    //
    //     exitCode.Should().Be(0, "the process should have run to completion");
    //     errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
    //     infoMessages.ToString().Should().ContainEquivalentOf($@"{user.DomainName}\{user.UserName}");
    // }
    //
    static string EchoEnvironmentVariable(string varName)
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"%{varName}%" : $"${varName}";
}
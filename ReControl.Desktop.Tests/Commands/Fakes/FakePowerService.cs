using System.Collections.Generic;
using System.Threading.Tasks;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Tests.Commands.Fakes;

/// <summary>
/// Hand-rolled fake IPowerService that records which power method fired.
/// No Moq — tests use hand-rolled fakes rather than a mocking dependency.
/// </summary>
public class FakePowerService : IPowerService
{
    public List<string> Calls { get; } = new();

    public Task ShutdownAsync() { Calls.Add("shutdown"); return Task.CompletedTask; }

    public Task RestartAsync() { Calls.Add("restart"); return Task.CompletedTask; }

    public Task SleepAsync() { Calls.Add("sleep"); return Task.CompletedTask; }

    public Task HibernateAsync() { Calls.Add("hibernate"); return Task.CompletedTask; }

    public Task LogOffAsync() { Calls.Add("logOff"); return Task.CompletedTask; }

    public Task LockAsync() { Calls.Add("lock"); return Task.CompletedTask; }

    public bool IsOperationSupported(string operation) => true;
}

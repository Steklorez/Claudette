using Claudette.Core.ProjectTools;

namespace Claudette.App.Tests.Support;

/// <summary>Stands in for every running process on the machine (DESIGN.md §18, "Project tools"): only the ones a test says.</summary>
internal sealed class FakeSystemProcesses : ISystemProcesses
{
    private readonly List<SystemProcess> _running = [];

    public void Start(SystemProcess process)
    {
        lock (_running)
        {
            _running.Add(process);
        }
    }

    public void End(int pid)
    {
        lock (_running)
        {
            _running.RemoveAll(p => p.Pid == pid);
        }
    }

    public IReadOnlyList<SystemProcess> Find(IReadOnlyCollection<string> names)
    {
        lock (_running)
        {
            return _running.Where(p => SystemProcessNames.Matches(p.Name, names)).ToArray();
        }
    }

    public void KillTree(SystemProcess process) => End(process.Pid);
}

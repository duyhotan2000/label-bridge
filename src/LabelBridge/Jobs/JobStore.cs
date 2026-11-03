namespace LabelBridge.Jobs;

/// <summary>Thread-safe list of recent jobs (ids from 1, keeps the last 50).</summary>
public sealed class JobStore
{
    const int MaxJobs = 50;
    readonly List<PrintJob> _jobs = [];
    int _nextId = 1;

    public PrintJob Add(string name, string user, string format, byte[] doc)
    {
        lock (_jobs)
        {
            var job = new PrintJob { Id = _nextId++, Name = name, UserName = user, Format = format, Document = doc };
            _jobs.Add(job);
            for (var i = 0; _jobs.Count > MaxJobs && i < _jobs.Count;)
            {
                if (_jobs[i].IsFinished)
                    _jobs.RemoveAt(i);
                else
                    i++;
            }
            return job;
        }
    }

    public PrintJob? Get(int id)
    {
        lock (_jobs)
            return _jobs.FirstOrDefault(j => j.Id == id);
    }

    public IReadOnlyList<PrintJob> All()
    {
        lock (_jobs)
            return [.. _jobs];
    }

    public bool IsBusy
    {
        get
        {
            lock (_jobs)
                return _jobs.Any(j => !j.IsFinished);
        }
    }
}

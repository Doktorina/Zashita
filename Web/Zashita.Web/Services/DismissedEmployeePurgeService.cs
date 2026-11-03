using Zashita.Core.Employees;
using Zashita.Core.Med;

namespace Zashita.Web.Services;

/// <summary>Удаляет уволенных из медданных и списков по индексу CSV 1С.</summary>
public sealed class DismissedEmployeePurgeService
{
    private readonly OneCEmployeeSyncService _sync;
    private readonly MedExamStore _exams;
    private readonly MedPersonListStore _personLists;

    public DismissedEmployeePurgeService(
        OneCEmployeeSyncService sync,
        MedExamStore exams,
        MedPersonListStore personLists)
    {
        _sync = sync;
        _exams = exams;
        _personLists = personLists;
    }

    public DismissedEmployeePurgeResult PurgeFromAllLists()
    {
        var files = OneCEmployeeSyncService.SelectCurrentCsvBatch(_sync.FindSharedFiles())
            .Select(f => f.FullName)
            .ToList();

        if (files.Count == 0)
        {
            return new DismissedEmployeePurgeResult();
        }

        var index = OneCDismissedEmployeeIndex.BuildFromCsvFiles(files);
        return PurgeUsingIndex(index);
    }

    public DismissedEmployeePurgeResult PurgeUsingIndex(OneCDismissedEmployeeIndex index)
    {
        var result = new DismissedEmployeePurgeResult
        {
            DismissedInCsv = index.Count
        };

        var exams = _exams.LoadAll();
        var keptExams = new List<MedExamRecord>(exams.Count);
        foreach (var exam in exams)
        {
            if (index.IsDismissed(exam.Organization, exam.EmployeeName))
            {
                result.RemovedExams++;
                continue;
            }

            keptExams.Add(exam);
        }

        if (result.RemovedExams > 0)
        {
            _exams.SaveAll(keptExams);
        }

        var personLists = _personLists.LoadAll();
        var personListsChanged = false;
        foreach (var record in personLists)
        {
            var before = record.Lines.Count;
            record.Lines.RemoveAll(line =>
                index.IsDismissed(record.Organization, line.EmployeeName));
            var removed = before - record.Lines.Count;
            if (removed > 0)
            {
                result.RemovedPersonListLines += removed;
                personListsChanged = true;
            }
        }

        if (personListsChanged)
        {
            _personLists.SaveAll(personLists.Where(r => r.Lines.Count > 0));
        }

        return result;
    }
}

public sealed class DismissedEmployeePurgeResult
{
    public int DismissedInCsv { get; set; }
    public int RemovedExams { get; set; }
    public int RemovedPersonListLines { get; set; }

    public bool AnyChanges => RemovedExams > 0 || RemovedPersonListLines > 0;
}

using System;

namespace AI_Study_Hub_v2.Components.Pages.Dashboard;

public class FolderViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SubjectCode { get; set; } = string.Empty;
    public string Semester { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
}

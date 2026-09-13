namespace NMU.Platform.Components.Models;

public class QuizSubject
{
    /// <summary>Clean display name (subject without code/branch), e.g. "Structured Programming".</summary>
    public string Name { get; set; } = "";
    /// <summary>Full archive file path, e.g. "Data/CSE014 - .../Quizzes/Dr. X/Programming-quize.json".</summary>
    public string Path { get; set; } = "";
    public string Rel { get; set; } = "";
    /// <summary>Full subject folder, e.g. "CSE014 - Structured Programming.(ALL)".</summary>
    public string SubjectFullName { get; set; } = "";
    public string Code { get; set; } = "";
    public string Branch { get; set; } = "ALL";
    /// <summary>Lecturer folder under Data/{Subject}/Quizzes/{Lecturer}/...</summary>
    public string Lecturer { get; set; } = "";
    public string Level { get; set; } = "";
    public string Semester { get; set; } = "";
    public string ArchiveId { get; set; } = "";
}

namespace NMU.Platform.Components.Models;

public class StudentProfile
{
    public string Name { get; set; } = string.Empty;
    public string AcademicLevel { get; set; } = "Level 1";
    public string Semester { get; set; } = "Semester 1";

    /// <summary>
    /// When true, the app only shows the subjects the student explicitly selected
    /// (credit-hour system: the student can be registered in subjects from any
    /// level + semester, not just their own).
    /// </summary>
    public bool CustomSubjectsMode { get; set; }

    /// <summary>
    /// The subjects the student selected in custom mode. Each entry pins a subject
    /// to the level + semester where its content lives in the archive.
    /// </summary>
    public List<CustomSubjectSelection> CustomSubjects { get; set; } = new();
}

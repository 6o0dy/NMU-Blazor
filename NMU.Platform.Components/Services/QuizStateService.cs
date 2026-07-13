using NMU.Platform.Components.Models;

namespace NMU.Platform.Components.Services;

public class QuizStateService
{
    public string SubjectName { get; set; } = "";
    public string SubjectPath { get; set; } = "";
    public Dictionary<string, string> SubjectPathMap { get; set; } = new();
    public List<QuizChapter> Chapters { get; set; } = new();
    public List<QuizQuestion> CurrentQuestions { get; set; } = new();
    public int CurrentIndex { get; set; }
    public int ScoreCorrect { get; set; }
    public int ScoreWrong { get; set; }
    public bool IsQuizActive { get; set; }
    public bool IsRealExam { get; set; }
    public bool IsTimed { get; set; }
    public bool RandomMode { get; set; } = true;
    public int TimeLimitSeconds { get; set; }
    public int TimeLeftSeconds { get; set; }
    public string?[] UserAnswers { get; set; } = Array.Empty<string?>();
    public HashSet<int> FlaggedQuestions { get; set; } = new();
    public List<WrongAnswerEntry> WrongHistory { get; set; } = new();
    public bool ExpLangAr { get; set; }
    public string? ResultSubjectName { get; set; }
    public int ResultTotalQuestions { get; set; }
    public int ResultCorrect { get; set; }
    public int ResultWrong { get; set; }
    public bool IsTimeout { get; set; }
    public bool BackRequested { get; set; }

    public event Action? StateChanged;
    public void NotifyStateChanged() => StateChanged?.Invoke();

    public void RequestBack()
    {
        BackRequested = true;
        NotifyStateChanged();
    }

    public void StartQuiz(List<QuizQuestion> questions, bool isRealExam, bool isTimed, int timeMinutes)
    {
        var pool = questions.Select(q =>
        {
            var clone = System.Text.Json.JsonSerializer.Serialize(q);
            return System.Text.Json.JsonSerializer.Deserialize<QuizQuestion>(clone)!;
        }).ToList();

        if (RandomMode || isRealExam)
            Shuffle(pool);

        foreach (var q in pool)
        {
            var opts = q.Options;
            ShuffleObjects(opts);
        }

        CurrentQuestions = pool;
        CurrentIndex = 0;
        ScoreCorrect = 0;
        ScoreWrong = 0;
        IsQuizActive = true;
        IsRealExam = isRealExam;
        IsTimed = isTimed;
        TimeLimitSeconds = timeMinutes * 60;
        TimeLeftSeconds = timeMinutes * 60;
        UserAnswers = new string?[pool.Count];
        FlaggedQuestions = new HashSet<int>();
        WrongHistory = new List<WrongAnswerEntry>();
        NotifyStateChanged();
    }

    public void CheckAnswer(string selectedValue, string correctValue)
    {
        UserAnswers[CurrentIndex] = selectedValue;
        if (selectedValue == correctValue)
        {
            ScoreCorrect++;
        }
        else
        {
            ScoreWrong++;
            WrongHistory.Add(new WrongAnswerEntry
            {
                Question = CurrentQuestions[CurrentIndex],
                UserSelected = selectedValue
            });
        }
        NotifyStateChanged();
    }

    public void SelectAnswer(int index, string value)
    {
        UserAnswers[index] = value;
        NotifyStateChanged();
    }

    public void ToggleFlag()
    {
        if (FlaggedQuestions.Contains(CurrentIndex))
            FlaggedQuestions.Remove(CurrentIndex);
        else
            FlaggedQuestions.Add(CurrentIndex);
        NotifyStateChanged();
    }

    public void NextQuestion()
    {
        if (CurrentIndex < CurrentQuestions.Count - 1)
        {
            CurrentIndex++;
            NotifyStateChanged();
        }
    }

    public void PreviousQuestion()
    {
        if (CurrentIndex > 0)
        {
            CurrentIndex--;
            NotifyStateChanged();
        }
    }

    public void GoToQuestion(int index)
    {
        if (index >= 0 && index < CurrentQuestions.Count)
        {
            CurrentIndex = index;
            NotifyStateChanged();
        }
    }

    public void TimerTick()
    {
        if (TimeLeftSeconds > 0)
        {
            TimeLeftSeconds--;
            NotifyStateChanged();
        }
    }

    public void FinishQuiz(bool timeout = false)
    {
        if (IsRealExam)
        {
            ScoreCorrect = 0;
            ScoreWrong = 0;
            for (int i = 0; i < CurrentQuestions.Count; i++)
            {
                var correct = CurrentQuestions[i].CorrectAnswerSerialized;
                if (UserAnswers[i] == correct)
                    ScoreCorrect++;
                else
                    ScoreWrong++;
            }
        }
        ResultSubjectName = SubjectName;
        ResultTotalQuestions = CurrentQuestions.Count;
        ResultCorrect = ScoreCorrect;
        ResultWrong = ScoreWrong;
        IsTimeout = timeout;
        IsQuizActive = false;
        NotifyStateChanged();
    }

    public void Reset()
    {
        CurrentQuestions.Clear();
        Chapters.Clear();
        UserAnswers = Array.Empty<string?>();
        FlaggedQuestions.Clear();
        WrongHistory.Clear();
        IsQuizActive = false;
        ScoreCorrect = 0;
        ScoreWrong = 0;
        CurrentIndex = 0;
        BackRequested = false;
        NotifyStateChanged();
    }

    private static void Shuffle<T>(List<T> list)
    {
        var rng = new Random();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static void ShuffleObjects(List<QuizOptionItem> options)
    {
        var rng = new Random();
        for (int i = options.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (options[i], options[j]) = (options[j], options[i]);
        }
    }
}

public class WrongAnswerEntry
{
    public QuizQuestion Question { get; set; } = new();
    public string UserSelected { get; set; } = "";
}

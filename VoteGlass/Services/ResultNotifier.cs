namespace VoteGlass.Services;

public class ResultsNotifier
{
    public event Action<int>? ResultsChanged;

    public void Notify(int electionId) => ResultsChanged?.Invoke(electionId);
}
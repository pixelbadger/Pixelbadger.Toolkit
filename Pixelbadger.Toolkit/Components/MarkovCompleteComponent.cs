using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Components;

public class MarkovCompleteComponent
{
    private readonly IMarkovModelService _modelService;
    private readonly Random _random;

    public MarkovCompleteComponent(IMarkovModelService modelService, Random? random = null)
    {
        _modelService = modelService;
        _random = random ?? Random.Shared;
    }

    public async Task<string> CompleteAsync(string inputText, string modelDirectory, int wordCount = 50)
    {
        if (string.IsNullOrWhiteSpace(inputText))
            throw new ArgumentException("Input text cannot be empty.", nameof(inputText));

        var model = await _modelService.LoadModelAsync(modelDirectory);

        var inputWords = inputText.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var currentWord = inputWords[^1];
        var generated = new List<string>();

        for (int i = 0; i < wordCount; i++)
        {
            if (!model.TryGetValue(currentWord, out var nextWords) || nextWords.Count == 0)
                break;

            currentWord = nextWords[_random.Next(nextWords.Count)];
            generated.Add(currentWord);
        }

        if (generated.Count == 0)
            return inputText;

        return inputText + " " + string.Join(" ", generated);
    }
}

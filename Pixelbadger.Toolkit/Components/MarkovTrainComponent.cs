using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Components;

public class MarkovTrainComponent
{
    private readonly IMarkovModelService _modelService;

    public MarkovTrainComponent(IMarkovModelService modelService)
    {
        _modelService = modelService;
    }

    public async Task<int> TrainAsync(string sourceFilePath, string modelDirectory)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException($"Source file '{sourceFilePath}' does not exist.");

        var text = await File.ReadAllTextAsync(sourceFilePath);
        var model = BuildModel(text);
        await _modelService.SaveModelAsync(modelDirectory, model);
        return model.Count;
    }

    internal static Dictionary<string, List<string>> BuildModel(string text)
    {
        var words = text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var model = new Dictionary<string, List<string>>();

        for (int i = 0; i < words.Length - 1; i++)
        {
            var word = words[i];
            var nextWord = words[i + 1];

            if (!model.TryGetValue(word, out var nextWords))
            {
                nextWords = [];
                model[word] = nextWords;
            }
            nextWords.Add(nextWord);
        }

        return model;
    }
}

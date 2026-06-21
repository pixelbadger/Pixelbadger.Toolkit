namespace Pixelbadger.Toolkit.Services;

public interface IMarkovModelService
{
    Task SaveModelAsync(string directory, Dictionary<string, List<string>> model);
    Task<Dictionary<string, List<string>>> LoadModelAsync(string directory);
}

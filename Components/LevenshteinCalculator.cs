namespace Pixelbadger.Toolkit.Components;

public class LevenshteinCalculator
{
    public async Task<int> CalculateDistanceAsync(string input1, string input2)
    {
        var string1 = await GetStringAsync(input1);
        var string2 = await GetStringAsync(input2);
        
        return CalculateLevenshteinDistance(string1, string2);
    }
    
    private async Task<string> GetStringAsync(string input)
    {
        if (File.Exists(input))
        {
            return await File.ReadAllTextAsync(input);
        }
        return input;
    }
    
    private int CalculateLevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
            return target?.Length ?? 0;
        
        if (string.IsNullOrEmpty(target))
            return source.Length;
        
        var sourceLength = source.Length;
        var targetLength = target.Length;
        var matrix = new int[sourceLength + 1, targetLength + 1];
        
        for (int i = 0; i <= sourceLength; i++)
            matrix[i, 0] = i;
        
        for (int j = 0; j <= targetLength; j++)
            matrix[0, j] = j;
        
        for (int i = 1; i <= sourceLength; i++)
        {
            for (int j = 1; j <= targetLength; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }
        
        return matrix[sourceLength, targetLength];
    }
}
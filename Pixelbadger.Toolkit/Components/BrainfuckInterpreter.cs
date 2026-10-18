namespace Pixelbadger.Toolkit.Components;

public class BrainfuckInterpreter
{
    private const int MemorySize = 30000;
    
    public async Task<string> ExecuteAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Brainfuck program file not found: {filePath}");
        }

        var program = await File.ReadAllTextAsync(filePath, cancellationToken);
        return Execute(program, cancellationToken);
    }

    public string Execute(string program, CancellationToken cancellationToken = default)
    {
        var memory = new byte[MemorySize];
        var dataPointer = 0;
        var instructionPointer = 0;
        var output = new StringWriter();
        var input = new StringReader("");
        var loopStack = new Stack<int>();

        var executed = 0;

        while (instructionPointer < program.Length)
        {
            if ((++executed & 4095) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            var command = program[instructionPointer];
            
            switch (command)
            {
                case '>':
                    dataPointer = (dataPointer + 1) % MemorySize;
                    break;
                    
                case '<':
                    dataPointer = (dataPointer - 1 + MemorySize) % MemorySize;
                    break;
                    
                case '+':
                    memory[dataPointer] = (byte)((memory[dataPointer] + 1) % 256);
                    break;
                    
                case '-':
                    memory[dataPointer] = (byte)((memory[dataPointer] - 1 + 256) % 256);
                    break;
                    
                case '.':
                    output.Write((char)memory[dataPointer]);
                    break;
                    
                case ',':
                    var inputChar = input.Read();
                    memory[dataPointer] = inputChar == -1 ? (byte)0 : (byte)inputChar;
                    break;
                    
                case '[':
                    if (memory[dataPointer] == 0)
                    {
                        var bracketCount = 1;
                        instructionPointer++;
                        
                        while (instructionPointer < program.Length && bracketCount > 0)
                        {
                            if (program[instructionPointer] == '[')
                                bracketCount++;
                            else if (program[instructionPointer] == ']')
                                bracketCount--;
                            
                            instructionPointer++;
                        }
                        instructionPointer--;
                    }
                    else
                    {
                        loopStack.Push(instructionPointer);
                    }
                    break;
                    
                case ']':
                    if (memory[dataPointer] != 0)
                    {
                        instructionPointer = loopStack.Peek();
                    }
                    else
                    {
                        loopStack.Pop();
                    }
                    break;
            }
            
            instructionPointer++;
        }

        return output.ToString();
    }
}
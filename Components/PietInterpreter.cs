using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Pixelbadger.Toolkit.Components;

public enum PietColor
{
    LightRed, Red, DarkRed,
    LightYellow, Yellow, DarkYellow,
    LightGreen, Green, DarkGreen,
    LightCyan, Cyan, DarkCyan,
    LightBlue, Blue, DarkBlue,
    LightMagenta, Magenta, DarkMagenta,
    White, Black, Unknown
}

public enum Direction { Right, Down, Left, Up }
public enum CodelChooser { Left, Right }

public struct Position
{
    public int X, Y;
    public Position(int x, int y) { X = x; Y = y; }
}

public class ColorBlock
{
    public PietColor Color { get; set; }
    public HashSet<Position> Codels { get; set; } = new HashSet<Position>();
    public int Size => Codels.Count;
}

public class PietInterpreter
{
    private readonly PietColor[,] _program;
    private readonly Stack<int> _stack = new();
    private readonly int _width, _height;
    private Position _currentPosition;
    private Direction _directionPointer = Direction.Right;
    private CodelChooser _codelChooser = CodelChooser.Left;
    private bool _terminated = false;
    private int _executionSteps = 0;
    private const int MAX_EXECUTION_STEPS = 10000;
    private bool _debugMode = false;

    // Piet color definitions based on standard RGB values
    private static readonly Dictionary<Rgb24, PietColor> ColorMap = new()
    {
        { new Rgb24(255, 192, 192), PietColor.LightRed },     // #FFC0C0
        { new Rgb24(255, 0, 0), PietColor.Red },              // #FF0000
        { new Rgb24(192, 0, 0), PietColor.DarkRed },          // #C00000
        { new Rgb24(255, 255, 192), PietColor.LightYellow },  // #FFFFC0
        { new Rgb24(255, 255, 0), PietColor.Yellow },         // #FFFF00
        { new Rgb24(192, 192, 0), PietColor.DarkYellow },     // #C0C000
        { new Rgb24(192, 255, 192), PietColor.LightGreen },   // #C0FFC0
        { new Rgb24(0, 255, 0), PietColor.Green },            // #00FF00
        { new Rgb24(0, 192, 0), PietColor.DarkGreen },        // #00C000
        { new Rgb24(192, 255, 255), PietColor.LightCyan },    // #C0FFFF
        { new Rgb24(0, 255, 255), PietColor.Cyan },           // #00FFFF
        { new Rgb24(0, 192, 192), PietColor.DarkCyan },       // #00C0C0
        { new Rgb24(192, 192, 255), PietColor.LightBlue },    // #C0C0FF
        { new Rgb24(0, 0, 255), PietColor.Blue },             // #0000FF
        { new Rgb24(0, 0, 192), PietColor.DarkBlue },         // #0000C0
        { new Rgb24(255, 192, 255), PietColor.LightMagenta }, // #FFC0FF
        { new Rgb24(255, 0, 255), PietColor.Magenta },        // #FF00FF
        { new Rgb24(192, 0, 192), PietColor.DarkMagenta },    // #C000C0
        { new Rgb24(255, 255, 255), PietColor.White },        // #FFFFFF
        { new Rgb24(0, 0, 0), PietColor.Black },              // #000000
    };

    public PietInterpreter(string imagePath, int codelSize = 1, bool debugMode = false)
    {
        _debugMode = debugMode;
        using var image = Image.Load<Rgb24>(imagePath);
        
        // Calculate program dimensions based on codel size
        _width = image.Width / codelSize;
        _height = image.Height / codelSize;
        _program = new PietColor[_width, _height];
        _currentPosition = new Position(0, 0);

        // Load image into program array, sampling one pixel per codel
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                // Sample the top-left pixel of each codel
                var pixel = image[x * codelSize, y * codelSize];
                _program[x, y] = GetPietColor(pixel);
            }
        }
    }

    private static PietColor GetPietColor(Rgb24 pixel)
    {
        if (ColorMap.TryGetValue(pixel, out var color))
            return color;

        // Find closest match by color distance for non-standard colors
        var minDistance = double.MaxValue;
        var closestColor = PietColor.White;

        foreach (var kvp in ColorMap)
        {
            var distance = ColorDistance(pixel, kvp.Key);
            if (distance < minDistance)
            {
                minDistance = distance;
                closestColor = kvp.Value;
            }
        }

        return closestColor;
    }

    private static double ColorDistance(Rgb24 c1, Rgb24 c2)
    {
        return Math.Sqrt(Math.Pow(c1.R - c2.R, 2) + Math.Pow(c1.G - c2.G, 2) + Math.Pow(c1.B - c2.B, 2));
    }

    public async Task ExecuteAsync()
    {
        while (!_terminated)
        {
            _executionSteps++;
            if (_executionSteps >= MAX_EXECUTION_STEPS)
            {
                Console.WriteLine($"\nExecution terminated after {MAX_EXECUTION_STEPS} steps to prevent infinite loop.");
                _terminated = true;
                break;
            }

            var currentBlock = FindColorBlock(_currentPosition);
            var nextPosition = FindNextPosition(currentBlock);
            
            if (nextPosition == null)
            {
                _terminated = true;
                break;
            }

            var nextBlock = FindColorBlock(nextPosition.Value);
            ExecuteCommand(currentBlock, nextBlock);
            
            _currentPosition = nextPosition.Value;
            
            if (_debugMode)
            {
                Console.WriteLine($"Step {_executionSteps}: Pos({_currentPosition.X},{_currentPosition.Y}) Color:{_program[_currentPosition.X, _currentPosition.Y]} Stack:[{string.Join(",", _stack)}]");
            }
            
            // Prevent infinite loops
            await Task.Delay(1);
        }
    }

    private ColorBlock FindColorBlock(Position start)
    {
        var color = _program[start.X, start.Y];
        var visited = new HashSet<Position>();
        var queue = new Queue<Position>();
        var block = new ColorBlock { Color = color };

        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0)
        {
            var pos = queue.Dequeue();
            block.Codels.Add(pos);

            // Check adjacent positions
            foreach (var adj in GetAdjacentPositions(pos))
            {
                if (IsValidPosition(adj) && !visited.Contains(adj) && _program[adj.X, adj.Y] == color)
                {
                    visited.Add(adj);
                    queue.Enqueue(adj);
                }
            }
        }

        return block;
    }

    private Position? FindNextPosition(ColorBlock currentBlock)
    {
        // Find edge in direction of DP
        var edge = GetEdgePositions(currentBlock, _directionPointer);
        if (!edge.Any()) return null;

        // Choose position on edge based on CC
        var chosenPosition = ChooseEdgePosition(edge, _directionPointer, _codelChooser);
        
        // Try to move in DP direction
        var attempts = 0;
        while (attempts < 8) // Maximum 8 attempts (4 DP rotations × 2 CC toggles)
        {
            var nextPos = GetNextPosition(chosenPosition, _directionPointer);
            
            if (IsValidPosition(nextPos) && _program[nextPos.X, nextPos.Y] != PietColor.Black)
            {
                return nextPos;
            }

            // Rotate DP and try again
            _directionPointer = RotateDirection(_directionPointer);
            if (attempts % 2 == 1)
            {
                _codelChooser = _codelChooser == CodelChooser.Left ? CodelChooser.Right : CodelChooser.Left;
            }
            
            edge = GetEdgePositions(currentBlock, _directionPointer);
            if (edge.Any())
            {
                chosenPosition = ChooseEdgePosition(edge, _directionPointer, _codelChooser);
            }
            
            attempts++;
        }

        return null; // Blocked
    }

    private List<Position> GetEdgePositions(ColorBlock block, Direction direction)
    {
        return direction switch
        {
            Direction.Right => block.Codels.Where(p => p.X == block.Codels.Max(c => c.X)).ToList(),
            Direction.Down => block.Codels.Where(p => p.Y == block.Codels.Max(c => c.Y)).ToList(),
            Direction.Left => block.Codels.Where(p => p.X == block.Codels.Min(c => c.X)).ToList(),
            Direction.Up => block.Codels.Where(p => p.Y == block.Codels.Min(c => c.Y)).ToList(),
            _ => new List<Position>()
        };
    }

    private Position ChooseEdgePosition(List<Position> edge, Direction direction, CodelChooser chooser)
    {
        return direction switch
        {
            Direction.Right or Direction.Left => 
                chooser == CodelChooser.Left ? edge.MinBy(p => p.Y) : edge.MaxBy(p => p.Y),
            Direction.Down or Direction.Up => 
                chooser == CodelChooser.Left ? edge.MinBy(p => p.X) : edge.MaxBy(p => p.X),
            _ => edge.First()
        };
    }

    private void ExecuteCommand(ColorBlock from, ColorBlock to)
    {
        if (from.Color == PietColor.White || to.Color == PietColor.White || 
            from.Color == PietColor.Black || to.Color == PietColor.Black ||
            from.Color == PietColor.Unknown || to.Color == PietColor.Unknown)
        {
            return; // No operation
        }

        var hueChange = GetHueChange(from.Color, to.Color);
        var lightnessChange = GetLightnessChange(from.Color, to.Color);

        var command = (hueChange, lightnessChange) switch
        {
            (0, 1) => "push",
            (0, 2) => "pop",
            (1, 0) => "add",
            (1, 1) => "subtract",
            (1, 2) => "multiply",
            (2, 0) => "divide",
            (2, 1) => "mod",
            (2, 2) => "not",
            (3, 0) => "greater",
            (3, 1) => "pointer",
            (3, 2) => "switch",
            (4, 0) => "duplicate",
            (4, 1) => "roll",
            (4, 2) => "in_number",
            (5, 0) => "in_char",
            (5, 1) => "out_number",
            (5, 2) => "out_char",
            _ => ""
        };

        if (_debugMode && !string.IsNullOrEmpty(command))
        {
            Console.WriteLine($"  Command: {command} (blockSize: {from.Size}, hue: {hueChange}, light: {lightnessChange})");
        }
        
        ExecuteOperation(command, from.Size);
    }

    private int GetHueChange(PietColor from, PietColor to)
    {
        var hues = new[] {
            PietColor.Red, PietColor.Yellow, PietColor.Green, 
            PietColor.Cyan, PietColor.Blue, PietColor.Magenta
        };

        var fromHue = GetHue(from);
        var toHue = GetHue(to);

        if (fromHue == -1 || toHue == -1) return 0;

        return (toHue - fromHue + 6) % 6;
    }

    private int GetLightnessChange(PietColor from, PietColor to)
    {
        var fromLight = GetLightness(from);
        var toLight = GetLightness(to);

        if (fromLight == -1 || toLight == -1) return 0;

        return (toLight - fromLight + 3) % 3;
    }

    private int GetHue(PietColor color)
    {
        return color switch
        {
            PietColor.LightRed or PietColor.Red or PietColor.DarkRed => 0,
            PietColor.LightYellow or PietColor.Yellow or PietColor.DarkYellow => 1,
            PietColor.LightGreen or PietColor.Green or PietColor.DarkGreen => 2,
            PietColor.LightCyan or PietColor.Cyan or PietColor.DarkCyan => 3,
            PietColor.LightBlue or PietColor.Blue or PietColor.DarkBlue => 4,
            PietColor.LightMagenta or PietColor.Magenta or PietColor.DarkMagenta => 5,
            _ => -1
        };
    }

    private int GetLightness(PietColor color)
    {
        return color switch
        {
            PietColor.LightRed or PietColor.LightYellow or PietColor.LightGreen or
            PietColor.LightCyan or PietColor.LightBlue or PietColor.LightMagenta => 0,
            PietColor.Red or PietColor.Yellow or PietColor.Green or
            PietColor.Cyan or PietColor.Blue or PietColor.Magenta => 1,
            PietColor.DarkRed or PietColor.DarkYellow or PietColor.DarkGreen or
            PietColor.DarkCyan or PietColor.DarkBlue or PietColor.DarkMagenta => 2,
            _ => -1
        };
    }

    private void ExecuteOperation(string command, int blockSize)
    {
        try
        {
            switch (command)
            {
                case "push":
                    _stack.Push(blockSize);
                    break;
                case "pop":
                    if (_stack.Count > 0) _stack.Pop();
                    break;
                case "add":
                    if (_stack.Count >= 2)
                    {
                        var b = _stack.Pop();
                        var a = _stack.Pop();
                        _stack.Push(a + b);
                    }
                    break;
                case "subtract":
                    if (_stack.Count >= 2)
                    {
                        var b = _stack.Pop();
                        var a = _stack.Pop();
                        _stack.Push(a - b);
                    }
                    break;
                case "multiply":
                    if (_stack.Count >= 2)
                    {
                        var b = _stack.Pop();
                        var a = _stack.Pop();
                        _stack.Push(a * b);
                    }
                    break;
                case "divide":
                    if (_stack.Count >= 2)
                    {
                        var b = _stack.Pop();
                        var a = _stack.Pop();
                        if (b != 0) _stack.Push(a / b);
                    }
                    break;
                case "mod":
                    if (_stack.Count >= 2)
                    {
                        var b = _stack.Pop();
                        var a = _stack.Pop();
                        if (b != 0) _stack.Push(a % b);
                    }
                    break;
                case "not":
                    if (_stack.Count >= 1)
                    {
                        var a = _stack.Pop();
                        _stack.Push(a == 0 ? 1 : 0);
                    }
                    break;
                case "greater":
                    if (_stack.Count >= 2)
                    {
                        var b = _stack.Pop();
                        var a = _stack.Pop();
                        _stack.Push(a > b ? 1 : 0);
                    }
                    break;
                case "pointer":
                    if (_stack.Count >= 1)
                    {
                        var rotations = _stack.Pop() % 4;
                        for (int i = 0; i < rotations; i++)
                        {
                            _directionPointer = RotateDirection(_directionPointer);
                        }
                    }
                    break;
                case "switch":
                    if (_stack.Count >= 1)
                    {
                        var toggles = _stack.Pop() % 2;
                        if (toggles == 1)
                        {
                            _codelChooser = _codelChooser == CodelChooser.Left ? CodelChooser.Right : CodelChooser.Left;
                        }
                    }
                    break;
                case "duplicate":
                    if (_stack.Count >= 1)
                    {
                        var top = _stack.Peek();
                        _stack.Push(top);
                    }
                    break;
                case "roll":
                    if (_stack.Count >= 2)
                    {
                        var rolls = _stack.Pop();
                        var depth = _stack.Pop();
                        if (depth > 0 && depth <= _stack.Count)
                        {
                            var values = new int[depth];
                            for (int i = 0; i < depth; i++)
                            {
                                values[i] = _stack.Pop();
                            }
                            
                            rolls = rolls % depth;
                            if (rolls < 0) rolls += depth;
                            
                            for (int i = 0; i < depth; i++)
                            {
                                _stack.Push(values[(i + rolls) % depth]);
                            }
                        }
                    }
                    break;
                case "in_number":
                    Console.Write("Enter number: ");
                    if (int.TryParse(Console.ReadLine(), out var num))
                    {
                        _stack.Push(num);
                    }
                    break;
                case "in_char":
                    var key = Console.ReadKey(true);
                    _stack.Push(key.KeyChar);
                    break;
                case "out_number":
                    if (_stack.Count > 0)
                    {
                        Console.Write(_stack.Pop());
                    }
                    break;
                case "out_char":
                    if (_stack.Count > 0)
                    {
                        var charValue = _stack.Pop();
                        if (charValue >= 32 && charValue <= 126) // Printable ASCII
                        {
                            Console.Write((char)charValue);
                        }
                        else
                        {
                            Console.Write($"[{charValue}]"); // Show non-printable as numbers
                        }
                    }
                    break;
            }
        }
        catch
        {
            // Ignore invalid operations
        }
    }

    private static Direction RotateDirection(Direction dir)
    {
        return (Direction)(((int)dir + 1) % 4);
    }

    private Position GetNextPosition(Position pos, Direction dir)
    {
        return dir switch
        {
            Direction.Right => new Position(pos.X + 1, pos.Y),
            Direction.Down => new Position(pos.X, pos.Y + 1),
            Direction.Left => new Position(pos.X - 1, pos.Y),
            Direction.Up => new Position(pos.X, pos.Y - 1),
            _ => pos
        };
    }

    private bool IsValidPosition(Position pos)
    {
        return pos.X >= 0 && pos.X < _width && pos.Y >= 0 && pos.Y < _height;
    }

    private IEnumerable<Position> GetAdjacentPositions(Position pos)
    {
        yield return new Position(pos.X + 1, pos.Y);
        yield return new Position(pos.X - 1, pos.Y);
        yield return new Position(pos.X, pos.Y + 1);
        yield return new Position(pos.X, pos.Y - 1);
    }
}
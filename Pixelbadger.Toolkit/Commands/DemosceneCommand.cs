using System.CommandLine;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Commands;

public static class DemosceneCommand
{
    public static Command Create()
    {
        var command = new Command("demoscene", "Classic Amiga demoscene effects rendered in the terminal using half-block ANSI characters");

        command.Add(CreateKefrensBarsCommand());
        command.Add(CreateBoingCommand());

        return command;
    }

    private static Command CreateKefrensBarsCommand()
    {
        var command = new Command("kefrens", "Kefrens-style sine wave colour bars scrolling across the screen");

        var framesOption = new Option<int>("--frames") { Description = "Number of frames to animate (default: 200)", DefaultValueFactory = _ => 200 };
        command.Add(framesOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            int frames = parseResult.GetValue(framesOption);
            int width = Console.WindowWidth > 0 ? Console.WindowWidth : 80;
            int height = (Console.WindowHeight > 0 ? Console.WindowHeight - 1 : 24) * 2;

            var buffer = new PixelBuffer(width, height);
            var component = new KefrensBarsComponent();

            Console.CursorVisible = false;
            Console.Clear();

            try
            {
                for (int frame = 0; frame < frames; frame++)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    component.RenderFrame(buffer, frame);
                    Console.SetCursorPosition(0, 0);
                    Console.Write(buffer.Render());
                    Thread.Sleep(50);
                }
            }
            finally
            {
                Console.CursorVisible = true;
                Console.Write("\x1b[0m");
            }

            return Task.FromResult(0);
        });

        return command;
    }

    private static Command CreateBoingCommand()
    {
        var command = new Command("boing", "Classic Amiga Boing Ball — a red-and-white checkered ball bouncing on a grey grid");

        var framesOption = new Option<int>("--frames") { Description = "Number of frames to animate (default: 200)", DefaultValueFactory = _ => 200 };
        command.Add(framesOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            int frames = parseResult.GetValue(framesOption);
            int width = Console.WindowWidth > 0 ? Console.WindowWidth : 80;
            int height = (Console.WindowHeight > 0 ? Console.WindowHeight - 1 : 24) * 2;

            var buffer = new PixelBuffer(width, height);
            var component = new BoingBallComponent();

            Console.CursorVisible = false;
            Console.Clear();

            try
            {
                for (int frame = 0; frame < frames; frame++)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    component.RenderFrame(buffer, frame);
                    Console.SetCursorPosition(0, 0);
                    Console.Write(buffer.Render());
                    Thread.Sleep(50);
                }
            }
            finally
            {
                Console.CursorVisible = true;
                Console.Write("\x1b[0m");
            }

            return Task.FromResult(0);
        });

        return command;
    }
}

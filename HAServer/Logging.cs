using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console.Internal;
using System;
using System.Runtime.InteropServices;

// NUGET: Microsoft.Extensions.Logging 1.1.0, ..Logging.Console

namespace HAServer
{
    // Setup logging for all classes to use
    public static class ApplicationLogging
    {
        public static ILoggerFactory Logger { get; } = new LoggerFactory();
        public static ILogger CreateLogger<T>() =>
        Logger.CreateLogger<T>();
    }

    public static class MyLoggerProviderExtensions
    {
        public static ILoggerFactory AddMyLogger(this ILoggerFactory loggerFactory)
        {
            loggerFactory.AddProvider(new MyLoggerProvider());
            return loggerFactory;
        }
    }

    public class MyLoggerProvider : ILoggerProvider
    {

        public MyLoggerProvider()
        {
        }

        public ILogger CreateLogger(string callerName)
        {
            return new MyLogger(callerName);
        }

        public void Dispose()
        {
        }

        void IDisposable.Dispose()
        {
            throw new NotImplementedException();
        }
    }

    public class MyLogger : ILogger
    {
        private string _callerName;
        private IConsole _console;

        public MyLogger(string callerName)
        {
            _callerName = callerName;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Console = new WindowsLogConsole();
            }
            else
            {
                Console = new AnsiLogConsole(new AnsiSystemConsole());
            }
        }

        public IConsole Console
        {
            get { return _console; }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }
                _console = value;
            }
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            string level = null;
            ConsoleColors levelColors = new ConsoleColors(ConsoleColor.Black, ConsoleColor.Gray);
            ConsoleColors statusColors = new ConsoleColors(ConsoleColor.Black, ConsoleColor.Gray);
            switch (logLevel)
            {
                case LogLevel.Trace:
                    levelColors = new ConsoleColors(ConsoleColor.Black, ConsoleColor.Gray);
                    statusColors = new ConsoleColors(ConsoleColor.Black, ConsoleColor.DarkGray);
                    level = "TRACE";
                    break;

                case LogLevel.Debug:
                    levelColors = new ConsoleColors(ConsoleColor.Black, ConsoleColor.Gray);
                    statusColors = new ConsoleColors(ConsoleColor.Black, ConsoleColor.DarkGray);
                    level = "DEBUG";
                    break;

                case LogLevel.Information:
                    levelColors = new ConsoleColors(ConsoleColor.Black, ConsoleColor.Green);
                    statusColors = new ConsoleColors(ConsoleColor.Black, ConsoleColor.DarkGreen);
                    level = "INFO";
                    break;

                case LogLevel.Warning:
                    levelColors = new ConsoleColors(ConsoleColor.Black, ConsoleColor.Yellow);
                    statusColors = new ConsoleColors(ConsoleColor.Black, ConsoleColor.Yellow);
                    level = "WARNING";
                    break;

                case LogLevel.Error:
                    levelColors = new ConsoleColors(ConsoleColor.DarkMagenta, ConsoleColor.White);
                    statusColors = new ConsoleColors(ConsoleColor.Black, ConsoleColor.White);
                    level = "ERROR";
                    break;

                case LogLevel.Critical:
                    level = "CRITICAL";
                    levelColors = new ConsoleColors(ConsoleColor.Red, ConsoleColor.Black);
                    statusColors = new ConsoleColors(ConsoleColor.Black, ConsoleColor.Red);
                    break;
            }
            Console.Write(String.Format("{0,-12} {1,-10}", DateTime.Now.ToString("HH:mm:ss.fff"), "[" + _callerName + "] "), ConsoleColor.Black, ConsoleColor.Gray);
            Console.Write("".PadLeft(28 - _callerName.Length - level.Length), ConsoleColor.Black, ConsoleColor.Gray);
            Console.Write(level, levelColors.Foreground, levelColors.Background);
            Console.Write(" " + state.ToString(), statusColors.Foreground, statusColors.Background);
            Console.WriteLine("", levelColors.Foreground, levelColors.Background);
            Console.Flush();
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return new NoopDisposable();
        }

        private struct ConsoleColors
        {
            public ConsoleColors(ConsoleColor? foreground, ConsoleColor? background)
            {
                Foreground = foreground;
                Background = background;
            }
            public ConsoleColor? Foreground { get; }
            public ConsoleColor? Background { get; }
        }

        private class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }

        // For Linux consoles
        private class AnsiSystemConsole : IAnsiSystemConsole
        {
            public void Write(string message)
            {
                System.Console.Write(message);
            }

            public void WriteLine(string message)
            {
                System.Console.WriteLine(message);
            }
        }
    }
}


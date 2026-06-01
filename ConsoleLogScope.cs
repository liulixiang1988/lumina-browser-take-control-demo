using System.Text;

internal sealed class ConsoleLogScope : IDisposable
{
    private readonly TextWriter originalOut;
    private readonly TextWriter originalError;
    private readonly StreamWriter logWriter;
    private readonly TextWriter teeOut;
    private readonly TextWriter teeError;

    private ConsoleLogScope(string fullPath, TextWriter originalOut, TextWriter originalError, StreamWriter logWriter)
    {
        FullPath = fullPath;
        this.originalOut = originalOut;
        this.originalError = originalError;
        this.logWriter = logWriter;
        teeOut = new TeeTextWriter(originalOut, logWriter);
        teeError = new TeeTextWriter(originalError, logWriter);
        Console.SetOut(teeOut);
        Console.SetError(teeError);
    }

    public string FullPath { get; }

    public static ConsoleLogScope? Start(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var logWriter = new StreamWriter(fullPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };
        return new ConsoleLogScope(fullPath, Console.Out, Console.Error, logWriter);
    }

    public void Dispose()
    {
        Console.SetOut(originalOut);
        Console.SetError(originalError);
        teeOut.Dispose();
        teeError.Dispose();
        logWriter.Dispose();
    }

    private sealed class TeeTextWriter : TextWriter
    {
        private readonly TextWriter consoleWriter;
        private readonly TextWriter logWriter;

        public TeeTextWriter(TextWriter consoleWriter, TextWriter logWriter)
        {
            this.consoleWriter = consoleWriter;
            this.logWriter = logWriter;
        }

        public override Encoding Encoding => consoleWriter.Encoding;

        public override void Write(char value)
        {
            consoleWriter.Write(value);
            logWriter.Write(value);
        }

        public override void Write(string? value)
        {
            consoleWriter.Write(value);
            logWriter.Write(value);
        }

        public override void WriteLine(string? value)
        {
            consoleWriter.WriteLine(value);
            logWriter.WriteLine(value);
        }

        public override void WriteLine()
        {
            consoleWriter.WriteLine();
            logWriter.WriteLine();
        }

        public override void Flush()
        {
            consoleWriter.Flush();
            logWriter.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Flush();
            }
        }
    }
}

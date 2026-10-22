using System;
using System.IO;
using System.Threading.Tasks;

namespace NodeKit.Cli.Tests.Fakes
{
    /// <summary>Captures output like <see cref="StringWriter"/> and completes <see cref="Seen"/> once a written
    /// text contains the marker. The CLI output loops write from one thread at a time.</summary>
    internal sealed class MarkerSignalWriter : StringWriter
    {
        private readonly string _marker;

        private readonly TaskCompletionSource _seen = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MarkerSignalWriter(string marker)
        {
            _marker = marker;
        }

        public Task Seen => _seen.Task;

        public override void Write(string? value)
        {
            base.Write(value);
            Check(value);
        }

        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            Check(value);
        }

        private void Check(string? value)
        {
            if (value is not null && value.Contains(_marker, StringComparison.Ordinal))
            {
                _seen.TrySetResult();
            }
        }
    }
}

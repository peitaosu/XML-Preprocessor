using System;
using System.IO;

namespace XMLPreprocessor
{

    public delegate void ProcessedStreamEventHandler(object sender, ProcessedStreamEventArgs e);

    public class ProcessedStreamEventArgs : EventArgs
    {
        private string sourceFile;
        private Stream processed;

        public ProcessedStreamEventArgs(string sourceFile, Stream processed)
        {
            this.sourceFile = sourceFile;
            this.processed = processed;
        }

        public string SourceFile
        {
            get { return this.sourceFile; }
        }

        public Stream Processed
        {
            get { return this.processed; }
        }
    }
}

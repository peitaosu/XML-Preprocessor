/* 
    Preprocessor implementation is from https://github.com/wixtoolset 
*/

using System;

namespace XMLPreprocessor
{

    public delegate void IncludedFileEventHandler(object sender, IncludedFileEventArgs e);

    public class IncludedFileEventArgs : EventArgs
    {
        private string fullName;

        public IncludedFileEventArgs(string fullName)
        {
            this.fullName = fullName;
        }

        public string FullName
        {
            get { return this.fullName; }
        }
    }
}

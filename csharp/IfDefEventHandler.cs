/* 
    Preprocessor implementation is from https://github.com/wixtoolset 
*/

using System;

namespace XMLPreprocessor
{
    public delegate void IfDefEventHandler(object sender, IfDefEventArgs e);

    public class IfDefEventArgs : EventArgs
    {
        private bool isIfDef;
        private bool isDefined;
        private string variableName;

        public IfDefEventArgs(bool isIfDef, bool isDefined, string variableName)
        {
            this.isIfDef = isIfDef;
            this.isDefined = isDefined;
            this.variableName = variableName;
        }

        public bool IsDefined
        {
            get { return this.isDefined; }
        }

        public bool IsIfDef
        {
            get { return this.isIfDef; }
        }

        public string VariableName
        {
            get { return this.variableName; }
        }
    }
}

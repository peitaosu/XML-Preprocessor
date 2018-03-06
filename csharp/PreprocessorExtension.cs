/* 
    Preprocessor implementation is from https://github.com/wixtoolset 
*/

using System.Diagnostics.CodeAnalysis;
using System.Xml;

namespace XMLPreprocessor
{

    public abstract class PreprocessorExtension
    {
        private PreprocessorCore core;

        public PreprocessorCore Core
        {
            get { return this.core; }
            set { this.core = value; }
        }

        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public virtual string[] Prefixes
        {
            get { return null; }
        }

        public virtual string GetVariableValue(string prefix, string name)
        {
            return null;
        }

        public virtual string EvaluateFunction(string prefix, string function, string[] args)
        {
            return null;
        }

        public virtual bool ProcessPragma(string prefix, string pragma, string args, XmlWriter writer)
        {
            return false;
        }

        public virtual void FinalizePreprocess()
        {
        }

        public virtual void InitializePreprocess()
        {
        }

        [SuppressMessage("Microsoft.Design", "CA1059:MembersShouldNotExposeCertainConcreteTypes")]
        public virtual void PreprocessDocument(XmlDocument document)
        {
        }

        public virtual string PreprocessParameter(string name)
        {
            return null;
        }
    }
}
